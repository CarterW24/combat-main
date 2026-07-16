using System.Collections.Generic;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

// A combat job's weapon/ability kit. One per job, registered in JobKits by ProfileId. Adding a job = write one
// of these + one registry line; nothing else in the combat/login/item-def plumbing needs a per-job branch. The
// heavy per-job data still lives in the job's own class (e.g. ArcherWeaponAbilities) — a kit is the thin surface.
public interface IJobKit
{
    // Profiles.json id (the registry key).
    int ProfileId { get; }

    // Combat jobs use the ability-handler energy bar, not the mana regen.
    bool UsesCombatEnergy { get; }

    // Auto-target reach for an unselected attack (bow range vs melee).
    float AutoTargetReach { get; }

    // The bar slot def ids the client asks us to define for the AbilitiesScreen columns (4895 Attack / 4899 Special).
    IReadOnlyList<int> SlotAbilityDefIds { get; }

    // The equipped-weapon hotbar (op36/5), or null.
    AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources);

    // A pressed bar slot (0 = basic, 1 = special) -> its ability.
    WeaponAbility ResolveAbility(Player player, int slot);

    // A client def request (op36/12) -> name/desc/icon, or null if not ours.
    (int NameId, int DescId, int IconId)? ResolveDefinition(Player player, int abilityDefId);

    // Weapon def ids whose item-definition Abilities list we seed (feeds the screen columns). Empty if none yet.
    IReadOnlyList<int> WeaponDefIds { get; }

    // The two ability entries (slot 0 = Attack, 1 = Special) for a weapon's item definition.
    List<ItemDefinition.ItemAbilityEntry> BuildItemAbilityEntries(int weaponDefId);

    // The profile ability-experience list (Traits section) for a rank, or null if the job hasn't data'd it.
    List<AbilityExperience>? BuildTraitEntries(int rank);

    // Name/desc/icon for an AbilitiesScreen column (slot 0 = Attack, 1 = Special).
    (int NameId, int DescId, int IconId) SlotNameIcon(int weaponDefId, int slot);
}
