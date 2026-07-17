using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

public static class GenericWeaponToolbar
{
    public static AbilityPacketSetDefinition? Build(Player player, IResourceManager resources, NinjaWeapon? weapon)
    {
        if (weapon is null)
            return AbilityPacketSetDefinition.CreateEmpty(player.ActiveProfileId);

        var nameId = 0;
        if (resources.ClientItemDefinitions.TryGetValue(player.GetEquippedWeaponDefinitionId(), out var weaponDef))
            nameId = weaponDef.NameId;

        var def = new AbilityPacketSetDefinition { ProfileId = player.ActiveProfileId, SlotCount = 8 };
        def.Slots.Add(MakeSlot(NinjaWeaponAbilities.MeleeAbilityDefId, weapon.Melee.IconImageId, nameId, manaCost: 0));
        def.Slots.Add(MakeSlot(NinjaWeaponAbilities.SpecialAbilityDefId, weapon.Special.IconImageId, nameId,
            manaCost: weapon.Special.EnergyCost));
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
