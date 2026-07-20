using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

public static class HeldPowerupProbe
{
    private const int FlameWaveIconId = 26832;
    private const int QuakeIconId = 26835;
    private const int ShieldIconId = 26838;
    private const int PowerupNameId = 5102385;
    private const uint ShieldBuffNameId = 1352849523;
    private const int ShieldBuffMs = 8000;

    private const int EnergyModelId = 737;
    private const int FlameModelId = 1949;
    private const int QuakeModelId = 1950;
    private const int ShieldModelId = 1951;
    private const int PickupFxId = 15032;
    private const float PickupRange = 2.5f;

    private const int FlameWaveDamage = 500;
    private const int QuakeDamage = 350;
    private const float UseRadius = 10f;

    private static readonly ConcurrentDictionary<ulong, string> _held = new();

    private static string? Normalize(string kind) => kind switch
    {
        "flame" or "flamewave" or "fire" => "flame",
        "quake" or "earth" or "earthshard" => "quake",
        "shield" or "super" or "supershield" => "shield",
        _ => null,
    };

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

        if (kind == "shield")
            StatusEffects.ApplyBuffTag(player, ShieldIconId, ShieldBuffNameId, ShieldBuffMs);

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
                    ShowFloatingText = true,
                    Unknown2 = npc.MaxHealth,
                    Unknown3 = npc.Health,
                    Unknown4 = -damage,
                    IsCriticalHit = false, // powerup bursts have no crit roll
                });
            }
        }

        SendSlot(player, resources);
        return true;
    }

    // onlyKind: null = the full ring of four; "flame"/"quake"/"shield"/"energy" = just that pickup
    // dropped right in front of the player (single-powerup FX-tuning loop).
    public static void SpawnPickups(BaseZone zone, Player player, IResourceManager resources, string? onlyKind = null)
    {
        var pickups = new List<(Npc Npc, string Kind)>();
        var kinds = new[] { ("energy", EnergyModelId), ("flame", FlameModelId), ("quake", QuakeModelId), ("shield", ShieldModelId) };
        if (onlyKind is not null)
        {
            var norm = onlyKind == "energy" ? "energy" : Normalize(onlyKind);
            kinds = Array.FindAll(kinds, k => k.Item1 == norm);
        }

        for (var i = 0; i < kinds.Length; i++)
        {
            if (!zone.TryCreateNpc(out var pu))
                continue;

            // Full ring for the 4-pack; a single requested pickup lands just ahead of the player.
            var angle = MathF.PI * 2f * i / kinds.Length;
            var dist = kinds.Length == 1 ? 3f : 5f;
            var pos = new Vector4(
                player.Position.X + MathF.Sin(angle) * dist,
                player.Position.Y,
                player.Position.Z + MathF.Cos(angle) * dist,
                1f);

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
            pu.MovementType = 2;
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

                        if (kind == "energy" && CombatBuffs.IsEnergyFull?.Invoke(player) == true)
                            continue;

                        Grant(player, kind, resources);
                        pu.GracefulRemoval = (false, 0, 5000, PickupFxId, 1000);
                        pu.Dispose();
                        pickups.RemoveAt(i);
                    }
                }

                foreach (var (pu, _) in pickups)
                    pu.Dispose();
            }
            catch {  }
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
