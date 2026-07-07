namespace Sanctuary.Database.Entities;

public class DbCharacterQuest
{
    public int QuestId { get; set; }

    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;

    public bool Completed { get; set; }

    /// <summary>
    /// Number of the quest's goals completed so far (goals tick off in order). 0 = on the first goal.
    /// Lets multi-goal progress survive relog; single-goal quests only ever hit 0 -> turn-in.
    /// </summary>
    public int GoalProgress { get; set; }
}
