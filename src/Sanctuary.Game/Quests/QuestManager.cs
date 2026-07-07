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

/// <summary>
/// Data-driven implementation of <see cref="IQuestManager"/>. Every packet sequence here is the one
/// the previously-hardcoded "Introduce Yourself" flow used (verified in-game); only the source of the
/// values changed - they now come from the <see cref="QuestDefinition"/> instead of constants.
/// </summary>
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

        // 1. Goal progression / turn-in: is this NPC the target of the ACTIVE goal of a quest the player
        // has active (accepted, not yet completed)? Talking to it ticks that goal off; the last goal hands
        // the quest in (end screen). Multi-goal quests can point intermediate goals at different NPCs, so we
        // check each active quest's current goal rather than only the quest's turn-in NPC.
        foreach (var (questId, completed) in player.Quests)
        {
            if (completed || !quests.TryGet(questId, out var activeQuest))
                continue;

            var goals = activeQuest.EffectiveGoals;
            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            if (done >= goals.Count)
                continue; // all goals already done (turn-in fires on the last goal, so this shouldn't linger)

            if (GoalTargetGuid(activeQuest, done) == npc.Guid)
            {
                CompleteGoal(player, activeQuest, done);
                return;
            }
        }

        // 2. Offer: is this NPC the giver of a quest the player can currently take?
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

    public void AcceptQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !quest.IsOfferableFor(player.Quests))
            return;

        player.Quests[questId] = false;
        player.QuestGoalProgress.Remove(questId); // fresh accept starts on the first goal
        player.LastQuestAcceptedAt = DateTime.UtcNow; // guards against a stray post-accept QuestAbandon

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

        RefreshQuestNotification(player, quest.GiverGuid);
        RefreshQuestNotification(player, quest.TargetGuid);

        // Finalize the interaction so the offer camera doesn't stay frozen on the giver (sub-opcode 29
        // recomputes the camera + dispatches QuestStartHandler:DismissEndScreen).
        player.SendTunneled(new CommandPacketQuestDialogComplete());
    }

    public void CompleteQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (player.Quests.TryGetValue(questId, out var done) && done)
            return; // already finalized

        player.Quests[questId] = true;

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

        // Clear the badges on both quest NPCs.
        RefreshQuestNotification(player, quest.GiverGuid);
        RefreshQuestNotification(player, quest.TargetGuid);

        // The next quest in the chain becomes offerable automatically (IsOfferable checks the prereq);
        // refresh its giver's badge so the "!" appears without a relog if that NPC is already spawned.
        if (quest.NextQuestId != 0 && _resourceManager.Quests.TryGet(quest.NextQuestId, out var next))
            RefreshQuestNotification(player, next.GiverGuid);

        // Clear the completed quest's tracker arrow / mini-map indicator (or re-point at another active quest).
        RefreshObjectiveTarget(player);
    }

    public void AbandonQuest(Player player, int questId)
    {
        // Ignore a stray abandon fired in the moments right after accepting (the client has been seen
        // retransmitting it around the accept flow) - that would drop a just-taken quest.
        if ((DateTime.UtcNow - player.LastQuestAcceptedAt).TotalSeconds < 3)
            return;

        // Prefer the id the client sent; if it isn't a quest the player currently has active, fall back
        // to their single active quest (guards against the client sending an unexpected id).
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

        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
            {
                db.CharacterQuests.Remove(dbQuest);
                db.SaveChanges();
            }
        }

        // Tell the client to remove the quest from the Hero's Journal, then restore the giver's "!".
        player.SendTunneled(new QuestAbandonedPacket { QuestId = questId });

        RefreshQuestNotification(player, quest.GiverGuid);
        RefreshQuestNotification(player, quest.TargetGuid);

        // Remove the now-dangling tracker arrow / mini-map indicator (re-point at another active quest, or clear).
        RefreshObjectiveTarget(player);
    }

    public void SetActiveQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (player.Quests.TryGetValue(questId, out var completed) && !completed)
        {
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

            // Point the tracker/breadcrumb at the active goal's target.
            SendObjectiveTarget(player, GoalTargetGuid(quest, done));
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

        // A plain AddNpc resend does NOT live-update an already-spawned NPC's world badge, so remove
        // the NPC and re-add it with the updated NotificationImageSetId.
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
            player.SendTunneled(new PlayerUpdatePacketRemoveNotifications { Guids = [npc.Guid] });
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

    /// <summary>Sends the quest offer popup (QuestInfoPacket) for the giver NPC.</summary>
    private static void Offer(Player player, QuestDefinition quest)
    {
        player.SendTunneled(new QuestInfoPacket
        {
            QuestId = quest.QuestId,
            // The patched SWF routes the offer's TitleId arg (reg4) into NPCText (the top speech), so the
            // giver's spoken dialogue goes here for the retail look.
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
            RewardCoins = quest.RewardCoins
        });
    }

    /// <summary>
    /// Ticks off the goal at <paramref name="goalIndex"/>: sends the objective checkmark, advances the
    /// player's progress, then either activates+retargets the next goal or, when this was the last goal,
    /// hands the quest in (reward + end screen). Goals complete in order.
    /// </summary>
    private void CompleteGoal(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;

        player.SendTunneled(new QuestObjectiveCompletePacket
        {
            QuestId = quest.QuestId,
            ObjectiveId = goals[goalIndex].NameId,
            Percent = 1f,
            Silent = false // real completion -> show the "Goal complete!" banner
        });

        int done = goalIndex + 1;
        player.QuestGoalProgress[quest.QuestId] = done;

        // Persist progress so a relog mid-quest resumes on the right goal.
        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == quest.QuestId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
            {
                dbQuest.GoalProgress = done;
                db.SaveChanges();
            }
        }

        if (done >= goals.Count)
        {
            // Final goal done -> hand in (reward + "Quest Complete" end screen).
            TurnIn(player, quest);
            return;
        }

        // More goals to go: activate the next one and re-point the tracker/breadcrumb at its NPC.
        player.SendTunneled(new QuestObjectiveActivatedPacket
        {
            QuestId = quest.QuestId,
            ObjectiveId = goals[done].NameId,
            RequiredCount = goals[done].RequiredCount,
            Unknown2 = false
        });
        SendObjectiveTarget(player, GoalTargetGuid(quest, done));

        // Mid-quest NPC reply via the stock conversation dialog (CommandPacketShowDialog, 26/3): a speech
        // bubble with a green-check response button, HTML-rendered (colored <font> tags show), NO details
        // box, NO journal touch (so no duplicate). Camera focuses the NPC (CameraFocusParam) and restores
        // on the button click (client sends 26/6 -> BaseCommandPacketHandler replies with EndDialog).
        var completedGoal = goals[goalIndex];
        if (completedGoal.DialogueId != 0)
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
                LabelTextId = YouGotItTextId, // "You got it!"
                Param1 = GreenCheckImageId,   // node+0x14 -> button icon = green checkmark (confirmed in-game)
                Param2 = GreenButtonImageSet, // node+0x18 -> button skin = "dialog green button" imageSet
            });
            player.SendTunneled(dialog);
        }
    }

    /// <summary>Global.Text id for the generic "You got it!" response button.</summary>
    private const int YouGotItTextId = 103085;

    /// <summary>Image id of ui_dialog_greencheck (Images.txt) - the response button's green check icon.</summary>
    private const int GreenCheckImageId = 300;

    /// <summary>ImageSet id 17 = "dialog green button" (ImageSets.txt) - the green response-button skin.</summary>
    private const int GreenButtonImageSet = 17;

    /// <summary>Shows the "Quest Complete" end screen; finalize happens on the Complete click. The completing
    /// goal's checkmark is already sent by <see cref="CompleteGoal"/> before this is called.</summary>
    private void TurnIn(Player player, QuestDefinition quest)
    {
        // No QuestAdd re-send here: the end screen's bubble reads live QuestData column 10, which was set
        // to TurnInDialogueId by SendActiveState at accept and is no longer changed mid-quest, so it's
        // already correct. Re-sending QuestAdd would APPEND a duplicate journal row (the client never
        // dedupes) that completion then can't fully clear - the bug that left finished quests in the journal.
        player.SendTunneled(new QuestEndPacket
        {
            // Camera focus = the LAST goal's NPC (where hand-in happens). For single-goal quests this is
            // quest.TargetGuid; for multi-goal it's the final goal's target (e.g. back at the giver).
            NpcGuid = GoalTargetGuid(quest, quest.EffectiveGoals.Count - 1),
            QuestId = quest.QuestId,
            // With the ScriptsBase details-split applied, the end screen's speech bubble reads
            // SetNPCDialog(showEndText), and showEndText is fed by THIS packet's TitleId field (verified
            // in-game: the bubble showed whatever went here). So put the turn-in DIALOGUE here. The panel
            // title + "Show Details" description come from QuestData columns 1/2 (set by SendActiveState:
            // col1=TitleId title, col2=ObjectiveDescriptionId objective), independent of this packet.
            TitleId = quest.TurnInDialogueId, // -> showEndText -> speech bubble = the NPC's turn-in line
            DescriptionId = quest.TitleId,    // -> showEndId (not rendered as text); harmless
            RewardCoins = quest.RewardCoins
        });

        // Reward/completion is applied when the player clicks "Complete" (QuestEndReply invokes this).
        player.PendingQuestEndAction = () => CompleteQuest(player, quest.QuestId);
    }

    /// <summary>
    /// The journal/tracker entry. HelperTextId (client QuestData column 10) is read ONLY by the
    /// end screen's speech bubble - a patched ScriptsBase.bin points ShowEndScreen's SetNPCDialog at
    /// column 10 instead of DescriptionId, decoupling it from the journal. It's read LIVE each time
    /// an end screen shows, so re-sending this packet (the client updates an existing journal entry
    /// in place) swaps the bubble text: intermediate goal dialogs pass that goal's DialogueId,
    /// accept/turn-in pass <see cref="QuestDefinition.TurnInDialogueId"/>.
    /// </summary>
    private static void SendQuestAdd(Player player, QuestDefinition quest, int helperTextId, float completedPercentage = 0f)
    {
        player.SendTunneled(new QuestAddPacket
        {
            QuestId = quest.QuestId,
            TitleId = quest.TitleId,
            // DescriptionId (client QuestData col 2) feeds BOTH the on-screen tracker's header line AND the
            // StoryBook journal's right-page description. Use the objective ("Introduce yourself to X in Y")
            // so the tracker header reads as the objective; the shorter sub-goal ("Talk to X") is the goal
            // row (QuestObjectiveAddedPacket, from the goal's NameId). They share this one client slot, so
            // the journal description shows the objective too rather than the longer flavour DescriptionId.
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

    /// <summary>QuestAdd + objective packets that put the quest into the client's journal + tracker.</summary>
    private void SendActiveState(Player player, QuestDefinition quest)
    {
        int alreadyDone = player.QuestGoalProgress.TryGetValue(quest.QuestId, out var p) ? p : 0;
        SendQuestAdd(player, quest, quest.TurnInDialogueId, (float)alreadyDone / quest.EffectiveGoals.Count);

        // One objective row per goal (the client keys them by add-order into QuestObjectiveData, which the
        // tracker renders as a checklist). Goals tick off in order; re-sending on relog replays completed
        // goals as ticked so the checklist and active goal are restored.
        var goals = quest.EffectiveGoals;
        int done = player.QuestGoalProgress.TryGetValue(quest.QuestId, out var progress) ? progress : 0;

        for (int i = 0; i < goals.Count; i++)
        {
            player.SendTunneled(new QuestObjectiveAddedPacket
            {
                QuestId = quest.QuestId,
                // Body int0 is the objective's IDENTITY (the client hashes rows by it - traced
                // FUN_00bab950: row+0xf0 = int0) AND its name text id; Activated/Complete find the row
                // by sending the same value as ObjectiveId. Goal NameIds must therefore be unique
                // within a quest. (A raw index here broke everything: id 0 rendered as
                // "<STRING 0 NOT FOUND>" and the Activated/Complete lookups missed, so checkmarks and
                // goal advance never showed client-side.)
                ObjectiveNameId = goals[i].NameId,
                // The tracker goal row renders from body int1 ("Talk to Shakey").
                ObjectiveDescriptionId = goals[i].NameId,
                // Body int2 = the journal "Objectives" sub-line ("Shakey should be hanging out in
                // front of the Wildwood Speedway...").
                ObjectiveField2 = goals[i].DescriptionId != 0 ? goals[i].DescriptionId : goals[i].NameId
            });
        }

        // Replay already-completed goals as ticked (restores checkmarks after relog).
        for (int i = 0; i < done && i < goals.Count; i++)
        {
            player.SendTunneled(new QuestObjectiveCompletePacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = goals[i].NameId,
                Percent = 1f,
                Silent = true // relog replay -> tick the checkmark but don't re-banner old goals
            });
        }

        // Activate the current goal (the first not-yet-done one).
        if (done < goals.Count)
        {
            player.SendTunneled(new QuestObjectiveActivatedPacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = goals[done].NameId,
                RequiredCount = goals[done].RequiredCount,
                Unknown2 = false
            });
        }

        // Point the tracker + "Take Me There" breadcrumb at the active goal's target NPC.
        SendObjectiveTarget(player, GoalTargetGuid(quest, done));
    }

    /// <summary>
    /// The NPC guid the goal at <paramref name="goalIndex"/> points at: the goal's own TargetGuid, or the
    /// quest's turn-in TargetGuid when the goal doesn't override it (or when all goals are already done).
    /// </summary>
    private static ulong GoalTargetGuid(QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;
        if (goalIndex >= 0 && goalIndex < goals.Count && goals[goalIndex].TargetGuid != 0)
            return goals[goalIndex].TargetGuid;
        return quest.TargetGuid;
    }

    /// <summary>
    /// Sends the ObjectiveTargetUpdatePacket that drives the tracker arrow, mini-map indicator and the
    /// "Take Me There" green breadcrumb trail. Target is the given NPC guid (the active goal's NPC); if it
    /// isn't spawned in the player's current zone we send nothing (no destination to point at).
    /// </summary>
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
            // Display name shown on the tracker/mini-map indicator; the client resolves this id to the
            // label (0/invalid renders the "Default Housing NPC" fallback).
            NameId = target.NameId,
            PositionX = pos.X,
            PositionY = pos.Y,
            PositionZ = pos.Z,
            PositionW = 1f
        });
    }

    /// <summary>
    /// Re-points the objective tracker/mini-map indicator at a still-active quest whose target NPC is
    /// present, or clears it entirely (Active=false) when no trackable quest remains. Call after a quest
    /// leaves the active set (abandon/complete) so a dangling indicator doesn't stay on screen.
    /// </summary>
    private void RefreshObjectiveTarget(Player player)
    {
        foreach (var (questId, completed) in player.Quests)
        {
            if (completed || !_resourceManager.Quests.TryGet(questId, out var quest))
                continue;

            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            ulong targetGuid = GoalTargetGuid(quest, done);
            if (targetGuid != 0 && player.Zone.TryGetNpc(targetGuid, out _))
            {
                SendObjectiveTarget(player, targetGuid);
                return;
            }
        }

        player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
    }

    public bool TryGetActiveObjectiveTarget(Player player, out Vector3 targetPosition)
    {
        foreach (var (questId, completed) in player.Quests)
        {
            if (completed || !_resourceManager.Quests.TryGet(questId, out var quest))
                continue;

            // Same goal-aware target the tracker arrow uses: the ACTIVE goal's NPC, not the quest's
            // final turn-in NPC (they differ mid-quest on multi-goal quests).
            int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
            ulong targetGuid = GoalTargetGuid(quest, done);
            if (targetGuid != 0 && player.Zone.TryGetNpc(targetGuid, out var target))
            {
                var pos = target.Position;
                targetPosition = new Vector3(pos.X, pos.Y, pos.Z);
                return true;
            }
        }

        targetPosition = default;
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

            // Reward-earned celebration (coins fly-in + sound) then the updated coin total.
            player.SendTunneled(new RewardBundlePacket { RewardCoins = coins });
            player.SendTunneled(new ClientUpdatePacketCoinCount { Coins = newTotal });
        }

        // Item rewards - defined per quest in Resources/Quests.json ("RewardItems": [id, ...]).
        foreach (var itemDefinitionId in quest.RewardItems)
            GrantItem(player, itemDefinitionId);
    }

    /// <summary>
    /// Grants one of <paramref name="definitionId"/> to the player: stacks it in the DB (by definition +
    /// tint), mirrors it into the in-memory inventory, and tells the client (ItemAdd for a new item, or
    /// ItemUpdate for an incremented stack). Mirrors the coin-store grant path.
    /// </summary>
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
