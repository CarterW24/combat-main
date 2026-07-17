using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

public interface IJobKit
{
    int ProfileId { get; }

    bool UsesCombatEnergy { get; }

    float AutoTargetReach { get; }

    IReadOnlyList<int> SlotAbilityDefIds { get; }

    AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources);

    WeaponAbility ResolveAbility(Player player, int slot);

    (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId);

    IReadOnlyList<int> WeaponDefIds { get; }

    List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId);

    List<AbilityExperience>? BuildTraitEntries(int rank);

    (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot);
}
