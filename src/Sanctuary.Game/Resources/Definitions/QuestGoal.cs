using System.Collections.Generic;

namespace Sanctuary.Game.Resources.Definitions;

/// <summary>
/// How a <see cref="QuestGoal"/> is completed. Drives which server event ticks the goal off.
/// Only <see cref="TalkToNpc"/> is wired today (the "talk to an NPC" quest shape); the others are
/// placeholders for the richer objective types FR uses (reach a place, collect/kill counts).
/// </summary>
public enum QuestGoalType
{
    /// <summary>Completes when the player interacts with <see cref="QuestGoal.TargetGuid"/>.</summary>
    TalkToNpc = 0,

    // Future trigger types (not yet wired):
    ReachLocation = 1,
    Collect = 2,
    Kill = 3,
}

/// <summary>
/// One goal (checklist row) within a quest. Each goal becomes a client objective row
/// (QuestObjectiveAddedPacket) shown in the quest tracker with a status icon that ticks off when the
/// goal's trigger fires (QuestObjectiveCompletePacket). Goals complete in order; the active goal is
/// the first one not yet completed, and the quest is ready to hand in once every goal is done.
/// </summary>
public class QuestGoal
{
    /// <summary>Localized text id for the goal row shown in the tracker/journal ("Talk to Shakey").</summary>
    public int NameId { get; set; }

    /// <summary>
    /// Optional longer description id shown as the journal "Objectives" sub-line under the goal row
    /// ("Shakey should be hanging out in front of the Wildwood Speedway..."); 0 = reuse <see cref="NameId"/>.
    /// </summary>
    public int DescriptionId { get; set; }

    /// <summary>
    /// What the goal's NPC says when this goal is completed at them. Currently only shown for the
    /// FINAL goal: it becomes the turn-in end screen's speech bubble (so a quest that ends back at
    /// the giver shows the giver's closing line, not the intermediate NPC's). 0 = fall back to the
    /// quest's TargetDialogueId.
    /// </summary>
    public int DialogueId { get; set; }

    /// <summary>How this goal completes.</summary>
    public QuestGoalType Type { get; set; } = QuestGoalType.TalkToNpc;

    /// <summary>
    /// For <see cref="QuestGoalType.TalkToNpc"/>: the NPC guid the player must interact with to
    /// complete this goal. 0 falls back to the quest's TargetGuid (the turn-in NPC).
    /// </summary>
    public ulong TargetGuid { get; set; }

    /// <summary>
    /// For count goals (<see cref="QuestGoalType.Collect"/>/<see cref="QuestGoalType.Kill"/>): how many
    /// of the thing are required. 0 falls back to <see cref="CollectSpawns"/>.Count (collect them all).
    /// The tracker renders "current/required" as the player collects.
    /// </summary>
    public int RequiredCount { get; set; }

    /// <summary>
    /// For <see cref="QuestGoalType.Collect"/>: the model (Models.txt id) each collectible world object
    /// uses - e.g. 93 = bw_collectible_mushrooms_01. Spawned as interactable pickups the player clicks.
    /// </summary>
    public int CollectModelId { get; set; }

    /// <summary>For <see cref="QuestGoalType.Collect"/>: the collectible's hover/name text id (Global.Text).</summary>
    public int CollectNameId { get; set; }

    /// <summary>
    /// For <see cref="QuestGoalType.Collect"/>: world positions ([x, y, z] each) where the collectible
    /// pickups spawn. Interacting with one credits the goal; at <see cref="RequiredCount"/> the goal ticks
    /// off and the next goal (the "return" step) activates. Place at least <see cref="RequiredCount"/>.
    /// </summary>
    public List<float[]> CollectSpawns { get; set; } = new();
}
