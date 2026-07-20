using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Quests;

public sealed class QuestManager : IQuestManager
{
    private readonly IResourceManager _resourceManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly ILogger<QuestManager> _logger;

    public QuestManager(IResourceManager resourceManager, IDbContextFactory<DatabaseContext> dbContextFactory, ILogger<QuestManager> logger)
    {
        _resourceManager = resourceManager;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public bool IsQuestNpc(ulong npcGuid)
        => _resourceManager.Quests.ByGiver.ContainsKey(npcGuid) || _resourceManager.Quests.ByTarget.ContainsKey(npcGuid);

    public void OnNpcInteract(Player player, Npc npc)
    {
        var quests = _resourceManager.Quests;

        foreach (var (questId, completed) in player.Quests)
        {
            if (completed || !quests.TryGet(questId, out var activeQuest))
                continue;

            var goals = activeQuest.EffectiveGoals;
            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            if (done >= goals.Count)
                continue;

            if (goals[done].Type is QuestGoalType.Collect or QuestGoalType.Kill or QuestGoalType.EncounterComplete)
                continue;

            if (GoalTargetGuid(activeQuest, done) == npc.Guid)
            {
                CompleteGoal(player, activeQuest, done);
                return;
            }
        }

        if (quests.ByGiver.TryGetValue(npc.Guid, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var offerableQuest) && offerableQuest.IsOfferableFor(player.Quests))
                {
                    Offer(player, offerableQuest);
                    return;
                }
            }
        }
    }

    private const int CollectPickupEffect = 5386;

    public void OnCollectInteract(Player player, Npc npc)
    {
        if (!_resourceManager.Quests.Collectibles.TryGetValue(npc.Guid, out var loc))
            return;

        var (questId, goalIndex) = loc;
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (!player.Quests.TryGetValue(questId, out var completed) || completed)
            return;

        int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
        if (done != goalIndex)
            return;

        var goal = quest.EffectiveGoals[goalIndex];
        if (goal.Type != QuestGoalType.Collect)
            return;

        int required = goal.RequiredCount > 0 ? goal.RequiredCount : goal.CollectSpawns.Count;
        if (required <= 0)
            return;

        int count = (player.QuestCollectProgress.TryGetValue(questId, out var c) ? c : 0) + 1;

        _logger.LogInformation("Collect: quest={quest} goal={goal} pickup={guid} -> {count}/{required}",
            questId, goalIndex, npc.Guid, count, required);

        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = npc.Guid,
            CompositeEffectId = CollectPickupEffect,
            Position = npc.Position
        }, sendToSelf: true);

        player.SendTunneled(new PlayerUpdatePacketRemovePlayer { Guid = npc.Guid });

        if (count >= required)
        {
            player.QuestCollectProgress.Remove(questId);
            CompleteGoal(player, quest, goalIndex);
        }
        else
        {
            player.QuestCollectProgress[questId] = count;
            player.SendTunneled(new QuestObjectiveUpdatePacket
            {
                QuestId = questId,
                ObjectiveId = goal.NameId,
                CurrentCount = count,
                CompletedPercentage = (float)count / required
            });

            PersistCollectCount(player, questId, count);
        }
    }

    public void OnNpcKilled(Player player, Npc npc)
    {
        if (npc.NameId == 0)
            return;

        foreach (var (questId, completed) in player.Quests)
        {
            if (completed || !_resourceManager.Quests.TryGet(questId, out var quest))
                continue;

            var goals = quest.EffectiveGoals;
            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            if (done >= goals.Count)
                continue;

            var goal = goals[done];
            if (goal.Type != QuestGoalType.Kill || goal.KillNpcNameId != npc.NameId)
                continue;

            int required = goal.RequiredCount > 0 ? goal.RequiredCount : 1;
            int count = (player.QuestCollectProgress.TryGetValue(questId, out var c) ? c : 0) + 1;

            _logger.LogInformation("Kill goal: quest={quest} goal={goal} victim nameId={nameId} -> {count}/{required}",
                questId, done, npc.NameId, count, required);

            if (count >= required)
            {
                player.QuestCollectProgress.Remove(questId);
                CompleteGoal(player, quest, done);
            }
            else
            {
                player.QuestCollectProgress[questId] = count;
                player.SendTunneled(new QuestObjectiveUpdatePacket
                {
                    QuestId = questId,
                    ObjectiveId = goal.NameId,
                    CurrentCount = count,
                    CompletedPercentage = (float)count / required
                });

                PersistCollectCount(player, questId, count);
            }

            return;
        }
    }

    public void OnEncounterComplete(Player player, int encounterId)
    {
        foreach (var (questId, completed) in player.Quests)
        {
            if (completed || !_resourceManager.Quests.TryGet(questId, out var quest))
                continue;

            var goals = quest.EffectiveGoals;
            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            if (done >= goals.Count)
                continue;

            var goal = goals[done];
            if (goal.Type != QuestGoalType.EncounterComplete || goal.EncounterId != encounterId)
                continue;

            _logger.LogInformation("Encounter goal: quest={quest} goal={goal} encounter={enc} completed.",
                questId, done, encounterId);

            CompleteGoal(player, quest, done);
            return;
        }
    }

    private void PersistCollectCount(Player player, int questId, int count)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
        if (dbQuest is not null)
        {
            dbQuest.GoalCount = count;
            db.SaveChanges();
        }
    }

    private void RespawnQuestCollectibles(Player player, int questId)
    {
        var relevance = new PlayerUpdatePacketNpcRelevance();

        foreach (var entry in _resourceManager.Quests.Collectibles)
        {
            if (entry.Value.QuestId != questId)
                continue;
            if (!player.Zone.TryGetNpc(entry.Key, out var npc))
                continue;

            player.SendTunneled(npc.GetAddNpcPacket());

            if (npc.CursorId != 0)
            {
                relevance.Entries.Add(new PlayerUpdatePacketNpcRelevance.Entry
                {
                    Guid = npc.Guid,
                    Unknown = true,
                    CursorId = npc.CursorId,
                    HasCursor = true
                });
            }
        }

        if (relevance.Entries.Count > 0)
            player.SendTunneled(relevance);
    }

    public void AcceptQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !quest.IsOfferableFor(player.Quests))
            return;

        player.Quests[questId] = false;
        player.QuestGoalProgress.Remove(questId);
        player.QuestCollectProgress.Remove(questId);
        player.ActiveQuestId = questId;
        player.LastQuestAcceptedAt = DateTime.UtcNow;

        using (var db = _dbContextFactory.CreateDbContext())
        {
            db.CharacterQuests.Add(new DbCharacterQuest
            {
                QuestId = questId,
                CharacterId = player.CharacterId,
                Completed = false
            });
            db.SaveChanges();
        }

        SendActiveState(player, quest);

        RespawnQuestCollectibles(player, questId);

        RefreshQuestNotification(player, quest.GiverGuid);
        RefreshQuestNotification(player, quest.TargetGuid);

        player.SendTunneled(new CommandPacketQuestDialogComplete());
    }

    public void CompleteQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (player.Quests.TryGetValue(questId, out var done) && done)
            return;

        player.Quests[questId] = true;
        player.QuestCollectProgress.Remove(questId);

        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
            {
                dbQuest.Completed = true;
                db.SaveChanges();
            }
        }

        player.SendTunneled(new QuestCompletePacket { QuestId = questId });

        GrantReward(player, quest);

        RefreshQuestNotification(player, quest.GiverGuid);
        RefreshQuestNotification(player, quest.TargetGuid);

        if (quest.NextQuestId != 0 && _resourceManager.Quests.TryGet(quest.NextQuestId, out var next))
            RefreshQuestNotification(player, next.GiverGuid);

        RefreshObjectiveTarget(player);
    }

    public void AbandonQuest(Player player, int questId)
    {
        if ((DateTime.UtcNow - player.LastQuestAcceptedAt).TotalSeconds < 3)
            return;

        if (!(player.Quests.TryGetValue(questId, out var completed) && !completed))
        {
            var active = player.Quests.Where(entry => !entry.Value).Select(entry => entry.Key).ToList();
            if (active.Count != 1)
                return;

            questId = active[0];
        }

        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        player.Quests.Remove(questId);
        player.QuestCollectProgress.Remove(questId);

        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
            {
                db.CharacterQuests.Remove(dbQuest);
                db.SaveChanges();
            }
        }

        player.SendTunneled(new QuestAbandonedPacket { QuestId = questId });

        RefreshQuestNotification(player, quest.GiverGuid);
        RefreshQuestNotification(player, quest.TargetGuid);

        RefreshObjectiveTarget(player);
    }

    public void SetActiveQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (player.Quests.TryGetValue(questId, out var completed) && !completed)
        {
            player.ActiveQuestId = questId;

            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            var goals = quest.EffectiveGoals;

            if (done < goals.Count)
            {
                player.SendTunneled(new QuestObjectiveActivatedPacket
                {
                    QuestId = questId,
                    ObjectiveId = goals[done].NameId,
                    RequiredCount = goals[done].RequiredCount,
                    Unknown2 = false
                });
            }

            SendObjectiveTarget(player, ResolveGoalTargetGuid(player, quest, done));
        }
    }

    public void RestoreJournal(Player player)
    {
        foreach (var (questId, completed) in player.Quests)
        {
            if (!completed && _resourceManager.Quests.TryGet(questId, out var quest))
                SendActiveState(player, quest);
        }
    }

    public void RefreshQuestNotification(Player player, ulong npcGuid)
    {
        if (npcGuid == 0 || !player.Zone.TryGetNpc(npcGuid, out var npc))
            return;

        var imageId = player.GetNotificationImageId(npc);

        player.SendTunneled(new PlayerUpdatePacketRemovePlayer { Guid = npc.Guid });

        var addNpcPacket = npc.GetAddNpcPacket();
        addNpcPacket.NotificationImageSetId = imageId;
        player.SendTunneled(addNpcPacket);

        if (npc.CursorId != 0)
        {
            var relevance = new PlayerUpdatePacketNpcRelevance();
            relevance.Entries.Add(new PlayerUpdatePacketNpcRelevance.Entry
            {
                Guid = npc.Guid,
                Unknown = true,
                CursorId = npc.CursorId,
                HasCursor = imageId != 0
            });
            player.SendTunneled(relevance);
        }

        if (imageId == 0)
        {
            player.SendTunneled(new PlayerUpdatePacketRemoveNotifications { Entries = [new() { Guid = npc.Guid }] });
            return;
        }

        var notifications = new PlayerUpdatePacketAddNotifications();
        notifications.Notifications.Add(new NotificationInfo
        {
            Guid = npc.Guid,
            Combat = false,
            ImageId = imageId,
            NameId = npc.NameId,
            SubTextId = npc.SubTextNameId,
        });
        player.SendTunneled(notifications);
    }

    private void Offer(Player player, QuestDefinition quest)
    {
        player.SendTunneled(new QuestInfoPacket
        {
            QuestId = quest.QuestId,
            TitleId = quest.GiverDialogueId,
            DescriptionId = quest.DescriptionId,
            HelperTextId = quest.ObjectiveDescriptionId,
            IconId = quest.IconId,
            Unknown6 = quest.ObjectiveDescriptionId, // offer "Goals" list
            Unknown7 = false,
            NpcGuid = quest.GiverGuid,
            Unknown10 = 0,
            Unknown11 = false,
            Unknown12 = false,
            RewardCoins = quest.RewardCoins,
            RewardExperience = quest.RewardExperience,
            RewardItems = BuildRewardItems(quest)
        });
    }

    private List<RewardBundleItem> BuildRewardItems(QuestDefinition quest)
    {
        var items = new List<RewardBundleItem>();
        foreach (var definitionId in quest.RewardItems)
        {
            if (_resourceManager.ClientItemDefinitions.TryGetValue(definitionId, out var itemDef))
            {
                items.Add(new RewardBundleItem
                {
                    IconId = itemDef.Icon.Id,
                    NameId = itemDef.NameId,
                    Count = 1
                });
            }
        }
        return items;
    }

    private void CompleteGoal(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;

        bool isFinalGoal = goalIndex + 1 >= goals.Count;

        player.SendTunneled(new QuestObjectiveCompletePacket
        {
            QuestId = quest.QuestId,
            ObjectiveId = goals[goalIndex].NameId,
            Percent = 1f,
            Silent = isFinalGoal
        });

        int done = goalIndex + 1;
        player.QuestGoalProgress[quest.QuestId] = done;

        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == quest.QuestId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
            {
                dbQuest.GoalProgress = done;
                dbQuest.GoalCount = 0;
                db.SaveChanges();
            }
        }

        if (done >= goals.Count)
        {
            TurnIn(player, quest);
            return;
        }

        player.SendTunneled(new QuestObjectiveActivatedPacket
        {
            QuestId = quest.QuestId,
            ObjectiveId = goals[done].NameId,
            RequiredCount = goals[done].RequiredCount,
            Unknown2 = false
        });
        SendObjectiveTarget(player, ResolveGoalTargetGuid(player, quest, done));

        var completedGoal = goals[goalIndex];
        if (completedGoal.DialogueId != 0 && completedGoal.Type == QuestGoalType.TalkToNpc)
        {
            var dialog = new CommandPacketShowDialog
            {
                DialogueTextId = completedGoal.DialogueId,
                NpcGuid = GoalTargetGuid(quest, goalIndex),
                CameraFocusParam = 1f,
            };

            dialog.Responses.Add(new CommandPacketShowDialog.Response
            {
                Id = 1,
                LabelTextId = YouGotItTextId,
                Param1 = GreenCheckImageId,
                Param2 = GreenButtonImageSet,
            });
            player.SendTunneled(dialog);
        }
    }

    private const int YouGotItTextId = 103085;

    private const int GreenCheckImageId = 300;

    private const int GreenButtonImageSet = 17;

    private void TurnIn(Player player, QuestDefinition quest)
    {
        player.SendTunneled(new QuestEndPacket
        {
            NpcGuid = GoalTargetGuid(quest, quest.EffectiveGoals.Count - 1),
            QuestId = quest.QuestId,
            TitleId = quest.TurnInDialogueId,
            DescriptionId = quest.TitleId,
            RewardCoins = quest.RewardCoins,
            RewardExperience = quest.RewardExperience,
            RewardItems = BuildRewardItems(quest)
        });

        player.PendingQuestEndAction = () => CompleteQuest(player, quest.QuestId);
    }

    private static void SendQuestAdd(Player player, QuestDefinition quest, int helperTextId, float completedPercentage = 0f)
    {
        player.SendTunneled(new QuestAddPacket
        {
            QuestId = quest.QuestId,
            TitleId = quest.TitleId,
            DescriptionId = quest.ObjectiveDescriptionId,
            HelperTextId = helperTextId,
            MembersOnly = false,
            TimeStarted = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ProfileId = 0,
            CompletedPercentage = completedPercentage,
            IconId = quest.IconId,
            SystemQuest = false
        });
    }

    private void SendActiveState(Player player, QuestDefinition quest)
    {
        int alreadyDone = player.QuestGoalProgress.TryGetValue(quest.QuestId, out var p) ? p : 0;
        SendQuestAdd(player, quest, quest.TurnInDialogueId, (float)alreadyDone / quest.EffectiveGoals.Count);

        var goals = quest.EffectiveGoals;
        int done = player.QuestGoalProgress.TryGetValue(quest.QuestId, out var progress) ? progress : 0;

        for (int i = 0; i < goals.Count; i++)
        {
            player.SendTunneled(new QuestObjectiveAddedPacket
            {
                QuestId = quest.QuestId,
                ObjectiveNameId = goals[i].NameId,
                ObjectiveDescriptionId = goals[i].NameId,
                ObjectiveField2 = goals[i].DescriptionId != 0 ? goals[i].DescriptionId : goals[i].NameId
            });
        }

        for (int i = 0; i < done && i < goals.Count; i++)
        {
            player.SendTunneled(new QuestObjectiveCompletePacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = goals[i].NameId,
                Percent = 1f,
                Silent = true
            });
        }

        if (done < goals.Count)
        {
            var activeGoal = goals[done];

            player.SendTunneled(new QuestObjectiveActivatedPacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = activeGoal.NameId,
                RequiredCount = activeGoal.RequiredCount,
                Unknown2 = false
            });

            if (activeGoal.Type is QuestGoalType.Collect or QuestGoalType.Kill
                && player.QuestCollectProgress.TryGetValue(quest.QuestId, out var collected) && collected > 0)
            {
                int req = activeGoal.RequiredCount > 0 ? activeGoal.RequiredCount : activeGoal.CollectSpawns.Count;
                player.SendTunneled(new QuestObjectiveUpdatePacket
                {
                    QuestId = quest.QuestId,
                    ObjectiveId = activeGoal.NameId,
                    CurrentCount = collected,
                    CompletedPercentage = req > 0 ? (float)collected / req : 0f
                });
            }
        }

        SendObjectiveTarget(player, ResolveGoalTargetGuid(player, quest, done));
    }

    private static ulong GoalTargetGuid(QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;
        if (goalIndex >= 0 && goalIndex < goals.Count && goals[goalIndex].TargetGuid != 0)
            return goals[goalIndex].TargetGuid;
        return quest.TargetGuid;
    }

    private static ulong ResolveGoalTargetGuid(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;
        if (goalIndex >= 0 && goalIndex < goals.Count
            && goals[goalIndex].Type == QuestGoalType.EncounterComplete
            && player.Zone is StartingZone startingZone)
        {
            var entry = goals[goalIndex].EncounterId switch
            {
                Zones.FrostfangArenaZone.EncounterId => startingZone.GrowlerWolf,
                Zones.TormentedSpiritsArenaZone.EncounterId => startingZone.TormentedSpiritEntry(),
                _ => null,
            };
            if (entry is not null)
                return entry.Guid;
        }

        return GoalTargetGuid(quest, goalIndex);
    }

    private void SendObjectiveTarget(Player player, ulong targetGuid)
    {
        if (targetGuid == 0 || !player.Zone.TryGetNpc(targetGuid, out var target))
            return;

        var pos = target.Position;
        var zoneAreaId = player.Zone is StartingZone startingZone
            ? startingZone.GetZoneAreaId(pos)
            : player.Zone.Id;

        player.SendTunneled(new ObjectiveTargetUpdatePacket
        {
            Active = true,
            LocationX = pos.X,
            LocationZ = pos.Z,
            ZoneId = zoneAreaId,
            Guid = targetGuid,
            NameId = target.NameId,
            PositionX = pos.X,
            PositionY = pos.Y,
            PositionZ = pos.Z,
            PositionW = 1f
        });
    }

    public void RefreshObjectiveTarget(Player player)
    {
        ulong targetGuid = GetTrackedTargetGuid(player);
        if (targetGuid != 0)
            SendObjectiveTarget(player, targetGuid);
        else
            player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
    }

    public bool TryGetActiveObjectiveTarget(Player player, out Vector3 targetPosition)
    {
        ulong targetGuid = GetTrackedTargetGuid(player);
        if (targetGuid != 0 && player.Zone.TryGetNpc(targetGuid, out var target))
        {
            var pos = target.Position;
            targetPosition = new Vector3(pos.X, pos.Y, pos.Z);
            return true;
        }

        targetPosition = default;
        return false;
    }

    private ulong GetTrackedTargetGuid(Player player)
    {
        if (player.ActiveQuestId != 0
            && player.Quests.TryGetValue(player.ActiveQuestId, out var activeCompleted) && !activeCompleted
            && TryGetGoalTargetGuid(player, player.ActiveQuestId, out var activeTarget))
        {
            return activeTarget;
        }

        foreach (var (questId, completed) in player.Quests)
        {
            if (completed)
                continue;
            if (TryGetGoalTargetGuid(player, questId, out var targetGuid))
                return targetGuid;
        }

        return 0;
    }

    private bool TryGetGoalTargetGuid(Player player, int questId, out ulong targetGuid)
    {
        targetGuid = 0;
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return false;

        int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
        ulong guid = ResolveGoalTargetGuid(player, quest, done);
        if (guid != 0 && player.Zone.TryGetNpc(guid, out _))
        {
            targetGuid = guid;
            return true;
        }
        return false;
    }

    private void GrantReward(Player player, QuestDefinition quest)
    {
        var coins = quest.RewardCoins;
        if (coins > 0)
        {
            int newTotal;
            using (var db = _dbContextFactory.CreateDbContext())
            {
                var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
                if (dbCharacter is null)
                    return;

                dbCharacter.Coins += coins;
                db.SaveChanges();
                newTotal = dbCharacter.Coins;
            }

            player.Coins = newTotal;
            player.SendTunneled(new ClientUpdatePacketCoinCount { Coins = newTotal });
        }

        var experience = quest.RewardExperience;
        if (experience > 0)
            player.AwardXp(experience);

        if (coins > 0 || experience > 0)
            player.SendTunneled(new RewardBundlePacket { Coins = coins, Xp = experience });

        foreach (var itemDefinitionId in quest.RewardItems)
        {
            GrantItem(player, itemDefinitionId);

            player.SendTunneled(new RewardNonBundledItemPacket { ItemDefinitionId = itemDefinitionId, Quantity = 1 });
        }
    }

    private void GrantItem(Player player, int definitionId)
    {
        if (!_resourceManager.ClientItemDefinitions.TryGetValue(definitionId, out var itemDef))
            return;

        int tint = itemDef.IsTintable ? 0 : itemDef.Icon.TintId;

        int itemId, count;
        using (var db = _dbContextFactory.CreateDbContext())
        {
            var row = db.Characters
                .Where(c => c.Id == player.CharacterId)
                .Select(c => new
                {
                    Character = c,
                    Item = c.Items.FirstOrDefault(i => i.Definition == definitionId && i.Tint == tint),
                    NextId = c.Items.Max(i => (int?)i.Id) ?? 0
                })
                .FirstOrDefault();

            if (row is null)
                return;

            if (row.Item is not null)
            {
                row.Item.Count += 1;
                itemId = row.Item.Id;
                count = row.Item.Count;
            }
            else
            {
                var dbItem = new DbItem { Id = row.NextId + 1, Definition = definitionId, Tint = tint, Count = 1 };
                row.Character.Items.Add(dbItem);
                itemId = dbItem.Id;
                count = 1;
            }

            db.SaveChanges();
        }

        var clientItem = player.Items.FirstOrDefault(x => x.Definition == definitionId && x.Tint == tint);
        if (clientItem is not null)
        {
            clientItem.Count = count;
            player.SendTunneled(new ClientUpdatePacketItemUpdate { ItemGuid = clientItem.Id, Count = clientItem.Count });
        }
        else
        {
            clientItem = new ClientItem { Id = itemId, Tint = tint, Count = count, Definition = definitionId };
            player.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);
            itemDef.Serialize(writer);
            player.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });
        }
    }
}
