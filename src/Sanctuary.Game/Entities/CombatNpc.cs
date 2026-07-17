using System;
using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Entities;

public class CombatNpc : Npc
{
    public int CurrentHitpoints { get; set; }
    public int MaxHitpoints { get; set; }
    public int AttackDamage { get; set; }
    public int Defense { get; set; }
    public int Level { get; set; }
    public int XpReward { get; set; }

    public float AttackIntervalSeconds { get; set; } = 2.0f;

    public float AggroRange { get; set; } = 15.0f;

    public float LeashRange { get; set; } = 40.0f;

    public float AttackRange { get; set; } = 5.0f;

    public float CombatSpeed { get; set; } = 6.0f;

    public float ReturnSpeed { get; set; } = 6.0f;

    public Vector4 SpawnPosition { get; set; }
    public Quaternion SpawnRotation { get; set; }
    public bool IsDead { get; set; }
    public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
    public DateTime DeathTime { get; set; }
    public Player? AggroTarget { get; set; }
    public CombatState State { get; set; } = CombatState.Idle;

    public float RespawnSeconds { get; set; } = 30.0f;

    public Vector4 LastSentPosition { get; set; }

    public float LastSentExpectedSpeed { get; set; } = -1f;

    public static readonly IReadOnlyDictionary<int, int> ExplicitAttackAnimByModel = new Dictionary<int, int>
    {
        [1944] = 1099,
    };

    public CombatNpc(IZone zone) : base(zone)
    {
        Disposition = 0;
    }

    public void InitializeFromLevel(int level)
    {
        Level = level;
        MaxHitpoints = 200 + (level * 150);
        CurrentHitpoints = MaxHitpoints;
        AttackDamage = 20 + (level * 15);
        Defense = level * 5;
        XpReward = 50 + (level * 25);
        AttackIntervalSeconds = Math.Max(1.5f, 2.5f - (level * 0.05f));
    }

    public override void UpdateEveryTick()
    {
        if (IsDead || !Visible)
            return;

        switch (State)
        {
            case CombatState.Idle:
                UpdateIdle();
                break;
            case CombatState.Pursuing:
                UpdatePursuing();
                break;
            case CombatState.Attacking:
                UpdateAttacking();
                break;
            case CombatState.Returning:
                UpdateReturning();
                break;
        }
    }

    public override void UpdateEverySecond()
    {
        if (!IsDead)
            return;

        if ((DateTime.UtcNow - DeathTime).TotalSeconds >= RespawnSeconds)
        {
            Respawn();
        }
    }

    private void UpdateIdle()
    {
        var closestPlayer = FindClosestPlayer(AggroRange);

        if (closestPlayer is not null && !closestPlayer.IsDead)
        {
            AggroTarget = closestPlayer;
            State = CombatState.Pursuing;
        }
    }

    private void UpdatePursuing()
    {
        if (AggroTarget is null || AggroTarget.IsDead || !AggroTarget.Visible)
        {
            StartReturning();
            return;
        }

        var distToSpawn = DistanceTo(SpawnPosition);
        if (distToSpawn > LeashRange)
        {
            StartReturning();
            return;
        }

        var distToTarget = DistanceTo(AggroTarget.Position);

        if (distToTarget <= AttackRange)
        {
            BroadcastStop();
            State = CombatState.Attacking;
            return;
        }

        MoveTowards(AggroTarget.Position, CombatSpeed);
    }

    private void UpdateAttacking()
    {
        if (AggroTarget is null || AggroTarget.IsDead || !AggroTarget.Visible)
        {
            StartReturning();
            return;
        }

        var distToTarget = DistanceTo(AggroTarget.Position);

        if (distToTarget > AttackRange * 1.5f)
        {
            State = CombatState.Pursuing;
            return;
        }

        var distToSpawn = DistanceTo(SpawnPosition);
        if (distToSpawn > LeashRange)
        {
            StartReturning();
            return;
        }

        FaceTarget(AggroTarget.Position);

        if ((DateTime.UtcNow - LastAttackTime).TotalSeconds >= AttackIntervalSeconds)
        {
            PerformAttack(AggroTarget);
            LastAttackTime = DateTime.UtcNow;
        }
    }

    private void UpdateReturning()
    {
        var distToSpawn = DistanceTo(SpawnPosition);

        if (distToSpawn < 0.5f)
        {
            UpdatePosition(SpawnPosition, SpawnRotation);
            BroadcastStop();
            State = CombatState.Idle;
            AggroTarget = null;

            if (CurrentHitpoints < MaxHitpoints)
            {
                CurrentHitpoints = MaxHitpoints;
                BroadcastHpUpdate();
            }
            return;
        }

        if (distToSpawn <= LeashRange * 0.8f)
        {
            var closestPlayer = FindClosestPlayer(AggroRange * 0.5f);
            if (closestPlayer is not null && !closestPlayer.IsDead)
            {
                AggroTarget = closestPlayer;
                State = CombatState.Pursuing;
                return;
            }
        }

        MoveTowards(SpawnPosition, ReturnSpeed);
    }

    private void StartReturning()
    {
        AggroTarget = null;
        State = CombatState.Returning;
    }

    private void PerformAttack(Player target)
    {
        if (target.IsDead)
            return;

        var random = Random.Shared;
        var variance = random.NextSingle() * 0.4f + 0.8f;
        var baseDamage = (int)(AttackDamage * variance);

        var defense = target.Stats[CharacterStatId.Defense].Int;
        var damageReduction = target.Stats[CharacterStatId.DamageReductionAmount].Int;
        var damageReductionPct = target.Stats[CharacterStatId.DamageReductionPercent].Int;

        var finalDamage = baseDamage - defense - damageReduction;
        if (damageReductionPct > 0)
            finalDamage = (int)(finalDamage * (1f - damageReductionPct / 100f));

        finalDamage = Math.Max(1, finalDamage);

        if (target.TryDodgeIncomingAttack(Guid))
        {
            if (ExplicitAttackAnimByModel.TryGetValue(ModelId, out var missAnim))
                foreach (var p in VisiblePlayers.Values)
                    p.SendTunneled(new PlayerUpdatePacketSetAnimation { Guid = Guid, AnimationId = missAnim });
            return;
        }

        finalDamage = target.ReduceIncomingDamage(finalDamage);

        target.TakeDamage(finalDamage, this);

        var attack = new CombatPacketAttackProcessed
        {
            AttackerGuid = Guid,
            TargetGuid = target.Guid,
            Damage = finalDamage,
            MaxHealth = target.Stats[CharacterStatId.MaxHealth].Int,
            CurrentHealth = target.CurrentHitpoints,
            CompositeEffectId = 0,
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(attack);

        if (ExplicitAttackAnimByModel.TryGetValue(ModelId, out var swingAnimId))
        {
            var swing = new PlayerUpdatePacketSetAnimation { Guid = Guid, AnimationId = swingAnimId };
            foreach (var player in VisiblePlayers.Values)
                player.SendTunneled(swing);
        }
    }

    public void AggroOnto(Player source)
    {
        if (IsDead || source.IsDead)
            return;

        if (AggroTarget is null || !AggroTarget.Visible || AggroTarget.IsDead)
        {
            AggroTarget = source;
            if (State is CombatState.Idle or CombatState.Returning)
                State = CombatState.Pursuing;
        }
    }

    public void TakeDamage(int amount, Player source)
    {
        if (IsDead)
            return;

        CurrentHitpoints = Math.Max(0, CurrentHitpoints - amount);

        var hpMod = new PlayerUpdatePacketHitPointModification
        {
            Guid = source.Guid,
            Guid2 = Guid,
            Unknown = true,
            Unknown2 = MaxHitpoints,
            Unknown3 = CurrentHitpoints,
            Unknown4 = -amount
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(hpMod);

        BroadcastHpUpdate();

        if (AggroTarget is null || !AggroTarget.Visible || AggroTarget.IsDead)
        {
            AggroTarget = source;
            State = CombatState.Pursuing;
        }

        if (CurrentHitpoints <= 0)
        {
            Die(source);
        }
    }

    private void Die(Player killer)
    {
        IsDead = true;
        DeathTime = DateTime.UtcNow;
        State = CombatState.Idle;
        AggroTarget = null;

        var destroyedPacket = new PlayerUpdatePacketDestroyed
        {
            Guid = Guid,
            KillerGuid = killer.Guid,
            Unknown = 0
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(destroyedPacket);

        killer.AwardXp(XpReward);

        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisibleNpcs([this]);

        Visible = false;
    }

    private void Respawn()
    {
        IsDead = false;
        CurrentHitpoints = MaxHitpoints;
        State = CombatState.Idle;
        AggroTarget = null;

        UpdatePosition(SpawnPosition, SpawnRotation);
        LastSentPosition = SpawnPosition;

        Visible = true;
        UpdateZoneTile();

        foreach (var player in VisiblePlayers.Values)
            player.OnAddVisibleNpcs([this]);
    }

    private void MoveTowards(Vector4 target, float speed)
    {
        var dx = target.X - Position.X;
        var dz = target.Z - Position.Z;
        var dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist < 0.1f)
            return;

        SendExpectedSpeed(speed);

        var moveAmount = speed * 0.1f;
        if (moveAmount > dist)
            moveAmount = dist;

        var nx = dx / dist;
        var nz = dz / dist;

        var frac = moveAmount / dist;
        var newPos = new Vector4(
            Position.X + nx * moveAmount,
            Position.Y + (target.Y - Position.Y) * frac,
            Position.Z + nz * moveAmount,
            1f
        );

        var angle = MathF.Atan2(dx, dz);
        var halfAngle = angle / 2f;
        var newRot = new Quaternion(0, MathF.Sin(halfAngle), 0, MathF.Cos(halfAngle));

        UpdatePosition(newPos, newRot);

        var sentDx = newPos.X - LastSentPosition.X;
        var sentDz = newPos.Z - LastSentPosition.Z;
        var sentDist = MathF.Sqrt(sentDx * sentDx + sentDz * sentDz);

        if (sentDist >= 0.3f)
        {
            BroadcastPositionUpdate(2);
            LastSentPosition = newPos;
        }
    }

    private void SendExpectedSpeed(float speed)
    {
        if (LastSentExpectedSpeed == speed)
            return;
        LastSentExpectedSpeed = speed;
        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(new PlayerUpdatePacketExpectedSpeed { Guid = Guid, ExpectedSpeed = speed });
    }

    private void BroadcastStop()
    {
        SendExpectedSpeed(0f);
        BroadcastPositionUpdate(0);
        LastSentPosition = Position;
    }

    private void FaceTarget(Vector4 target)
    {
        var dx = target.X - Position.X;
        var dz = target.Z - Position.Z;

        var angle = MathF.Atan2(dx, dz);
        var halfAngle = angle / 2f;
        var newRot = new Quaternion(0, MathF.Sin(halfAngle), 0, MathF.Cos(halfAngle));

        UpdatePosition(Position, newRot);
    }

    private void BroadcastPositionUpdate(byte state)
    {
        var posUpdate = new PlayerUpdatePacketUpdatePosition
        {
            Guid = Guid,
            Position = Position,
            Rotation = Rotation,
            State = state,
            Unknown = 0
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(posUpdate);
    }

    private void BroadcastHpUpdate()
    {
        var hpUpdate = new PlayerUpdatePacketUpdateHitpoints
        {
            Guid = Guid,
            Hitpoints = CurrentHitpoints,
            MaxHitpoints = MaxHitpoints
        };

        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(hpUpdate);
    }

    private Player? FindClosestPlayer(float maxRange)
    {
        Player? closest = null;
        float closestDist = maxRange;

        foreach (var player in VisiblePlayers.Values)
        {
            if (player.IsDead || !player.Visible)
                continue;

            var dist = DistanceTo(player.Position);

            if (dist < closestDist)
            {
                closestDist = dist;
                closest = player;
            }
        }

        return closest;
    }

    private float DistanceTo(Vector4 target)
    {
        var dx = target.X - Position.X;
        var dz = target.Z - Position.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override PlayerUpdatePacketAddNpc GetAddNpcPacket()
    {
        var packet = base.GetAddNpcPacket();

        // Show health bar on hostile NPCs
        // Unknown41 appears to control health bar display
        packet.Unknown41 = true;

        return packet;
    }
}

public enum CombatState
{
    Idle,
    Pursuing,
    Attacking,
    Returning
}
