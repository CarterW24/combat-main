using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

// OVERWORLD POWERUP TEST BED (2026-07-15, user request): grant/use the Frostfang held powerups in ANY
// zone so FX/anim/icon tuning doesn't require running the whole encounter. Reuses the arena's
// live-tunable FX/anim statics, so "!pufx" retunes BOTH places at once. Flame/Quake here radially
// damage whatever damageable NPC is around (the starting-zone training dummy = damage-number test
// target per user); the arena keeps its own richer flow (wolf stun anims, shield invulnerability).
public static class HeldPowerupProbe
{
    // The REAL powerup icons (icon_*_32.dds — each powerup_*.adr binds its icon by this name).
    private const int FlameWaveIconId = 26832;
    private const int QuakeIconId = 26835;
    private const int ShieldIconId = 26838;
    private const int PowerupNameId = 5102385;  // live damage-powerup NameId (plate hidden anyway)

    private const int EnergyModelId = 737;      // powerup_mana_buff.adr
    private const int FlameModelId = 1949;      // powerup_flame_wave.adr
    private const int QuakeModelId = 1950;      // powerup_quake.adr ("Earth Shard")
    private const int ShieldModelId = 1951;     // powerup_super_shield.adr
    private const int PickupFxId = 15032;       // heart pickup sparkle (same as the arena)
    private const float PickupRange = 2.5f;

    // Provisional use numbers — mirror the arena's (FrostfangArenaZone) values.
    private const int FlameWaveDamage = 500;
    private const int QuakeDamage = 350;
    private const float UseRadius = 10f;

    /// <summary>kind (normalized: "flame"/"quake"/"shield") currently held per player, outside the arena.</summary>
    private static readonly ConcurrentDictionary<ulong, string> _held = new();

    private static string? Normalize(string kind) => kind switch
    {
        "flame" or "flamewave" or "fire" => "flame",
        "quake" or "earth" or "earthshard" => "quake",
        "shield" or "super" or "supershield" => "shield",
        _ => null,
    };

    /// <summary>"!pu &lt;kind&gt;" outside the arena: put the powerup straight on the "3" key
    /// (or fire the instant energy refill).</summary>
    public static bool Grant(Player player, string kind, IResourceManager resources)
    {
        if (kind == "energy")
        {
            CombatBuffs.RequestEnergyRefill(player);
            return true;
        }

        if (Normalize(kind) is not { } k)
            return false;

        _held[player.Guid] = k;
        SendSlot(player, resources);
        return true;
    }

    /// <summary>The "3" key outside the arena: play the use presentation (optional anim + FX) and
    /// radially damage any damageable NPC around (training-dummy numbers). Returns false if empty.</summary>
    public static bool TryUse(Player player, IResourceManager resources)
    {
        if (!_held.TryRemove(player.Guid, out var kind))
            return false;

        var (fxId, animId) = kind switch
        {
            "flame" => (FrostfangArenaZone.FlameWaveFxId, FrostfangArenaZone.FlameWaveAnimId),
            "quake" => (FrostfangArenaZone.QuakeFxId, FrostfangArenaZone.QuakeAnimId),
            _ => (FrostfangArenaZone.ShieldFxId, FrostfangArenaZone.ShieldAnimId),
        };

        // No use-gesture existed in the real game (user) — anim only plays when one was set via !pufx.
        if (animId > 0)
        {
            player.SendTunneled(new AbilityPacketStartCasting
            {
                Unknown = player.Guid,
                Unknown2 = player.Guid,
                Animation = animId,
                AbilityId = 3,
                ActionTime = 0.4f,
                HasActionProgress = false,
            });
        }

        player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = player.Guid,
            CompositeEffectId = fxId,
            Position = player.Position,
        });

        var damage = kind switch { "flame" => FlameWaveDamage, "quake" => QuakeDamage, _ => 0 };
        if (damage > 0 && player.Zone is BaseZone zone)
        {
            foreach (var npc in zone.Npcs)
            {
                if (!npc.IsAlive || !npc.IsDamageable)
                    continue;

                var dx = npc.Position.X - player.Position.X;
                var dz = npc.Position.Z - player.Position.Z;
                if (dx * dx + dz * dz > UseRadius * UseRadius)
                    continue;

                npc.ApplyDamage(damage);
                player.SendTunneled(new PlayerUpdatePacketHitPointModification
                {
                    Guid = player.Guid,
                    Guid2 = npc.Guid,
                    Unknown = true,
                    Unknown2 = npc.MaxHealth,
                    Unknown3 = npc.Health,
                    Unknown4 = -damage,
                });
            }
        }

        SendSlot(player, resources); // slot 3 cleared
        return true;
    }

    /// <summary>"!puspawn": drop the four pickup models in a ring around the player (any zone) with
    /// real walk-over collection, so the whole drop→pickup→"3" flow is testable outside the arena.
    /// Pickups poof after 2 minutes if not collected.</summary>
    public static void SpawnPickups(BaseZone zone, Player player, IResourceManager resources)
    {
        var pickups = new List<(Npc Npc, string Kind)>();
        var kinds = new[] { ("energy", EnergyModelId), ("flame", FlameModelId), ("quake", QuakeModelId), ("shield", ShieldModelId) };

        for (var i = 0; i < kinds.Length; i++)
        {
            if (!zone.TryCreateNpc(out var pu))
                continue;

            var angle = MathF.PI * 2f * i / kinds.Length;
            var pos = new Vector4(
                player.Position.X + MathF.Sin(angle) * 5f,
                player.Position.Y,
                player.Position.Z + MathF.Cos(angle) * 5f,
                1f);

            // Same actor recipe as the arena drops (capture-matched: neutral, no plate, walk-over).
            pu.ModelId = kinds[i].Item2;
            pu.Name = null;
            pu.NameId = PowerupNameId;
            pu.Disposition = 1;
            pu.Scale = 1f;
            pu.IsInteractable = false;
            pu.InteractRange = 0;
            pu.Visible = true;
            pu.MaxHealth = 0;
            pu.ShowHealthBar = false;
            pu.HideNamePlate = true;
            pu.ActiveProfile = 8;
            pu.WalkAnimId = -1;
            pu.RunAnimId = -1;
            pu.StandAnimId = -1;
            pu.MovementType = 2; // PHYSICS — the live pickup shape
            pu.RiderGuid = ulong.MaxValue;
            pu.UpdatePosition(pos, Quaternion.Identity);

            player.OnAddVisibleNpcs(pu);
            pu.OnAddVisiblePlayers(player);
            pickups.Add((pu, kinds[i].Item1));
        }

        _ = Task.Run(async () =>
        {
            try
            {
                for (var elapsed = 0; elapsed < 120_000 && pickups.Count > 0; elapsed += 250)
                {
                    await Task.Delay(250);
                    for (var i = pickups.Count - 1; i >= 0; i--)
                    {
                        var (pu, kind) = pickups[i];
                        var dx = player.Position.X - pu.Position.X;
                        var dz = player.Position.Z - pu.Position.Z;
                        if (dx * dx + dz * dz > PickupRange * PickupRange)
                            continue;

                        // ENERGY at a full bar stays on the ground (same gate as the arena).
                        if (kind == "energy" && CombatBuffs.IsEnergyFull?.Invoke(player) == true)
                            continue;

                        Grant(player, kind, resources);
                        pu.GracefulRemoval = (false, 0, 5000, PickupFxId, 1000);
                        pu.Dispose();
                        pickups.RemoveAt(i);
                    }
                }

                foreach (var (pu, _) in pickups)
                    pu.Dispose(); // uncollected leftovers poof at timeout
            }
            catch { /* player disconnected mid-probe — nothing to clean up */ }
        });
    }

    private static void SendSlot(Player player, IResourceManager resources)
    {
        var slot = _held.TryGetValue(player.Guid, out var kind)
            ? JobWeaponAbilities.MakePowerupSlot(kind switch
            {
                "flame" => FlameWaveIconId,
                "quake" => QuakeIconId,
                _ => ShieldIconId,
            }, PowerupNameId)
            : null;

        player.SendTunneled(JobWeaponAbilities.BuildToolbar(player, resources, slot));
    }
}
