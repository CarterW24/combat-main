using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Combat;

public static class WizardWandAbilities
{
    public const int WizardProfileId = 12;

    private const int MeleeHitFx = 7;
    private const float BasicReach = 25f;
    private const float SuperAoeRadius = 12f;

    public static readonly IReadOnlyDictionary<int, NinjaWeapon> ByWeaponDefId = new Dictionary<int, NinjaWeapon>
    {
        [4962] = new(
            new("Zap", 294, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38672] = new(
            new("Zap 2", 14956, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [16365] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [16366] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [16367] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [16368] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [16369] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [77449] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [48189] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [22207] = new(
            new("Zap 4", 14463, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38693] = new(
            new("Zap 4", 14463, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [48147] = new(
            new("Zap 4", 14463, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [48219] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [48171] = new(
            new("Zap 6", 14439, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1143, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [13674] = new(
            new("Starshower", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Orbital Strike", 294, 8302, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [55339] = new(
            new("Starshower", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Orbital Strike", 294, 8302, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [9035] = new(
            new("Magic Missile", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaotic Flux", 23023, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [13675] = new(
            new("Magic Missile", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaotic Flux", 23023, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [55366] = new(
            new("Magic Missile", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaotic Flux", 23023, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [48195] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [76562] = new(
            new("Jingle Spells", 28516, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Candy Hurricane", 27727, 8302, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [30481] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [30487] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [30489] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38673] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38677] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38679] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38681] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38683] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38685] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [38689] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [48243] = new(
            new("Zap 10", 4339, 853, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 3492, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [4914] = new(
            new("Zap 6", 14439, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 4", 23006, 1143, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [30003] = new(
            new("Zap 6", 14439, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 4", 23006, 1143, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [48207] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [22204] = new(
            new("Zap 2", 14956, 1492, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 6107, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [48319] = new(
            new("Burn 3", 4359, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 4732, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        [75158] = new(
            new("Burn 4", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 1548, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        [75157] = new(
            new("Chill 5", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 1955, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        [75161] = new(
            new("Shock 3", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chain Lightning", 23019, 3492, 1114, 0, 16291, AoeRadius: SuperAoeRadius)),

        [75156] = new(
            new("Zap 10", 4339, 853, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 3841, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [75159] = new(
            new("Splash 3", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami", 23034, 3492, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        [75160] = new(
            new("Blast 3", 4339, 853, 1114, MeleeHitFx, Reach: BasicReach),
            new("Energy Vortex", 23025, 3841, 1017, 0, 16151, AoeRadius: SuperAoeRadius)),

        [75168] = new(
            new("Scorch 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Arcane Chain 2", 22608, 6717, 1114, 0, 16036, AoeRadius: SuperAoeRadius)),

        [75169] = new(
            new("Freeze 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Protective Barrier", 23031, 0, 1132, 0, 16124, BuffMultiplierPct: 0, BuffDurationMs: 15000, PersistEffectId: 16124)),

        [75164] = new(
            new("Burn 3", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 2707, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        [75163] = new(
            new("Chill 4", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 3762, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        [75167] = new(
            new("Shock 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chain Lightning", 23019, 6717, 1114, 0, 16291, AoeRadius: SuperAoeRadius)),

        [75162] = new(
            new("Zap 9", 4359, 1492, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 6717, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [75165] = new(
            new("Splash 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami", 23034, 6717, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        [75166] = new(
            new("Blast 2", 4359, 1492, 1114, MeleeHitFx, Reach: BasicReach),
            new("Energy Vortex", 23025, 6717, 1017, 0, 16151, AoeRadius: SuperAoeRadius)),

        [78201] = new(
            new("Feral Blast", 39217, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Feral Spirit", 39237, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [75176] = new(
            new("Scorch", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Arcane Chain", 22608, 10674, 1114, 0, 16036, AoeRadius: SuperAoeRadius)),

        [75178] = new(
            new("Boom", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaos Explosion", 23022, 2557, 1114, 0, 16125, AoeRadius: SuperAoeRadius)),

        [75177] = new(
            new("Freeze", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Protective Barrier", 23031, 0, 1132, 0, 16124, BuffMultiplierPct: 0, BuffDurationMs: 15000, PersistEffectId: 16124)),

        [75171] = new(
            new("Chill 3", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 5977, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        [75175] = new(
            new("Shock", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chain Lightning 2", 23019, 10674, 1114, 0, 16291, AoeRadius: SuperAoeRadius)),

        [75170] = new(
            new("Zap 8", 14451, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 11741, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [75179] = new(
            new("Flare", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Mass Transfigure", 23028, 6575, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [75173] = new(
            new("Splash 4", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami 2", 23034, 10674, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        [75174] = new(
            new("Blast", 14451, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Energy Vortex", 23025, 11741, 1017, 0, 16151, AoeRadius: SuperAoeRadius)),

        [75151] = new(
            new("Chill 2", 4347, 254, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 704, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        [75150] = new(
            new("Zap 7", 4347, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 1257, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [75154] = new(
            new("Burn", 14433, 444, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 885, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        [75153] = new(
            new("Chill", 14433, 444, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 1230, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        [75152] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 2197, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        [75155] = new(
            new("Splash", 14433, 444, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami", 23034, 1998, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        [76811] = new(
            new("Charm", 30209, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Heart Breaker", 30190, 6575, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        [75172] = new(
            new("Burn 2", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm 2", 22611, 4732, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),
    };

    public static readonly int[] AllWeaponDefIds = ByWeaponDefId.Keys.ToArray();

    public static NinjaWeapon? GetEquippedWeapon(Player player)
    {
        var defId = player.GetEquippedWeaponDefinitionId();
        return defId != 0 && ByWeaponDefId.TryGetValue(defId, out var weapon) ? weapon : null;
    }
}
