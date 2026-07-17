using System;
using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

// Wizard kit — surface over the WizardWandAbilities wand tables (49 wands, spreadsheet-confirmed).
// No traits/AbilitiesScreen data yet: those return null/none until the wizard sheet rows land.
public sealed class WizardJobKit : IJobKit
{
    public int ProfileId => WizardWandAbilities.WizardProfileId;
    public bool UsesCombatEnergy => true;
    public float AutoTargetReach => 25f; // wand basics are RANGED
    public IReadOnlyList<int> SlotAbilityDefIds { get; } = new[] { NinjaWeaponAbilities.MeleeAbilityDefId, NinjaWeaponAbilities.SpecialAbilityDefId };
    public IReadOnlyList<int> WeaponDefIds => Array.Empty<int>();

    public AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        GenericWeaponToolbar.Build(player, resources, WizardWandAbilities.GetEquippedWeapon(player));

    public WeaponAbility ResolveAbility(Player player, int slot)
    {
        var weapon = WizardWandAbilities.GetEquippedWeapon(player);
        if (weapon is null)
            return NinjaWeaponAbilities.BareMelee;
        return slot <= 0 ? weapon.Melee : weapon.Special;
    }

    public (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId) => null;

    public List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId) => [];

    public List<AbilityExperience>? BuildTraitEntries(int rank) => null;

    public (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot) => (0, 0, 0);
}
