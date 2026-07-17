using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Dungeons;

public sealed class DungeonEnemy
{
    public required int ModelId;
    public int Count = 1;
    public int Health = 1000;
    public float Scale = 1f;
    public bool Boss;
}

public sealed class DungeonDefinition
{
    public required int ActivityId;

    public int PoiId;

    public required string World;
    public required float CenterX;
    public required float CenterZ;
    public float GroundY;
    public required int TitleNameId;
    public required int DescriptionId;
    public int Difficulty = 1;
    public int IconId = 1345;
    public int Xp = 12;

    public float Radius = 64f;

    public required DungeonEnemy[] Enemies;
    public string Comment = "";

    public int TotalEnemies => Enemies.Sum(e => e.Count);
}

public static class DungeonCatalog
{
    public static readonly IReadOnlyDictionary<int, DungeonDefinition> ByActivity = new Dictionary<int, DungeonDefinition>
    {
        [93] = new()
        {
            ActivityId = 93, Comment = "Band of Robgoblins!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 41569, DescriptionId = 41570, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [94] = new()
        {
            ActivityId = 94, Comment = "Robgoblin Adept Trouble!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 42062, DescriptionId = 42057, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [95] = new()
        {
            ActivityId = 95, Comment = "Explosive Robgoblins!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 42055, DescriptionId = 42058, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [96] = new()
        {
            ActivityId = 96, Comment = "Troll Summoner Madness!",
            World = "sh_random_encounter_01", CenterX = 136f, CenterZ = 152f, GroundY = 6f,
            TitleNameId = 42056, DescriptionId = 42059, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 100, Count = 3, Health = 1200 },
                new() { ModelId = 182, Count = 2, Health = 1000 },
                new() { ModelId = 168, Count = 1, Health = 1400 },
            ],
        },

        [97] = new()
        {
            ActivityId = 97, Comment = "Robgoblin Camp!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 70112, DescriptionId = 70148, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [110] = new()
        {
            ActivityId = 110, Comment = "Robgoblin Geomancer!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 42053, DescriptionId = 73658, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [111] = new()
        {
            ActivityId = 111, Comment = "Robgoblin Creek!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 71695, DescriptionId = 73036, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [112] = new()
        {
            ActivityId = 112, Comment = "Hooligan Brawling Club!",
            World = "bw_random_encounter_02", CenterX = 128f, CenterZ = 151f, GroundY = 0f,
            TitleNameId = 74374, DescriptionId = 75440, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 203, Count = 4, Health = 700 },
                new() { ModelId = 201, Count = 2, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [114] = new()
        {
            ActivityId = 114, Comment = "Treasure of the Bone Shaman!",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 71698, DescriptionId = 72949, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 71, Count = 3, Health = 600 },
                new() { ModelId = 72, Count = 3, Health = 600 },
                new() { ModelId = 1692, Count = 2, Health = 800 },
            ],
        },

        [130] = new()
        {
            ActivityId = 130, Comment = "Nettleseed Nibblers!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 73711, DescriptionId = 73951, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 134, Count = 4, Health = 800 },
                new() { ModelId = 3250, Count = 2, Health = 1400, Scale = 1.2f },
            ],
        },

        [131] = new()
        {
            ActivityId = 131, Comment = "Moldy Shamblers!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 73712, DescriptionId = 73986, Difficulty = 4, Xp = 24,
            Enemies =
            [
                new() { ModelId = 160, Count = 4, Health = 700 },
                new() { ModelId = 3250, Count = 2, Health = 1300, Scale = 1.2f },
            ],
        },

        [132] = new()
        {
            ActivityId = 132, Comment = "The Mushroom Gigas!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 73713, DescriptionId = 74054, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 160, Count = 4, Health = 700 },
                new() { ModelId = 3250, Count = 2, Health = 1300, Scale = 1.2f },
            ],
        },

        [133] = new()
        {
            ActivityId = 133, Comment = "Cray Marauders",
            World = "ss_random_encounter_01", CenterX = 125f, CenterZ = 160f, GroundY = 0f,
            TitleNameId = 73977, DescriptionId = 76570, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 1667, Count = 6, Health = 700 },
                new() { ModelId = 470, Count = 3, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 1800, Scale = 1.4f, Boss = true },
            ],
        },

        [134] = new()
        {
            ActivityId = 134, Comment = "Thugawug Bumbler!",
            World = "sg_random_encounter_treefort", CenterX = 124f, CenterZ = 150f, GroundY = 0f,
            TitleNameId = 74377, DescriptionId = 75456, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 199, Count = 5, Health = 800 },
                new() { ModelId = 200, Count = 3, Health = 900 },
            ],
        },

        [135] = new()
        {
            ActivityId = 135, Comment = "Mushroom Mania!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 73989, DescriptionId = 74047, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 160, Count = 4, Health = 700 },
                new() { ModelId = 3250, Count = 2, Health = 1300, Scale = 1.2f },
            ],
        },

        [136] = new()
        {
            ActivityId = 136, Comment = "Stealthy Despoilers",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 74637, DescriptionId = 95061, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 203, Count = 4, Health = 700 },
                new() { ModelId = 201, Count = 2, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [137] = new()
        {
            ActivityId = 137, Comment = "Hooligan Bullies!",
            World = "bw_random_encounter_02", CenterX = 128f, CenterZ = 151f, GroundY = 0f,
            TitleNameId = 74754, DescriptionId = 74755, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 203, Count = 4, Health = 700 },
                new() { ModelId = 201, Count = 2, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [140] = new()
        {
            ActivityId = 140, Comment = "Thugawug Sneak!",
            World = "sg_random_encounter_treefort", CenterX = 124f, CenterZ = 150f, GroundY = 0f,
            TitleNameId = 75449, DescriptionId = 92381, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 199, Count = 5, Health = 800 },
                new() { ModelId = 200, Count = 3, Health = 900 },
            ],
        },

        [141] = new()
        {
            ActivityId = 141, Comment = "Thugawug Thug!",
            World = "sg_random_encounter_treefort", CenterX = 124f, CenterZ = 150f, GroundY = 0f,
            TitleNameId = 75450, DescriptionId = 77891, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 199, Count = 5, Health = 800 },
                new() { ModelId = 200, Count = 3, Health = 900 },
            ],
        },

        [142] = new()
        {
            ActivityId = 142, Comment = "Fleetfoot Ninja!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 75451, DescriptionId = 76336, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [143] = new()
        {
            ActivityId = 143, Comment = "Bixies Gone Wild!",
            World = "bw_random_encounter_03", CenterX = 134f, CenterZ = 158f, GroundY = 1f,
            TitleNameId = 75452, DescriptionId = 76503, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 211, Count = 4, Health = 700 },
                new() { ModelId = 218, Count = 2, Health = 900 },
                new() { ModelId = 4472, Count = 1, Health = 1600, Scale = 1.3f, Boss = true },
            ],
        },

        [149] = new()
        {
            ActivityId = 149, Comment = "Frostfang Snarlers!",
            World = "sg_random_encounter_clearing", CenterX = 136f, CenterZ = 165f, GroundY = 0f,
            TitleNameId = 78089, DescriptionId = 78097, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 177, Count = 5, Health = 760 },
                new() { ModelId = 176, Count = 1, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [150] = new()
        {
            ActivityId = 150, Comment = "Bergram Stumpfinger's Ghost",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 78143, DescriptionId = 90096, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 71, Count = 3, Health = 600 },
                new() { ModelId = 72, Count = 3, Health = 600 },
                new() { ModelId = 1692, Count = 2, Health = 800 },
            ],
        },

        [151] = new()
        {
            ActivityId = 151, Comment = "Mudshell",
            World = "ss_random_encounter_01", CenterX = 125f, CenterZ = 160f, GroundY = 0f,
            TitleNameId = 78144, DescriptionId = 91293, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 1667, Count = 6, Health = 700 },
                new() { ModelId = 470, Count = 3, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 1800, Scale = 1.4f, Boss = true },
            ],
        },

        [152] = new()
        {
            ActivityId = 152, Comment = "Snatching Snappers!",
            World = "ss_random_encounter_01", CenterX = 125f, CenterZ = 160f, GroundY = 0f,
            TitleNameId = 78145, DescriptionId = 92031, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 1667, Count = 6, Health = 700 },
                new() { ModelId = 470, Count = 3, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 1800, Scale = 1.4f, Boss = true },
            ],
        },

        [153] = new()
        {
            ActivityId = 153, Comment = "Seaside Swoopers",
            World = "ss_random_encounter_01", CenterX = 125f, CenterZ = 160f, GroundY = 0f,
            TitleNameId = 78146, DescriptionId = 93388, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 1667, Count = 6, Health = 700 },
                new() { ModelId = 470, Count = 3, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 1800, Scale = 1.4f, Boss = true },
            ],
        },

        [155] = new()
        {
            ActivityId = 155, Comment = "Shady Smugglers",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 90148, DescriptionId = 91880, Difficulty = 4, Xp = 24,
            Enemies =
            [
                new() { ModelId = 203, Count = 4, Health = 700 },
                new() { ModelId = 201, Count = 2, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [161] = new()
        {
            ActivityId = 161, Comment = "Vulture Watch!",
            World = "bw_random_encounter_03", CenterX = 134f, CenterZ = 158f, GroundY = 1f,
            TitleNameId = 93260, DescriptionId = 139445, Difficulty = 4, Xp = 24,
            Enemies =
            [
                new() { ModelId = 343, Count = 5, Health = 900 },
                new() { ModelId = 3986, Count = 3, Health = 1100 },
            ],
        },

        [162] = new()
        {
            ActivityId = 162, Comment = "Eight-Legged Monstrosities!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 93261, DescriptionId = 101738, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 54, Count = 6, Health = 600 },
                new() { ModelId = 1667, Count = 2, Health = 900 },
            ],
        },

        [163] = new()
        {
            ActivityId = 163, Comment = "Chugawump!",
            World = "sg_random_encounter_treefort", CenterX = 124f, CenterZ = 150f, GroundY = 0f,
            TitleNameId = 93262, DescriptionId = 140061, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 199, Count = 5, Health = 800 },
                new() { ModelId = 200, Count = 3, Health = 900 },
            ],
        },

        [164] = new()
        {
            ActivityId = 164, Comment = "Snakes in a Maze!",
            World = "bw_random_encounter_thistlerow_01", CenterX = 303f, CenterZ = 303f, GroundY = 6f,
            TitleNameId = 93264, DescriptionId = 115960, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 180, Count = 7, Health = 700 },
            ],
        },

        [165] = new()
        {
            ActivityId = 165, Comment = "When Plants Attack!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 93265, DescriptionId = 103441, Difficulty = 4, Xp = 24,
            Enemies =
            [
                new() { ModelId = 134, Count = 4, Health = 800 },
                new() { ModelId = 3250, Count = 2, Health = 1400, Scale = 1.2f },
            ],
        },

        [168] = new()
        {
            ActivityId = 168, Comment = "Robgoblin Troublemakers!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 93268, DescriptionId = 103603, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [169] = new()
        {
            ActivityId = 169, Comment = "Thugawug Bandits!",
            World = "sg_random_encounter_treefort", CenterX = 124f, CenterZ = 150f, GroundY = 0f,
            TitleNameId = 93269, DescriptionId = 101360, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 199, Count = 5, Health = 800 },
                new() { ModelId = 200, Count = 3, Health = 900 },
            ],
        },

        [171] = new()
        {
            ActivityId = 171, Comment = "Alpha Wolf!",
            World = "sg_random_encounter_creek", CenterX = 149f, CenterZ = 185f, GroundY = 0f,
            TitleNameId = 93271, DescriptionId = 102129, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 177, Count = 5, Health = 760 },
                new() { ModelId = 176, Count = 1, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [172] = new()
        {
            ActivityId = 172, Comment = "Oasis of Peril!",
            World = "ss_random_encounter_01", CenterX = 125f, CenterZ = 160f, GroundY = 0f,
            TitleNameId = 93272, DescriptionId = 139744, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 1667, Count = 6, Health = 700 },
                new() { ModelId = 470, Count = 3, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 1800, Scale = 1.4f, Boss = true },
            ],
        },

        [173] = new()
        {
            ActivityId = 173, Comment = "Petty Yetis!",
            World = "sh_random_encounter_01", CenterX = 136f, CenterZ = 152f, GroundY = 6f,
            TitleNameId = 93274, DescriptionId = 116757, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 1944, Count = 1, Health = 2500, Scale = 1.4f, Boss = true },
                new() { ModelId = 100, Count = 4, Health = 1000 },
            ],
        },

        [175] = new()
        {
            ActivityId = 175, Comment = "Ice Troll Scout!",
            World = "sh_random_encounter_01", CenterX = 136f, CenterZ = 152f, GroundY = 6f,
            TitleNameId = 93277, DescriptionId = 116829, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 100, Count = 3, Health = 1200 },
                new() { ModelId = 182, Count = 2, Health = 1000 },
                new() { ModelId = 168, Count = 1, Health = 1400 },
            ],
        },

        [176] = new()
        {
            ActivityId = 176, Comment = "Robgoblin Pondblasters!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 94365, DescriptionId = 95048, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [182] = new()
        {
            ActivityId = 182, Comment = "Thugamug",
            World = "sg_random_encounter_treefort", CenterX = 124f, CenterZ = 150f, GroundY = 0f,
            TitleNameId = 103031, DescriptionId = 103748, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 199, Count = 5, Health = 800 },
                new() { ModelId = 200, Count = 3, Health = 900 },
            ],
        },

        [184] = new()
        {
            ActivityId = 184, Comment = "Cursed Graveyard!",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 115822, DescriptionId = 115823, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 71, Count = 3, Health = 600 },
                new() { ModelId = 72, Count = 3, Health = 600 },
                new() { ModelId = 1692, Count = 2, Health = 800 },
            ],
        },

        [186] = new()
        {
            ActivityId = 186, Comment = "Prince of Low Tide!",
            World = "ss_random_encounter_01", CenterX = 125f, CenterZ = 160f, GroundY = 0f,
            TitleNameId = 116053, DescriptionId = 116060, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 1667, Count = 6, Health = 700 },
                new() { ModelId = 470, Count = 3, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 1800, Scale = 1.4f, Boss = true },
            ],
        },

        [187] = new()
        {
            ActivityId = 187, Comment = "Brutus the Brute!",
            World = "bw_random_encounter_02", CenterX = 128f, CenterZ = 151f, GroundY = 0f,
            TitleNameId = 116054, DescriptionId = 116061, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 203, Count = 4, Health = 700 },
                new() { ModelId = 201, Count = 2, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [188] = new()
        {
            ActivityId = 188, Comment = "Call of the Wildest!",
            World = "sg_random_encounter_creek", CenterX = 149f, CenterZ = 185f, GroundY = 0f,
            TitleNameId = 116055, DescriptionId = 116062, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 177, Count = 5, Health = 760 },
                new() { ModelId = 176, Count = 1, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [190] = new()
        {
            ActivityId = 190, Comment = "Spawn of Necrosis!",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 116058, DescriptionId = 116070, Difficulty = 4, Xp = 24,
            Enemies =
            [
                new() { ModelId = 1692, Count = 4, Health = 700 },
                new() { ModelId = 73, Count = 3, Health = 700 },
                new() { ModelId = 71, Count = 2, Health = 900 },
            ],
        },

        [215] = new()
        {
            ActivityId = 215, Comment = "Grave Danger!",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 141037, DescriptionId = 385616, Difficulty = 4, Xp = 24,
            Enemies =
            [
                new() { ModelId = 71, Count = 3, Health = 600 },
                new() { ModelId = 72, Count = 3, Health = 600 },
                new() { ModelId = 1692, Count = 2, Health = 800 },
            ],
        },

        [216] = new()
        {
            ActivityId = 216, Comment = "Wraiths of Wrath!",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 141038, DescriptionId = 141355, Difficulty = 5, Xp = 28,
            Enemies =
            [
                new() { ModelId = 1692, Count = 4, Health = 700 },
                new() { ModelId = 73, Count = 3, Health = 700 },
                new() { ModelId = 71, Count = 2, Health = 900 },
            ],
        },

        [217] = new()
        {
            ActivityId = 217, Comment = "Pixie Hunters!",
            World = "bw_random_encounter_03", CenterX = 134f, CenterZ = 158f, GroundY = 1f,
            TitleNameId = 141039, DescriptionId = 382801, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 211, Count = 4, Health = 700 },
                new() { ModelId = 218, Count = 2, Health = 900 },
                new() { ModelId = 4472, Count = 1, Health = 1600, Scale = 1.3f, Boss = true },
            ],
        },

        [218] = new()
        {
            ActivityId = 218, Comment = "Feisty Floren!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 141040, DescriptionId = 141113, Difficulty = 2, Xp = 16,
            Enemies =
            [
                new() { ModelId = 134, Count = 4, Health = 800 },
                new() { ModelId = 3250, Count = 2, Health = 1400, Scale = 1.2f },
            ],
        },

        [219] = new()
        {
            ActivityId = 219, Comment = "Return to Sender!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 141043, DescriptionId = 385617, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [220] = new()
        {
            ActivityId = 220, Comment = "Venomous Frogs!",
            World = "bw_random_encounter_bristlewood_01", CenterX = 134f, CenterZ = 166f, GroundY = 0f,
            TitleNameId = 383185, DescriptionId = 384346, Difficulty = 3, Xp = 20,
            Enemies =
            [
                new() { ModelId = 1688, Count = 5, Health = 900 },
            ],
        },

        [349] = new()
        {
            ActivityId = 349, Comment = "Village in distress!",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 415192, DescriptionId = 415509, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [351] = new()
        {
            ActivityId = 351, Comment = "Twilight Ritual",
            World = "bw_random_encounter_01", CenterX = 154f, CenterZ = 172f, GroundY = 0f,
            TitleNameId = 415194, DescriptionId = 415510, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 71, Count = 3, Health = 600 },
                new() { ModelId = 72, Count = 3, Health = 600 },
                new() { ModelId = 1692, Count = 2, Health = 800 },
            ],
        },

        [371] = new()
        {
            ActivityId = 371, Comment = "Crafty Robgoblins",
            World = "sg_random_encounter_skullcamp", CenterX = 134f, CenterZ = 152f, GroundY = 1f,
            TitleNameId = 422969, DescriptionId = 422970, Difficulty = 1, Xp = 12,
            Enemies =
            [
                new() { ModelId = 4, Count = 6, Health = 700 },
                new() { ModelId = 189, Count = 2, Health = 1200 },
                new() { ModelId = 191, Count = 1, Health = 900 },
            ],
        },

        [12] = new()
        {
            ActivityId = 12, PoiId = 52, Comment = "Arachnia's Lair",
            World = "bw_spider_lair", CenterX = 273f, CenterZ = 223f, GroundY = 31f, Radius = 383f,
            TitleNameId = 2273, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 54, Count = 12, Health = 600 },
                new() { ModelId = 1667, Count = 4, Health = 900 },
            ],
        },

        [31] = new()
        {
            ActivityId = 31, PoiId = 53, Comment = "Hot Springs Haven",
            World = "sh_yeti_cave", CenterX = 228f, CenterZ = 358f, GroundY = 81f, Radius = 195f,
            TitleNameId = 3269, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 100, Count = 6, Health = 1000 },
                new() { ModelId = 1944, Count = 1, Health = 3000, Scale = 1.5f, Boss = true },
            ],
        },

        [29] = new()
        {
            ActivityId = 29, PoiId = 54, Comment = "Bandit Hideout",
            World = "sg_bandit_hideout", CenterX = 153f, CenterZ = 168f, GroundY = 34f, Radius = 200f,
            TitleNameId = 5172, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 199, Count = 9, Health = 800 },
                new() { ModelId = 200, Count = 5, Health = 900 },
                new() { ModelId = 202, Count = 1, Health = 1600, Scale = 1.3f, Boss = true },
            ],
        },

        [30] = new()
        {
            ActivityId = 30, PoiId = 55, Comment = "Tavern Cellar",
            World = "sg_tavern_cellar", CenterX = 124f, CenterZ = 75f, GroundY = 19f, Radius = 162f,
            TitleNameId = 5493, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 4, Count = 10, Health = 700 },
                new() { ModelId = 189, Count = 4, Health = 1200 },
                new() { ModelId = 191, Count = 2, Health = 900 },
            ],
        },

        [17] = new()
        {
            ActivityId = 17, PoiId = 56, Comment = "The Snarling Hedges",
            World = "bw_snarling_hedges", CenterX = 669f, CenterZ = 622f, GroundY = 0f, Radius = 600f,
            TitleNameId = 21188, DescriptionId = 382845, Difficulty = 5, Xp = 50,
            Enemies =
            [
                new() { ModelId = 134, Count = 7, Health = 800 },
                new() { ModelId = 3250, Count = 4, Health = 1400, Scale = 1.2f },
            ],
        },

        [15] = new()
        {
            ActivityId = 15, PoiId = 57, Comment = "Snowy Canyon",
            World = "sh_canyon_combat", CenterX = 137f, CenterZ = 165f, GroundY = 43f, Radius = 575f,
            TitleNameId = 4229, DescriptionId = 382845, Difficulty = 5, Xp = 50,
            Enemies =
            [
                new() { ModelId = 100, Count = 6, Health = 1000 },
                new() { ModelId = 1944, Count = 1, Health = 3000, Scale = 1.5f, Boss = true },
            ],
        },

        [27] = new()
        {
            ActivityId = 27, PoiId = 58, Comment = "The Bat Cave!",
            World = "sh_bat_cave", CenterX = 415f, CenterZ = 294f, GroundY = 82f, Radius = 195f,
            TitleNameId = 6456, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 71, Count = 6, Health = 600 },
                new() { ModelId = 72, Count = 6, Health = 600 },
                new() { ModelId = 1692, Count = 4, Health = 800 },
            ],
        },

        [33] = new()
        {
            ActivityId = 33, PoiId = 59, Comment = "Frostfang Caverns",
            World = "sh_frostfang_cavern", CenterX = 221f, CenterZ = 348f, GroundY = 80f, Radius = 313f,
            TitleNameId = 5698, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 177, Count = 10, Health = 760 },
                new() { ModelId = 176, Count = 2, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [32] = new()
        {
            ActivityId = 32, PoiId = 60, Comment = "Robgoblin Treasure Trove",
            World = "sg_robgoblin_trove", CenterX = 250f, CenterZ = 143f, GroundY = 30f, Radius = 205f,
            TitleNameId = 5697, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 4, Count = 10, Health = 700 },
                new() { ModelId = 189, Count = 4, Health = 1200 },
                new() { ModelId = 191, Count = 2, Health = 900 },
            ],
        },

        [34] = new()
        {
            ActivityId = 34, PoiId = 61, Comment = "Deep Mines",
            World = "sh_deep_mines", CenterX = 121f, CenterZ = 136f, GroundY = 15f, Radius = 200f,
            TitleNameId = 5699, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 4, Count = 10, Health = 700 },
                new() { ModelId = 189, Count = 4, Health = 1200 },
                new() { ModelId = 191, Count = 2, Health = 900 },
            ],
        },

        [37] = new()
        {
            ActivityId = 37, PoiId = 62, Comment = "Bixie Hive",
            World = "sg_bixie_hive", CenterX = 246f, CenterZ = 321f, GroundY = 81f, Radius = 200f,
            TitleNameId = 5702, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 211, Count = 7, Health = 700 },
                new() { ModelId = 218, Count = 4, Health = 900 },
                new() { ModelId = 4472, Count = 1, Health = 2000, Scale = 1.4f, Boss = true },
            ],
        },

        [38] = new()
        {
            ActivityId = 38, PoiId = 63, Comment = "Forgotten Caves",
            World = "sg_changeling_caverns", CenterX = 370f, CenterZ = 346f, GroundY = 27f, Radius = 180f,
            TitleNameId = 5703, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 71, Count = 6, Health = 600 },
                new() { ModelId = 72, Count = 6, Health = 600 },
                new() { ModelId = 1692, Count = 4, Health = 800 },
            ],
        },

        [46] = new()
        {
            ActivityId = 46, PoiId = 64, Comment = "Danger Peaks",
            World = "sh_howling_hills", CenterX = 223f, CenterZ = 142f, GroundY = 47f, Radius = 340f,
            TitleNameId = 5711, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 177, Count = 10, Health = 760 },
                new() { ModelId = 176, Count = 2, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [43] = new()
        {
            ActivityId = 43, PoiId = 65, Comment = "Forest Troll Fort",
            World = "sg_troll_fort", CenterX = 131f, CenterZ = 111f, GroundY = 80f, Radius = 329f,
            TitleNameId = 5708, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 100, Count = 6, Health = 1200 },
                new() { ModelId = 182, Count = 4, Health = 1000 },
                new() { ModelId = 168, Count = 2, Health = 1400 },
            ],
        },

        [42] = new()
        {
            ActivityId = 42, PoiId = 66, Comment = "Briarheart Caverns",
            World = "bw_briarheart_caverns", CenterX = -53f, CenterZ = -198f, GroundY = 18f, Radius = 200f,
            TitleNameId = 5707, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 160, Count = 7, Health = 700 },
                new() { ModelId = 3250, Count = 4, Health = 1300, Scale = 1.2f },
            ],
        },

        [41] = new()
        {
            ActivityId = 41, PoiId = 67, Comment = "Trail of Betrayal",
            World = "bw_trail_of_betrayal", CenterX = 138f, CenterZ = 199f, GroundY = 54f, Radius = 314f,
            TitleNameId = 5706, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 203, Count = 8, Health = 700 },
                new() { ModelId = 201, Count = 4, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 2000, Scale = 1.4f, Boss = true },
            ],
        },

        [45] = new()
        {
            ActivityId = 45, PoiId = 68, Comment = "Highroad Hijinx",
            World = "sg_highroad_hijinx", CenterX = 204f, CenterZ = 192f, GroundY = 43f, Radius = 300f,
            TitleNameId = 5710, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 199, Count = 9, Health = 800 },
                new() { ModelId = 200, Count = 5, Health = 900 },
                new() { ModelId = 202, Count = 1, Health = 1600, Scale = 1.3f, Boss = true },
            ],
        },

        [40] = new()
        {
            ActivityId = 40, PoiId = 69, Comment = "Floren Forest",
            World = "sg_floren_forest", CenterX = 244f, CenterZ = 215f, GroundY = 18f, Radius = 370f,
            TitleNameId = 5705, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 134, Count = 9, Health = 900 },
                new() { ModelId = 3250, Count = 3, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [39] = new()
        {
            ActivityId = 39, PoiId = 70, Comment = "Mugwort's Hollow",
            World = "sg_mugworts_hollow", CenterX = 231f, CenterZ = 257f, GroundY = -14f, Radius = 354f,
            TitleNameId = 5704, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 160, Count = 7, Health = 700 },
                new() { ModelId = 3250, Count = 4, Health = 1300, Scale = 1.2f },
            ],
        },

        [23] = new()
        {
            ActivityId = 23, PoiId = 71, Comment = "Briar Patch",
            World = "bw_briar_patch", CenterX = 197f, CenterZ = 233f, GroundY = -14f, Radius = 331f,
            TitleNameId = 5078, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 134, Count = 7, Health = 800 },
                new() { ModelId = 3250, Count = 4, Health = 1400, Scale = 1.2f },
            ],
        },

        [59] = new()
        {
            ActivityId = 59, PoiId = 72, Comment = "Croaking Vale",
            World = "bw_vale_of_thorns", CenterX = 166f, CenterZ = 311f, GroundY = -4f, Radius = 300f,
            TitleNameId = 18219, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 1688, Count = 9, Health = 900 },
            ],
        },

        [21] = new()
        {
            ActivityId = 21, PoiId = 73, Comment = "Howling Hills",
            World = "sh_howling_hills", CenterX = 223f, CenterZ = 142f, GroundY = 47f, Radius = 340f,
            TitleNameId = 6488, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 177, Count = 10, Health = 760 },
                new() { ModelId = 176, Count = 2, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [19] = new()
        {
            ActivityId = 19, PoiId = 74, Comment = "Dark Spore Depths",
            World = "bw_mushroom_cave", CenterX = 197f, CenterZ = 187f, GroundY = 30f, Radius = 299f,
            TitleNameId = 4469, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 160, Count = 7, Health = 700 },
                new() { ModelId = 3250, Count = 4, Health = 1300, Scale = 1.2f },
            ],
        },

        [25] = new()
        {
            ActivityId = 25, PoiId = 75, Comment = "Mushroom Cavern",
            World = "bw_mushroom_cave", CenterX = 197f, CenterZ = 187f, GroundY = 30f, Radius = 299f,
            TitleNameId = 4187, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 160, Count = 7, Health = 700 },
                new() { ModelId = 3250, Count = 4, Health = 1300, Scale = 1.2f },
            ],
        },

        [36] = new()
        {
            ActivityId = 36, PoiId = 76, Comment = "Bristlewood Glade",
            World = "bw_bristlewood_glade", CenterX = 137f, CenterZ = 437f, GroundY = -38f, Radius = 120f,
            TitleNameId = 5701, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 134, Count = 7, Health = 800 },
                new() { ModelId = 3250, Count = 4, Health = 1400, Scale = 1.2f },
            ],
        },

        [52] = new()
        {
            ActivityId = 52, PoiId = 77, Comment = "Vale of the Ancients",
            World = "bw_vale_of_thorns", CenterX = 166f, CenterZ = 311f, GroundY = -4f, Radius = 300f,
            TitleNameId = 18143, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 160, Count = 7, Health = 700 },
                new() { ModelId = 3250, Count = 4, Health = 1300, Scale = 1.2f },
            ],
        },

        [28] = new()
        {
            ActivityId = 28, PoiId = 78, Comment = "Treeleaf's Retreat",
            World = "bw_treeleaf_retreat", CenterX = 269f, CenterZ = 221f, GroundY = -9f, Radius = 500f,
            TitleNameId = 5133, DescriptionId = 382845, Difficulty = 5, Xp = 50,
            Enemies =
            [
                new() { ModelId = 134, Count = 9, Health = 900 },
                new() { ModelId = 3250, Count = 3, Health = 1500, Scale = 1.3f, Boss = true },
            ],
        },

        [26] = new()
        {
            ActivityId = 26, PoiId = 79, Comment = "Grexan's Camp",
            World = "sg_bandit_hideout", CenterX = 153f, CenterZ = 168f, GroundY = 34f, Radius = 200f,
            TitleNameId = 162, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 199, Count = 9, Health = 800 },
                new() { ModelId = 200, Count = 5, Health = 900 },
                new() { ModelId = 202, Count = 1, Health = 1600, Scale = 1.3f, Boss = true },
            ],
        },

        [58] = new()
        {
            ActivityId = 58, PoiId = 80, Comment = "Darvon's Descent",
            World = "sg_changeling_caverns", CenterX = 370f, CenterZ = 346f, GroundY = 27f, Radius = 180f,
            TitleNameId = 7697, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 71, Count = 6, Health = 600 },
                new() { ModelId = 72, Count = 6, Health = 600 },
                new() { ModelId = 1692, Count = 4, Health = 800 },
            ],
        },

        [116] = new()
        {
            ActivityId = 116, PoiId = 82, Comment = "Sweetwater Climb",
            World = "wc_sweetwater_climb", CenterX = 182f, CenterZ = 95f, GroundY = 63f, Radius = 300f,
            TitleNameId = 71769, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 211, Count = 7, Health = 700 },
                new() { ModelId = 218, Count = 4, Health = 900 },
                new() { ModelId = 4472, Count = 1, Health = 2000, Scale = 1.4f, Boss = true },
            ],
        },

        [117] = new()
        {
            ActivityId = 117, PoiId = 83, Comment = "Den of Secrets",
            World = "mv_den_of_secrets", CenterX = 290f, CenterZ = 239f, GroundY = 9f, Radius = 155f,
            TitleNameId = 71770, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 71, Count = 6, Health = 600 },
                new() { ModelId = 72, Count = 6, Health = 600 },
                new() { ModelId = 1692, Count = 4, Health = 800 },
            ],
        },

        [115] = new()
        {
            ActivityId = 115, PoiId = 84, Comment = "Cray Caves",
            World = "ss_cray_caves", CenterX = 324f, CenterZ = 230f, GroundY = 70f, Radius = 225f,
            TitleNameId = 71694, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 1667, Count = 10, Health = 700 },
                new() { ModelId = 470, Count = 5, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [118] = new()
        {
            ActivityId = 118, PoiId = 85, Comment = "Cracked Claw Caverns",
            World = "bs_cracked_claw_caverns", CenterX = 181f, CenterZ = 268f, GroundY = 39f, Radius = 125f,
            TitleNameId = 71771, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 1667, Count = 10, Health = 700 },
                new() { ModelId = 470, Count = 5, Health = 1000 },
                new() { ModelId = 4446, Count = 1, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [158] = new()
        {
            ActivityId = 158, PoiId = 86, Comment = "Tanglewood Fort",
            World = "bw_tanglewood_fort", CenterX = 452f, CenterZ = 467f, GroundY = -52f, Radius = 500f,
            TitleNameId = 23555, DescriptionId = 382845, Difficulty = 5, Xp = 50,
            Enemies =
            [
                new() { ModelId = 203, Count = 8, Health = 700 },
                new() { ModelId = 201, Count = 4, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 2000, Scale = 1.4f, Boss = true },
            ],
        },

        [119] = new()
        {
            ActivityId = 119, PoiId = 87, Comment = "Sheep Watch",
            World = "sg_sheep_watch", CenterX = 386f, CenterZ = 549f, GroundY = -57f, Radius = 300f,
            TitleNameId = 71772, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 177, Count = 10, Health = 760 },
                new() { ModelId = 176, Count = 2, Health = 2200, Scale = 1.5f, Boss = true },
            ],
        },

        [91] = new()
        {
            ActivityId = 91, PoiId = 88, Comment = "Haunted Mines",
            World = "sg_haunted_mines", CenterX = 80f, CenterZ = 73f, GroundY = 23f, Radius = 114f,
            TitleNameId = 5700, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 71, Count = 6, Health = 600 },
                new() { ModelId = 72, Count = 6, Health = 600 },
                new() { ModelId = 1692, Count = 4, Health = 800 },
            ],
        },

        [900089] = new()
        {
            ActivityId = 900089, PoiId = 89, Comment = "Hewey's Escape",
            World = "sg_bandit_hideout", CenterX = 153f, CenterZ = 168f, GroundY = 34f, Radius = 200f,
            TitleNameId = 102010, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 199, Count = 9, Health = 800 },
                new() { ModelId = 200, Count = 5, Health = 900 },
                new() { ModelId = 202, Count = 1, Health = 1600, Scale = 1.3f, Boss = true },
            ],
        },

        [159] = new()
        {
            ActivityId = 159, PoiId = 90, Comment = "Misty Mountain",
            World = "gl_misty_mountain", CenterX = 273f, CenterZ = 408f, GroundY = 40f, Radius = 300f,
            TitleNameId = 71773, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 100, Count = 6, Health = 1000 },
                new() { ModelId = 1944, Count = 1, Health = 3000, Scale = 1.5f, Boss = true },
            ],
        },

        [339] = new()
        {
            ActivityId = 339, PoiId = 119, Comment = "Bone Bog Cemetery",
            World = "bs_bone_bog_cemetery", CenterX = 611f, CenterZ = 664f, GroundY = -32f, Radius = 250f,
            TitleNameId = 414849, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 1692, Count = 7, Health = 700 },
                new() { ModelId = 73, Count = 5, Health = 700 },
                new() { ModelId = 71, Count = 3, Health = 900 },
            ],
        },

        [900121] = new()
        {
            ActivityId = 900121, PoiId = 121, Comment = "Cursed Graveyard!",
            World = "bs_bone_bog_cemetery", CenterX = 611f, CenterZ = 664f, GroundY = -32f, Radius = 250f,
            TitleNameId = 115822, DescriptionId = 382845, Difficulty = 4, Xp = 44,
            Enemies =
            [
                new() { ModelId = 71, Count = 6, Health = 600 },
                new() { ModelId = 72, Count = 6, Health = 600 },
                new() { ModelId = 1692, Count = 4, Health = 800 },
            ],
        },

        [900146] = new()
        {
            ActivityId = 900146, PoiId = 146, Comment = "The Rumbledome",
            World = "gw_rumbledome", CenterX = 154f, CenterZ = 323f, GroundY = 72f, Radius = 100f,
            TitleNameId = 439064, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 203, Count = 8, Health = 700 },
                new() { ModelId = 201, Count = 4, Health = 800 },
                new() { ModelId = 202, Count = 1, Health = 2000, Scale = 1.4f, Boss = true },
            ],
        },

        [900148] = new()
        {
            ActivityId = 900148, PoiId = 148, Comment = "Sandscale Oasis",
            World = "ss_cray_caves", CenterX = 324f, CenterZ = 230f, GroundY = 70f, Radius = 225f,
            TitleNameId = 439065, DescriptionId = 382845, Difficulty = 3, Xp = 38,
            Enemies =
            [
                new() { ModelId = 180, Count = 12, Health = 700 },
            ],
        },
    };

    public static readonly IReadOnlySet<int> WorldEnemyModelIds = new HashSet<int>
    {
        190,
        786,
        1681,
        1682,
        3967,
        4202,

        77,
        166,
        167,
        314,
        408,
        739,

        471,
        754,

        142,
        1521,
        3284,
        62,
        91,
        92,
        235,
        121,
        2140,

        669,
        685,
        730,
        1054,
        767,
        751,
        1701,
        1702,
        3217,
        4220,
        4563,
        4564,
        4565,
        1694,
        353,

        393,
        394,
        395,
        397,
        945,
        4094,

        4464,
        4465,
        4466,
        4467,
        4468,
        4473,
        4474,
    };

    public static readonly IReadOnlySet<int> EnemyModelIds =
        ByActivity.Values.SelectMany(d => d.Enemies).Select(e => e.ModelId)
            .Concat(WorldEnemyModelIds).ToHashSet();

    public static readonly IReadOnlyDictionary<int, DungeonDefinition> ByAtlasPoi =
        ByActivity.Values.Where(d => d.PoiId != 0).ToDictionary(d => d.PoiId);
}
