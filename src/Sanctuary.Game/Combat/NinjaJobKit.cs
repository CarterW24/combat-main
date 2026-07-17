using System;
using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

public sealed class NinjaJobKit : IJobKit
{
    public int ProfileId => NinjaWeaponAbilities.NinjaProfileId;
    public bool UsesCombatEnergy => true;
    public float AutoTargetReach => 7f;
    public IReadOnlyList<int> SlotAbilityDefIds { get; } = new[] { 4895, 4899 };
    public IReadOnlyList<int> WeaponDefIds => NinjaWeaponAbilities.AllWeaponDefIds;

    public AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        NinjaWeaponAbilities.BuildToolbar(player, resources);

    public WeaponAbility ResolveAbility(Player player, int slot) =>
        NinjaWeaponAbilities.ResolveAbility(player, slot);

    public (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId) =>
        NinjaWeaponAbilities.ResolveDefinition(player, abilityDefId);

    public List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId) =>
        NinjaWeaponAbilities.BuildItemAbilityEntries(weaponDefId);

    public List<AbilityExperience>? BuildTraitEntries(int rank) => NinjaWeaponAbilities.BuildTraitEntries(rank);

    public (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot) =>
        NinjaWeaponAbilities.SlotNameIcon(weaponDefId, slot);
}
