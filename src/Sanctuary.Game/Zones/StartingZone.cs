using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.IO;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class StartingZone : BaseZone
{
    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly StartingZoneDefinition _zoneDefinition;
    private readonly Sanctuary.Game.Quests.IQuestManager _questManager;
    private readonly Sanctuary.Game.Party.IPartyManager _partyManager;

    public StartingZone(StartingZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        _zoneDefinition = zoneDefinition;

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _questManager = serviceProvider.GetRequiredService<Sanctuary.Game.Quests.IQuestManager>();
        _partyManager = serviceProvider.GetRequiredService<Sanctuary.Game.Party.IPartyManager>();

        SpawnNpcs();

        SpawnDungeonEntrances();

        SpawnEncounterEntryNpcs();
    }

    private const ulong NpcGuidBase = 100000000000UL;

    private ulong _spiritEntranceGuid;

    public ulong SpiritEntranceGuid => _spiritEntranceGuid;

    private void SpawnNpcs()
    {
        int spawnedCount = 0;

        foreach (var definition in _resourceManager.Npcs.Values)
        {
            var guid = NpcGuidBase + (ulong)definition.Id;

            if (IsWorldEnemyDefinition(definition))
            {
                SpawnWorldEnemy(definition);
                spawnedCount++;
                continue;
            }

            if (definition.NameId == TormentedSpiritsArenaZone.EntryNpcNameId)
            {
                if (_spiritEntranceGuid != 0)
                {
                    SpawnWorldEnemy(definition);
                    spawnedCount++;
                    continue;
                }

                _spiritEntranceGuid = guid;
            }

            if (!TryCreateNpc(guid, out var npc))
                continue;

            npc.ModelId = definition.ModelId;
            npc.NameId = definition.NameId;
            npc.TextureAlias = definition.TextureAlias;
            npc.Name = definition.Name;
            npc.Static = definition.Static;
            npc.Scale = _resourceManager.Models.TryGetValue(definition.ModelId, out var model) && model.Scale != 0f
                ? model.Scale
                : 1f;
            npc.Visible = true;

            if (_resourceManager.NpcVendors.TryGetValue(guid, out var vendorDef))
            {
                npc.VendorItems = vendorDef.Items;
                npc.VendorCosts = vendorDef.ItemCosts;
                npc.VendorBundles = vendorDef.Bundles;
                npc.ResourceManager = _resourceManager;
                npc.CursorId = 17;
                npc.NameplateImageId = vendorDef.NameplateImageId;
                npc.ImageSetId = vendorDef.ImageSetId;
                npc.NotificationImageSetId = vendorDef.NotificationImageSetId;
                if (vendorDef.ActiveProfile != 0)
                    npc.ActiveProfile = vendorDef.ActiveProfile;
                if (vendorDef.SubTextNameId != 0)
                    npc.SubTextNameId = vendorDef.SubTextNameId;
                var capturedNpc = npc;
                npc.InteractAction = (interactingPlayer) =>
                {
                    var itemListPacket = new CoinStoreItemListPacket();
                    foreach (var itemDefId in capturedNpc.VendorItems)
                    {
                        if (_resourceManager.CoinStoreItems.TryGetValue(itemDefId, out var meta))
                            itemListPacket.StaticItems[itemDefId] = meta;
                        else if (_resourceManager.ClientItemDefinitions.TryGetValue(itemDefId, out var def))
                            itemListPacket.StaticItems[itemDefId] = new ItemDefinitionMetaData { Id = itemDefId, CategoryId = def.CategoryId };
                    }
                    interactingPlayer.SendTunneled(itemListPacket);

                    var merchantPacket = new CoinStoreMerchantListPacket();
                    merchantPacket.MerchantList.PlayerGuid = (long)interactingPlayer.Guid;
                    merchantPacket.MerchantList.NpcGuid = capturedNpc.Guid;
                    merchantPacket.MerchantList.NameId = capturedNpc.NameId;
                    foreach (var itemDefId in capturedNpc.VendorItems)
                    {
                        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefId, out var def))
                            continue;
                        merchantPacket.MerchantList.Entries.Add(new MerchantList.Entry
                        {
                            ItemDefinitionId = itemDefId,
                            IconId = def.Icon.Id,
                            TintId = def.Icon.TintId,
                            NameId = def.NameId,
                            DescriptionId = def.DescriptionId,
                            PurchasableQty = -1,
                            MembersOnly = def.MembersOnly,
                            CanBuy = true
                        });
                    }
                    interactingPlayer.ActiveMerchantGuid = capturedNpc.Guid;
                    interactingPlayer.SendTunneled(merchantPacket);
                };
            }

            if (_questManager.IsQuestNpc(guid))
            {
                npc.CursorId = 17;
                var questNpc = npc;
                npc.InteractAction = interactingPlayer => _questManager.OnNpcInteract(interactingPlayer, questNpc);
            }

            if (_resourceManager.Quests.KillTargetNameIds.Contains(definition.NameId))
                MakeQuestHostile(npc);

            if (definition.NameId == TormentedSpiritsArenaZone.EntryNpcNameId)
            {
                npc.CursorId = 11;
                npc.InteractRange = 15;
            }

            npc.UpdatePosition(definition.Position, definition.Rotation);

            var tile = GetTileFromPosition(definition.Position);
            tile.Entities.TryAdd(npc.Guid, npc);

            spawnedCount++;
        }

        SpawnQuestCollectibles();
    }

    private void SpawnQuestCollectibles()
    {
        foreach (var collectible in _resourceManager.Quests.CollectibleSpawns)
        {
            if (!TryCreateNpc(collectible.Guid, out var npc))
                continue;

            npc.ModelId = collectible.ModelId;
            npc.NameId = collectible.NameId;
            npc.Static = true;
            npc.Scale = _resourceManager.Models.TryGetValue(collectible.ModelId, out var model) && model.Scale != 0f
                ? model.Scale
                : 1f;
            npc.Visible = true;
            npc.CursorId = 17;

            var questCollectible = npc;
            npc.InteractAction = interactingPlayer => _questManager.OnCollectInteract(interactingPlayer, questCollectible);

            npc.UpdatePosition(collectible.Position, System.Numerics.Quaternion.Identity);
            GetTileFromPosition(collectible.Position).Entities.TryAdd(npc.Guid, npc);
        }
    }

    #region Client Is Ready

    public override void OnClientIsReady(Player player)
    {
        SendQuickChatData(player);

        SendPointOfInterests(player);

        player.RecalculateStats(refill: true);

        SendReferenceData(player);

        SendCoinStoreItemList(player);

        SendAdventurersJournalInfo(player);

        if (!player.LoginBurstSent)
        {
            player.LoginBurstSent = true;
            SendWelcomeInfo(player);
        }

        SendPlayerCustomizations(player);

        SendMembershipSubscriptionInfo(player);

        SendListOfActivities(player);

        SendInGamePurchase(player);

        player.SendTunneled(new PacketZoneDoneSendingInitialData());
        player.SendTunneled(new ClientUpdatePacketDoneSendingPreloadCharacters());

        SendFriendList(player);
        SendIgnoreList(player);

        UpdateFriendStatus(player);

        if (!player.JournalRestored)
        {
            player.JournalRestored = true;
            _questManager.RestoreJournal(player);
        }

        SpawnTrainingDummy(player);

        SpawnGrowlerWolf(player);

        SendJobAbilityToolbar(player);
    }

    public override void OnClientFinishedLoading(Player player)
    {
        _questManager.RefreshObjectiveTarget(player);

        player.SendTunneled(new CommandPacketQuestDialogComplete());
    }

    private void SendJobAbilityToolbar(Player player)
    {
        if (!JobWeaponAbilities.SendToolbarWithFxPreload(player, _resourceManager))
            return;

        _logger.LogInformation("Job toolbar on zone-load: profile={profile}, equipped weapon def={def}.",
            player.ActiveProfileId, player.GetEquippedWeaponDefinitionId());
    }

    private Npc? _trainingDummy;

    private const int TrainingDummyMaxHealth = 50000;

    private Npc? _nameColorTestDummy;

    public void SpawnNameColorTestDummy(Player player, int argb)
    {
        _nameColorTestDummy?.Dispose();
        _nameColorTestDummy = null;

        if (!TryCreateNpc(out var npc))
            return;

        npc.ModelId = 4;
        npc.Name = "Name Color Test";
        npc.NameId = 0;
        npc.NameColor = argb;
        npc.ActiveProfile = 1;
        npc.Scale = 1f;
        npc.IsInteractable = false;
        npc.Visible = true;

        var pos = new Vector4(player.Position.X + 3f, player.Position.Y, player.Position.Z + 1f, 1f);
        npc.UpdatePosition(pos, SpawnRotation);

        player.OnAddVisibleNpcs(npc);
        _nameColorTestDummy = npc;
    }

    private void SpawnTrainingDummy(Player player)
    {
        if (_trainingDummy is null)
        {
            if (!TryCreateNpc(out var npc))
                return;

            npc.ModelId = 4;
            npc.Name = "Training Dummy";
            npc.NameId = 0;
            npc.Disposition = 0;
            npc.ActiveProfile = 1;
            npc.Scale = 1f;
            npc.IsInteractable = false;
            npc.Visible = true;
            npc.CursorId = 11;

            npc.MaxHealth = TrainingDummyMaxHealth;
            npc.Health = TrainingDummyMaxHealth;
            npc.ShowHealthBar = true;

            var pos = new Vector4(SpawnPosition.X + 5f, SpawnPosition.Y, SpawnPosition.Z, SpawnPosition.W);
            npc.UpdatePosition(pos, SpawnRotation);

            _trainingDummy = npc;
        }

        player.OnAddVisibleNpcs(_trainingDummy);

        SendNpcRelevance(player, _trainingDummy);

        SendNpcHealth(player, _trainingDummy);
    }

    private Npc? _growlerWolf;

    private void SpawnGrowlerWolf(Player player)
    {
        if (_growlerWolf is null)
        {
            if (!TryCreateNpc(out var npc))
                return;

            npc.ModelId = 176;
            npc.Name = "Frostfang Growler";
            npc.NameId = 0;
            npc.Disposition = 1;
            npc.Scale = 1f;
            npc.IsInteractable = true;
            npc.Visible = true;
            npc.CursorId = 11;
            npc.NameplateImageId = 0;
            npc.ImageSetId = 0;
            npc.ShowHealthBar = false;

            var pos = new Vector4(202.2f, 34.6f, 504.7f, 1f);
            npc.UpdatePosition(pos, SpawnRotation);

            _growlerWolf = npc;
        }

        player.OnAddVisibleNpcs(_growlerWolf);

        SendNpcRelevance(player, _growlerWolf);

        SendGrowlerBadge(player);
    }

    private void SendGrowlerBadge(Player player)
    {
        if (_growlerWolf is null)
            return;

        var badge = new PlayerUpdatePacketAddNotifications();
        badge.Notifications.Add(new NotificationInfo
        {
            Guid = _growlerWolf.Guid,
            ImageId = 24,
            Type = 3,
            Unknown3 = 7,    // live combat-badge value (was default 1)
            Unknown10 = true // constant 1 across all live samples
        });

        player.SendTunneled(badge);
    }

    public Npc? GrowlerWolf => _growlerWolf;

    public Npc? TormentedSpiritEntry()
    {
        foreach (var (id, definition) in _resourceManager.Npcs)
        {
            if (definition.NameId != TormentedSpiritsArenaZone.EntryNpcNameId)
                continue;

            if (TryGetNpc(NpcGuidBase + (ulong)id, out var npc))
                return npc;
        }

        return null;
    }

    public void ShowGrowlerWolf(Player player)
    {
        if (_growlerWolf is not null)
        {
            player.OnAddVisibleNpcs(_growlerWolf);
            SendGrowlerBadge(player);
        }
    }

    public Npc? TrainingDummy => _trainingDummy;

    public void ResetTrainingDummy()
    {
        var dummy = _trainingDummy;

        if (dummy is null)
            return;

        dummy.Health = dummy.MaxHealth;

        foreach (var zonePlayer in Players)
            SendNpcHealth(zonePlayer, dummy);
    }

    private const int QuestHostileHealth = 5000;

    private const int QuestHostileDeathFxId = 5017;
    private const int QuestHostileDeathHoldMs = 2000;

    private const int QuestHostileRespawnMs = 20_000;

    private static void MakeQuestHostile(Npc npc)
    {
        npc.Disposition = 0;
        npc.ActiveProfile = 1;
        npc.EnemyStatus = true;
        npc.CursorId = 11;
        npc.MaxHealth = QuestHostileHealth;
        npc.Health = QuestHostileHealth;
    }

    private void RespawnQuestHostile(NpcDefinition definition)
    {
        var guid = NpcGuidBase + (ulong)definition.Id;

        if (!TryCreateNpc(guid, out var npc))
            return;

        npc.ModelId = definition.ModelId;
        npc.NameId = definition.NameId;
        npc.TextureAlias = definition.TextureAlias;
        npc.Name = definition.Name;
        npc.Static = definition.Static;
        npc.Scale = _resourceManager.Models.TryGetValue(definition.ModelId, out var model) && model.Scale != 0f
            ? model.Scale
            : 1f;
        npc.Visible = true;

        MakeQuestHostile(npc);

        npc.UpdatePosition(definition.Position, definition.Rotation);

        var tile = GetTileFromPosition(definition.Position);
        tile.Entities.TryAdd(npc.Guid, npc);
    }

    private const int WorldEnemyLevel = 3;

    private const int WorldEnemyRespawnMs = 8_000;

    private bool IsWorldEnemyDefinition(NpcDefinition definition)
    {
        var guid = NpcGuidBase + (ulong)definition.Id;
        return !_resourceManager.NpcVendors.ContainsKey(guid)
            && !_questManager.IsQuestNpc(guid)
            && !_resourceManager.Quests.KillTargetNameIds.Contains(definition.NameId)
            && Sanctuary.Game.Dungeons.DungeonCatalog.EnemyModelIds.Contains(definition.ModelId);
    }

    private void SpawnWorldEnemy(NpcDefinition definition)
    {
        if (!TryCreateCombatNpc(out var enemy))
            return;

        enemy.ModelId = definition.ModelId;
        enemy.NameId = definition.NameId;
        enemy.TextureAlias = definition.TextureAlias;
        enemy.Name = definition.Name;
        enemy.Static = false;
        enemy.Scale = _resourceManager.Models.TryGetValue(definition.ModelId, out var model) && model.Scale != 0f
            ? model.Scale
            : 1f;
        enemy.Visible = true;
        enemy.EnemyStatus = true;
        enemy.ActiveProfile = 1;
        enemy.CursorId = 11;
        enemy.IsInteractable = false;
        enemy.ShowHealthBar = true;
        enemy.MovementType = 2;

        enemy.InitializeFromLevel(WorldEnemyLevel);
        enemy.Speed = enemy.CombatSpeed;

        enemy.MaxHealth = enemy.MaxHitpoints;
        enemy.Health = enemy.CurrentHitpoints;

        enemy.SpawnPosition = definition.Position;
        enemy.SpawnRotation = definition.Rotation;
        enemy.LastSentPosition = definition.Position;
        enemy.UpdatePosition(definition.Position, definition.Rotation);

        var tile = GetTileFromPosition(definition.Position);
        tile.Entities.TryAdd(enemy.Guid, enemy);
    }

    private void RespawnWorldEnemy(int modelId, int nameId, string? name, string? textureAlias, float scale,
        int level, Vector4 spawnPosition, Quaternion spawnRotation)
    {
        if (!TryCreateCombatNpc(out var enemy))
            return;

        enemy.ModelId = modelId;
        enemy.NameId = nameId;
        enemy.TextureAlias = textureAlias;
        enemy.Name = name;
        enemy.Static = false;
        enemy.Scale = scale;
        enemy.Visible = true;
        enemy.EnemyStatus = true;
        enemy.ActiveProfile = 1;
        enemy.CursorId = 11;
        enemy.IsInteractable = false;
        enemy.ShowHealthBar = true;
        enemy.MovementType = 2;

        enemy.InitializeFromLevel(level);
        enemy.Speed = enemy.CombatSpeed;
        enemy.MaxHealth = enemy.MaxHitpoints;
        enemy.Health = enemy.CurrentHitpoints;

        enemy.SpawnPosition = spawnPosition;
        enemy.SpawnRotation = spawnRotation;
        enemy.LastSentPosition = spawnPosition;
        enemy.UpdatePosition(spawnPosition, spawnRotation);

        var tile = GetTileFromPosition(spawnPosition);
        tile.Entities.TryAdd(enemy.Guid, enemy);

        foreach (var player in Players)
        {
            if (enemy.VisiblePlayers.ContainsKey(player.Guid))
                continue;

            var playerTile = GetTileFromPosition(player.Position);
            if (playerTile == tile || playerTile.VisibleTiles.Contains(tile))
            {
                player.OnAddVisibleNpcs([enemy]);
                enemy.OnAddVisiblePlayers(player);
            }
        }
    }

    private const int AtlasEntranceModelId = 511;

    private void SpawnDungeonEntrances()
    {
        foreach (var poi in _resourceManager.PointOfInterests.Values)
        {
            if (poi.NotificationType != 3)
                continue;
            if (!Sanctuary.Game.Dungeons.DungeonCatalog.ByAtlasPoi.TryGetValue(poi.Id, out var dungeon))
                continue;
            if (!TryCreateNpc(out var entrance))
                continue;

            entrance.ModelId = AtlasEntranceModelId;
            entrance.NameId = dungeon.TitleNameId;
            entrance.Name = dungeon.Comment;
            entrance.Static = true;
            entrance.Scale = 1f;
            entrance.Visible = true;
            entrance.HideNamePlate = false;
            entrance.CursorId = 11;
            entrance.InteractRange = 18;

            var pos = poi.SpawnPosition != default ? poi.SpawnPosition : poi.Position;
            var rot = new Quaternion(MathF.Sin(poi.Heading), 0f, MathF.Cos(poi.Heading), 0f);
            var capturedDungeon = dungeon;
            entrance.InteractAction = player => SendDungeonOffer(player, capturedDungeon);

            entrance.UpdatePosition(pos, rot);
            GetTileFromPosition(pos).Entities.TryAdd(entrance.Guid, entrance);
        }
    }

    public static void SendDungeonOffer(Player player, Sanctuary.Game.Dungeons.DungeonDefinition dungeon)
    {
        const int instanceId = 1;

        foreach (var state in new[] { 2, 3, 4 })
        {
            player.SendTunneled(new EncounterStatePacket
            {
                EncounterId = dungeon.ActivityId,
                InstanceId = instanceId,
                State = state,
            });
        }

        player.SendTunneled(new EncounterDetailsResponsePacket
        {
            Unknown = dungeon.ActivityId,
            Unknown2 = instanceId,
            NameId = dungeon.TitleNameId,
            DescriptionId = dungeon.DescriptionId,
            Difficulty = dungeon.Difficulty,
            IconId = dungeon.IconId,
            MiniGameType = 4,
            PreviewRewards = FrostfangArenaZone.GetPrizePreviewFor(player),
            PreviewCoins = FrostfangArenaZone.PrizeCoins,
            PreviewXp = FrostfangArenaZone.PrizeXp,
            ProfileType = FrostfangArenaZone.CombatProfileType,
            ActivityId = dungeon.ActivityId,
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(600);
            player.SendTunneled(new EncounterZoneIsReadyPacket());
            player.SendTunneled(new EncounterStatePacket
            {
                EncounterId = dungeon.ActivityId,
                InstanceId = instanceId,
                State = 5,
            });
        });
    }

    private static readonly Dictionary<string, string> EncounterThemeKeyword = new()
    {
        ["sg_random_encounter_skullcamp"] = "Robgoblin",
        ["sg_random_encounter_treefort"] = "Thugawug",
        ["sg_random_encounter_creek"] = "Wolf",
        ["sg_random_encounter_clearing"] = "Wolf",
        ["sh_random_encounter_01"] = "Troll",
        ["bw_random_encounter_bristlewood_01"] = "Floren",
        ["bw_random_encounter_01"] = "Grave",
        ["bw_random_encounter_02"] = "Hooligan",
        ["bw_random_encounter_03"] = "Bixie",
        ["bw_random_encounter_thistlerow_01"] = "Asp",
        ["ss_random_encounter_01"] = "Cray",
    };

    private void SpawnEncounterEntryNpcs()
    {
        var anchors = _resourceManager.Npcs.Values
            .Where(d => (d.SpawnPosition[0] != 0f || d.SpawnPosition[2] != 0f) && !IsWorldEnemyDefinition(d))
            .ToList();
        if (anchors.Count == 0)
            return;

        var used = new HashSet<int>();
        int spreadCursor = 0;
        int spawned = 0;

        foreach (var dungeon in Sanctuary.Game.Dungeons.DungeonCatalog.ByActivity.Values)
        {
            if (dungeon.PoiId != 0)
                continue;

            var anchor = FindThematicAnchor(dungeon, anchors, used)
                         ?? PickSpreadAnchor(anchors, used, ref spreadCursor);
            if (anchor is null)
                break;
            used.Add(anchor.Id);

            if (!TryCreateEncounterEntryNpc(out var entry))
                continue;

            var lead = System.Array.Find(dungeon.Enemies, e => e.Boss) ?? dungeon.Enemies[0];
            entry.ModelId = lead.ModelId;
            entry.NameId = dungeon.TitleNameId;
            entry.Name = dungeon.Comment;
            entry.Static = false;
            entry.Scale = (_resourceManager.Models.TryGetValue(lead.ModelId, out var model) && model.Scale != 0f
                ? model.Scale : 1f) * 1.15f;
            entry.Visible = true;
            entry.CursorId = 11;
            entry.MovementType = 2;
            entry.InteractRange = 6;
            entry.ShowHealthBar = false;

            var pos = new Vector4(anchor.Position.X + 4f, anchor.Position.Y, anchor.Position.Z + 4f, 1f);

            var captured = dungeon;
            entry.InteractAction = player => SendDungeonOffer(player, captured);

            entry.UpdatePosition(pos, anchor.Rotation);
            entry.StartWander(pos);
            GetTileFromPosition(pos).Entities.TryAdd(entry.Guid, entry);
            spawned++;
        }

        _logger.LogInformation("Spawned {count} wandering combat-encounter entries.", spawned);
    }

    private static NpcDefinition? FindThematicAnchor(Sanctuary.Game.Dungeons.DungeonDefinition dungeon,
        List<NpcDefinition> anchors, HashSet<int> used)
    {
        var keywords = new List<string>();
        foreach (var raw in dungeon.Comment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = new string(raw.Where(char.IsLetter).ToArray());
            if (word.Length < 4)
                continue;
            keywords.Add(word);
            if (word.EndsWith('s'))
                keywords.Add(word[..^1]);
        }
        if (EncounterThemeKeyword.TryGetValue(dungeon.World, out var themeKeyword))
            keywords.Add(themeKeyword);

        foreach (var keyword in keywords)
            foreach (var anchor in anchors)
                if (!used.Contains(anchor.Id) && !string.IsNullOrEmpty(anchor.Name)
                    && anchor.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return anchor;

        return null;
    }

    private static NpcDefinition? PickSpreadAnchor(List<NpcDefinition> anchors, HashSet<int> used, ref int cursor)
    {
        int stride = Math.Max(1, anchors.Count / 60);
        for (int i = 0; i < anchors.Count; i++)
        {
            var anchor = anchors[(cursor + i * stride) % anchors.Count];
            if (!used.Contains(anchor.Id))
            {
                cursor = (cursor + i * stride + stride) % anchors.Count;
                return anchor;
            }
        }
        return null;
    }

    private static void BroadcastKillSignal(Player killer, Npc npc)
    {
        var clear = new PlayerUpdatePacketRemoveNotifications { Guids = { npc.Guid } };
        foreach (var viewer in npc.VisiblePlayers.Values)
            viewer.SendTunneled(clear);
        if (!npc.VisiblePlayers.ContainsKey(killer.Guid))
            killer.SendTunneled(clear);
    }

    private const float XpShareRange = 100f;

    private void AwardSharedXp(Player killer, int xp, Vector4 killPos)
    {
        killer.AwardXp(xp);

        var party = _partyManager.GetParty(killer);
        if (party is null)
            return;

        var range2 = XpShareRange * XpShareRange;

        foreach (var member in party.Members)
        {
            if (member.Guid == killer.Guid || member.Zone != this)
                continue;

            var dx = member.Position.X - killPos.X;
            var dz = member.Position.Z - killPos.Z;
            if (dx * dx + dz * dz > range2)
                continue;

            member.AwardXp(xp);
        }
    }

    public override void OnNpcKilled(Player killer, Npc npc)
    {
        if (ReferenceEquals(npc, _trainingDummy))
        {
            ResetTrainingDummy();
            return;
        }

        if (npc is Sanctuary.Game.Entities.CombatNpc worldEnemy)
        {
            if (worldEnemy.IsDead)
                return;
            worldEnemy.IsDead = true;

            AwardSharedXp(killer, worldEnemy.XpReward, worldEnemy.Position);

            int modelId = worldEnemy.ModelId, nameId = worldEnemy.NameId, level = worldEnemy.Level;
            string? name = worldEnemy.Name, textureAlias = worldEnemy.TextureAlias;
            float scale = worldEnemy.Scale;
            var spawnPos = worldEnemy.SpawnPosition;
            var spawnRot = worldEnemy.SpawnRotation;

            BroadcastKillSignal(killer, npc);

            var killerSaw = npc.VisiblePlayers.ContainsKey(killer.Guid);

            npc.GracefulRemoval = (true, QuestHostileDeathHoldMs, 0, QuestHostileDeathFxId, 1000);
            npc.Dispose();

            if (!killerSaw)
                killer.SendTunneled(new PlayerUpdatePacketRemovePlayerGracefully
                {
                    Guid = npc.Guid,
                    Animate = true,
                    Delay = QuestHostileDeathHoldMs,
                    EffectDelay = 0,
                    CompositeEffectId = QuestHostileDeathFxId,
                    Duration = 1000,
                });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(WorldEnemyRespawnMs);
                    RespawnWorldEnemy(modelId, nameId, name, textureAlias, scale, level, spawnPos, spawnRot);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "World enemy respawn failed (model {model}).", modelId);
                }
            });

            return;
        }

        _questManager.OnNpcKilled(killer, npc);

        if (npc.Guid > NpcGuidBase
            && _resourceManager.Npcs.TryGetValue((int)(npc.Guid - NpcGuidBase), out var definition)
            && _resourceManager.Quests.KillTargetNameIds.Contains(definition.NameId))
        {
            BroadcastKillSignal(killer, npc);
            npc.GracefulRemoval = (true, QuestHostileDeathHoldMs, 0, QuestHostileDeathFxId, 1000);
            npc.Dispose();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(QuestHostileRespawnMs);
                    RespawnQuestHostile(definition);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Quest hostile respawn failed for npc definition {id}.", definition.Id);
                }
            });
        }
    }

    public override void OnNpcDamaged(Player player, Npc npc)
    {
        if (npc is Sanctuary.Game.Entities.CombatNpc enemy)
            enemy.AggroOnto(player);
    }

    private const int ShadowCloneModelId = 945;
    private const int ShadowCloneSmokePoof = 21;
    private const int CloneTickMs = 300;
    private const int CloneAttackCooldownMs = 1400;
    private const int CloneAttackAnimation = 1021;
    private const int CloneAttackDamage = 200;
    private const int CloneHitFx = 15999;
    private const float CloneMoveSpeed = 9f;
    private const float CloneAttackRange = 2.5f;
    private const int CloneRunAnim = 3;

    public void SummonShadowClones(Player summoner, int count, int lifetimeSeconds)
    {
        (float dx, float dz)[] offsets = [(-2f, -2f), (2f, -2f), (0f, -3f), (-3f, 1f), (3f, 1f)];

        var clones = new List<Npc>(count);

        for (var i = 0; i < count; i++)
        {
            if (!TryCreateNpc(out var clone))
                break;

            var (dx, dz) = offsets[i % offsets.Length];
            var pos = new Vector4(summoner.Position.X + dx, summoner.Position.Y, summoner.Position.Z + dz, summoner.Position.W);

            clone.ModelId = ShadowCloneModelId;
            clone.Name = "Shadow Ninja";
            clone.NameId = 0;
            clone.HideNamePlate = false;
            clone.Disposition = 2;
            clone.Scale = 1f;
            clone.IsInteractable = false;
            clone.CursorId = 0;
            clone.CompositeEffectId = 0;
            clone.RunAnimId = CloneRunAnim;
            clone.WalkAnimId = 2;
            clone.StandAnimId = 1;
            clone.Visible = true;
            clone.UpdatePosition(pos, summoner.Rotation);

            summoner.OnAddVisibleNpcs(clone);
            clone.OnAddVisiblePlayers(summoner);

            summoner.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = clone.Guid,
                CompositeEffectId = ShadowCloneSmokePoof,
                Position = pos,
            });

            clones.Add(clone);
        }

        if (clones.Count == 0)
            return;

        _logger.LogInformation("Shadow Army: summoned {n} clones for {sec}s (model {model}).",
            clones.Count, lifetimeSeconds, summoner.Model);

        _ = Task.Run(async () =>
        {
            try
            {
                var totalMs = lifetimeSeconds * 1000;
                var nextAttackMs = new int[clones.Count];

                for (var elapsed = 0; elapsed < totalMs; elapsed += CloneTickMs)
                {
                    await Task.Delay(CloneTickMs);

                    var dummy = _trainingDummy;
                    if (dummy is null)
                        continue;

                    var target = new Vector3(dummy.Position.X, dummy.Position.Y, dummy.Position.Z);

                    for (var i = 0; i < clones.Count; i++)
                    {
                        var clone = clones[i];
                        var here = new Vector3(clone.Position.X, clone.Position.Y, clone.Position.Z);
                        var toTarget = target - here;
                        var dist = toTarget.Length();

                        var yaw = (float)Math.Atan2(toTarget.X, toTarget.Z);
                        var rot = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f);

                        if (dist > CloneAttackRange)
                        {
                            var step = Math.Min(CloneMoveSpeed * (CloneTickMs / 1000f), dist - CloneAttackRange);
                            var dir = toTarget / dist;
                            var np = here + dir * step;
                            var newPos = new Vector4(np.X, np.Y, np.Z, clone.Position.W);

                            clone.UpdatePosition(newPos, rot);
                            summoner.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = clone.Guid, Position = newPos, Rotation = rot, State = 1, Unknown = 0,
                            });
                        }
                        else
                        {
                            clone.UpdatePosition(clone.Position, rot);
                            summoner.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = clone.Guid, Position = clone.Position, Rotation = rot, State = 0, Unknown = 0,
                            });

                            if (elapsed >= nextAttackMs[i] && dummy.IsAlive)
                            {
                                nextAttackMs[i] = elapsed + CloneAttackCooldownMs;

                                summoner.SendTunneled(new AbilityPacketStartCasting
                                {
                                    Unknown = clone.Guid, Unknown2 = dummy.Guid, CompositeEffectId = 0,
                                    Animation = CloneAttackAnimation, AbilityId = 0, ActionTime = 0.3f, HasActionProgress = false,
                                });

                                var killed = dummy.ApplyDamage(CloneAttackDamage);
                                summoner.SendTunneled(new CombatPacketAttackProcessed
                                {
                                    AttackerGuid = clone.Guid,
                                    TargetGuid = dummy.Guid,
                                    Damage = CloneAttackDamage,
                                    MaxHealth = dummy.MaxHealth,
                                    CompositeEffectId = CloneHitFx,
                                    CurrentHealth = dummy.Health,
                                });

                                if (killed)
                                    ResetTrainingDummy();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shadow Army clone AI failed.");
            }
            finally
            {
                foreach (var clone in clones)
                {
                    summoner.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = clone.Guid,
                        CompositeEffectId = ShadowCloneSmokePoof,
                        Position = clone.Position,
                    });

                    clone.Dispose();
                }
            }
        });
    }

    private void SendQuickChatData(Player player)
    {
        var quickChatSendDataPacket = new QuickChatSendDataPacket();

        quickChatSendDataPacket.QuickChats = _resourceManager.QuickChats.ToDictionary();

        player.SendTunneled(quickChatSendDataPacket);
    }

    private void SendPointOfInterests(Player player)
    {
        var packetPointOfInterestDefinitionReply = new PacketPointOfInterestDefinitionReply();
        using var writer = new PacketWriter();

        foreach (var pointOfInterest in _resourceManager.PointOfInterests.Values)
        {
            writer.Write(true);

            pointOfInterest.Serialize(writer);
        }

        writer.Write(false);

        packetPointOfInterestDefinitionReply.Payload = writer.Buffer;

        player.SendTunneled(packetPointOfInterestDefinitionReply);
    }

    private void SendReferenceData(Player player)
    {
        var referenceDataPacketItemClassDefinitions = new ReferenceDataPacketItemClassDefinitions();

        referenceDataPacketItemClassDefinitions.ItemClasses = _resourceManager.ItemClasses.ToDictionary();

        player.SendTunneled(referenceDataPacketItemClassDefinitions);

        var referenceDataPacketItemCategoryDefinitions = new ReferenceDataPacketItemCategoryDefinitions();

        referenceDataPacketItemCategoryDefinitions.ItemCategories = _resourceManager.ItemCategories.ToDictionary();
        referenceDataPacketItemCategoryDefinitions.ItemCategoryGroups = _resourceManager.ItemCategoryGroups.ToDictionary();

        player.SendTunneled(referenceDataPacketItemCategoryDefinitions);

        var referenceDataPacketClientProfileData = new ReferenceDataPacketClientProfileData();

        referenceDataPacketClientProfileData.Profiles = _resourceManager.Profiles.ToDictionary();

        player.SendTunneled(referenceDataPacketClientProfileData);
    }

    private void SendCoinStoreItemList(Player player)
    {
        var coinStoreItemListPacket = new CoinStoreItemListPacket();

        coinStoreItemListPacket.StaticItems = _resourceManager.CoinStoreItems.ToDictionary();

        player.SendTunneled(coinStoreItemListPacket);

        var clientItemDefinitions = new List<ClientItemDefinition>();

        foreach (var coinStoreItem in _resourceManager.CoinStoreItems)
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(coinStoreItem.Key, out var clientItemDefinition))
                continue;

            clientItemDefinitions.Add(clientItemDefinition);
        }

        using var writer = new PacketWriter();

        writer.Write(clientItemDefinitions);

        var playerUpdatePacketItemDefinitions = new PlayerUpdatePacketItemDefinitions();

        playerUpdatePacketItemDefinitions.Payload = writer.Buffer;

        player.SendTunneled(playerUpdatePacketItemDefinitions);
    }

    private void SendAdventurersJournalInfo(Player player)
    {

        var adventurersJournal = new AdventurersJournalInfoPacket();

        AdventurersJournalRegionDefinition[] regions =
        [
            new()
            {
                Id = 1,
                NameId = 5100069,
                DescriptionId = 5100031,
                TabImageId = 35449,
                ChapterMapImageId = 0,
                GeometryId = 244,
                CompletedStringId = 5101408
            },
            new()
            {
                Id = 2,
                NameId = 442123,
                DescriptionId = 5100032,
                TabImageId = 9532,
                ChapterMapImageId = 0,
                GeometryId = 5,
                CompletedStringId = 442681,
            },
            new()
            {
                Id = 3,
                NameId = 3501,
                DescriptionId = 2129,
                TabImageId = 9538,
                ChapterMapImageId = 0,
                GeometryId = 8,
                CompletedStringId = 5101409,
            },
            new()
            {
                Id = 4,
                NameId = 3505,
                DescriptionId = 442685,
                TabImageId = 9529,
                ChapterMapImageId = 0,
                GeometryId = 1,
                CompletedStringId = 442686,
            }
        ];

        adventurersJournal.Regions = regions.ToDictionary(x => x.Id);

        AdventurersJournalHubDefinition[] hubs =
        [
            new()
            {
                Id = 1,
                RegionId = 1,
                DisplayOrder = 1,
                NameId = 442216,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44310,
                CompletedDescriptionId = 5100071,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 2,
                RegionId = 1,
                DisplayOrder = 2,
                NameId = 18735,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44311,
                CompletedDescriptionId = 5100072,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 3,
                RegionId = 1,
                DisplayOrder = 3,
                NameId = 5100069,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44309,
                CompletedDescriptionId = 5100073,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 4,
                RegionId = 2,
                DisplayOrder = 1,
                NameId = 7262,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44941,
                CompletedDescriptionId = 442125,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 5,
                RegionId = 2,
                DisplayOrder = 2,
                NameId = 428995,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44942,
                CompletedDescriptionId = 442126,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 6,
                RegionId = 2,
                DisplayOrder = 3,
                NameId = 442124,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44945,
                CompletedDescriptionId = 442127,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 7,
                RegionId = 2,
                DisplayOrder = 4,
                NameId = 4428,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44943,
                CompletedDescriptionId = 442128,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 8,
                RegionId = 3,
                DisplayOrder = 1,
                NameId = 5101823,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45267,
                CompletedDescriptionId = 5101824,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 9,
                RegionId = 3,
                DisplayOrder = 2,
                NameId = 5101825,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45268,
                CompletedDescriptionId = 5101826,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 10,
                RegionId = 4,
                DisplayOrder = 1,
                NameId = 442623,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45600,
                CompletedDescriptionId = 442687,
                MapX = 0,
                MapY = 0
            }
        ];

        adventurersJournal.Hubs = hubs.ToDictionary(x => x.Id);

        AdventurersJournalHubQuestDefinition[] hubQuests =
        [
            new()
            {
                HubId = 1,
                Id = 2514,
                Unknown = 2
            },
            new()
            {
                HubId = 1,
                Id = 2513,
                Unknown = 1
            },
            new()
            {
                HubId = 2,
                Id = 2521,
                Unknown = 2
            },
            new()
            {
                HubId = 2,
                Id = 2526,
                Unknown = 7
            },
            new()
            {
                HubId = 2,
                Id = 2522,
                Unknown = 3
            },
            new()
            {
                HubId = 2,
                Id = 2523,
                Unknown = 4
            },
            new()
            {
                HubId = 2,
                Id = 2524,
                Unknown = 5
            },
            new()
            {
                HubId = 2,
                Id = 2525,
                Unknown = 6
            },
            new()
            {
                HubId = 3,
                Id = 2529,
                Unknown = 3
            },
            new()
            {
                HubId = 3,
                Id = 2528,
                Unknown = 2
            },
            new()
            {
                HubId = 3,
                Id = 2527,
                Unknown = 1
            },
            new()
            {
                HubId = 3,
                Id = 2566,
                Unknown = 5
            },
            new()
            {
                HubId = 3,
                Id = 2530,
                Unknown = 4
            },
            new()
            {
                HubId = 4,
                Id = 2493,
                Unknown = 6
            },
            new()
            {
                HubId = 4,
                Id = 2492,
                Unknown = 5
            },
            new()
            {
                HubId = 4,
                Id = 2491,
                Unknown = 4
            },
            new()
            {
                HubId = 4,
                Id = 2490,
                Unknown = 3
            },
            new()
            {
                HubId = 4,
                Id = 2489,
                Unknown = 2
            },
            new()
            {
                HubId = 4,
                Id = 2538,
                Unknown = 1
            },
            new()
            {
                HubId = 5,
                Id = 2498,
                Unknown = 6
            },
            new()
            {
                HubId = 5,
                Id = 2497,
                Unknown = 5
            },
            new()
            {
                HubId = 5,
                Id = 2496,
                Unknown = 4
            },
            new()
            {
                HubId = 5,
                Id = 2495,
                Unknown = 3
            },
            new()
            {
                HubId = 5,
                Id = 2494,
                Unknown = 2
            },
            new()
            {
                HubId = 5,
                Id = 2531,
                Unknown = 1
            },
            new()
            {
                HubId = 6,
                Id = 2502,
                Unknown = 4
            },
            new()
            {
                HubId = 6,
                Id = 2501,
                Unknown = 3
            },
            new()
            {
                HubId = 6,
                Id = 2500,
                Unknown = 2
            },
            new()
            {
                HubId = 6,
                Id = 2499,
                Unknown = 1
            },
            new()
            {
                HubId = 6,
                Id = 2503,
                Unknown = 5
            },
            new()
            {
                HubId = 7,
                Id = 2533,
                Unknown = 7
            },
            new()
            {
                HubId = 7,
                Id = 2532,
                Unknown = 1
            },
            new()
            {
                HubId = 7,
                Id = 2504,
                Unknown = 2
            },
            new()
            {
                HubId = 7,
                Id = 2508,
                Unknown = 6
            },
            new()
            {
                HubId = 7,
                Id = 2507,
                Unknown = 5
            },
            new()
            {
                HubId = 7,
                Id = 2505,
                Unknown = 3
            },
            new()
            {
                HubId = 7,
                Id = 2506,
                Unknown = 4
            },
            new()
            {
                HubId = 8,
                Id = 2580,
                Unknown = 5
            },
            new()
            {
                HubId = 8,
                Id = 2578,
                Unknown = 3
            },
            new()
            {
                HubId = 8,
                Id = 2579,
                Unknown = 4
            },
            new()
            {
                HubId = 8,
                Id = 2577,
                Unknown = 2
            },
            new()
            {
                HubId = 8,
                Id = 2576,
                Unknown = 1
            },
            new()
            {
                HubId = 9,
                Id = 2585,
                Unknown = 10
            },
            new()
            {
                HubId = 9,
                Id = 2584,
                Unknown = 9
            },
            new()
            {
                HubId = 9,
                Id = 2583,
                Unknown = 8
            },
            new()
            {
                HubId = 9,
                Id = 2582,
                Unknown = 7
            },
            new()
            {
                HubId = 9,
                Id = 2581,
                Unknown = 6
            },
            new()
            {
                HubId = 9,
                Id = 2600,
                Unknown = 11
            },
            new()
            {
                HubId = 10,
                Id = 2595,
                Unknown = 6
            },
            new()
            {
                HubId = 10,
                Id = 2594,
                Unknown = 5
            },
            new()
            {
                HubId = 10,
                Id = 2591,
                Unknown = 4
            },
            new()
            {
                HubId = 10,
                Id = 2590,
                Unknown = 3
            },
            new()
            {
                HubId = 10,
                Id = 2596,
                Unknown = 7
            },
            new()
            {
                HubId = 10,
                Id = 2588,
                Unknown = 1
            },
            new()
            {
                HubId = 10,
                Id = 2599,
                Unknown = 10
            },
            new()
            {
                HubId = 10,
                Id = 2598,
                Unknown = 9
            },
            new()
            {
                HubId = 10,
                Id = 2597,
                Unknown = 8
            },
            new()
            {
                HubId = 10,
                Id = 2589,
                Unknown = 2
            }
        ];

        adventurersJournal.HubQuests = hubQuests.ToDictionary(x => x.Id);

        AdventurersJournalStickerDefinition[] stickers =
        [
            new()
            {
                Id = 1,
                RegionId = 1,
                DisplayOrder = 1,
                QuestId = 2563,
                NameId = 5100479,
                DescriptionId = 5100480,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 2,
                RegionId = 1,
                DisplayOrder = 2,
                QuestId = 2564,
                NameId = 5100483,
                DescriptionId = 5100484,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 3,
                RegionId = 1,
                DisplayOrder = 3,
                QuestId = 2565,
                NameId = 5100487,
                DescriptionId = 5100488,
                CompletedImageSetId = 43273,
                ImageSetId = 43272,
                Unknown = 0
            },
            new()
            {
                Id = 4,
                RegionId = 1,
                DisplayOrder = 4,
                QuestId = 2572,
                NameId = 5100772,
                DescriptionId = 5100773,
                CompletedImageSetId = 43281,
                ImageSetId = 43280,
                Unknown = 0
            },
            new()
            {
                Id = 5,
                RegionId = 1,
                DisplayOrder = 5,
                QuestId = 2573,
                NameId = 5100776,
                DescriptionId = 5100777,
                CompletedImageSetId = 43291,
                ImageSetId = 43290,
                Unknown = 0
            },
            new()
            {
                Id = 6,
                RegionId = 1,
                DisplayOrder = 6,
                QuestId = 2587,
                NameId = 5101187,
                DescriptionId = 5101188,
                CompletedImageSetId = 43283,
                ImageSetId = 43282,
                Unknown = 0
            },
            new()
            {
                Id = 16,
                RegionId = 2,
                DisplayOrder = 1,
                QuestId = 2568,
                NameId = 5100756,
                DescriptionId = 5100757,
                CompletedImageSetId = 43305,
                ImageSetId = 43304,
                Unknown = 0
            },
            new()
            {
                Id = 17,
                RegionId = 2,
                DisplayOrder = 2,
                QuestId = 2569,
                NameId = 5100760,
                DescriptionId = 5100761,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 18,
                RegionId = 2,
                DisplayOrder = 3,
                QuestId = 2570,
                NameId = 5100764,
                DescriptionId = 5100765,
                CompletedImageSetId = 43273,
                ImageSetId = 43272,
                Unknown = 0
            },
            new()
            {
                Id = 19,
                RegionId = 2,
                DisplayOrder = 4,
                QuestId = 2571,
                NameId = 5100768,
                DescriptionId = 5100769,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 20,
                RegionId = 2,
                DisplayOrder = 5,
                QuestId = 2574,
                NameId = 5100780,
                DescriptionId = 5100781,
                CompletedImageSetId = 43277,
                ImageSetId = 43276,
                Unknown = 0
            },
            new()
            {
                Id = 21,
                RegionId = 2,
                DisplayOrder = 6,
                QuestId = 2575,
                NameId = 5100784,
                DescriptionId = 5100785,
                CompletedImageSetId = 43283,
                ImageSetId = 43282,
                Unknown = 0
            },
            new()
            {
                Id = 32,
                RegionId = 3,
                DisplayOrder = 2,
                QuestId = 2602,
                NameId = 442851,
                DescriptionId = 442857,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 35,
                RegionId = 3,
                DisplayOrder = 5,
                QuestId = 2605,
                NameId = 442854,
                DescriptionId = 442860,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 36,
                RegionId = 3,
                DisplayOrder = 6,
                QuestId = 2606,
                NameId = 442855,
                DescriptionId = 442861,
                CompletedImageSetId = 43305,
                ImageSetId = 43304,
                Unknown = 0
            },
            new()
            {
                Id = 37,
                RegionId = 4,
                DisplayOrder = 1,
                QuestId = 2592,
                NameId = 0,
                DescriptionId = 0,
                CompletedImageSetId = 0,
                ImageSetId = 0,
                Unknown = 0
            }
        ];

        adventurersJournal.Stickers = stickers.ToDictionary(x => x.Id);

        player.SendTunneled(adventurersJournal);
    }

    private void SendWelcomeInfo(Player player)
    {
        var packetLoadWelcomeScreen = new PacketLoadWelcomeScreen();

        packetLoadWelcomeScreen.Contents.AddRange(
        [
            new ContentInfo
            {
                NameId = 6185,
                DescriptionId = 6186,
            },
            new ContentInfo
            {
                NameId = 6187,
                DescriptionId = 6188,
            },
            new ContentInfo
            {
                NameId = 6189,
                DescriptionId = 6190,
            }
        ]);

        packetLoadWelcomeScreen.ClaimCodes.AddRange(
        [
            new ClaimCodeInfo
            {
                Code = "MMMDONUT",
                NameId = 401519,
                DescriptionId = 401534,
                IconId = 929
            },
            new ClaimCodeInfo
            {
                Code = "BERRYCUPCAKE",
                NameId = 401517,
                DescriptionId = 401532,
                IconId = 939
            },
            new ClaimCodeInfo
            {
                Code = "SKELETAL",
                NameId = 409157,
                DescriptionId = 109132,
                IconId = 3459
            },
            new ClaimCodeInfo
            {
                Code = "STRAWBERRIES",
                NameId = 409158,
                DescriptionId = 108948,
                IconId = 3441
            },
            new ClaimCodeInfo
            {
                Code = "FROGGY",
                NameId = 409159,
                DescriptionId = 3141,
                IconId = 1258
            },
            new ClaimCodeInfo
            {
                Code = "SANDWICH",
                NameId = 409160,
                DescriptionId = 2430,
                IconId = 949
            },
            new ClaimCodeInfo
            {
                Code = "BOSSCAKE",
                NameId = 30109,
                DescriptionId = 30118,
                IconId = 6380
            }
        ]);

        player.SendTunneled(packetLoadWelcomeScreen);
    }

    public override void RefreshPlayerCustomizations(Player player)
    {
        SendPlayerCustomizations(player);
        player.SendTunneled(new ClientUpdatePacketDoneSendingPreloadCharacters());
    }

    private void SendPlayerCustomizations(Player player)
    {
        var playerUpdatePacketCustomizationData = new PlayerUpdatePacketCustomizationData();

        var customizations = new[]
        {
            new PlayerCustomizationData
            {
                Id = 0,
                Param = player.HeadId,
                StringParam = player.Head
            },
            new PlayerCustomizationData
            {
                Id = 1,
                Param = player.SkinToneId,
                StringParam = player.SkinTone
            },
            new PlayerCustomizationData
            {
                Id = 2,
                Param = player.HairId,
                StringParam = player.Hair
            },
            new PlayerCustomizationData
            {
                Id = 3,
                Param = player.HairColor
            },
            new PlayerCustomizationData
            {
                Id = 4,
                Param = player.EyeColor
            },
            new PlayerCustomizationData
            {
                Id = 5,
                Param = player.ModelCustomizationId,
                StringParam = player.ModelCustomization
            },
            new PlayerCustomizationData
            {
                Id = 6,
                Param = player.FacePaintId,
                StringParam = player.FacePaint
            },
            new PlayerCustomizationData
            {
                Id = 8,
                Param = player.TemporaryAppearance != 0 ? player.TemporaryAppearance : player.Model
            }
        };

        playerUpdatePacketCustomizationData.Customizations.AddRange(customizations);

        player.SendTunneled(playerUpdatePacketCustomizationData);
    }

    private void SendMembershipSubscriptionInfo(Player player)
    {
        var packetMembershipSubscriptionInfo = new PacketMembershipSubscriptionInfo
        {
            IsMember = player.MembershipStatus != 0
        };

        player.SendTunneled(packetMembershipSubscriptionInfo);
    }

    private void SendListOfActivities(Player player)
    {

        var clientActivities = _resourceManager.ClientActivityDefinitions.Values.Where(x => x.ServerType == 2).ToList();

        var activityPacketListOfActivities = new ActivityPacketListOfActivities
        {
            ServerType = 2,
            Activities = clientActivities
        };

        player.SendTunneled(activityPacketListOfActivities);

        var clientWorldActivities = _resourceManager.ClientActivityDefinitions.Values.Where(x => x.ServerType == 1).ToList();

        activityPacketListOfActivities.ServerType = 1;
        activityPacketListOfActivities.Activities = clientWorldActivities;

        player.SendTunneled(activityPacketListOfActivities);
    }

    private void SendInGamePurchase(Player player)
    {
        var packetInGamePurchaseEnableMarketplace = new PacketInGamePurchaseEnableMarketplace
        {
            Enabled = true
        };

        player.SendTunneled(packetInGamePurchaseEnableMarketplace);

        var packetInGamePurchaseStoreEnablePaymentSources = new PacketInGamePurchaseStoreEnablePaymentSources
        {
            Sms = true,
            Paypal = true
        };

        player.SendTunneled(packetInGamePurchaseStoreEnablePaymentSources);

        var packetInGamePurchaseStoreBundleCategoryGroups = new PacketInGamePurchaseStoreBundleCategoryGroups();

        packetInGamePurchaseStoreBundleCategoryGroups.CategoryGroups = _resourceManager.StoreBundleCategoryGroups.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleCategoryGroups);

        var packetInGamePurchaseStoreBundleCategories = new PacketInGamePurchaseStoreBundleCategories();

        packetInGamePurchaseStoreBundleCategories.CategoryTree.Categories = _resourceManager.StoreBundleCategories.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleCategories);

        if (_resourceManager.Stores.TryGetValue(1, out var mainStore))
        {
            var packetInGamePurchaseStoreBundles = new PacketInGamePurchaseStoreBundles();

            packetInGamePurchaseStoreBundles.StoreId = mainStore.Id;

            packetInGamePurchaseStoreBundles.Store.Id = mainStore.Id;
            packetInGamePurchaseStoreBundles.Store.NameId = mainStore.NameId;
            packetInGamePurchaseStoreBundles.Store.DescriptionId = mainStore.DescriptionId;
            packetInGamePurchaseStoreBundles.Store.Image = mainStore.Image;

            foreach (var storeBundle in mainStore.Bundles.Values)
            {
                var valid = storeBundle.Entries.All(x => _resourceManager.ClientItemDefinitions.ContainsKey(x.MarketingItemId));

                if (valid)
                    packetInGamePurchaseStoreBundles.Store.Bundles.Add(storeBundle.Id, storeBundle);
            }

            player.SendTunneled(packetInGamePurchaseStoreBundles);
        }

        var packetInGamePurchaseStoreBundleGroups = new PacketInGamePurchaseStoreBundleGroups();

        packetInGamePurchaseStoreBundleGroups.BundleGroups = _resourceManager.StoreBundleGroups.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleGroups);

        player.SendTunneled(new PromotionalBundleDataPacket());

    }

    private void SendFriendList(Player player)
    {
        var friendListPacket = new FriendListPacket();

        friendListPacket.Friends = player.Friends;

        player.SendTunneled(friendListPacket);
    }

    private void SendIgnoreList(Player player)
    {
        var ignoreListPacket = new IgnoreListPacket();

        ignoreListPacket.Ignores = player.Ignores;

        player.SendTunneled(ignoreListPacket);
    }

    private void SendPetList(Player player)
    {
        var petListPacket = new PetListPacket
        {
            Pets = player.Pets
        };

        player.SendTunneled(petListPacket);
    }

    private void UpdateFriendStatus(Player player)
    {
        var friendOnlinePacket = new FriendOnlinePacket();

        friendOnlinePacket.Guid = player.Guid;

        friendOnlinePacket.IsLocal = true;

        var friendStatusPacket = new FriendStatusPacket
        {
            Guid = player.Guid,
            Status =
            {
                ProfileId = player.ActiveProfile.Id,
                ProfileRank = player.ActiveProfile.Rank,
                ProfileIconId = player.ActiveProfile.Icon,
                ProfileNameId = player.ActiveProfile.NameId,
                ProfileBackgroundImageId = player.ActiveProfile.BadgeImageSet
            }
        };

        foreach (var friend in player.Friends)
        {
            if (!_zoneManager.TryGetPlayer(friend.Guid, out var friendPlayer))
                continue;

            var otherFriendPlayer = friendPlayer.Friends.FirstOrDefault(x => x.Guid == player.Guid);

            if (otherFriendPlayer is null || otherFriendPlayer.Online)
                continue;

            otherFriendPlayer.Online = true;

            friendPlayer.SendTunneled(friendOnlinePacket);
            friendPlayer.SendTunneled(friendStatusPacket);
        }
    }

    #endregion

    public int GetZoneAreaId(Vector4 position)
    {
        foreach (var areaDefinition in _zoneDefinition.AreaDefinitions)
        {
            if (areaDefinition.Shape == "Circle")
            {
                var circle = new Vector3(areaDefinition.X1, 0, areaDefinition.Z1);

                if (position.IsInCircle(circle, areaDefinition.Radius))
                    return areaDefinition.Id;
            }
            else if (areaDefinition.Shape == "Rectangle")
            {
                var p1 = new Vector3(areaDefinition.X1, 0, areaDefinition.Z1);
                var p2 = new Vector3(areaDefinition.X2, 0, areaDefinition.Z2);

                if (position.IsInRectangle(p1, p2))
                    return areaDefinition.Id;
            }
            else
            {
                throw new NotImplementedException(nameof(areaDefinition.Shape));
            }
        }

        return 0;
    }

    public override int GetClaimCodeItemCount(string code, int itemId)
    {
        if (string.Equals(code, "BOSSCAKE", StringComparison.OrdinalIgnoreCase) && itemId == 69828)
            return 3;
        return base.GetClaimCodeItemCount(code, itemId);
    }

    public override List<ClaimCodeInfo> GetClaimCodes()
    {
        return
        [
            new ClaimCodeInfo
            {
                Code = "MMMDONUT",
                NameId = 401519,
                DescriptionId = 401534,
                IconId = 929
            },
            new ClaimCodeInfo
            {
                Code = "BERRYCUPCAKE",
                NameId = 401517,
                DescriptionId = 401532,
                IconId = 939
            },
            new ClaimCodeInfo
            {
                Code = "SKELETAL",
                NameId = 409157,
                DescriptionId = 109132,
                IconId = 3459
            },
            new ClaimCodeInfo
            {
                Code = "STRAWBERRIES",
                NameId = 409158,
                DescriptionId = 108948,
                IconId = 3441
            },
            new ClaimCodeInfo
            {
                Code = "FROGGY",
                NameId = 409159,
                DescriptionId = 3141,
                IconId = 1258
            },
            new ClaimCodeInfo
            {
                Code = "SANDWICH",
                NameId = 409160,
                DescriptionId = 2430,
                IconId = 949
            },
            new ClaimCodeInfo
            {
                Code = "BOSSCAKE",
                NameId = 30109,
                DescriptionId = 30118,
                IconId = 6380
            }
        ];
    }
}
