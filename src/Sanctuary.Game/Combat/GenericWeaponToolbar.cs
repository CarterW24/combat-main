using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

public static class GenericWeaponToolbar
{
    public static AbilityPacketSetDefinition? Build(Player player, IResourceManager resources, NinjaWeapon? weapon)
    {
        if (weapon is null)
            return new AbilityPacketSetDefinition { ProfileId = player.ActiveProfileId };

        var nameId = 0;
        if (resources.ClientItemDefinitions.TryGetValue(player.GetEquippedWeaponDefinitionId(), out var weaponDef))
            nameId = weaponDef.NameId;

        var def = new AbilityPacketSetDefinition { ProfileId = player.ActiveProfileId };
        def.AbilitySet.Abilities[0] = MakeSlot(NinjaWeaponAbilities.MeleeAbilityDefId, weapon.Melee.IconImageId, nameId, manaCost: 0);
        def.AbilitySet.Abilities[1] = MakeSlot(NinjaWeaponAbilities.SpecialAbilityDefId, weapon.Special.IconImageId, nameId,
            manaCost: weapon.Special.EnergyCost);
        return def;
    }

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
