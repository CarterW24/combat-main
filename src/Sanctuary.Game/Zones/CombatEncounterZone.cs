using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public class EncounterMobState
{
    public bool Charging;
    public float SlotAngle;
    public long NextAttackTicks;
    public Vector4 Home;
    public bool Idling;
    public bool Planted;
}

public abstract class CombatEncounterZone : BaseZone
{
    protected CombatEncounterZone(BaseZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
    }

    protected const int KnockoutLimit = 5;

    private readonly object _knockoutLock = new();
    private int _knockouts;

    protected abstract int FailEncounterId { get; }
    protected virtual int FailInstanceId => 1;

    protected virtual string EncounterLogName => GetType().Name;

    protected override int ReviveCooldownMs => 30000;

    protected abstract void ReturnHome(Player player);

    public void EndEncounterForPlayer(Player player) => EndEncounterForPlayer(player, won: false);

    public void EndEncounterForPlayer(Player player, bool won)
    {
        if (won)
            player.SendTunneled(new MiniGameGameOverPacket(won: true));
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });
        player.SendTunneled(new UiObjectiveClearPacket());
        _logger.LogInformation("{label}: encounter released for {name}.", EncounterLogName, player.Name);
    }

    protected void ResetKnockouts(ulong guid)
    {
        lock (_knockoutLock)
            _knockouts = 0;
    }

    protected int KnockoutsUsed(ulong guid)
    {
        lock (_knockoutLock)
            return _knockouts;
    }

    protected static void EnterAtFullVitals(Player player)
    {
        var startHp = player.Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : 2500;
        player.CurrentHitpoints = startHp;
        player.SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = startHp, MaxHitpoints = startHp });
        player.SendTunneled(new ClientUpdatePacketMana { CurrentMana = 100, MaxMana = 100 });
    }

    private readonly object _exitDoorLock = new();
    private Npc? _exitDoor;

    protected Npc? ExitDoor
    {
        get { lock (_exitDoorLock) return _exitDoor; }
    }

    protected void SetExitDoor(Npc? door)
    {
        lock (_exitDoorLock)
            _exitDoor = door;
    }

    public bool IsExitDoor(ulong guid)
    {
        lock (_exitDoorLock)
            return _exitDoor is { } door && door.Guid == guid;
    }

    public virtual void UseExitDoor(Player player)
    {
        _logger.LogInformation("{label}: {name} used the exit door.", EncounterLogName, player.Name);
        ReturnHome(player);
    }

    public void LeaveEncounter(Player player)
    {
        if (player.Zone != this)
            return;

        _logger.LogInformation("{label}: {name} left the encounter.", EncounterLogName, player.Name);
        ReturnHome(player);
    }

    protected const int TickMs = 300;
    protected const float MobYSpeed = 12f;
    protected const float MobAttackRange = 2.6f;
    protected const float MobEngageRadius = 1.9f;
    protected const int MobAttackCooldownMs = 4000;
    protected const int MobAttackGlobalGapMs = 1200;
    protected const int MobAttackDamage = 150;
    protected const int MobAttackCritDamage = 300;
    protected const int MobAttackCritPercent = 10;
    protected const int MobAttackFxId = 5409;
    protected const int MobAttackCritFxId = 5622;

    protected virtual float MobChaseSpeed => 5f;

    private readonly Dictionary<ulong, long> _lastAttackTicksByTarget = [];

    protected abstract void Broadcast(ISerializablePacket packet);

    protected static float MoveToward(float current, float goal, float maxDelta)
    {
        var delta = goal - current;
        if (MathF.Abs(delta) <= maxDelta)
            return goal;
        return current + MathF.Sign(delta) * maxDelta;
    }

    protected static Player? NearestLivePlayer(Vector3 pos, IReadOnlyList<Player> players)
    {
        Player? best = null;
        var best2 = float.MaxValue;
        foreach (var p in players)
        {
            if (p.IsDead)
                continue;
            var dx = p.Position.X - pos.X;
            var dz = p.Position.Z - pos.Z;
            var d2 = dx * dx + dz * dz;
            if (d2 < best2)
            {
                best2 = d2;
                best = p;
            }
        }
        return best;
    }

    protected void TickMobReturnHome(Npc mob, EncounterMobState state, float dt)
    {
        state.Charging = false;
        state.Planted = false;
        var here = new Vector3(mob.Position.X, mob.Position.Y, mob.Position.Z);
        var toHome = new Vector2(state.Home.X - here.X, state.Home.Z - here.Z);
        var distHome = toHome.Length();
        if (distHome > 0.6f)
        {
            state.Idling = false;
            var step = MathF.Min(MobChaseSpeed * dt, distHome);
            var dir = toHome / distHome;
            var ny = MoveToward(here.Y, state.Home.Y, MobYSpeed * dt);
            var np = new Vector4(here.X + dir.X * step, ny, here.Z + dir.Y * step, mob.Position.W);
            var frot = new Quaternion(dir.X, 0f, dir.Y, 0f);
            mob.UpdatePosition(np, frot);
            Broadcast(new PlayerUpdatePacketUpdatePosition { Guid = mob.Guid, Position = np, Rotation = frot, State = 0, Unknown = 0 });
        }
        else if (!state.Idling)
        {
            state.Idling = true;
            Broadcast(new PlayerUpdatePacketUpdatePosition { Guid = mob.Guid, Position = mob.Position, Rotation = mob.Rotation, State = 1, Unknown = 0 });
        }
    }

    protected void TickMobCombat(Npc mob, EncounterMobState state, Player player, Vector3 playerPos, long now, float dt)
    {
        var here = new Vector3(mob.Position.X, mob.Position.Y, mob.Position.Z);
        var slot = playerPos + new Vector3(MathF.Sin(state.SlotAngle), 0f, MathF.Cos(state.SlotAngle)) * MobEngageRadius;
        var toPlayerH = new Vector2(playerPos.X - here.X, playerPos.Z - here.Z);
        var distToPlayerH = toPlayerH.Length();
        var face = distToPlayerH > 0.01f ? toPlayerH / distToPlayerH : new Vector2(0f, 1f);
        var rot = new Quaternion(face.X, 0f, face.Y, 0f);
        var newY = MoveToward(here.Y, playerPos.Y, MobYSpeed * dt);

        if (distToPlayerH > MobAttackRange)
        {
            state.Planted = false;
            var toSlot = new Vector2(slot.X - here.X, slot.Z - here.Z);
            var distToSlot = toSlot.Length();
            var step = MathF.Min(MobChaseSpeed * dt, distToSlot);
            var dir = distToSlot > 0.01f ? toSlot / distToSlot : Vector2.Zero;
            var np = new Vector4(here.X + dir.X * step, newY, here.Z + dir.Y * step, mob.Position.W);
            mob.UpdatePosition(np, rot);
            Broadcast(new PlayerUpdatePacketUpdatePosition { Guid = mob.Guid, Position = np, Rotation = rot, State = 0, Unknown = 0 });
        }
        else
        {
            if (!state.Planted)
            {
                state.Planted = true;
                var np = new Vector4(here.X, newY, here.Z, mob.Position.W);
                mob.UpdatePosition(np, rot);
                Broadcast(new PlayerUpdatePacketUpdatePosition { Guid = mob.Guid, Position = np, Rotation = rot, State = 1, Unknown = 0 });
            }

            _lastAttackTicksByTarget.TryGetValue(player.Guid, out var lastAttackOnTarget);
            if (now >= state.NextAttackTicks && now - lastAttackOnTarget >= MobAttackGlobalGapMs && !player.IsDead)
            {
                state.NextAttackTicks = now + MobAttackCooldownMs;
                _lastAttackTicksByTarget[player.Guid] = now;
                PerformMobAttack(mob, player);
            }
        }
    }

    protected void PerformMobAttack(Npc attacker, Player player)
    {
        var maxHp = player.Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : 2500;

        if (player.TryDodgeIncomingAttack(attacker.Guid))
        {
            if (CombatNpc.ExplicitAttackAnimByModel.TryGetValue(attacker.ModelId, out var missAnim))
                Broadcast(new PlayerUpdatePacketSetAnimation { Guid = attacker.Guid, AnimationId = missAnim });
            return;
        }

        var crit = Random.Shared.Next(100) < MobAttackCritPercent;
        var dmg = player.ReduceIncomingDamage(crit ? MobAttackCritDamage : MobAttackDamage);
        player.TakeDamage(dmg);
        Broadcast(new CombatPacketAttackProcessed
        {
            AttackerGuid = attacker.Guid,
            TargetGuid = player.Guid,
            Damage = dmg,
            MaxHealth = maxHp,
            CompositeEffectId = crit ? MobAttackCritFxId : MobAttackFxId,
            CurrentHealth = player.CurrentHitpoints,
        });
        if (CombatNpc.ExplicitAttackAnimByModel.TryGetValue(attacker.ModelId, out var swingAnimId))
            Broadcast(new PlayerUpdatePacketSetAnimation { Guid = attacker.Guid, AnimationId = swingAnimId });
    }

    public override void OnPlayerKnockedOut(Player player)
    {
        if (player.Zone != this)
            return;

        int kos;
        lock (_knockoutLock)
            kos = ++_knockouts;

        _logger.LogInformation("{label}: {name} knocked out ({kos}/{limit} group pool).", EncounterLogName, player.Name, kos, KnockoutLimit);

        Broadcast(new MiniGameKnockOutPacket(kos, KnockoutLimit));

        if (kos >= KnockoutLimit)
        {
            var members = new List<Player>(Players);
            foreach (var member in members)
                SendFailEndScreen(member);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FailCardHoldMs);
                    foreach (var member in members)
                    {
                        if (member.Zone != this)
                            continue;
                        ReturnHome(member);
                        member.Respawn();
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Fail-return failed."); }
            });
            return;
        }

        player.SendTunneled(new EncounterShowRespawnWindowPacket(FailEncounterId, FailInstanceId));
        ScheduleAutoRevive(player);
    }

    public override void OnPlayerRespawn(Player player)
    {
        var pos = player.DeathPosition;
        player.Respawn();

        var hide = new EncounterHideRespawnWindowPacket();
        hide.Unknown = FailEncounterId;
        hide.Unknown2 = FailInstanceId;
        player.SendTunneled(hide);

        if (player.Zone == this)
        {
            player.UpdatePosition(pos, player.Rotation);
            player.SendTunneled(new ClientUpdatePacketUpdateLocation
            {
                Position = pos,
                Rotation = player.Rotation,
                Teleport = true,
            });
        }
    }
}
