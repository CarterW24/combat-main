using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

// Archer kit — a thin surface over ArcherWeaponAbilities.
public sealed class ArcherJobKit : IJobKit
{
    public int ProfileId => ArcherWeaponAbilities.ArcherProfileId;
    public bool UsesCombatEnergy => true;
    public float AutoTargetReach => ArcherWeaponAbilities.BowReach;
    public IReadOnlyList<int> SlotAbilityDefIds { get; } =
        new[] { ArcherWeaponAbilities.BasicAbilityDefId, ArcherWeaponAbilities.SpecialAbilityDefId };
    public IReadOnlyList<int> WeaponDefIds => ArcherWeaponAbilities.AllWeaponDefIds;

    public AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        ArcherWeaponAbilities.BuildToolbar(player, resources);

    public WeaponAbility ResolveAbility(Player player, int slot) =>
        ArcherWeaponAbilities.ResolveAbility(player, slot);

    public (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId) =>
        ArcherWeaponAbilities.ResolveDefinition(player, abilityDefId);

    public List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId) =>
        ArcherWeaponAbilities.BuildItemAbilityEntries(weaponDefId);

    public List<AbilityExperience>? BuildTraitEntries(int rank) =>
        ArcherWeaponAbilities.BuildTraitEntries(rank);

    public (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot) =>
        ArcherWeaponAbilities.SlotNameIcon(weaponDefId, slot);
}
