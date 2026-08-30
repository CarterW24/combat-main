using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Combat;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Game;

public class CombatManager : ICombatManager
{
    private const float BasicDamageDelay = 0.15f;
    private const float SpecialDamageDelay = 0.4f;

    private const float SweepArcCosine = 0.819f;
    private const float SweepMaxReach = 15f;
    private const int MaxSweepTargets = 3;

    private const float ImpactProximity = 2f;
    private const float MaxFlightSeconds = 3f;

    private const int MultiHitSpacingMs = 300;
    private const int SummonTickMs = 300;
    private const float SummonLeashRange = 30f;

    private static readonly Vector3[] SummonOffsets =
    [
        new(-2f, 0f, -2f),
        new(2f, 0f, -2f),
        new(0f, 0f, -3f),
        new(-3f, 0f, 1f),
        new(3f, 0f, 1f)
    ];

    private readonly ILogger<CombatManager> _logger;
    private readonly IResourceManager _resourceManager;

    private readonly ConcurrentDictionary<ulong, long> _nextBasicSwingTicks = new();

    private int _nextProjectileId;

    public CombatManager(ILogger<CombatManager> logger, IResourceManager resourceManager)
    {
        _logger = logger;
        _resourceManager = resourceManager;
    }

    #region Toolbar

    public bool SendToolbar(Player player)
    {
        if (!_resourceManager.CombatJobs.TryGetValue(player.ActiveProfileId, out var kit))
            return false;

        var weaponDefinitionId = player.GetEquippedWeaponDefinitionId();
        var (basic, special) = ResolveWeaponAbilities(kit, weaponDefinitionId);

        var weaponNameId = 0;
        if (_resourceManager.ClientItemDefinitions.TryGetValue(weaponDefinitionId, out var weaponDefinition))
            weaponNameId = weaponDefinition.NameId;

        var setDefinition = new AbilityPacketSetDefinition { ProfileId = kit.ProfileId };

        if (basic is not null)
        {
            setDefinition.AbilitySet.Abilities[0] = CreateToolbarSlot(kit.BasicSlotDefId, basic.IconId, weaponNameId, manaCost: 0);
            SendAbilityDefinition(player, kit.BasicSlotDefId, basic);
        }

        if (special is not null)
        {
            setDefinition.AbilitySet.Abilities[1] = CreateToolbarSlot(kit.SpecialSlotDefId, special.IconId, weaponNameId, special.EnergyCost);
            SendAbilityDefinition(player, kit.SpecialSlotDefId, special);
        }

        player.SendTunneled(setDefinition);

        player.SetEnergy(Math.Min(player.Energy, kit.Energy.Max), kit.Energy.Max);

        PreloadAbilityEffects(player, basic, special);

        return true;
    }

    public bool TrySendAbilityDefinition(Player player, int abilityDefinitionId)
    {
        if (!_resourceManager.CombatJobs.TryGetValue(player.ActiveProfileId, out var kit))
            return false;

        var (basic, special) = ResolveWeaponAbilities(kit, player.GetEquippedWeaponDefinitionId());

        if (abilityDefinitionId == kit.BasicSlotDefId && basic is not null)
            SendAbilityDefinition(player, abilityDefinitionId, basic);
        else if (abilityDefinitionId == kit.SpecialSlotDefId && special is not null)
            SendAbilityDefinition(player, abilityDefinitionId, special);
        else
            return false;

        return true;
    }

    private void SendAbilityDefinition(Player player, int abilityDefinitionId, AbilityDefinition ability)
    {
        player.SendTunneled(new AbilityPacketAbilityDefinition
        {
            AbilityId = abilityDefinitionId,
            NameId = ability.NameId,
            DescriptionId = ability.DescriptionId,
            IconId = ability.IconId,
            ManaCost = ability.EnergyCost
        });
    }

    private static Ability CreateToolbarSlot(int abilityDefinitionId, int iconId, int nameId, int manaCost) => new()
    {
        Type = 3,
        Unknown2 = abilityDefinitionId,
        ManaCost = manaCost,
        IconId = iconId,
        NameId = nameId,
        Unknown7 = 4,
        Unknown9 = 1,
        AbilityDefinitionId = abilityDefinitionId,
        Unknown12 = true
    };

    private static void PreloadAbilityEffects(Player player, params AbilityDefinition?[] abilities)
    {
        var effectIds = new HashSet<int>();

        foreach (var ability in abilities)
        {
            if (ability is null)
                continue;

            effectIds.Add(ability.HitEffectId);
            effectIds.Add(ability.CastEffectId);
            effectIds.Add(ability.CasterEndEffectId);
            effectIds.Add(ability.EnemyExtraEffectId);

            if (ability.Summon is not null)
            {
                effectIds.Add(ability.Summon.SpawnEffectId);
                effectIds.Add(ability.Summon.HitEffectId);
            }
        }

        var warmPosition = new Vector4(player.Position.X, player.Position.Y - 400f, player.Position.Z, 1f);

        foreach (var effectId in effectIds)
        {
            if (effectId <= 0)
                continue;

            player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = 0,
                CompositeEffectId = effectId,
                LifetimeMs = 1500,
                Position = warmPosition
            });
        }
    }

    #endregion

    #region Ability execution

    public bool TryExecuteAbility(Player player, AbilityPacketClientRequestStartAbility request)
    {
        if (request.Data.Id != 1)
            return false;

        if (!_resourceManager.CombatJobs.TryGetValue(player.ActiveProfileId, out var kit))
            return false;

        var weaponDefinitionId = player.GetEquippedWeaponDefinitionId();
        var (basic, special) = ResolveWeaponAbilities(kit, weaponDefinitionId);

        var ability = request.Data.Slot switch
        {
            <= 0 => basic,
            1 => special,
            _ => null
        };

        if (ability is null)
            return false;

        var now = Environment.TickCount64;
        if (_nextBasicSwingTicks.TryGetValue(player.Guid, out var nextSwing) && now < nextSwing)
            return true;
        _nextBasicSwingTicks[player.Guid] = now + kit.BasicRecastMs;

        var recastMs = kit.BasicRecastMs;

        if (ability.EnergyCost > 0)
        {
            if (player.Energy < ability.EnergyCost)
                return false;

            player.SetEnergy(player.Energy - ability.EnergyCost, kit.Energy.Max);

            var energyShortfall = ability.EnergyCost - player.Energy;
            if (energyShortfall > 0)
                recastMs = energyShortfall * 1000 / kit.Energy.RegenPerSecond;
        }

        var reach = request.Data.Slot <= 0 && kit.BasicAutoTargetReach > 0f ? kit.BasicAutoTargetReach : kit.AutoTargetReach;

        var primaryTarget = ResolvePrimaryTarget(player, request.Guid, reach);
        var targetGuid = primaryTarget?.Guid ?? (request.Guid != 0 ? request.Guid : player.Guid);

        player.SendTunneledToVisible(new AbilityPacketStartCasting
        {
            CasterGuid = player.Guid,
            TargetGuid = targetGuid,
            AbilityId = request.Data.Slot <= 0 ? kit.BasicSlotDefId : kit.SpecialSlotDefId
        }, sendToSelf: true);

        var targets = ResolveEffectTargets(player, ability, primaryTarget, reach);
        var clientVictimBeat = ability.Projectile is null;

        player.SendTunneledToVisible(new AbilityPacketLaunchAndLand
        {
            Guid = player.Guid,
            Targets = [.. targets.Select(target => Target.CreateCharacterGuid((long)target.Guid))],
            CasterAnimationId = ability.AnimationId,
            CasterEffectId = ability.CastEffectId,
            RecastMs = recastMs,
            TargetAnimationId = clientVictimBeat ? ability.TargetAnimationId : 0,
            TargetEffectId = clientVictimBeat ? ability.HitEffectId : 0,
            TargetEffectDuration = clientVictimBeat ? ability.TargetEffectDurationMs / 1000f : 0f,
            ScheduleMeleeContact = clientVictimBeat ? ResolveContactEffectId(ability, weaponDefinitionId) : 0,
            ActionBarId = request.Data.Id,
            ActionBarSlot = Math.Max(request.Data.Slot, 0)
        }, sendToSelf: true);

        if (ability.WeaponEffectId > 0)
            ApplyWeaponEffect(player, ability.WeaponEffectId, ability.WeaponEffectDurationMs);

        if (ability.Summon is not null)
            SummonAllies(player, ability.Summon);

        if (ability.Heal is { Amount: > 0 } partyHeal && !string.Equals(partyHeal.Scope, "Self", StringComparison.OrdinalIgnoreCase))
            HealParty(player, partyHeal);

        if (targets.Count == 0)
        {
            if (ability.Projectile is not null)
                FireProjectileForward(player, ability.Projectile);

            return true;
        }

        player.EnterWorldCombat();

        var damage = DamageFor(player, ability);
        var hitCount = Math.Max(1, ability.HitCount);
        var casterEndEffectId = ability.CasterEndEffectId;
        foreach (var target in targets)
        {
            var damageDelay = request.Data.Slot <= 0 ? BasicDamageDelay : SpecialDamageDelay;

            if (ability.Projectile is not null)
            {
                FireProjectileAt(player, target, ability.Projectile);

                damageDelay = MathF.Max(damageDelay, FlightTimeTo(player, target, ability.Projectile));
            }

            for (var hit = 0; hit < hitCount; hit++)
            {
                ResolveDamageAfterDelay(player, target, kit, ability, damage, damageDelay + hit * MultiHitSpacingMs / 1000f, casterEndEffectId, !clientVictimBeat, hit == hitCount - 1);
                casterEndEffectId = 0;
            }
        }

        return true;
    }

    private static int DamageFor(Player player, AbilityDefinition ability)
    {
        if (ability.DamageByLevel is null || ability.DamageByLevel.Count == 0)
            return ability.Damage;

        var rank = player.ActiveProfile.Rank;
        var bestLevel = -1;
        var damage = ability.Damage;

        foreach (var (level, value) in ability.DamageByLevel)
        {
            if (level <= rank && level > bestLevel)
            {
                bestLevel = level;
                damage = value;
            }
        }

        return damage;
    }

    private static void HealParty(Player player, AbilityHealDefinition heal)
    {
        var radiusSquared = heal.Radius * heal.Radius;

        foreach (var other in player.Zone.Players)
        {
            var dx = other.Position.X - player.Position.X;
            var dz = other.Position.Z - player.Position.Z;

            if (dx * dx + dz * dz <= radiusSquared)
                other.Heal(heal.Amount, player.Guid);
        }
    }

    private void ApplyWeaponEffect(Player player, int compositeEffectId, int durationMs)
    {
        const int PrimaryWeaponSlot = 7;

        player.SendTunneledToVisible(new PlayerUpdatePacketSlotCompositeEffectOverride
        {
            Guid = player.Guid,
            Slot = PrimaryWeaponSlot,
            CompositeEffect = compositeEffectId
        }, sendToSelf: true);

        if (durationMs <= 0)
            return;

        var zone = player.Zone;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs);

                if (player.Zone != zone)
                    return;

                player.SendTunneledToVisible(new PlayerUpdatePacketSlotCompositeEffectOverride
                {
                    Guid = player.Guid,
                    Slot = PrimaryWeaponSlot,
                    CompositeEffect = 0
                }, sendToSelf: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Weapon effect expiry failed.");
            }
        });
    }

    public int ResolveWieldType(Player player, int itemClassWieldType)
    {
        if (itemClassWieldType != 0)
            return itemClassWieldType;

        return _resourceManager.CombatJobs.TryGetValue(player.ActiveProfileId, out var kit) ? kit.WieldType : 0;
    }

    private int ResolveContactEffectId(AbilityDefinition ability, int weaponDefinitionId)
    {
        if (ability.ContactEffectId > 0)
            return ability.ContactEffectId;

        return _resourceManager.ClientItemDefinitions.TryGetValue(weaponDefinitionId, out var weapon)
            ? weapon.WeaponTrailEffectId
            : 0;
    }

    private (AbilityDefinition? Basic, AbilityDefinition? Special) ResolveWeaponAbilities(JobKitDefinition kit, int weaponDefinitionId)
    {
        var mapping = weaponDefinitionId != 0
            ? kit.Weapons.FirstOrDefault(w => w.WeaponDefIds.Contains(weaponDefinitionId))
            : null;

        var basicId = mapping?.BasicAbilityId ?? kit.FallbackBasicAbilityId;
        var specialId = mapping?.SpecialAbilityId ?? 0;

        return (
            _resourceManager.CombatAbilities.TryGetValue(basicId, out var basic) ? basic : null,
            _resourceManager.CombatAbilities.TryGetValue(specialId, out var special) ? special : null);
    }

    private static Npc? ResolvePrimaryTarget(Player player, ulong requestedGuid, float reach)
    {
        var forward = player.Forward;

        if (requestedGuid != 0 && player.Zone.TryGetNpc(requestedGuid, out var selected)
            && selected.IsDamageable && selected.IsAlive)
        {
            var selectedDx = selected.Position.X - player.Position.X;
            var selectedDz = selected.Position.Z - player.Position.Z;

            if (forward.X * selectedDx + forward.Z * selectedDz > 0f
                && selectedDx * selectedDx + selectedDz * selectedDz <= reach * reach)
            {
                return selected;
            }
        }

        Npc? nearest = null;
        var best = reach * reach;

        foreach (var npc in player.Zone.Npcs)
        {
            if (!npc.IsHostile || !npc.IsDamageable || !npc.IsAlive)
                continue;

            var dx = npc.Position.X - player.Position.X;
            var dz = npc.Position.Z - player.Position.Z;
            var distanceSquared = dx * dx + dz * dz;

            if (distanceSquared >= best || forward.X * dx + forward.Z * dz <= 0f)
                continue;

            best = distanceSquared;
            nearest = npc;
        }

        return nearest;
    }

    private static List<Npc> ResolveEffectTargets(Player player, AbilityDefinition ability, Npc? primaryTarget, float reach)
    {
        if (string.Equals(ability.EffectType, "AoeDamage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ability.EffectType, "AoeDamageHeal", StringComparison.OrdinalIgnoreCase))
        {
            return ability.Damage > 0 || ability.DamageByLevel is not null ? ResolveAoeTargets(player, ability.AoeRadius) : [];
        }

        if (string.Equals(ability.EffectType, "SingleTargetDamage", StringComparison.OrdinalIgnoreCase))
            return primaryTarget is null ? [] : [primaryTarget];

        if (string.Equals(ability.EffectType, "Summon", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ability.EffectType, "Buff", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return primaryTarget is null ? [] : ResolveSweepTargets(player, primaryTarget, reach);
    }

    private static List<Npc> ResolveAoeTargets(Player player, float radius)
    {
        var radiusSquared = radius * radius;
        var center = player.Position;

        return player.Zone.Npcs
            .Where(npc => npc.IsHostile && npc.IsDamageable && npc.IsAlive)
            .Where(npc =>
            {
                var dx = npc.Position.X - center.X;
                var dz = npc.Position.Z - center.Z;
                return dx * dx + dz * dz <= radiusSquared;
            })
            .ToList();
    }

    private static List<Npc> ResolveSweepTargets(Player player, Npc primaryTarget, float reach)
    {
        var targets = new List<Npc>();

        var px = player.Position.X;
        var pz = player.Position.Z;
        var dirX = primaryTarget.Position.X - px;
        var dirZ = primaryTarget.Position.Z - pz;
        var dirLength = MathF.Sqrt(dirX * dirX + dirZ * dirZ);

        if (dirLength < 0.01f)
        {
            targets.Add(primaryTarget);
            return targets;
        }

        dirX /= dirLength;
        dirZ /= dirLength;

        var arcReach = MathF.Min(reach, SweepMaxReach);
        var arcReachSquared = arcReach * arcReach;

        foreach (var npc in player.Zone.Npcs)
        {
            if (!npc.IsHostile || !npc.IsDamageable || !npc.IsAlive)
                continue;

            var dx = npc.Position.X - px;
            var dz = npc.Position.Z - pz;
            var distanceSquared = dx * dx + dz * dz;

            if (distanceSquared > arcReachSquared)
                continue;

            if (distanceSquared < 0.0001f)
            {
                targets.Add(npc);
                continue;
            }

            var inverseDistance = 1f / MathF.Sqrt(distanceSquared);
            var dot = dirX * dx * inverseDistance + dirZ * dz * inverseDistance;

            if (dot >= SweepArcCosine)
                targets.Add(npc);
        }

        if (targets.Count > MaxSweepTargets)
        {
            targets = [.. targets
                .OrderByDescending(npc =>
                {
                    var tx = npc.Position.X - px;
                    var tz = npc.Position.Z - pz;
                    var length = MathF.Sqrt(tx * tx + tz * tz);
                    return length < 0.0001f ? 1f : (dirX * tx + dirZ * tz) / length;
                })
                .Take(MaxSweepTargets)];
        }

        return targets;
    }

    private void ResolveDamageAfterDelay(Player player, Npc target, JobKitDefinition kit, AbilityDefinition ability,
        int baseDamage, float delaySeconds, int casterEndEffectId, bool sendHitEffect, bool lastHit)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay((int)(delaySeconds * 1000));

                player.EnterWorldCombat();

                if (casterEndEffectId > 0)
                {
                    player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = player.Guid,
                        CompositeEffectId = casterEndEffectId,
                        LifetimeMs = 2000,
                        Position = Vector4.Zero
                    }, sendToSelf: true);
                }

                if (!target.IsAlive)
                    return;

                if (baseDamage <= 0)
                    return;

                var damage = RollDamage(player, kit.Traits, baseDamage, out var isCriticalHit);

                if (sendHitEffect && ability.HitEffectId > 0)
                {
                    player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = target.Guid,
                        CompositeEffectId = ability.HitEffectId,
                        Position = Vector4.Zero
                    }, sendToSelf: true);
                }

                if (ability.EnemyExtraEffectId > 0)
                {
                    player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = target.Guid,
                        CompositeEffectId = ability.EnemyExtraEffectId,
                        Position = Vector4.Zero
                    }, sendToSelf: true);
                }

                ApplyHit(player, player.Guid, target, damage, isCriticalHit, ability.Id);

                if (ability.Heal is { PercentOfDamage: > 0 } steal && string.Equals(steal.Scope, "Self", StringComparison.OrdinalIgnoreCase))
                    player.Heal(damage * steal.PercentOfDamage / 100, player.Guid);

                if (ability.EnergySteal is { Amount: > 0 } energySteal)
                    player.SetEnergy(Math.Min(kit.Energy.Max, player.Energy + energySteal.Amount), kit.Energy.Max);

                if (lastHit && ability.Dot is { TickDamage: > 0, TickMs: > 0 } dot)
                    StartDot(player, target, ability, dot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ability damage resolution failed.");
            }
        });
    }

    private void StartDot(Player player, Npc target, AbilityDefinition ability, AbilityDotDefinition dot)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var elapsed = dot.TickMs; elapsed <= dot.DurationMs; elapsed += dot.TickMs)
                {
                    await Task.Delay(dot.TickMs);

                    if (!target.IsAlive || player.Zone != target.Zone)
                        return;

                    if (ability.HitEffectId > 0)
                    {
                        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = target.Guid,
                            CompositeEffectId = ability.HitEffectId,
                            Position = Vector4.Zero
                        }, sendToSelf: true);
                    }

                    ApplyHit(player, player.Guid, target, dot.TickDamage, false, ability.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Damage-over-time resolution failed.");
            }
        });
    }

    private void ApplyHit(Player player, ulong sourceGuid, Npc target, int damage, bool isCriticalHit, int abilityId)
    {
        var killed = target.ApplyDamage(damage);

        player.SendTunneledToVisible(new PlayerUpdatePacketHitPointModification
        {
            Guid = sourceGuid,
            Guid2 = target.Guid,
            ShowFloatingText = true,
            Unknown2 = target.MaxHealth,
            Unknown3 = target.Health,
            Unknown4 = -damage,
            IsCriticalHit = isCriticalHit
        }, sendToSelf: true);

        _logger.LogInformation("Ability {ability} hit {name} ({guid}) for {damage} -> {health}/{maxHealth} (crit={crit}, killed={killed})",
            abilityId, target.Name, target.Guid, damage, target.Health, target.MaxHealth, isCriticalHit, killed);

        if (killed && target.RestoreOnDeath)
        {
            target.Health = target.MaxHealth;

            foreach (var other in player.Zone.Players)
                player.Zone.SendNpcHealth(other, target);
        }
    }

    private void SummonAllies(Player player, AbilitySummonDefinition summon)
    {
        for (var i = 0; i < Math.Max(1, summon.Count); i++)
        {
            if (!player.Zone.TryCreateNpc(null, out var npc))
                return;

            var offset = SummonOffsets[i % SummonOffsets.Length];
            var position = new Vector4(player.Position.X + offset.X, player.Position.Y, player.Position.Z + offset.Z, 1f);

            npc.ModelId = summon.ModelId;
            npc.Name = summon.Name;
            npc.WieldType = summon.WieldType;
            npc.Scale = 1f;
            npc.IsInteractable = false;
            npc.CursorId = 0;
            npc.Speed = summon.MoveSpeed;
            npc.Visible = true;
            npc.UpdatePosition(position, player.Rotation);

            PlaySummonEffect(player, npc, summon.SpawnEffectId, position);

            _ = RunSummonAsync(player, npc, summon);
        }
    }

    private async Task RunSummonAsync(Player player, Npc npc, AbilitySummonDefinition summon)
    {
        var zone = npc.Zone;
        var expires = Environment.TickCount64 + summon.LifetimeMs;
        var nextAttack = 0L;
        var moving = false;

        try
        {
            while (Environment.TickCount64 < expires && player.Zone == zone)
            {
                await Task.Delay(SummonTickMs);

                var target = NearestHostile(zone, npc.Position, SummonLeashRange);

                if (target is null)
                {
                    if (moving)
                    {
                        npc.MoveTo(new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z), true);
                        moving = false;
                    }

                    continue;
                }

                var dx = target.Position.X - npc.Position.X;
                var dz = target.Position.Z - npc.Position.Z;

                if (dx * dx + dz * dz > summon.AttackRange * summon.AttackRange)
                {
                    npc.MoveTo(new Vector3(target.Position.X, target.Position.Y, target.Position.Z), true);

                    if (!moving)
                    {
                        SendAnimation(player, npc.Guid, summon.RunAnimationId);
                        moving = true;
                    }

                    continue;
                }

                if (moving)
                {
                    npc.MoveTo(new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z), true);
                    moving = false;
                }

                var now = Environment.TickCount64;
                if (now < nextAttack)
                    continue;

                nextAttack = now + summon.AttackCooldownMs;

                SendAnimation(player, npc.Guid, summon.AttackAnimationId);

                if (summon.HitEffectId > 0)
                {
                    player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = target.Guid,
                        CompositeEffectId = summon.HitEffectId,
                        Position = Vector4.Zero
                    }, sendToSelf: true);
                }

                if (summon.AttackDamage > 0)
                {
                    player.EnterWorldCombat();
                    ApplyHit(player, player.Guid, target, summon.AttackDamage, false, 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Summon behaviour failed.");
        }
        finally
        {
            PlaySummonEffect(player, npc, summon.SpawnEffectId, npc.Position);
            npc.Dispose();
        }
    }

    private static Npc? NearestHostile(IZone zone, Vector4 origin, float range)
    {
        Npc? nearest = null;
        var best = range * range;

        foreach (var npc in zone.Npcs)
        {
            if (!npc.IsHostile || !npc.IsDamageable || !npc.IsAlive)
                continue;

            var dx = npc.Position.X - origin.X;
            var dz = npc.Position.Z - origin.Z;
            var distanceSquared = dx * dx + dz * dz;

            if (distanceSquared >= best)
                continue;

            best = distanceSquared;
            nearest = npc;
        }

        return nearest;
    }

    private static void SendAnimation(Player player, ulong guid, int animationId)
    {
        if (animationId <= 0)
            return;

        player.SendTunneledToVisible(new PlayerUpdatePacketSetAnimation
        {
            Guid = guid,
            AnimationId = animationId,
            PlayType = 0
        }, sendToSelf: true);
    }

    private static void PlaySummonEffect(Player player, Npc npc, int effectId, Vector4 position)
    {
        if (effectId <= 0)
            return;

        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = npc.Guid,
            CompositeEffectId = effectId,
            LifetimeMs = 2000,
            Position = position
        }, sendToSelf: true);
    }

    private static int RollDamage(Player player, JobKitTraitDefinition traits, int baseDamage, out bool isCriticalHit)
    {
        isCriticalHit = false;

        var rank = player.ActiveProfile.Rank;
        var damage = (float)baseDamage;

        if (rank >= traits.PrecisionLevel)
            damage *= 1f + traits.PrecisionDamageBonus;

        var critChance = traits.BaseCritChancePercent;

        if (rank >= traits.PrecisionLevel)
            critChance += traits.PrecisionCritChanceBonus;

        if (critChance > 0 && Random.Shared.Next(100) < critChance)
        {
            var critMultiplier = traits.BaseCritMultiplier;

            if (rank >= traits.MarksmanshipLevel)
                critMultiplier += traits.MarksmanshipCritBonus;

            damage *= critMultiplier;
            isCriticalHit = true;
        }

        return Math.Max(1, (int)damage);
    }

    #endregion

    #region Projectiles

    private float FlightTimeTo(Player player, Npc target, AbilityProjectileDefinition projectile)
    {
        var dx = target.Position.X - player.Position.X;
        var dy = target.Position.Y + 1f - (player.Position.Y + projectile.MuzzleHeight);
        var dz = target.Position.Z - player.Position.Z;
        var distance = MathF.Max(0f, MathF.Sqrt(dx * dx + dy * dy + dz * dz) - ImpactProximity);

        return MathF.Min(distance / projectile.Speed, MaxFlightSeconds);
    }

    private void FireProjectileAt(Player player, Npc target, AbilityProjectileDefinition projectile)
    {
        var start = new Vector4(
            player.Position.X,
            player.Position.Y + projectile.MuzzleHeight,
            player.Position.Z,
            1f);

        player.SendTunneledToVisible(new PlayerUpdatePacketLaunchProjectile
        {
            ProjectileId = Interlocked.Increment(ref _nextProjectileId),
            Speed = projectile.Speed,
            FlightType = PlayerUpdatePacketLaunchProjectile.FlightTypeBeam,
            Direction = new Vector4(
                target.Position.X - start.X,
                target.Position.Y + 1f - start.Y,
                target.Position.Z - start.Z,
                0f),
            StartPosition = start,
            ModelFileName = projectile.ModelFileName,
            Source = Target.CreateCharacterGuid((long)player.Guid),
            Destination = Target.CreateCharacterGuid((long)target.Guid),
            SpinAxis = new Vector4(0f, 0f, 1f, 0f),
            TrailCompositeEffectId = projectile.TrailEffectId
        }, sendToSelf: true);
    }

    private void FireProjectileForward(Player player, AbilityProjectileDefinition projectile)
    {
        var forward = player.Forward;

        player.SendTunneledToVisible(new PlayerUpdatePacketLaunchProjectile
        {
            ProjectileId = Interlocked.Increment(ref _nextProjectileId),
            Speed = projectile.Speed,
            FlightType = PlayerUpdatePacketLaunchProjectile.FlightTypeBeam,
            FireForward = 1,
            Direction = new Vector4(forward.X, 0f, forward.Z, 0f),
            StartPosition = new Vector4(
                player.Position.X,
                player.Position.Y + projectile.MuzzleHeight,
                player.Position.Z,
                1f),
            ModelFileName = projectile.ModelFileName,
            Source = Target.CreateCharacterGuid((long)player.Guid),
            SpinAxis = new Vector4(0f, 0f, 1f, 0f),
            TrailCompositeEffectId = projectile.TrailEffectId
        }, sendToSelf: true);
    }

    #endregion
}
