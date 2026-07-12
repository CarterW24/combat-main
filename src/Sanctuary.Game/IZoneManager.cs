using System.Diagnostics.CodeAnalysis;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game;

public interface IZoneManager
{
    StartingZone StartingZone { get; }

    bool Load();

    bool TryGetPlayer(ulong guid, [MaybeNullWhen(false)] out Player player);
    bool TryGetPlayer(string name, [MaybeNullWhen(false)] out Player player);

    /// <summary>INSTANCE (Frostfang Fury): the sg_random_encounter_clearing arena zone — created on first
    /// use and reused (single shared instance for now; per-party instancing later).</summary>
    FrostfangArenaZone GetOrCreateFrostfangArena();

    /// <summary>INSTANCE (Tormented Spirits!): the bs_random_encounter_01 graveyard arena zone —
    /// same lazy shared-instance model as the Frostfang arena.</summary>
    TormentedSpiritsArenaZone GetOrCreateSpiritArena();

    /// <summary>INSTANCE (data-driven combat dungeons): the shared EncounterArenaZone for a DungeonCatalog
    /// activity id — created on first use and reused (same lazy shared-instance model as the two above).</summary>
    EncounterArenaZone GetOrCreateEncounterArena(int activityId);
}