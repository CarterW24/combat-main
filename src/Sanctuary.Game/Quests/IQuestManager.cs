using System.Numerics;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Quests;

/// <summary>
/// Data-driven quest engine. Drives the whole quest lifecycle (offer, accept, objective/turn-in,
/// complete, abandon, make-active, relog repopulation, world badges) from the quest definitions in
/// <see cref="IResourceManager.Quests"/>, so adding a quest is a Quests.json entry rather than code.
/// A DI singleton; every method takes the acting player, so it holds no per-player state itself.
/// </summary>
public interface IQuestManager
{
    /// <summary>True if the NPC gives or is a target of any quest (used to wire its interaction).</summary>
    bool IsQuestNpc(ulong npcGuid);

    /// <summary>Player interacted with a quest NPC: turn in an active objective, or offer an available quest.</summary>
    void OnNpcInteract(Player player, Npc npc);

    /// <summary>
    /// Player interacted with a Collect-goal pickup (a spawned collectible world object): credit the active
    /// Collect goal's count and, at the required count, tick the goal off and advance to the return step.
    /// </summary>
    void OnCollectInteract(Player player, Npc npc);

    /// <summary>Player accepted a quest offer (QuestReply, Accepted = true).</summary>
    void AcceptQuest(Player player, int questId);

    /// <summary>Player clicked "Complete" on the end screen (QuestEndReply): reward + mark complete.</summary>
    void CompleteQuest(Player player, int questId);

    /// <summary>Player dropped the quest from the journal (CommandPacketQuestAbandon).</summary>
    void AbandonQuest(Player player, int questId);

    /// <summary>Player picked the quest as their tracked quest ("Make Quest Active" / SelectQuest).</summary>
    void SetActiveQuest(Player player, int questId);

    /// <summary>On login, replay the journal/tracker packets for every in-progress quest.</summary>
    void RestoreJournal(Player player);

    /// <summary>Re-push an NPC's world badge to reflect the player's current quest state.</summary>
    void RefreshQuestNotification(Player player, ulong npcGuid);

    /// <summary>
    /// Position of the player's currently-tracked quest's target NPC, if any (a spawned turn-in NPC of an
    /// in-progress quest). Used to build the "Take Me There" path destination.
    /// </summary>
    bool TryGetActiveObjectiveTarget(Player player, out Vector3 targetPosition);
}
