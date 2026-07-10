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

            // Collect/Kill/EncounterComplete goals advance only by their own events (OnCollectInteract /
            // OnNpcKilled / OnEncounterComplete). Since they have no NPC target, GoalTargetGuid would fall
            // back to the quest's turn-in NPC - talking to it must NOT tick the goal off (that would bypass
            // the objective), so skip them here.
            if (goals[done].Type is QuestGoalType.Collect or QuestGoalType.Kill or QuestGoalType.EncounterComplete)
                continue;

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

    /// <summary>Composite effect played on a collectible when picked up (PFX_sparkles-swirl_gold_treasure-reward).</summary>
    private const int CollectPickupEffect = 5386;

    /// <summary>
    /// A collectible pickup was clicked. Credits the quest's active Collect goal (one per distinct pickup),
    /// hides the pickup for this player, animates the tracker counter, and completes the goal - advancing to
    /// the return step - once <see cref="QuestGoal.RequiredCount"/> is reached.
    /// </summary>
    public void OnCollectInteract(Player player, Npc npc)
    {
        if (!_resourceManager.Quests.Collectibles.TryGetValue(npc.Guid, out var loc))
            return;

        var (questId, goalIndex) = loc;
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        // Must have this quest active (accepted, not completed) and be ON this goal (earlier goals done).
        if (!player.Quests.TryGetValue(questId, out var completed) || completed)
            return;

        int done = player.QuestGoalProgress.TryGetValue(questId, out var progress) ? progress : 0;
        if (done != goalIndex)
            return; // not the active goal yet (a prior goal is pending) or already collected past it

        var goal = quest.EffectiveGoals[goalIndex];
        if (goal.Type != QuestGoalType.Collect)
            return;

        int required = goal.RequiredCount > 0 ? goal.RequiredCount : goal.CollectSpawns.Count;
        if (required <= 0)
            return;

        int count = (player.QuestCollectProgress.TryGetValue(questId, out var c) ? c : 0) + 1;

        _logger.LogInformation("Collect: quest={quest} goal={goal} pickup={guid} -> {count}/{required}",
            questId, goalIndex, npc.Guid, count, required);

        // Gold sparkle "reward" burst where the pickup is - immediate visual feedback that the collect
        // registered (plays before the removal so the effect's source actor still exists).
        player.SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = npc.Guid,
            CompositeEffectId = CollectPickupEffect,
            Position = npc.Position
        }, sendToSelf: true);

        // Hide this pickup for the collecting player so it can't be re-clicked. Collectibles are shared, so
        // other players still see it; a relog re-adds them all and restarts this goal's (in-memory) count.
        player.SendTunneled(new PlayerUpdatePacketRemovePlayer { Guid = npc.Guid });

        if (count >= required)
        {
            player.QuestCollectProgress.Remove(questId);
            // Final pickup -> tick the goal's checkmark and advance to the return goal (or turn in). Reuses
            // the same completion path as talk-to-NPC goals.
            CompleteGoal(player, quest, goalIndex);
        }
        else
        {
            player.QuestCollectProgress[questId] = count;
            // Animate the tracker's "current/required" counter (the client stores CurrentCount at the
            // objective's row+0xd4 and re-renders "count/required").
            player.SendTunneled(new QuestObjectiveUpdatePacket
            {
                QuestId = questId,
                ObjectiveId = goal.NameId,
                CurrentCount = count,
                CompletedPercentage = (float)count / required
            });

            // Persist so a relog mid-collect resumes at this count (done after the visual so the DB write
            // doesn't delay the on-screen feedback).
            PersistCollectCount(player, questId, count);
        }
    }

    /// <summary>
    /// An NPC died at the player's hands. Credits the active Kill goal (Type=3) of any in-progress quest
    /// whose <see cref="QuestGoal.KillNpcNameId"/> matches the victim's NameId, animating the tracker's
    /// "current/required" counter and completing the goal at <see cref="QuestGoal.RequiredCount"/>.
    /// Mirrors <see cref="OnCollectInteract"/> (same per-quest count storage + persistence).
    /// </summary>
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
                // Final kill -> tick the goal's checkmark and advance to the return step. Same completion
                // path as talk-to-NPC and collect goals.
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

                // Persist so a relog mid-hunt resumes at this count.
                PersistCollectCount(player, questId, count);
            }

            return; // one kill credits one goal
        }
    }

    /// <summary>
    /// The player won a battle-instance encounter. Completes the active EncounterComplete goal (Type=4)
    /// of any in-progress quest whose <see cref="QuestGoal.EncounterId"/> matches - i.e. the dungeon was
    /// this quest's objective. Advances to the next goal (usually "return to the giver").
    /// </summary>
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
            return; // one win credits one goal
        }
    }

    /// <summary>Persists the active Collect goal's in-progress count (DbCharacterQuest.GoalCount).</summary>
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

    /// <summary>
    /// Re-sends this quest's collectible pickups to the player so any hidden in a prior attempt reappear and
    /// are clickable again: AddNpc (re-adds the model; a no-op for one still showing) PLUS an NpcRelevance
    /// entry - that relevance packet, not just AddNpc's IsInteractable flag, is what registers a pickup as
    /// interactable client-side (this is how zone-entry wires them up). NB: no RemovePlayer first - a
    /// remove+re-add of the same guid races and can leave the pickup gone.
    /// </summary>
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
        player.QuestGoalProgress.Remove(questId); // fresh accept starts on the first goal
        player.QuestCollectProgress.Remove(questId); // and with no collect progress
        player.ActiveQuestId = questId; // a freshly accepted quest becomes the tracked one
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

        // Restore this quest's collectible pickups for the player: any collected in a PRIOR attempt were
        // hidden with RemovePlayer (which persists until relog), so without this a collect-then-abandon-then-
        // reaccept would leave fewer than RequiredCount pickups and the goal could never finish.
        RespawnQuestCollectibles(player, questId);

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
            player.ActiveQuestId = questId; // this is now the tracked quest for the arrow + "Take Me There"

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
    private void Offer(Player player, QuestDefinition quest)
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
            RewardCoins = quest.RewardCoins,
            RewardExperience = quest.RewardExperience, // job XP shown in the reward preview
            RewardItems = BuildRewardItems(quest) // item icons in the "Show Details" reward preview
        });
    }

    /// <summary>
    /// Resolves a quest's <see cref="QuestDefinition.RewardItems"/> def ids into reward-preview entries
    /// (icon + name + count) by looking up each item's ClientItemDefinition. Shown as icons in the offer
    /// and turn-in "Show Details" panels.
    /// </summary>
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

    /// <summary>
    /// Ticks off the goal at <paramref name="goalIndex"/>: sends the objective checkmark, advances the
    /// player's progress, then either activates+retargets the next goal or, when this was the last goal,
    /// hands the quest in (reward + end screen). Goals complete in order.
    /// </summary>
    private void CompleteGoal(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;

        // The final goal ticks SILENTLY (checkmark, no "Goal Complete!" banner): the "Quest Completed!" banner
        // fires right after on turn-in, and two banners back-to-back make the second wait on the first's
        // animation. Intermediate goals still banner normally.
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

        // Persist progress so a relog mid-quest resumes on the right goal.
        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == quest.QuestId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
            {
                dbQuest.GoalProgress = done;
                dbQuest.GoalCount = 0; // moving to the next goal - clear any collect count from the finished one
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
        SendObjectiveTarget(player, ResolveGoalTargetGuid(player, quest, done));

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
            RewardCoins = quest.RewardCoins,
            RewardExperience = quest.RewardExperience, // job XP shown in the reward preview
            RewardItems = BuildRewardItems(quest) // item icons in the "Show Details" reward preview
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
            var activeGoal = goals[done];

            player.SendTunneled(new QuestObjectiveActivatedPacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = activeGoal.NameId,
                RequiredCount = activeGoal.RequiredCount,
                Unknown2 = false
            });

            // If it's a count goal (Collect/Kill) with restored progress (relog mid-count), show the current
            // count so the tracker reads e.g. 3/8 instead of 0/8. Activated only sets the "required" half.
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

        // Point the tracker + "Take Me There" breadcrumb at the active goal's target NPC.
        SendObjectiveTarget(player, ResolveGoalTargetGuid(player, quest, done));
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
    /// Player-aware objective target: the NPC the tracker arrow / "Take Me There" breadcrumb should point
    /// at for the active goal. For an EncounterComplete goal this is the encounter's world giver (the
    /// Frostfang Growler wolf near spawn — the thing you click to enter the arena), whose guid is dynamic;
    /// for every other goal it's the static <see cref="GoalTargetGuid"/>.
    /// </summary>
    private static ulong ResolveGoalTargetGuid(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.EffectiveGoals;
        if (goalIndex >= 0 && goalIndex < goals.Count
            && goals[goalIndex].Type == QuestGoalType.EncounterComplete
            && player.Zone is StartingZone startingZone
            && startingZone.GrowlerWolf is { } growler)
        {
            return growler.Guid;
        }

        return GoalTargetGuid(quest, goalIndex);
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

    /// <summary>
    /// The NPC guid the tracker arrow and the "Take Me There" breadcrumb should point at: the ACTIVE goal
    /// of the player's <see cref="Player.ActiveQuestId"/> (the quest they selected in the quest helper /
    /// most recently accepted) when it's still active and its target NPC is spawned; otherwise the first
    /// active quest whose target NPC is present. Returns 0 when nothing is trackable.
    /// </summary>
    private ulong GetTrackedTargetGuid(Player player)
    {
        // Prefer the quest the player actually has selected - the whole point of "make active" is that the
        // arrow and Take Me There follow IT, not whatever quest happens to be first in storage order.
        if (player.ActiveQuestId != 0
            && player.Quests.TryGetValue(player.ActiveQuestId, out var activeCompleted) && !activeCompleted
            && TryGetGoalTargetGuid(player, player.ActiveQuestId, out var activeTarget))
        {
            return activeTarget;
        }

        // Fallback: the first active quest whose (goal-aware) target NPC is spawned in this zone.
        foreach (var (questId, completed) in player.Quests)
        {
            if (completed)
                continue;
            if (TryGetGoalTargetGuid(player, questId, out var targetGuid))
                return targetGuid;
        }

        return 0;
    }

    /// <summary>
    /// The active-goal target NPC guid for <paramref name="questId"/> (the ACTIVE goal's NPC, not the
    /// final turn-in NPC - they differ mid-quest on multi-goal quests), or false if the quest is unknown
    /// or that NPC isn't spawned in the player's current zone.
    /// </summary>
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

        // Job/profile XP - grant to the active job (updates the job's level bar).
        var experience = quest.RewardExperience;
        if (experience > 0)
            player.AwardXp(experience);

        // Reward-earned celebration (coins + XP fly-in with sound).
        if (coins > 0 || experience > 0)
            player.SendTunneled(new RewardBundlePacket { Coins = coins, Xp = experience });

        // Item rewards - defined per quest in Resources/Quests.json ("RewardItems": [id, ...]).
        foreach (var itemDefinitionId in quest.RewardItems)
        {
            GrantItem(player, itemDefinitionId);

            // "You earned an item" celebration (opcode 50/2): shows the item icon + "received 1".
            player.SendTunneled(new RewardNonBundledItemPacket { ItemDefinitionId = itemDefinitionId, Quantity = 1 });
        }
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
