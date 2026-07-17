using System.Collections.Generic;

namespace Sanctuary.Game.Resources.Definitions;

public enum QuestGoalType
{
    TalkToNpc = 0,

    ReachLocation = 1,

    Collect = 2,

    Kill = 3,

    EncounterComplete = 4,
}

public class QuestGoal
{
    public int NameId { get; set; }

    public int DescriptionId { get; set; }

    public int DialogueId { get; set; }

    public QuestGoalType Type { get; set; } = QuestGoalType.TalkToNpc;

    public ulong TargetGuid { get; set; }

    public int RequiredCount { get; set; }

    public int CollectModelId { get; set; }

    public int CollectNameId { get; set; }

    public int KillNpcNameId { get; set; }

    public int EncounterId { get; set; }

    public List<float[]> CollectSpawns { get; set; } = new();
}
