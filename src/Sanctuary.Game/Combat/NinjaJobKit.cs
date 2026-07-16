using System;
using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

// Ninja kit — surface over NinjaWeaponAbilities. Traits are data'd; the Attack/Special columns aren't mined yet
// (SlotNameIcon returns 0 name + no item ability entries), so those stay as-is until the ninja names land.
public sealed class NinjaJobKit : IJobKit
{
    public int ProfileId => NinjaWeaponAbilities.NinjaProfileId;
    public bool UsesCombatEnergy => true;
    public float AutoTargetReach => 7f;
    public IReadOnlyList<int> SlotAbilityDefIds { get; } = new[] { 4895, 4899 };
    public IReadOnlyList<int> WeaponDefIds { get; } = Array.Empty<int>();

    public AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        NinjaWeaponAbilities.BuildToolbar(player, resources);

    public WeaponAbility ResolveAbility(Player player, int slot) =>
        NinjaWeaponAbilities.ResolveAbility(player, slot);

    public (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId) =>
        NinjaWeaponAbilities.ResolveDefinition(player, abilityDefId);

    public List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId) => new();

    public List<AbilityExperience>? BuildTraitEntries(int rank) => NinjaWeaponAbilities.BuildTraitEntries(rank);

    // Ninja ability names/icons for the Attack/Special columns aren't mined yet — 0 name tells the screen setup
    // to leave the ability slots alone (the traits still show).
    public (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot) => (0, 0, 0);
}
