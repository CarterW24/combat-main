using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.UdpLibrary;

namespace Sanctuary.Game.Zones;

public interface IZone
{
    int Id { get; }
    string Name { get; }

    #region Events

    void OnClientIsReady(Player entity);
    void OnClientFinishedLoading(Player entity);

    /// <summary>COMBAT: called when an NPC in this zone is killed, so the zone decides the consequence
    /// (training dummy resets; encounter wolves despawn and advance the encounter; etc.).</summary>
    void OnNpcKilled(Player killer, Npc npc);

    /// <summary>COMBAT: called after every non-fatal hit lands on an NPC (post-ApplyDamage, when it did
    /// NOT die), so the zone can react to HP thresholds — e.g. the Frostfang Alpha flees at low health
    /// instead of dying. Default: no-op.</summary>
    void OnNpcDamaged(Player attacker, Npc npc);

    #endregion

    #region Entities

    IEnumerable<Npc> Npcs { get; }
    IEnumerable<Player> Players { get; }

    bool TryGetNpc(ulong guid, [MaybeNullWhen(false)] out Npc npc);
    bool TryGetPlayer(ulong guid, [MaybeNullWhen(false)] out Player player);
    bool TryGetEntity(ulong guid, [MaybeNullWhen(false)] out IEntity entity);

    bool TryAddMount(Mount mount);
    bool TryAddPlayer(Player player);

    bool TryCreateNpc([MaybeNullWhen(false)] out Npc npc);
    bool TryCreateMount(Player rider, MountDefinition definition, [MaybeNullWhen(false)] out Mount mount);
    bool TryCreatePlayer(ulong guid, UdpConnection connection, [MaybeNullWhen(false)] out Player player);

    bool TryRemoveNpc(ulong guid);
    bool TryRemovePlayer(ulong guid);

    #endregion

    #region Zone System

    ZoneTile GetTileFromPosition(Vector4 position);
    void UpdateEntityZoneTile(IEntity entity, ZoneTile from, ZoneTile to);

    #endregion
}