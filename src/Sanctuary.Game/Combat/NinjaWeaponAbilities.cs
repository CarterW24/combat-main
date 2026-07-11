using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

// COMBAT WIP: abilities are driven by the EQUIPPED WEAPON, the way Free Realms did it. Each ninja
// "Ninja's Shadow Blade of X" weapon (Resources/ClientItemDefinitions.json, ids 75110-75119) grants TWO
// abilities: Ability 1 = a melee sword technique (slot 0), Ability 2 = the named special (slot 1).
// Names + damage are from the Free Realms wiki (freerealms.fandom.com api.php).
//
// ICONS (cracked 2026-06-20): the ability-slot IconId is a flat IMAGE_ID, NOT an image-set id. The real
// ninja ability icons are the abil_ninja_* image sets' IMAGE_IDs (Client/Resources/Images/ImageSetMappings.txt,
// Small=type5). e.g. abil_ninja_shuriken_storm set 4902 -> Small IMAGE_ID 22986. (Sending the set id 4902 hit
// the food/fruit image #4902 instead.) FX ids match ActorCompositeEffectDefinitions.xml (confirmed live:
// id 1 = fire). Animations: 1099 com_swing is proven; per-ability anims TBD via !anim probe.
// EffectId = impact FX played on the TARGET (AttackProcessed). CastEffectId = FX played on the CASTER during
// StartCasting (the projectile/aura/ground-AoE you see come off the ninja); 0 = none. For projectile specials
// (Shuriken throw, Dragonstrike) CastEffectId = the launch/trail and EffectId = the land/impact; for ground-AoE
// specials the same id works for both. (Usage per drafts/ninja-special-anim-fx-research.md §FINDINGS iter 4.)
// SummonCount > 0 => this special also spawns that many temporary "shadow clone" NPCs around the caster
// (Shadow Army). See StartingZone.SummonShadowClones.
//
// SwordEffectId > 0 => bind this composite effect to the caster's WEAPON slot (item slot 7) via
//   PlayerUpdatePacketSlotCompositeEffectOverride on cast — i.e. the FX rides on the SWORD, not the body.
//   Used by abilities that "empower the weapon" (Mysticism / Mystical Blade).
// CasterEndEffectId > 0 => play this composite effect ON THE CASTER (at the player's position/feet) at the
//   END of the animation (after the cast delay) via PlayerUpdatePacketPlayCompositeEffect — for abilities
//   whose FX should land on the caster's feet when the move finishes (Dragonstrike).
// EnemyExtraEffectId > 0 => play this ADDITIONAL composite effect on the TARGET (on top of EffectId) at the
//   end of the cast — e.g. a ring overlaid on the enemy (Soul Power's purple ring). 0 = none.
// AoeRadius > 0 => the special is an AREA attack: it hits EVERY live hostile within this radius of the
//   CASTER (not just the selected target). Matches the real server, whose AoE specials land as a sub-0.1s
//   burst of one HitPointModification per victim in the 04-01 capture.
// CastEffectStopMs > 0 => the CastEffectId is a LINGERING/looping effect (e.g. a PRJ_* projectile
//   trail, which retail kills with its projectile — we have no projectiles yet): play it via an
//   effect TAG (op35/sub41) and remove it (op35/sub42) after this many ms. Such effects are also
//   excluded from the toolbar FX warm-up (an unstoppable loop under the map snows forever —
//   user-sighted with the Bow of Blizzards trail, 2026-07-10). 0 = a one-shot, plays normally.
// EnergyCost = what a NON-BASIC slot press drains from the 100-point energy bar (also the slot's
//   ManaCost, which drives the client grey-out). Weapon specials keep the live-decoded full bar
//   (100); extra job abilities can cost less so the bar supports more than one cast.
public sealed record WeaponAbility(string Name, int IconImageId, int Damage, int Animation, int EffectId, int CastEffectId = 0, int SummonCount = 0, int SwordEffectId = 0, int CasterEndEffectId = 0, int EnemyExtraEffectId = 0, float AoeRadius = 0f, int CastEffectStopMs = 0, int EnergyCost = 100);

public sealed record NinjaWeapon(WeaponAbility Melee, WeaponAbility Special);

public static class NinjaWeaponAbilities
{
    public const int WeaponSlot = 7;
    public const int NinjaProfileId = 2;

    private const int MeleeAnimation = 1021; // com_1hs_attack_01 = real 1-hand SWORD swing (human_m_com_1hs_attack_01.gr2).
                                              // Was 1099 (com_swing) which is NOT in human_m.adr -> client fell back to a
                                              // bare-hand swing. 1021-1024 = com_1hs_attack_01..04; group 1020=com_1hs_attack
                                              // picks one at random (authentic variety) if preferred.
    private const int MeleeHitFx = 7;        // PFX_Hit_Flash — proven generic flash. (FR's real melee hit is
                                             // material-based PFX_Hit_<weaponMat>_vs_<targetMat>; metal-vs-flesh
                                             // = 5414, crit 5627, KO 5637 — a candidate to verify live with
                                             // `!atk 500 50000 5414`, NOT yet visually confirmed.)
    // Melee slot shows the weapon's SWORD icon: the shadow-blade item set (3152) Small IMAGE_ID = 14407.
    // (abil_ninja_shadow_blade's 22599 renders as a leaf in our client; the item sword image is correct.)
    private const int MeleeIcon = 14407;

    // PROVEN-castable AbilityDefinitionIds from the original capture (client renders + lets us cast these).
    private const int MeleeSlotDefId = 4895;
    private const int SpecialSlotDefId = 4899;

    // Live icon-probe override (set by "!ticon <melee> <special>"); null = use the ability's own icon.
    public static int? DebugMeleeIcon;
    public static int? DebugSpecialIcon;

    public static readonly WeaponAbility BareMelee = new("Strike", MeleeIcon, 150, MeleeAnimation, MeleeHitFx);

    // weapon def id -> two abilities. IconImageId = the abil_ninja_* Small IMAGE_ID (real ability art).
    // EffectId = the ability's REAL per-special composite effect (CONFIRMED from ActorCompositeEffectDefinitions
    // .xml — the game's own dedicated `PFX_ninja_*` / `PFX_*_ninja-*` EffectDefinitions; see
    // drafts/ninja-special-anim-fx-research.md §FINDINGS iter 4).
    // Animation: ALL 10 now wired from the client's own animation table — decoded from the player model's
    // actor-def `human_m.adr` (slot->clip records) + AnimationGroups.xml, VERIFIED against the client asset log
    // (1011043->weapon_throw) and user sight (1034/1035). The 1hs specials reuse the shared named motion clips
    // (flip_stab/flying_chop/overhand_spin/bum_rush/air_throw/weapon_throw/sweep); Flaming Uppercut + Mystical
    // Blade use their DEDICATED clips (com_h2h_special_07=1017, com_cast_special_11=weapon_power=1061141); Shadow
    // Army uses com_spawn=1404 (no dedicated summon clip). Full table: drafts/anim-NAMED-CLIPS-breakthrough.md.
    public static readonly IReadOnlyDictionary<int, NinjaWeapon> ByWeaponDefId = new Dictionary<int, NinjaWeapon>
    {
        // 75112 — Shuriken Storm
        [75112] = new(
            new("Twisted Edge",   MeleeIcon, 2870, MeleeAnimation, MeleeHitFx),
            new("Shuriken Storm",     22986, 8302, 1039, 4012, 0)), // LIVE-OBSERVED: anim com_1hs_special_09=inverted_flip_attack (frontflip + downward melee); FX 4012 PFX_Slashes_Three_Symbol ("3 scratch marks") on the ENEMY only (impact); no cast FX on caster

        // 75113 — Flame Wave
        [75113] = new(
            new("Cinder Slash",   MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
            new("Flame Wave",         22974, 10674, 1032, 0, 16140)), // anim sweep; cast 16140 PFX_fire_orange_cog_ninja-flame-wave (ground AoE on caster, at feet); enemy impact REMOVED (user: FX only at my feet)

        // 75110 — Dragonstrike
        [75110] = new(
            new("Flame Flash",    MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
            new("Dragonstrike",       22965, 10674, 1035, 0, 0, 0, 0, 16186)), // anim flying_chop (confirmed); launch 16014 REMOVED; the LAND FX 16186 now plays ON THE CASTER (feet) at the END of the anim (CasterEndEffectId); nothing on the enemy (user round-2)

        // 75111 — 1000 Storms
        [75111] = new(
            new("Lightning Strike", MeleeIcon, 2372, MeleeAnimation, MeleeHitFx),
            new("1000 Storms",          22992, 8302, 1033, 0, 16088, AoeRadius: 12f)), // anim 1033 air_throw (jump-up + air-slam motion; same clip as Deception — user-chosen); cast 16088 PFX_lightning_blue_root_ninja-special on caster; enemy impact REMOVED (user: stop casting on target; FX at my sword at end of anim — sword placement TODO). AOE (user request 2026-07-03): hits the whole pack within 12u of the caster

        // 75114 — Shadow Armies
        [75114] = new(
            new("Dark Assault",   MeleeIcon, 2608, MeleeAnimation, MeleeHitFx),
            new("Shadow Army",        22989, 3000, 1061143, 21, 16483, 3)), // anim warcry; CAST 16483 PFX_summon_purple_cast (ONE-SHOT — 5276 was a _loop that never ended); impact 21 black smoke; SUMMONS 3 shadow clones

        // 75115 — Solar Flare (special is "Flaming Uppercut")
        [75115] = new(
            new("Ashen Strike",     MeleeIcon, 2870, MeleeAnimation, MeleeHitFx),
            new("Flaming Uppercut",     22977, 8302, 1017, 0, 16119)), // anim flaming_uppercut (DEDICATED); cast 16119 PFX_ninja_flaming-uppercut on caster; enemy impact REMOVED (user: player is the only one with the FX, not the NPC)

        // 75116 — Dragon Breath (special is "Flame Breath")
        [75116] = new(
            new("Fiery Slice",  MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
            new("Flame Breath",     22971, 10674, 1037, 0, 16129)), // anim bum_rush (WRONG — user to ID correct clip); cast 16129 PFX_fire_orange_mouth_ninja-flame-breath on caster; enemy impact REMOVED (user: FX played by me only, not any enemy)

        // 75117 — Mysticism (special is "Mystical Blade")
        [75117] = new(
            new("Mystic Rush",    MeleeIcon, 2608, MeleeAnimation, MeleeHitFx),
            new("Mystical Blade",     22980, 3000, 1061141, 0, 0, 0, 16169)), // anim weapon_power (DEDICATED); empowers the WEAPON: 16169 WFX_beam-trail_blue-purple_ninja-mystical-blade binds to the SWORD slot (SwordEffectId); NO body/enemy FX (user round-2: only on my sword, not on any bodies)

        // 75118 — Soul Power (special is "Mystical Drain")
        [75118] = new(
            new("Shadowslash",    MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
            new("Mystical Drain",     22983, 8302, 1034, 16180, 16180)), // anim flip_stab (confirmed); cast+impact 16180 PFX_beam_red_blue_circ_lg_AOE-drain

        // 75119 — Deception (special is "Fan of Blades")
        [75119] = new(
            new("Hidden Strike", MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
            new("Fan of Blades",     22968, 5977, 1033, 0, 16185)), // anim air_throw; cast 16185 PFX_sparkles_multi_cog_ninja-fan-of-blades on caster; enemy impact REMOVED (user: FX only on the animation, not the targets — sword bone placement still TODO)
    };

    public static readonly int[] AllWeaponDefIds = ByWeaponDefId.Keys.ToArray();

    public static NinjaWeapon? GetEquippedWeapon(Player player)
    {
        var defId = player.GetEquippedWeaponDefinitionId();
        return defId != 0 && ByWeaponDefId.TryGetValue(defId, out var weapon) ? weapon : null;
    }

    // slot 0 = melee, slot 1 = special.
    public static WeaponAbility ResolveAbility(Player player, int slot)
    {
        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return BareMelee;

        return slot <= 0 ? weapon.Melee : weapon.Special;
    }

    // Build the 2-slot ability toolbar from the equipped ninja weapon. Slot icon = each ability's real
    // IMAGE_ID (overridable live via !ticon for probing).
    public static AbilityPacketSetDefinition BuildToolbar(Player player, IResourceManager resources)
    {
        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return AbilityPacketSetDefinition.CreateEmpty(NinjaProfileId);

        var nameId = 0;
        if (resources.ClientItemDefinitions.TryGetValue(player.GetEquippedWeaponDefinitionId(), out var weaponDef))
            nameId = weaponDef.NameId;

        var def = new AbilityPacketSetDefinition { ProfileId = NinjaProfileId, SlotCount = 8 };

        def.Slots.Add(MakeSlot(MeleeSlotDefId, DebugMeleeIcon ?? weapon.Melee.IconImageId, nameId, manaCost: 0));
        // ENERGY (2026-07-03): the special costs the full 100 bar (ground-truthed from the 04-01 capture;
        // server gate lives in AbilityPacketClientRequestStartAbilityHandler). The slot's ManaCost is what
        // makes the CLIENT grey the button out while current energy (op38/sub13) is below the cost — with
        // 0 the client thinks it's free and the blocked presses just look dead.
        def.Slots.Add(MakeSlot(SpecialSlotDefId, DebugSpecialIcon ?? weapon.Special.IconImageId, nameId, manaCost: SpecialEnergyCost));

        return def;
    }

    /// <summary>Energy cost of every slot-1 weapon special (the full bar — see the ability handler's gate).</summary>
    public const int SpecialEnergyCost = 100;

    private static AbilityPacketSetDefinition.Slot MakeSlot(int abilityDefId, int iconId, int nameId, int manaCost) => new()
    {
        Type = 3,
        Unknown2 = abilityDefId,
        ManaCost = manaCost,
        IconId = iconId,
        NameId = nameId,
        AbilityDefinitionId = abilityDefId,
    };
}
