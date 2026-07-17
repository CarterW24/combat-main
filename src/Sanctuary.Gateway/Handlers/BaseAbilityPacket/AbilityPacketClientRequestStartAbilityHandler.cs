using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class AbilityPacketClientRequestStartAbilityHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, DateTimeOffset>> _itemCooldowns = new();

    private const int BoomboxIdleAnimId = 1;

    private const int BoomboxDurationMs = 120_000;

    private const int FoodEffectCooldownMs = 120_000;

    private const float MeleeActionTime = 0.15f;
    private const float SpecialActionTime = 0.4f;
    private const float MeleeDamageDelay = 0.15f;
    private const float SpecialDamageDelay = 0.4f;

    private const int BasicSwingMs = 660;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, long> _nextBasicSwingTicks = new();

    private const int MaxEnergy = 100;
    private const int SpecialEnergyCost = NinjaWeaponAbilities.SpecialEnergyCost;
    private const int EnergyRegenPerSec = 4;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, int> _energy = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, bool> _regenRunning = new();

    private static int GetEnergy(Player player) => _energy.TryGetValue(player.Guid, out var e) ? e : MaxEnergy;

    private static void SendEnergy(Player player, int energy) =>
        player.SendTunneled(new ClientUpdatePacketMana { CurrentMana = energy, MaxMana = MaxEnergy });

    private static void StartEnergyRegen(Player player)
    {
        if (!_regenRunning.TryAdd(player.Guid, true))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (GetEnergy(player) < MaxEnergy)
                {
                    await Task.Delay(1000);
                    var next = Math.Min(MaxEnergy, GetEnergy(player) + EnergyRegenPerSec);
                    _energy[player.Guid] = next;
                    SendEnergy(player, next);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Energy regen loop failed.");
            }
            finally
            {
                _regenRunning.TryRemove(player.Guid, out _);
            }
        });
    }

    private static int ApplyArcherTraitDamage(Player player, int baseDamage)
    {
        var dmg = (float)baseDamage;

        if (ArcherWeaponAbilities.HasTrait(player, ArcherWeaponAbilities.PrecisionLevel))
            dmg *= 1f + ArcherWeaponAbilities.PrecisionDamageBonus;

        var critChance = 0;
        if (ArcherWeaponAbilities.HasTrait(player, ArcherWeaponAbilities.PrecisionLevel))
            critChance = ArcherWeaponAbilities.BaseCritChancePercent + ArcherWeaponAbilities.PrecisionCritChanceBonus;

        if (critChance > 0 && Random.Shared.Next(100) < critChance)
        {
            var critMult = ArcherWeaponAbilities.BaseCritMultiplier;
            if (ArcherWeaponAbilities.HasTrait(player, ArcherWeaponAbilities.MarksmanshipLevel))
                critMult += ArcherWeaponAbilities.MarksmanshipCritBonus;
            dmg *= critMult;
        }

        return Math.Max(1, (int)dmg);
    }

    private static void TryLuckyShotEnergy(Player player)
    {
        if (!ArcherWeaponAbilities.HasTrait(player, ArcherWeaponAbilities.LuckyShotLevel))
            return;
        if (Random.Shared.Next(100) >= ArcherWeaponAbilities.LuckyShotChancePercent)
            return;

        var energy = GetEnergy(player);
        if (energy >= MaxEnergy)
            return;

        var next = Math.Min(MaxEnergy, energy + ArcherWeaponAbilities.LuckyShotEnergyRestore);
        _energy[player.Guid] = next;
        SendEnergy(player, next);
    }

    private static int _castFxTagCounter = 5000;

    public static int? DebugAnimationOverride;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(AbilityPacketClientRequestStartAbilityHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        CombatBuffs.EnergyRefillRequested += player =>
        {
            _energy[player.Guid] = MaxEnergy;
            SendEnergy(player, MaxEnergy);
        };
        CombatBuffs.IsEnergyFull = player => GetEnergy(player) >= MaxEnergy;
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!AbilityPacketClientRequestStartAbility.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}. ( Raw: {raw} )",
                nameof(AbilityPacketClientRequestStartAbility), Convert.ToHexString(data));
            return false;
        }

        _logger.LogInformation("AbilityPacket: Id={Id} Slot={Slot}", packet.Data.Id, packet.Data.Slot);

        if (connection.Player.IsDead)
            return true;

        if (StatusEffects.BlocksAbilities(connection.Player.Guid))
            return true;
        if (StatusEffects.IsSilenced(connection.Player.Guid) && packet.Data.Id != 2)
            return true;

        if (packet.Data.Id == 2)
            return HandleItemAbility(connection, packet);

        return HandleCombatAbility(connection, packet, data);
    }

    private static bool HandleItemAbility(GatewayConnection connection, AbilityPacketClientRequestStartAbility packet)
    {
        connection.Player.ActionBars.TryGetValue(2, out var actionBar);

        if (actionBar is null || !actionBar.Slots.TryGetValue(packet.Data.Slot, out var slot) || slot.IsEmpty)
            return SendFailure(connection);

        if (!connection.Player.ActionBarItemGuids.TryGetValue(2, out var slotItemGuids) ||
            !slotItemGuids.TryGetValue(packet.Data.Slot, out var itemGuid))
            return SendFailure(connection);

        var clientItem = connection.Player.Items.FirstOrDefault(x => x.Id == itemGuid);

        if (clientItem is null)
            return SendFailure(connection);

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(clientItem.Definition, out var itemDefinition) ||
            itemDefinition.ActivatableAbilityId == 0)
            return SendFailure(connection);

        if (_resourceManager.Consumables.Boomboxes.ContainsKey(itemDefinition.Id))
            return HandleBoombox(connection, packet.Data.Slot, clientItem, itemDefinition);

        if (_resourceManager.Consumables.Cakes.TryGetValue(itemDefinition.Id, out var cakeDefinition))
            return HandleCake(connection, packet.Data.Slot, clientItem, itemDefinition, cakeDefinition);

        var transformAbilityId = itemDefinition.ActivatableAbilityId;

        if (_resourceManager.Consumables.RandomTransformFoods.TryGetValue(itemDefinition.Id, out var randomFood) && randomFood.TransformAbilityIds.Length > 0)
            transformAbilityId = randomFood.TransformAbilityIds[Random.Shared.Next(randomFood.TransformAbilityIds.Length)];

        if (_resourceManager.Consumables.Transformations.TryGetValue(transformAbilityId, out var transform))
            return HandleTransformFood(connection, packet.Data.Slot, clientItem, itemDefinition, transform);

        if (_resourceManager.Consumables.FoodEffects.ContainsKey(itemDefinition.ActivatableAbilityId))
            return HandleFoodEffect(connection, packet.Data.Slot, clientItem, itemDefinition);

        TriggerAbilityEffect(connection, itemDefinition);

        if (itemDefinition.SingleUse)
            return ConsumeItem(connection, clientItem, itemDefinition, packet.Data.Slot);

        return true;
    }

    private static bool HandleBoombox(GatewayConnection connection, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition)
    {
        if (IsOnCooldown(connection.Player.Guid, itemDefinition.Id))
            return SendFailure(connection);

        SpawnBoomboxNpc(connection, itemDefinition);

        StartCooldown(connection.Player.Guid, itemDefinition.Id, BoomboxDurationMs);
        connection.Player.StartActionBarCooldown(2, slot, itemDefinition.Icon.Id, itemDefinition.NameId, clientItem.Count, BoomboxDurationMs);

        return true;
    }

    private static bool HandleCake(GatewayConnection connection, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition, CakeItemDefinition cakeDefinition)
    {
        if (IsOnCooldown(connection.Player.Guid, itemDefinition.Id))
            return SendFailure(connection);

        SpawnCakeNpc(connection, cakeDefinition);

        StartCooldown(connection.Player.Guid, itemDefinition.Id, cakeDefinition.CooldownMs);
        connection.Player.StartActionBarCooldown(2, slot, itemDefinition.Icon.Id, itemDefinition.NameId, clientItem.Count, cakeDefinition.CooldownMs);

        return true;
    }

    private static bool HandleTransformFood(GatewayConnection connection, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition, TransformAbilityDefinition transform)
    {
        if (IsOnCooldown(connection.Player.Guid, itemDefinition.Id))
            return SendFailure(connection);

        if (connection.Player.TemporaryAppearance != 0)
            return SendFailure(connection);

        connection.Player.ApplyTemporaryAppearance(transform.ModelId, transform.DurationMs, transform.CompositeEffectId);

        StartCooldown(connection.Player.Guid, itemDefinition.Id, transform.CooldownMs);

        var count = clientItem.Count;

        if (itemDefinition.SingleUse)
            ConsumeItem(connection, clientItem, itemDefinition, slot);

        if (count > 1)
            connection.Player.StartActionBarCooldown(2, slot, itemDefinition.Icon.Id, itemDefinition.NameId, count - 1, transform.CooldownMs);

        return true;
    }

    private static bool HandleFoodEffect(GatewayConnection connection, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition)
    {
        if (IsOnCooldown(connection.Player.Guid, itemDefinition.Id))
            return SendFailure(connection);

        StartCooldown(connection.Player.Guid, itemDefinition.Id, FoodEffectCooldownMs);

        TriggerAbilityEffect(connection, itemDefinition);

        var count = clientItem.Count;
        var hasItemLeft = !itemDefinition.SingleUse || count > 1;

        if (itemDefinition.SingleUse)
            ConsumeItem(connection, clientItem, itemDefinition, slot);

        if (hasItemLeft)
            connection.Player.StartActionBarCooldown(2, slot, itemDefinition.Icon.Id, itemDefinition.NameId,
                itemDefinition.SingleUse ? count - 1 : count, FoodEffectCooldownMs);

        return true;
    }

    private static bool IsOnCooldown(ulong playerGuid, int itemDefinitionId)
    {
        return _itemCooldowns.TryGetValue(playerGuid, out var cooldowns) &&
               cooldowns.TryGetValue(itemDefinitionId, out var expiry) &&
               DateTimeOffset.UtcNow < expiry;
    }

    private static void StartCooldown(ulong playerGuid, int itemDefinitionId, int cooldownMs)
    {
        var cooldowns = _itemCooldowns.GetOrAdd(playerGuid, _ => new ConcurrentDictionary<int, DateTimeOffset>());

        cooldowns[itemDefinitionId] = DateTimeOffset.UtcNow.AddMilliseconds(cooldownMs);
    }

    private static bool ConsumeItem(GatewayConnection connection, ClientItem clientItem, ClientItemDefinition clientItemDefinition, int actionBarSlot)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var dbItem = dbContext.Items.SingleOrDefault(i => i.CharacterId == characterId && i.Id == clientItem.Id);

        if (dbItem is null)
            return SendFailure(connection);

        dbItem.Count--;

        var shouldDeleteItem = dbItem.Count <= 0;

        if (shouldDeleteItem)
            dbContext.Items.Remove(dbItem);

        if (dbContext.SaveChanges() <= 0)
            return SendFailure(connection);

        if (shouldDeleteItem)
        {
            connection.Player.Items.Remove(clientItem);
            connection.SendTunneled(new ClientUpdatePacketItemDelete { ItemGuid = clientItem.Id });

            var slotPacket = new ClientUpdatePacketUpdateActionBarSlot { Data = { Id = 2, Slot = actionBarSlot } };
            slotPacket.Slot.IsEmpty = true;

            if (connection.Player.ActionBarItemGuids.TryGetValue(2, out var trackedItems))
                trackedItems.Remove(actionBarSlot);

            connection.SendTunneled(slotPacket);
        }
        else
        {
            clientItem.Count--;

            connection.SendTunneled(new ClientUpdatePacketItemUpdate
            {
                ItemGuid = clientItem.Id,
                Count = clientItem.Count,
                ConsumedCount = clientItem.ConsumedCount,
                AbilityCount = clientItem.AbilityCount,
                RentalExpirationTime = 0
            });

            var slotPacket = new ClientUpdatePacketUpdateActionBarSlot { Data = { Id = 2, Slot = actionBarSlot } };
            slotPacket.Slot.IsEmpty = false;
            slotPacket.Slot.IconId = clientItemDefinition.Icon.Id;
            slotPacket.Slot.NameId = clientItemDefinition.NameId;
            slotPacket.Slot.Unknown5 = 1;
            slotPacket.Slot.Unknown6 = 4;
            slotPacket.Slot.Unknown7 = 15;
            slotPacket.Slot.Enabled = true;
            slotPacket.Slot.Unknown10 = 1000;
            slotPacket.Slot.TotalRefreshTime = 1000;
            slotPacket.Slot.Quantity = clientItem.Count;
            slotPacket.Slot.ForceDismount = true;
            slotPacket.Slot.Unknown15 = 1000;

            connection.SendTunneled(slotPacket);
        }

        return true;
    }

    private static void TriggerAbilityEffect(GatewayConnection connection, ClientItemDefinition clientItemDefinition)
    {
        _resourceManager.Consumables.FoodEffects.TryGetValue(clientItemDefinition.ActivatableAbilityId, out var foodEffect);

        var effectId = foodEffect?.CompositeEffectId ?? clientItemDefinition.CompositeEffectId;
        var quickChatId = foodEffect?.QuickChatId ?? 0;
        var effectDelayMs = foodEffect?.EffectDelayMs ?? 0;

        if (quickChatId != 0)
        {
            connection.Player.SendTunneledToVisible(new QuickChatSendChatToChannelPacket
            {
                Id = quickChatId,
                Guid = connection.Player.Guid,
                Name = connection.Player.Name ?? new NameData(),
                Channel = ChatChannel.WorldArea,
                AreaNameId = 0,
                GuildGuid = 0
            }, true);
        }

        if (effectId != 0)
        {
            var effectPacket = new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = connection.Player.Guid,
                CompositeEffectId = effectId,
                Clear = true
            };

            if (effectDelayMs > 0)
                connection.Player.SendTunneledToVisibleDelayed(effectPacket, effectDelayMs, true);
            else
                connection.Player.SendTunneledToVisible(effectPacket, true);
        }
    }

    private static void SpawnCakeNpc(GatewayConnection connection, CakeItemDefinition cakeDefinition)
    {
        if (connection.Player.Zone is not StartingZone startingZone)
            return;

        if (!startingZone.TryCreateNpc(out var cakeNpc))
            return;

        cakeNpc.NameId = cakeDefinition.NameId;
        cakeNpc.ModelId = cakeDefinition.ModelId;
        cakeNpc.TextureAlias = "";
        cakeNpc.TintAlias = "";
        cakeNpc.Scale = 1.0f;
        cakeNpc.Animation = cakeDefinition.Animation;
        cakeNpc.HideNamePlate = false;
        cakeNpc.IsInteractable = true;
        cakeNpc.CursorId = (byte)cakeDefinition.CursorId;

        var forwardDirection = Vector3.Transform(new Vector3(0, 0, 1), connection.Player.Rotation);
        var spawnPosition = new Vector4(
            connection.Player.Position.X + forwardDirection.X * 1.5f,
            connection.Player.Position.Y + forwardDirection.Y * 1.5f,
            connection.Player.Position.Z + forwardDirection.Z * 1.5f,
            connection.Player.Position.W
        );

        cakeNpc.Visible = true;
        cakeNpc.UpdatePosition(spawnPosition, connection.Player.Rotation);

        if (cakeDefinition.Type == CakeItemType.BossCake)
        {
            cakeNpc.InteractAction = player =>
            {
                var abilityId = cakeDefinition.TransformAbilityIds[Random.Shared.Next(cakeDefinition.TransformAbilityIds.Length)];

                if (_resourceManager.Consumables.Transformations.TryGetValue(abilityId, out var transform))
                    player.ApplyTemporaryAppearance(transform.ModelId, transform.DurationMs, transform.CompositeEffectId);
            };
        }
        else
        {
            var scareReadyTime = DateTimeOffset.MinValue;

            cakeNpc.InteractAction = player =>
            {
                if (DateTimeOffset.UtcNow < scareReadyTime)
                    return;

                scareReadyTime = DateTimeOffset.UtcNow.AddMilliseconds(cakeDefinition.ScareCooldownMs);

                var roll = Random.Shared.Next(cakeDefinition.ScareGroups.Length + cakeDefinition.TransformAbilityIds.Length);

                if (roll < cakeDefinition.ScareGroups.Length)
                {
                    foreach (var effectId in cakeDefinition.ScareGroups[roll])
                    {
                        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = cakeNpc.Guid,
                            CompositeEffectId = effectId,
                            Position = cakeNpc.Position,
                            Clear = true
                        }, true);
                    }
                }
                else
                {
                    var abilityId = cakeDefinition.TransformAbilityIds[roll - cakeDefinition.ScareGroups.Length];

                    if (_resourceManager.Consumables.Transformations.TryGetValue(abilityId, out var transform))
                        player.ApplyTemporaryAppearance(transform.ModelId, transform.DurationMs, transform.CompositeEffectId);
                }
            };
        }

        var poofEffect = new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = cakeNpc.Guid,
            CompositeEffectId = cakeDefinition.SpawnPoofEffectId,
            Position = spawnPosition,
            Clear = false
        };

        connection.Player.SendTunneled(poofEffect);
        connection.Player.OnAddVisibleNpcs([cakeNpc]);

        foreach (var player in connection.Player.VisiblePlayers.Values)
        {
            player.SendTunneled(poofEffect);
            player.OnAddVisibleNpcs([cakeNpc]);
        }

        var despawnTime = DateTimeOffset.UtcNow.AddMilliseconds(cakeDefinition.LifetimeMs);

        cakeNpc.UpdateEverySecondAction = () =>
        {
            if (DateTimeOffset.UtcNow >= despawnTime)
                DespawnNpc(cakeNpc, cakeDefinition.SpawnPoofEffectId);
        };
    }

    private static void SpawnBoomboxNpc(GatewayConnection connection, ClientItemDefinition itemDefinition)
    {
        if (connection.Player.Zone is not StartingZone startingZone)
            return;

        if (!startingZone.TryCreateNpc(out var boomboxNpc))
            return;

        _resourceManager.Consumables.Boomboxes.TryGetValue(itemDefinition.Id, out var boomboxDefinition);

        var modelId = boomboxDefinition?.ModelId ?? 1062;
        var effectId = boomboxDefinition?.EffectId ?? 0;
        var danceSequence = boomboxDefinition?.DanceSequence ?? [3501, 3502, 3503, 3504, 3505];

        boomboxNpc.NameId = 0;
        boomboxNpc.ModelId = modelId;
        boomboxNpc.Name = "Boombox";
        boomboxNpc.TextureAlias = itemDefinition.TextureAlias ?? "";
        boomboxNpc.TintAlias = itemDefinition.TintAlias ?? "";
        boomboxNpc.Scale = 1.0f;
        boomboxNpc.Animation = 2100;
        boomboxNpc.CompositeEffectId = effectId;
        boomboxNpc.HideNamePlate = true;
        boomboxNpc.IsInteractable = false;

        var leftDirection = Vector3.Transform(new Vector3(-1, 0, 0), connection.Player.Rotation);
        var spawnPosition = new Vector4(
            connection.Player.Position.X + leftDirection.X * 2.0f,
            connection.Player.Position.Y + leftDirection.Y * 2.0f,
            connection.Player.Position.Z + leftDirection.Z * 2.0f,
            connection.Player.Position.W
        );

        boomboxNpc.Visible = true;
        boomboxNpc.UpdatePosition(spawnPosition, connection.Player.Rotation);

        var poofEffect = new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = boomboxNpc.Guid,
            CompositeEffectId = 21,
            Position = spawnPosition,
            Clear = false
        };

        var poofRecipients = boomboxNpc.VisiblePlayers.Values.ToList();

        if (!boomboxNpc.VisiblePlayers.ContainsKey(connection.Player.Guid))
        {
            connection.Player.SendTunneled(boomboxNpc.GetAddNpcPacket());
            poofRecipients.Insert(0, connection.Player);
        }

        foreach (var player in poofRecipients)
            player.SendTunneled(poofEffect);

        StartDanceLoop(startingZone, boomboxNpc, spawnPosition, danceSequence);
    }

    private static void StartDanceLoop(StartingZone startingZone, Npc boomboxNpc, Vector4 spawnPosition, int[] danceSequence)
    {
        const float BoomboxRangeInMeters = 15.0f;
        const int SwitchMs = 4000;

        var danceCenter = new Vector3(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);

        var dancing = new HashSet<ulong>();
        var elapsedMs = 0;
        var sinceSwitch = SwitchMs;
        var sequenceIndex = 0;
        var previousAnim = -1;
        var currentAnim = 0;

        boomboxNpc.UpdateEverySecondAction = () =>
        {
            if (elapsedMs >= BoomboxDurationMs)
            {
                foreach (var player in startingZone.Players.Where(p => dancing.Contains(p.Guid)))
                    StopDancing(player);

                DespawnNpc(boomboxNpc, 21);
                return;
            }

            var animChanged = false;

            if (sinceSwitch >= SwitchMs)
            {
                var selected = danceSequence.Length > 0 ? danceSequence[sequenceIndex % danceSequence.Length] : 3501;
                sequenceIndex++;
                sinceSwitch = 0;

                if (selected != previousAnim)
                {
                    currentAnim = selected;
                    previousAnim = selected;
                    animChanged = true;
                }
            }

            var players = startingZone.Players.ToList();
            var inRange = players.Where(p =>
                Vector3.Distance(new Vector3(p.Position.X, p.Position.Y, p.Position.Z), danceCenter) <= BoomboxRangeInMeters)
                .ToList();
            var inRangeGuids = inRange.Select(p => p.Guid).ToHashSet();

            foreach (var player in players.Where(p => dancing.Contains(p.Guid) && !inRangeGuids.Contains(p.Guid)))
                StopDancing(player);

            var newcomers = inRange.Where(p => !dancing.Contains(p.Guid)).ToList();
            dancing = inRangeGuids;

            if (animChanged)
                SyncDance(inRange, currentAnim);
            else if (newcomers.Count > 0)
                SyncDance(newcomers, currentAnim);

            elapsedMs += 1000;
            sinceSwitch += 1000;
        };
    }

    private static void SyncDance(List<Player> targets, int animationId)
    {
        if (targets.Count == 0)
            return;

        var sync = new PlayerUpdatePacketSetSynchronizedAnimations();

        foreach (var player in targets)
            sync.Animations.Add(new PlayerUpdatePacketSetSynchronizedAnimations.Animation { Guid = player.Guid, AnimationId = animationId });

        var recipients = new HashSet<Player>(targets);

        foreach (var player in targets)
            foreach (var visiblePlayer in player.VisiblePlayers.Values)
                recipients.Add(visiblePlayer);

        foreach (var recipient in recipients)
            recipient.SendTunneled(sync);
    }

    private static void StopDancing(Player player)
    {
        player.SendTunneledToVisible(new PlayerUpdatePacketSetAnimation
        {
            Guid = player.Guid,
            AnimationId = BoomboxIdleAnimId,
            PlayType = 1
        }, true);
    }

    private static void DespawnNpc(Npc npc, int effectId)
    {
        var removePacket = new PlayerUpdatePacketRemovePlayerGracefully
        {
            Guid = npc.Guid,
            Animate = false,
            Delay = 0,
            EffectDelay = 0,
            CompositeEffectId = effectId,
            Duration = 500
        };

        foreach (var player in npc.Zone.Players)
            player.SendTunneled(removePacket);

        npc.Dispose();
    }

    private static bool SendFailure(GatewayConnection connection)
    {
        connection.SendTunneled(new AbilityPacketFailed { StringId = 3079 });

        return true;
    }

    internal static void ApplyTransform(GatewayConnection connection, int temporaryAppearance, int durationMs, int effectId = 0)
        => connection.Player.ApplyTemporaryAppearance(temporaryAppearance, durationMs, effectId);

    internal static void RemoveTransform(GatewayConnection connection)
        => connection.Player.RemoveTemporaryAppearance();

    private static bool HandleCombatAbility(GatewayConnection connection, AbilityPacketClientRequestStartAbility packet, ReadOnlySpan<byte> data)
    {
        _logger.LogInformation(
            "StartAbility: ActionBar.Id={id} Slot={slot} Target={target} Guid={guid} Pos=({px},{py},{pz},{pw}) Raw={raw}",
            packet.Data.Id, packet.Data.Slot, packet.Target, packet.Guid,
            packet.Position.X, packet.Position.Y, packet.Position.Z, packet.Position.W,
            Convert.ToHexString(data));

        var player = connection.Player;
        var zone = player.Zone;

        if (packet.Data.Slot == 2)
        {
            if (zone is FrostfangArenaZone powerupArena)
                powerupArena.UseHeldPowerup(player);
            else
                HeldPowerupProbe.TryUse(player, _resourceManager);
            return true;
        }

        Npc? targetNpc = null;

        if (packet.Guid != 0 && zone.TryGetNpc(packet.Guid, out var selected) && selected.IsDamageable && selected.IsAlive)
        {
            targetNpc = selected;
        }
        else
        {
            var attackReach = JobWeaponAbilities.AutoTargetReach(player);
            var reach2 = attackReach * attackReach;
            var best2 = reach2;

            foreach (var n in zone.Npcs)
            {
                if (!n.IsHostile || !n.IsDamageable || !n.IsAlive)
                    continue;

                var dx = n.Position.X - player.Position.X;
                var dz = n.Position.Z - player.Position.Z;
                var d2 = dx * dx + dz * dz;
                if (d2 >= best2)
                    continue;

                best2 = d2;
                targetNpc = n;
            }
        }

        var targetGuid = targetNpc?.Guid ?? (packet.Guid != 0 ? packet.Guid : player.Guid);

        var ability = JobWeaponAbilities.ResolveAbility(player, packet.Data.Slot);

        var isBasicMelee = packet.Data.Slot <= 0;
        var actionTime = isBasicMelee ? MeleeActionTime : SpecialActionTime;
        var damageDelay = isBasicMelee ? MeleeDamageDelay : SpecialDamageDelay;

        if (isBasicMelee && BasicSwingMs > 0)
        {
            var now = Environment.TickCount64;
            if (_nextBasicSwingTicks.TryGetValue(player.Guid, out var next) && now < next)
                return true;
            _nextBasicSwingTicks[player.Guid] = now + BasicSwingMs;
        }

        if (!isBasicMelee)
        {
            var cost = ability.EnergyCost;
            var energy = GetEnergy(player);
            if (energy < cost)
            {
                _logger.LogInformation("StartAbility: ability blocked — energy {e}/{max} < {cost}.",
                    energy, MaxEnergy, cost);
                return true;
            }

            var remaining = energy - cost;
            _energy[player.Guid] = remaining;
            SendEnergy(player, remaining);
            StartEnergyRegen(player);
        }

        var startCastingFx = ability.CastEffectId;
        if (startCastingFx > 0 && ability.CastEffectStopMs > 0)
        {
            startCastingFx = 0;

            var tagId = System.Threading.Interlocked.Increment(ref _castFxTagCounter);
            player.SendTunneledToVisible(new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = tagId,
                CompositeEffectId = ability.CastEffectId,
                SourceGuid = player.Guid,
            }, sendToSelf: true);
            var stopMs = ability.CastEffectStopMs;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(stopMs);
                    player.SendTunneledToVisible(new PlayerUpdatePacketRemoveEffectTagCompositeEffect
                    {
                        Guid = player.Guid,
                        TagId = tagId,
                    }, sendToSelf: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lingering cast-FX stop failed.");
                }
            });
        }

        var startCasting = new AbilityPacketStartCasting
        {
            Unknown = player.Guid,            // caster
            Unknown2 = targetGuid,            // target
            CompositeEffectId = startCastingFx,
            Animation = DebugAnimationOverride ?? ability.Animation,
            AbilityId = packet.Data.Slot + 1,
            ActionTime = actionTime,
            HasActionProgress = false,
        };

        player.SendTunneledToVisible(startCasting, sendToSelf: true);

        if (ability.SwordEffectId > 0)
        {
            player.SendTunneledToVisible(new PlayerUpdatePacketSlotCompositeEffectOverride
            {
                Guid = player.Guid,
                Slot = NinjaWeaponAbilities.WeaponSlot,
                CompositeEffect = ability.SwordEffectId,
            }, sendToSelf: true);
        }

        if (ability.SummonCount > 0 && zone is StartingZone summonZone)
            summonZone.SummonShadowClones(player, ability.SummonCount, 12);

        if (ability.BuffMultiplierPct > 0)
        {
            CombatBuffs.AddDamageBuff(player.Guid, ability.BuffMultiplierPct, ability.BuffDurationMs);
            if (ability.PersistEffectId > 0)
            {
                var buffTagId = System.Threading.Interlocked.Increment(ref _castFxTagCounter);
                player.SendTunneledToVisible(new PlayerUpdatePacketAddEffectTagCompositeEffect
                {
                    Guid = player.Guid,
                    TagId = buffTagId,
                    CompositeEffectId = ability.PersistEffectId,
                    SourceGuid = player.Guid,
                }, sendToSelf: true);
                var buffMs = ability.BuffDurationMs;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(buffMs);
                        player.SendTunneledToVisible(new PlayerUpdatePacketRemoveEffectTagCompositeEffect
                        {
                            Guid = player.Guid,
                            TagId = buffTagId,
                        }, sendToSelf: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Buff persist-FX stop failed.");
                    }
                });
            }

            _logger.LogInformation("Ability slot {slot} = '{name}' self-buff ×{pct}% for {ms}ms.",
                packet.Data.Slot, ability.Name, ability.BuffMultiplierPct, ability.BuffDurationMs);
            return true;
        }

        System.Collections.Generic.List<Npc> targets;
        if (ability.AoeRadius > 0)
        {
            var r2 = ability.AoeRadius * ability.AoeRadius;
            var c = player.Position;
            targets = zone.Npcs
                .Where(n => n.IsHostile && n.IsDamageable && n.IsAlive)
                .Where(n =>
                {
                    var dx = n.Position.X - c.X;
                    var dz = n.Position.Z - c.Z;
                    return dx * dx + dz * dz <= r2;
                })
                .ToList();
        }
        else
        {
            targets = targetNpc is null ? [] : [targetNpc];
        }

        if (targets.Count == 0)
        {
            _logger.LogInformation("StartAbility: no damageable target found (slot {slot}, aoe {radius}).",
                packet.Data.Slot, ability.AoeRadius);
            return true;
        }

        player.EnterWorldCombat();

        _logger.LogInformation("Ability slot {slot} = '{name}' (dmg {dmg}, anim {anim}, fx {fx}, targets {count})",
            packet.Data.Slot, ability.Name, ability.Damage, ability.Animation, ability.EffectId, targets.Count);

        ResolveDamageAfterCast(player, targets, ability.Damage, ability.EffectId, damageDelay,
            ability.CasterEndEffectId, ability.EnemyExtraEffectId);

        return true;
    }

    private static void ResolveDamageAfterCast(Player player, System.Collections.Generic.IReadOnlyList<Npc> targets,
        int damage, int effectId, float damageDelay, int casterEndEffectId = 0, int enemyExtraEffectId = 0)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay((int)(damageDelay * 1000));

                player.EnterWorldCombat();

                if (casterEndEffectId > 0)
                {
                    player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = player.Guid,
                        CompositeEffectId = casterEndEffectId,
                        Position = player.Position,
                    }, sendToSelf: true);
                }

                foreach (var target in targets)
                {
                    if (!target.IsAlive)
                        continue;

                    var hitDamage = CombatBuffs.ApplyDamage(player.Guid, ApplyArcherTraitDamage(player, damage));

                    var killed = target.ApplyDamage(hitDamage);

                    if (effectId > 0)
                    {
                        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = target.Guid,
                            CompositeEffectId = effectId,
                            Position = target.Position,
                        }, sendToSelf: true);
                    }

                    if (enemyExtraEffectId > 0)
                    {
                        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = target.Guid,
                            CompositeEffectId = enemyExtraEffectId,
                            Position = target.Position,
                        }, sendToSelf: true);
                    }

                    player.SendTunneledToVisible(new PlayerUpdatePacketHitPointModification
                    {
                        Guid = player.Guid,
                        Guid2 = target.Guid,
                        Unknown = true,               // player->NPC sample had the leading bool = 01
                        Unknown2 = target.MaxHealth,  // max HP (bar denominator)
                        Unknown3 = target.Health,     // current HP AFTER the hit (bar position)
                        Unknown4 = -hitDamage,        // delta = -damage -> the floating number
                    }, sendToSelf: true);

                    TryLuckyShotEnergy(player);

                    _logger.LogInformation(
                        "Ability hit {name} ({guid}) for {dmg} -> {hp}/{max} HP (killed={killed})",
                        target.Name, target.Guid, hitDamage, target.Health, target.MaxHealth, killed);

                    if (killed)
                        player.Zone.OnNpcKilled(player, target);
                    else
                        player.Zone.OnNpcDamaged(player, target);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ability damage resolution failed.");
            }
        });
    }
}
