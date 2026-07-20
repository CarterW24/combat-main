using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

public sealed record WeaponAbility(string Name, int IconImageId, int Damage, int Animation, int EffectId, int CastEffectId = 0, int SummonCount = 0, int SwordEffectId = 0, int CasterEndEffectId = 0, int EnemyExtraEffectId = 0, float AoeRadius = 0f, int CastEffectStopMs = 0, int EnergyCost = 100, int BuffMultiplierPct = 0, int BuffDurationMs = 0, int PersistEffectId = 0, float Reach = 7f);

public sealed record NinjaWeapon(WeaponAbility Melee, WeaponAbility Special);

public static class NinjaWeaponAbilities
{
    public const int WeaponSlot = 7;
    public const int NinjaProfileId = 2;

    private const int MeleeAnimation = 1021;
    private const int MeleeHitFx = 7;
    private const int MeleeIcon = 14407;

    private const int MeleeSlotDefId = 4895;
    private const int SpecialSlotDefId = 4899;

    public static int? DebugMeleeIcon;
    public static int? DebugSpecialIcon;

    public static readonly WeaponAbility BareMelee = new("Strike", MeleeIcon, 150, MeleeAnimation, MeleeHitFx);

    public const int MeleeAbilityDefId = MeleeSlotDefId;
    public const int SpecialAbilityDefId = SpecialSlotDefId;

    private static readonly IReadOnlyDictionary<string, int> AbilityNameIds = new Dictionary<string, int>
    {
        ["Twisted Edge"] = 420638, ["Cinder Slash"] = 421045, ["Flame Wave"] = 421047, ["Shuriken Storm"] = 420984,
        ["Dragonstrike"] = 442457, ["Dark Assault"] = 421250, ["Ashen Strike"] = 421251, ["Fiery Slice"] = 421266,
        ["Mystic Rush"] = 421267, ["Flame Breath"] = 421278, ["Mystical Blade"] = 421279, ["Shadowslash"] = 421290,
        ["Hidden Strike"] = 421291, ["Mystical Drain"] = 421302, ["Fan of Blades"] = 421303, ["Shadow Army"] = 421248,
        ["Flaming Uppercut"] = 421249,
    };
    private const int FallbackMeleeNameId = 420638;
    private const int FallbackSpecialNameId = 420984;

    public static (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot)
    {
        ByWeaponDefId.TryGetValue(weaponDefId, out var weapon);
        var ability = weapon is null ? BareMelee : (slot == 1 ? weapon.Special : weapon.Melee);
        var nameId = AbilityNameIds.TryGetValue(ability.Name, out var id)
            ? id : (slot == 1 ? FallbackSpecialNameId : FallbackMeleeNameId);
        return (nameId, 0, ability.IconImageId);
    }

    public static List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId)
    {
        var (_, _, meleeIcon) = SlotNameIcon(weaponDefId, 0);
        var (_, _, specialIcon) = SlotNameIcon(weaponDefId, 1);
        return new List<ItemDefinition.ItemAbilityEntry>
        {
            new() { Slot = 0, Id = MeleeSlotDefId, IconId = meleeIcon },
            new() { Slot = 1, Id = SpecialSlotDefId, IconId = specialIcon },
        };
    }

    public static (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId)
    {
        var slot = abilityDefId switch
        {
            MeleeSlotDefId => 0,
            SpecialSlotDefId => 1,
            _ => -1,
        };
        if (slot < 0)
            return null;

        return SlotNameIcon(player.GetEquippedWeaponDefinitionId(), slot);
    }

    public const int ShroudedArmorLevel = 5;
    public const int NinjasGraceLevel = 10;
    public const int DragonsBoonLevel = 15;
    public const int InstigationLevel = 20;

    public const float ShroudedArmorDamageReduction = 0.12f;
    public const float NinjasGraceSpeedMultiplier = 1.15f;

    private static readonly JobTraits.Trait[] TraitData =
    [
        new(420947, 420971, 43,    ShroudedArmorLevel),
        new(420948, 420972, 40,    NinjasGraceLevel),
        new(420949, 420973, 26725, DragonsBoonLevel),
        new(420950, 420974, 11646, InstigationLevel),
    ];

    public static List<AbilityExperience> BuildTraitEntries(int rank) => JobTraits.Build(TraitData, rank);

    public static bool HasTrait(Player player, int traitLevel) =>
        player.ActiveProfileId == NinjaProfileId && player.ActiveProfile.Rank >= traitLevel;

    private static readonly NinjaWeapon ShurikenStormKit = new(
        new("Twisted Edge",   MeleeIcon, 2870, MeleeAnimation, MeleeHitFx),
        new("Shuriken Storm",     22986, 8302, 1039, 4012, 0));

    private static readonly NinjaWeapon FlameWaveKit = new(
        new("Cinder Slash",   MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
        new("Flame Wave",         22974, 10674, 1032, 0, 16140));

    private static readonly NinjaWeapon DragonstrikeKit = new(
        new("Flame Flash",    MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
        new("Dragonstrike",       22965, 10674, 1035, 0, 0, 0, 0, 16186));

    private static readonly NinjaWeapon ThousandStormsKit = new(
        new("Lightning Strike", MeleeIcon, 2372, MeleeAnimation, MeleeHitFx),
        new("1000 Storms",          22992, 8302, 1033, 0, 16088, AoeRadius: 12f));

    private static readonly NinjaWeapon ShadowArmiesKit = new(
        new("Dark Assault",   MeleeIcon, 2608, MeleeAnimation, MeleeHitFx),
        new("Shadow Army",        22989, 3000, 1061143, 21, 16483, 3));

    private static readonly NinjaWeapon SolarFlareKit = new(
        new("Ashen Strike",     MeleeIcon, 2870, MeleeAnimation, MeleeHitFx),
        new("Flaming Uppercut",     22977, 8302, 1017, 0, 16119));

    private static readonly NinjaWeapon DragonBreathKit = new(
        new("Fiery Slice",  MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
        new("Flame Breath",     22971, 10674, 1037, 0, 16129));

    private static readonly NinjaWeapon MysticismKit = new(
        new("Mystic Rush",    MeleeIcon, 2608, MeleeAnimation, MeleeHitFx),
        new("Mystical Blade",     22980, 0, 1061141, 0, 0, 0, 16169,
            BuffMultiplierPct: 200, BuffDurationMs: 15000));

    private static readonly NinjaWeapon SoulPowerKit = new(
        new("Shadowslash",    MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
        new("Mystical Drain",     22983, 8302, 1034, 16180, 16180));

    private static readonly NinjaWeapon DeceptionKit = new(
        new("Hidden Strike", MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
        new("Fan of Blades",     22968, 5977, 1033, 0, 16185));

    public static readonly IReadOnlyDictionary<int, NinjaWeapon> ByWeaponDefId = new Dictionary<int, NinjaWeapon>
    {
        [75090] = DragonstrikeKit,
        [75091] = ThousandStormsKit,

        [75092] = DragonstrikeKit,
        [75093] = ThousandStormsKit,
        [75094] = ShurikenStormKit,
        [75095] = FlameWaveKit,

        [75096] = DragonstrikeKit,
        [75097] = ThousandStormsKit,
        [75098] = ShurikenStormKit,
        [75099] = FlameWaveKit,
        [75100] = ShadowArmiesKit,
        [75101] = SolarFlareKit,

        [75102] = DragonstrikeKit,
        [75103] = ThousandStormsKit,
        [75104] = ShurikenStormKit,
        [75105] = FlameWaveKit,
        [75106] = ShadowArmiesKit,
        [75107] = SolarFlareKit,
        [75108] = DragonBreathKit,
        [75109] = MysticismKit,

        [75110] = DragonstrikeKit,
        [75111] = ThousandStormsKit,
        [75112] = ShurikenStormKit,
        [75113] = FlameWaveKit,
        [75114] = ShadowArmiesKit,
        [75115] = SolarFlareKit,
        [75116] = DragonBreathKit,
        [75117] = MysticismKit,
        [75118] = SoulPowerKit,
        [75119] = DeceptionKit,

        [13663] = DragonstrikeKit, [55337] = DragonstrikeKit, [70444] = DragonstrikeKit, [76470] = DragonstrikeKit,
        [9031] = ThousandStormsKit, [13669] = ThousandStormsKit, [55360] = ThousandStormsKit,
        [78715] = MysticismKit,
        [79022] = SoulPowerKit,
        [48322] = DragonBreathKit,
    };

    public static readonly int[] AllWeaponDefIds = ByWeaponDefId.Keys.ToArray();

    public static NinjaWeapon? GetEquippedWeapon(Player player)
    {
        var defId = player.GetEquippedWeaponDefinitionId();
        return defId != 0 && ByWeaponDefId.TryGetValue(defId, out var weapon) ? weapon : null;
    }

    public static WeaponAbility ResolveAbility(Player player, int slot)
    {
        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return BareMelee;

        return slot <= 0 ? weapon.Melee : weapon.Special;
    }

    public static AbilityPacketSetDefinition BuildToolbar(Player player, IResourceManager resources)
    {
        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return new AbilityPacketSetDefinition { ProfileId = NinjaProfileId };

        var nameId = 0;
        if (resources.ClientItemDefinitions.TryGetValue(player.GetEquippedWeaponDefinitionId(), out var weaponDef))
            nameId = weaponDef.NameId;

        var def = new AbilityPacketSetDefinition { ProfileId = NinjaProfileId };

        def.AbilitySet.Abilities[0] = MakeSlot(MeleeSlotDefId, DebugMeleeIcon ?? weapon.Melee.IconImageId, nameId, manaCost: 0);
        def.AbilitySet.Abilities[1] = MakeSlot(SpecialSlotDefId, DebugSpecialIcon ?? weapon.Special.IconImageId, nameId, manaCost: SpecialEnergyCost);

        return def;
    }

    public const int SpecialEnergyCost = 100;

    private static Sanctuary.Packet.Common.Ability MakeSlot(int abilityDefId, int iconId, int nameId, int manaCost) => new()
    {
        Type = 3,
        Unknown2 = abilityDefId,
        ManaCost = manaCost,
        IconId = iconId,
        NameId = nameId,
        Unknown7 = 4,
        Unknown9 = 1,
        AbilityDefinitionId = abilityDefId,
        ForceDismount = true,
    };
}
