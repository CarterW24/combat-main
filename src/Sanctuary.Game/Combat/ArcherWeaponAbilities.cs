using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

public sealed record ArcherWeapon(WeaponAbility Basic, WeaponAbility Special, WeaponAbility Sniper, WeaponAbility Rain);

public static class ArcherWeaponAbilities
{
    public const int ArcherProfileId = 35;

    public const float BowReach = 30f;

    public const int PrecisionLevel = 5;
    public const int MarksmanshipLevel = 10;
    public const int ReflexesLevel = 15;
    public const int LuckyShotLevel = 20;

    public const float PrecisionDamageBonus = 0.08f;
    public const int PrecisionCritChanceBonus = 12;
    public const int BaseCritChancePercent = 5;
    public const float MarksmanshipCritBonus = 0.75f;
    public const float BaseCritMultiplier = 2.0f;
    public const float ReflexesSpeedMultiplier = 1.15f;
    public const int ReflexesDodgePercent = 15;
    public const int LuckyShotChancePercent = 20;
    public const int LuckyShotEnergyRestore = 8;

    public static bool HasTrait(Player player, int traitLevel) =>
        player.ActiveProfileId == ArcherProfileId && player.ActiveProfile.Rank >= traitLevel;

    private static readonly (int NameId, int DescId, int IconId, int Level)[] TraitData =
    [
        (420934, 420958, 33,    PrecisionLevel),
        (420935, 420959, 31,    MarksmanshipLevel),
        (420936, 420960, 22570, ReflexesLevel),
        (420937, 420961, 39861, LuckyShotLevel),
    ];

    public static List<AbilityExperience> BuildTraitEntries(int rank)
    {
        var list = new List<AbilityExperience>(TraitData.Length + 1);
        AppendTraits(list, rank);
        list.Add(new AbilityExperience { Present = 0 });
        return list;
    }

    private static void AppendTraits(List<AbilityExperience> list, int rank)
    {
        foreach (var t in TraitData)
        {
            var unlocked = rank >= t.Level;
            list.Add(new AbilityExperience
            {
                Present = t.NameId,
                IsActivateable = false,
                NameId = t.NameId,
                DescriptionId = t.DescId,
                IconId = t.IconId,
                Level = unlocked ? 1 : 0,
                RequiredLevel = t.Level,
            });
        }
    }

    private static readonly IReadOnlyDictionary<string, (int NameId, int DescId)> AbilityText = new Dictionary<string, (int, int)>
    {
        ["Barrage"]        = (420256, 420257),
        ["Volley"]         = (420258, 420259),
        ["Icy Arrow"]      = (420384, 420385),
        ["Blizzard Blast"] = (420386, 420387),
        ["Charged Shot"]   = (420567, 420568),
        ["Explosive Shot"] = (420584, 420585),
        ["Multi-Shot"]     = (421006, 421007),
        ["Splitting Arrow"]= (421008, 421009),
        ["Power Shot"]     = (421184, 421185),
        ["Stunning Shot"]  = (426588, 421192),
        ["Smoldering Shot"]= (421245, 0),
        ["Flaming Arrow"]  = (421244, 0),
        ["Electric Arrow"] = (421260, 0),
        ["Lightning Call"] = (421272, 0),
        ["Sonic Arrow"]    = (421261, 0),
        ["Sonic Boom"]     = (421273, 0),
        ["Cover Fire"]     = (421284, 0),
        ["Ricochet"]       = (421296, 0),
        ["Ember Arrow"]    = (421285, 0),
        ["Firebomb"]       = (421297, 0),
    };

    private static AbilityExperience? BuildActiveEntry(WeaponAbility ability)
    {
        if (!AbilityText.TryGetValue(ability.Name, out var text))
            return null;

        return new AbilityExperience
        {
            Present = text.NameId,
            IsActivateable = true,
            NameId = text.NameId,
            DescriptionId = text.DescId,
            IconId = ability.IconImageId,
            Level = 1,
            RequiredLevel = 0,
        };
    }

    public static List<AbilityExperience> BuildProfileAbilityList(int rank, int weaponDefId)
    {
        var list = new List<AbilityExperience>(TraitData.Length + 3);

        var weapon = weaponDefId != 0 && ByWeaponDefId.TryGetValue(weaponDefId, out var w) ? w : FallbackWeapon;

        var basic = BuildActiveEntry(weapon.Basic);
        if (basic is not null) list.Add(basic);
        var special = BuildActiveEntry(weapon.Special);
        if (special is not null) list.Add(special);

        AppendTraits(list, rank);
        list.Add(new AbilityExperience { Present = 0 });
        return list;
    }

    private static readonly ArcherWeapon FallbackWeapon = Volleys(BowIcon, 350, 506);

    public static (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId)
    {
        var slot = SlotForDefId(abilityDefId);
        if (slot < 0)
            return null;

        return SlotNameIcon(player.GetEquippedWeaponDefinitionId(), slot);
    }

    public static int SlotForDefId(int abilityDefId) => abilityDefId switch
    {
        BasicSlotDefId => 0,
        SpecialSlotDefId => 1,
        SniperSlotDefId => 2,
        RainSlotDefId => 3,
        _ => -1,
    };

    public const int BasicAbilityDefId = BasicSlotDefId;
    public const int SpecialAbilityDefId = SpecialSlotDefId;

    public static List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId)
    {
        var (_, _, basicIcon) = SlotNameIcon(weaponDefId, 0);
        var (_, _, specialIcon) = SlotNameIcon(weaponDefId, 1);
        return new List<ItemDefinition.ItemAbilityEntry>
        {
            new() { Slot = 0, Id = BasicSlotDefId,   IconId = basicIcon },
            new() { Slot = 1, Id = SpecialSlotDefId, IconId = specialIcon },
        };
    }

    public static (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot)
    {
        var weapon = weaponDefId != 0 && ByWeaponDefId.TryGetValue(weaponDefId, out var w) ? w : FallbackWeapon;
        var ability = slot == 1 ? weapon.Special : weapon.Basic;
        var iconId = ability.IconImageId;

        if (!AbilityText.TryGetValue(ability.Name, out var text))
        {
            var fb = slot == 1 ? FallbackWeapon.Special : FallbackWeapon.Basic;
            AbilityText.TryGetValue(fb.Name, out text);
            iconId = fb.IconImageId;
        }

        return (text.NameId, text.DescId, iconId);
    }

    private const int BasicShotAnim = 1102;
    private const int SpecialAnim = 1106;
    private const int BasicHitFx = 7;

    private const int BowIcon = 14134;
    private const int HorseBowIcon = 14170;
    private const int CompositeBowIcon = 14176;
    private const int RecurveBowIcon = 14140;
    private const int RaptorBowIcon = 14146;
    private const int MoltenBowIcon = 14152;

    private const int VolleyIcon = 22801;
    private const int FreezingIcon = 22908;
    private const int ExplosiveIcon = 22798;
    private const int SplittingIcon = 22914;
    private const int StunningIcon = 11630;
    private const int FireArrowIcon = 22902;
    private const int LightningIcon = 22911;
    private const int ConcussiveIcon = 22899;
    private const int RicochetIcon = 22572;
    private const int FireBombIcon = 22905;

    private const int BasicSlotDefId = 4895;
    private const int SpecialSlotDefId = 4899;
    private const int SniperSlotDefId = 4896;
    private const int RainSlotDefId = 4897;

    private const int SniperIcon = 22575;
    private const int RainIcon = VolleyIcon;

    private const int SniperImpactFx = 15384;
    private const int RainLaunchFx = 1110;
    private const int RainLandFx = 1111;
    private const int LevelAbilityEnergyCost = 50;

    private static WeaponAbility SniperShot(int specialDmg) =>
        new("Sniper Shot", SniperIcon, (int)(specialDmg * 1.15f), SpecialAnim, SniperImpactFx,
            EnergyCost: LevelAbilityEnergyCost);

    private static WeaponAbility RainOfArrows(int specialDmg) =>
        new("Rain of Arrows", RainIcon, (int)(specialDmg * 0.75f), SpecialAnim, RainLandFx, RainLaunchFx,
            AoeRadius: 10f, EnergyCost: LevelAbilityEnergyCost);

    public static readonly WeaponAbility BareShot = new("Shoot", BowIcon, 150, BasicShotAnim, BasicHitFx);

    private static ArcherWeapon Make(WeaponAbility basic, WeaponAbility special, int specialDmg) =>
        new(basic, special, SniperShot(specialDmg), RainOfArrows(specialDmg));

    private static ArcherWeapon Volleys(int icon, int basicDmg, int specialDmg) => Make(
        new("Barrage", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Volley", VolleyIcon, specialDmg, SpecialAnim, 1111, 1110, CasterEndEffectId: 16204, AoeRadius: 10f),
        specialDmg);

    private static ArcherWeapon Blizzards(int icon, int basicDmg, int specialDmg) => Make(
        new("Icy Arrow", icon, basicDmg, BasicShotAnim, BasicHitFx, 16110, CastEffectStopMs: 1200),
        new("Blizzard Blast", FreezingIcon, specialDmg, SpecialAnim, 16116, 16110, CastEffectStopMs: 1200),
        specialDmg);

    private static ArcherWeapon Explosions(int icon, int basicDmg, int specialDmg) => Make(
        new("Charged Shot", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Explosive Shot", ExplosiveIcon, specialDmg, SpecialAnim, 15373),
        specialDmg);

    private static ArcherWeapon Splintering(int icon, int basicDmg, int specialDmg) => Make(
        new("Multi-Shot", icon, basicDmg, BasicShotAnim, 5307, 16056, CastEffectStopMs: 1200),
        new("Splitting Arrow", SplittingIcon, specialDmg, SpecialAnim, 5246, 15488, CastEffectStopMs: 1200),
        specialDmg);

    private static ArcherWeapon Stunning(int icon, int basicDmg, int specialDmg) => Make(
        new("Power Shot", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Stunning Shot", StunningIcon, specialDmg, SpecialAnim, 16054, 16050, CastEffectStopMs: 1200),
        specialDmg);

    private static ArcherWeapon Flame(int icon, int basicDmg, int specialDmg) => Make(
        new("Smoldering Shot", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Flaming Arrow", FireArrowIcon, specialDmg, SpecialAnim, 16121),
        specialDmg);

    private static ArcherWeapon Lightning(int icon, int basicDmg, int specialDmg) => Make(
        new("Electric Arrow", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Lightning Call", LightningIcon, specialDmg, SpecialAnim, 16117),
        specialDmg);

    private static ArcherWeapon Booming(int icon, int basicDmg, int specialDmg) => Make(
        new("Sonic Arrow", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Sonic Boom", ConcussiveIcon, specialDmg, SpecialAnim, 5710),
        specialDmg);

    private static ArcherWeapon Ricochet(int icon, int basicDmg, int specialDmg) => Make(
        new("Cover Fire", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Ricochet", RicochetIcon, specialDmg, SpecialAnim, 16215, 16214, CastEffectStopMs: 1200),
        specialDmg);

    private static ArcherWeapon Firebomb(int icon, int basicDmg, int specialDmg) => Make(
        new("Ember Arrow", icon, basicDmg, BasicShotAnim, BasicHitFx),
        new("Firebomb", FireBombIcon, specialDmg, SpecialAnim, 16118),
        specialDmg);

    public static readonly IReadOnlyDictionary<int, ArcherWeapon> ByWeaponDefId = new Dictionary<int, ArcherWeapon>
    {
        [75000] = Volleys(BowIcon, 350, 506),
        [75001] = Blizzards(BowIcon, 350, 640),

        [75002] = Volleys(HorseBowIcon, 855, 1000),
        [75003] = Blizzards(HorseBowIcon, 855, 1554),
        [75004] = Explosions(HorseBowIcon, 855, 1554),
        [75005] = Splintering(HorseBowIcon, 855, 1554),

        [75006] = Volleys(CompositeBowIcon, 1150, 1548),
        [75007] = Blizzards(CompositeBowIcon, 1150, 2100),
        [75008] = Explosions(CompositeBowIcon, 1150, 2100),
        [75009] = Splintering(CompositeBowIcon, 1150, 2100),
        [75010] = Stunning(CompositeBowIcon, 1150, 2100),
        [75011] = Flame(CompositeBowIcon, 1150, 2100),

        [75012] = Volleys(RecurveBowIcon, 1492, 2707),
        [75013] = Blizzards(RecurveBowIcon, 1492, 2707),
        [75014] = Explosions(RecurveBowIcon, 1492, 2707),
        [75015] = Splintering(RecurveBowIcon, 1492, 2707),
        [75016] = Stunning(RecurveBowIcon, 1492, 2707),
        [75017] = Flame(RecurveBowIcon, 1492, 2707),
        [75018] = Lightning(RecurveBowIcon, 1492, 2707),
        [75019] = Booming(RecurveBowIcon, 1492, 2707),

        [75020] = Volleys(RaptorBowIcon, 2372, 4750),
        [75021] = Blizzards(RaptorBowIcon, 2372, 4750),
        [75022] = Explosions(RaptorBowIcon, 2372, 4750),
        [75023] = Splintering(RaptorBowIcon, 2372, 4750),
        [75024] = Stunning(RaptorBowIcon, 2372, 4750),
        [75025] = Flame(RaptorBowIcon, 2372, 4750),
        [75026] = Lightning(RaptorBowIcon, 2372, 4750),
        [75027] = Booming(RaptorBowIcon, 2372, 4750),
        [75028] = Ricochet(RaptorBowIcon, 2372, 4750),
        [75029] = Firebomb(RaptorBowIcon, 2372, 4750),

        [9033] = Flame(MoltenBowIcon, 2372, 4750),
        [13655] = Flame(MoltenBowIcon, 2372, 4750),
        [55362] = Flame(MoltenBowIcon, 2372, 4750),
    };

    public static readonly int[] AllWeaponDefIds = ByWeaponDefId.Keys.ToArray();

    public static ArcherWeapon? GetEquippedWeapon(Player player)
    {
        var defId = player.GetEquippedWeaponDefinitionId();
        return defId != 0 && ByWeaponDefId.TryGetValue(defId, out var weapon) ? weapon : null;
    }

    public static WeaponAbility ResolveAbility(Player player, int slot)
    {
        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return BareShot;

        return slot switch
        {
            <= 0 => weapon.Basic,
            1 => weapon.Special,
            2 => weapon.Sniper,
            _ => weapon.Rain,
        };
    }

    public static AbilityPacketSetDefinition BuildToolbar(Player player, IResourceManager resources)
    {
        var weapon = GetEquippedWeapon(player);

        var equippedDefId = player.GetEquippedWeaponDefinitionId();
        var nameId = 0;
        if (resources.ClientItemDefinitions.TryGetValue(equippedDefId, out var weaponDef))
            nameId = weaponDef.NameId;

        if (weapon is null)
        {
            if (equippedDefId == 0)
                return AbilityPacketSetDefinition.CreateEmpty(ArcherProfileId);

            var fallback = new AbilityPacketSetDefinition { ProfileId = ArcherProfileId, SlotCount = 8 };
            fallback.Slots.Add(MakeSlot(BasicSlotDefId, BareShot.IconImageId, nameId, manaCost: 0));
            return fallback;
        }

        var def = new AbilityPacketSetDefinition { ProfileId = ArcherProfileId, SlotCount = 8 };

        def.Slots.Add(MakeSlot(BasicSlotDefId, weapon.Basic.IconImageId, nameId, manaCost: 0));
        def.Slots.Add(MakeSlot(SpecialSlotDefId, weapon.Special.IconImageId, nameId, manaCost: weapon.Special.EnergyCost));

        return def;
    }

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
