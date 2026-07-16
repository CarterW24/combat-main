using System;

namespace Sanctuary.Game.Leveling;

// Job (profile) leveling curve and level-scaled stat formulas. All values are tunable here in one place.
// A job's "Rank" is its level (1..MaxLevel); XP accrues into the current level until it
// reaches XpForLevel, which grants a level.
public static class JobLeveling
{
    // Highest level a job can reach.
    public const int MaxLevel = 20;

    // XP required to advance from level to level+1.
    public static int XpForLevel(int level) => 1000 + (Math.Max(1, level) - 1) * 500;

    // --- Level-scaled stats (Rank = job level) ---
    public static int MaxHealth(int level) => 2500 + (Math.Max(1, level) - 1) * 250;
    public static int MaxMana(int level) => 100 + (Math.Max(1, level) - 1) * 20;
    public static int HitPointRegen(int level) => 25 + (Math.Max(1, level) - 1) * 3;
    public static int ManaRegen(int level) => 4 + (Math.Max(1, level) - 1);

    // Progress bar (0..100) into the current level for a given amount of XP-into-level.
    public static int RankPercent(int level, int xpIntoLevel)
    {
        if (level >= MaxLevel)
            return 100;

        int need = XpForLevel(level);
        if (need <= 0)
            return 0;

        return Math.Clamp((int)((long)xpIntoLevel * 100 / need), 0, 100);
    }
}
