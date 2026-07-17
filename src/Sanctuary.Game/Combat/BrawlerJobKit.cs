using System;
using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

// Brawler kit — surface over the BrawlerWeaponAbilities table (Enrage is the one sheet-confirmed
// row so far). No traits/AbilitiesScreen data yet.
public sealed class BrawlerJobKit : IJobKit
{
    public int ProfileId => BrawlerWeaponAbilities.BrawlerProfileId;
    public bool UsesCombatEnergy => true;
    public float AutoTargetReach => 7f;
    public IReadOnlyList<int> SlotAbilityDefIds { get; } = new[] { NinjaWeaponAbilities.MeleeAbilityDefId, NinjaWeaponAbilities.SpecialAbilityDefId };
    public IReadOnlyList<int> WeaponDefIds => Array.Empty<int>();

    public AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        GenericWeaponToolbar.Build(player, resources, BrawlerWeaponAbilities.GetEquippedWeapon(player));

    public WeaponAbility ResolveAbility(Player player, int slot)
    {
        var weapon = BrawlerWeaponAbilities.GetEquippedWeapon(player);
        if (weapon is null)
            return NinjaWeaponAbilities.BareMelee;
        return slot <= 0 ? weapon.Melee : weapon.Special;
    }

    public (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId) => null;

    public List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId) => [];

    public List<AbilityExperience>? BuildTraitEntries(int rank) => null;

    public (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot) => (0, 0, 0);
}
