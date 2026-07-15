using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Combat;

// COMBAT: WIZARD wands, data-driven the same way as the ninja Shadow Blades - each wand grants
// Ability 1 = its basic attack (slot 0, ranged single-target) and Ability 2 = its super (slot 1).
// The wand -> ability -> damage mapping is the CONFIRMED set from the team's OSFR Combat Spreadsheet
// ("Wizard Weapon Map" sheet, Data Status = CONFIRMED, 55 rows). 49 wands mapped to item defs by name
// (Resources/ClientItemDefinitions.json Comment); a name matching several item ids (recolor variants,
// e.g. the 6 Balloon Wands) maps every variant to the same abilities, per the sheet's own note.
// UNMAPPABLE (no such item in the 2014 item table - revisit if the team finds the ids):
//   New School Forked Wand + Old School Forked Wand (both would hit the single 'Forked Wand' 13673 -
//   ambiguous which), Wizard's Awakened Wand of Forbidden Magic, Wizard's Forged Wand of Cunning,
//   Wizard's Ornate Wand of Burn, Wizard's Tentacle Wand of Riptide.
// Scaling wands ("254 (Lvl 1) ... 2372 (Lvl 16)") use the MAX tier - consistent with the ninja table,
// whose 8302/10674 damages are that same top tier.
// Icons/FX/anims come from the spreadsheet's "Wizard" ability catalog. FX status there is PENDING
// (visual verify to come); supers play their PFX on the caster (root AoE bursts). Basics have NO
// dedicated FX in the sheet yet -> generic hit flash on the victim. Anim 1114 = the sheet's Zap cast
// (catalog rows without an anim fall back to it).
public static class WizardWandAbilities
{
    public const int WizardProfileId = 12;

    private const int MeleeHitFx = 7;          // PFX_Hit_Flash - generic impact until per-spell FX are confirmed
    private const float BasicReach = 25f;      // wand basics are RANGED - auto-target picks hostiles out to 25u
    private const float SuperAoeRadius = 12f;  // super AoE envelope (matches ninja 1000 Storms)

    public static readonly IReadOnlyDictionary<int, NinjaWeapon> ByWeaponDefId = new Dictionary<int, NinjaWeapon>
    {
        // All-Star Wizard Wand - Zap 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [4962] = new(
            new("Zap", 294, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Aqua Wand - Zap 2 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38672] = new(
            new("Zap 2", 14956, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Balloon Wand - Party Tricks 2372 / Party Crasher 7685 (sheet CONFIRMED)
        [16365] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Balloon Wand - Party Tricks 2372 / Party Crasher 7685 (sheet CONFIRMED)
        [16366] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Balloon Wand - Party Tricks 2372 / Party Crasher 7685 (sheet CONFIRMED)
        [16367] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Balloon Wand - Party Tricks 2372 / Party Crasher 7685 (sheet CONFIRMED)
        [16368] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Balloon Wand - Party Tricks 2372 / Party Crasher 7685 (sheet CONFIRMED)
        [16369] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Balloon Wand - Party Tricks 2372 / Party Crasher 7685 (sheet CONFIRMED)
        [77449] = new(
            new("Party Tricks", 31020, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Party Crasher", 291, 7685, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Confetti Wand - Zap 3 488 / Lightning Blast 1998 (sheet CONFIRMED)
        [48189] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Fiery Wand - Zap 4 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [22207] = new(
            new("Zap 4", 14463, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Fiery Wand - Zap 4 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38693] = new(
            new("Zap 4", 14463, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Fiery Wand - Zap 4 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [48147] = new(
            new("Zap 4", 14463, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Gravel Wand - Zap 3 488 / Lightning Blast 1998 (sheet CONFIRMED)
        [48219] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Monarch Wand - Zap 6 279 / Lightning Blast 1143 (sheet CONFIRMED)
        [48171] = new(
            new("Zap 6", 14439, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1143, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // New School Orbital Wand - Starshower 2372 / Orbital Strike 8302 (sheet CONFIRMED)
        [13674] = new(
            new("Starshower", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Orbital Strike", 294, 8302, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // New School Orbital Wand - Starshower 2372 / Orbital Strike 8302 (sheet CONFIRMED)
        [55339] = new(
            new("Starshower", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Orbital Strike", 294, 8302, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // New School Shard Wand - Magic Missile 2372 / Chaotic Flux 9132 (sheet CONFIRMED)
        [9035] = new(
            new("Magic Missile", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaotic Flux", 23023, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // New School Shard Wand - Magic Missile 2372 / Chaotic Flux 9132 (sheet CONFIRMED)
        [13675] = new(
            new("Magic Missile", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaotic Flux", 23023, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // New School Shard Wand - Magic Missile 2372 / Chaotic Flux 9132 (sheet CONFIRMED)
        [55366] = new(
            new("Magic Missile", 14445, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaotic Flux", 23023, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Party Wand - Zap 3 488 / Lightning Blast 1998 (sheet CONFIRMED)
        [48195] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Red Ryder Rod - Jingle Spells 2372 / Candy Hurricane 8302 (sheet CONFIRMED)
        [76562] = new(
            new("Jingle Spells", 28516, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Candy Hurricane", 27727, 8302, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [30481] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [30487] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [30489] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38673] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38677] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38679] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38681] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38683] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38685] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Shadow Infused Wand - Zap 10 2609 / Lightning Blast 2 10674 (sheet CONFIRMED)
        [38689] = new(
            new("Zap 10", 4339, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 10674, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Spectrum Wand - Zap 10 853 / Lightning Blast 3492 (sheet CONFIRMED)
        [48243] = new(
            new("Zap 10", 4339, 853, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 3492, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Student Wizard Wand - Zap 6 279 / Lightning Blast 4 1143 (sheet CONFIRMED)
        [4914] = new(
            new("Zap 6", 14439, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 4", 23006, 1143, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Student Wizard Wand - Zap 6 279 / Lightning Blast 4 1143 (sheet CONFIRMED)
        [30003] = new(
            new("Zap 6", 14439, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 4", 23006, 1143, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Sunlit Wand - Zap 3 488 / Lightning Blast 1998 (sheet CONFIRMED)
        [48207] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast", 294, 1998, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Venom's Touch - Zap 2 1492 / Lightning Blast 2 6107 (sheet CONFIRMED)
        [22204] = new(
            new("Zap 2", 14956, 1492, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 2", 2236, 6107, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Wand of Spectral Fire - Burn 3 2372 / Firestorm 4732 (sheet CONFIRMED)
        [48319] = new(
            new("Burn 3", 4359, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 4732, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        // Wizard's Bone Wand of Firestorm - Burn 4 776 / Firestorm 1548 (sheet CONFIRMED)
        [75158] = new(
            new("Burn 4", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 1548, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        // Wizard's Bone Wand of Glaciers - Chill 5 776 / Ice Nova 1955 (sheet CONFIRMED)
        [75157] = new(
            new("Chill 5", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 1955, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        // Wizard's Bone Wand of Lightning - Shock 3 776 / Chain Lightning 3492 (sheet CONFIRMED)
        [75161] = new(
            new("Shock 3", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chain Lightning", 23019, 3492, 1114, 0, 16291, AoeRadius: SuperAoeRadius)),

        // Wizard's Bone Wand of Shock - Zap 10 853 / Lightning Blast 3 3841 (sheet CONFIRMED)
        [75156] = new(
            new("Zap 10", 4339, 853, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 3841, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Wizard's Bone Wand of Tsunami - Splash 3 776 / Tsunami 3492 (sheet CONFIRMED)
        [75159] = new(
            new("Splash 3", 4339, 776, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami", 23034, 3492, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        // Wizard's Bone Wand of Vortex - Blast 3 853 / Energy Vortex 3841 (sheet CONFIRMED)
        [75160] = new(
            new("Blast 3", 4339, 853, 1114, MeleeHitFx, Reach: BasicReach),
            new("Energy Vortex", 23025, 3841, 1017, 0, 16151, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Arcane Fire - Scorch 2 1357 / Arcane Chain 2 6717 (sheet CONFIRMED)
        [75168] = new(
            new("Scorch 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Arcane Chain 2", 22608, 6717, 1114, 0, 16036, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Energy - Freeze 2 1357 / Protective Barrier 1330 (sheet CONFIRMED)
        [75169] = new(
            new("Freeze 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Protective Barrier", 23031, 0, 1132, 0, 16124, BuffMultiplierPct: 0, BuffDurationMs: 15000, PersistEffectId: 16124)),

        // Wizard's Jewel Wand of Firestorm - Burn 3 1357 / Firestorm 2707 (sheet CONFIRMED)
        [75164] = new(
            new("Burn 3", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 2707, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Glaciers - Chill 4 1357 / Ice Nova 3762 (sheet CONFIRMED)
        [75163] = new(
            new("Chill 4", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 3762, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Lightning - Shock 2 1357 / Chain Lightning 6717 (sheet CONFIRMED)
        [75167] = new(
            new("Shock 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chain Lightning", 23019, 6717, 1114, 0, 16291, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Shock - Zap 9 1492 / Lightning Blast 3 6717 (sheet CONFIRMED)
        [75162] = new(
            new("Zap 9", 4359, 1492, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 6717, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Tsunami - Splash 2 1357 / Tsunami 6717 (sheet CONFIRMED)
        [75165] = new(
            new("Splash 2", 4359, 1357, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami", 23034, 6717, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        // Wizard's Jewel Wand of Vortex - Blast 2 1492 / Energy Vortex 6717 (sheet CONFIRMED)
        [75166] = new(
            new("Blast 2", 4359, 1492, 1114, MeleeHitFx, Reach: BasicReach),
            new("Energy Vortex", 23025, 6717, 1017, 0, 16151, AoeRadius: SuperAoeRadius)),

        // Wizard's Nature Wand - Feral Blast 2372 / Feral Spirit 9132 (sheet CONFIRMED)
        [78201] = new(
            new("Feral Blast", 39217, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Feral Spirit", 39237, 9132, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Arcane Fire - Scorch 2372 / Arcane Chain 10674 (sheet CONFIRMED)
        [75176] = new(
            new("Scorch", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Arcane Chain", 22608, 10674, 1114, 0, 16036, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Chaos - Boom 2372 / Chaos Explosion 2557 (sheet CONFIRMED)
        [75178] = new(
            new("Boom", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chaos Explosion", 23022, 2557, 1114, 0, 16125, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Energy - Freeze 2372 / Protective Barrier 2324 (sheet CONFIRMED)
        [75177] = new(
            new("Freeze", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Protective Barrier", 23031, 0, 1132, 0, 16124, BuffMultiplierPct: 0, BuffDurationMs: 15000, PersistEffectId: 16124)),

        // Wizard's Ornate Wand of Glaciers - Chill 3 2372 / Ice Nova 5977 (sheet CONFIRMED)
        [75171] = new(
            new("Chill 3", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 5977, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Lightning - Shock 2372 / Chain Lightning 2 10674 (sheet CONFIRMED)
        [75175] = new(
            new("Shock", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Chain Lightning 2", 23019, 10674, 1114, 0, 16291, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Shock - Zap 8 2609 / Lightning Blast 3 11741 (sheet CONFIRMED)
        [75170] = new(
            new("Zap 8", 14451, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 11741, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Transmutation - Flare 2372 / Mass Transfigure 6575 (sheet CONFIRMED)
        [75179] = new(
            new("Flare", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Mass Transfigure", 23028, 6575, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Tsunami - Splash 4 2372 / Tsunami 2 10674 (sheet CONFIRMED)
        [75173] = new(
            new("Splash 4", 14451, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami 2", 23034, 10674, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Vortex - Blast 2609 / Energy Vortex 11741 (sheet CONFIRMED)
        [75174] = new(
            new("Blast", 14451, 2609, 1114, MeleeHitFx, Reach: BasicReach),
            new("Energy Vortex", 23025, 11741, 1017, 0, 16151, AoeRadius: SuperAoeRadius)),

        // Wizard's Sparkle Twig of Glaciers - Chill 2 254 / Ice Nova 704 (sheet CONFIRMED)
        [75151] = new(
            new("Chill 2", 4347, 254, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 704, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        // Wizard's Sparkle Twig of Shock - Zap 7 279 / Lightning Blast 3 1257 (sheet CONFIRMED)
        [75150] = new(
            new("Zap 7", 4347, 279, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 1257, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Wizard's Wand of Firestorm - Burn 444 / Firestorm 885 (sheet CONFIRMED)
        [75154] = new(
            new("Burn", 14433, 444, 1114, MeleeHitFx, Reach: BasicReach),
            new("Firestorm", 22611, 885, 1018, 0, 16026, AoeRadius: SuperAoeRadius)),

        // Wizard's Wand of Glaciers - Chill 444 / Ice Nova 1230 (sheet CONFIRMED)
        [75153] = new(
            new("Chill", 14433, 444, 1114, MeleeHitFx, Reach: BasicReach),
            new("Ice Nova", 283, 1230, 1139, 0, 16172, AoeRadius: SuperAoeRadius)),

        // Wizard's Wand of Shock - Zap 3 488 / Lightning Blast 3 2197 (sheet CONFIRMED)
        [75152] = new(
            new("Zap 3", 14433, 488, 1114, MeleeHitFx, Reach: BasicReach),
            new("Lightning Blast 3", 294, 2197, 1138, 0, 16305, AoeRadius: SuperAoeRadius)),

        // Wizard's Wand of Tsunami - Splash 444 / Tsunami 1998 (sheet CONFIRMED)
        [75155] = new(
            new("Splash", 14433, 444, 1114, MeleeHitFx, Reach: BasicReach),
            new("Tsunami", 23034, 1998, 1137, 0, 16187, AoeRadius: SuperAoeRadius)),

        // Wooing Wand - Charm 2372 / Heart Breaker 6575 (sheet CONFIRMED)
        [76811] = new(
            new("Charm", 30209, 2372, 1114, MeleeHitFx, Reach: BasicReach),
            new("Heart Breaker", 30190, 6575, 1114, 0, 0, AoeRadius: SuperAoeRadius)),

        // Wizard's Ornate Wand of Firestorm - Burn 2 2372 / Firestorm 2 4732 (sheet CONFIRMED)
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
