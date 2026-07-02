using System;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

// INSTANCE WIP (Frostfang Fury): handles EncounterParticipantRequestEntrancePacket (op41/sub108, C2S) â€” what the
// GO! / "Press to Teleport" button on the adventure offer popup sends.
//
// âš ï¸ OBSERVE-ONLY FOR NOW. Two open questions resolved this BEFORE we teleport anywhere:
//   1) DESTINATION WORLD is NOT the ice cavern. "Frostfang Growler!" is a WANDERING COMBAT ENCOUNTER in the
//      Snowhill woods (locale: "Defeat Frostfang Growlers in Snowhill", "...wandering combat encounter"; the
//      offer text says the wolves "are in the woods"). sh_frostfang_cavern (POI 59) is the separate ice dungeon.
//      So the fight is likely an OVERWORLD encounter (EncounterOverworldCombatPacket op41/sub132) or a forest
//      arena â€” TBD. Don't hardcode the cavern.
//   2) LOADING-SPINNER GATE (tip from another dev): the client calls AddIdToActivityDatasource(name, id) to fill
//      the minigame detail/category datasource. Sending only the id (no datasource NAME) leaves the category
//      empty and the spinner hangs forever. So the GO!->enter path needs an Activity/MiniGame packet that
//      populates that datasource with BOTH name+id (IDA target).
// This handler logs the exact GO! bytes so we can reconstruct the C2S format + see what the client expects next.
[PacketHandler]
public static class EncounterParticipantRequestEntranceHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EncounterParticipantRequestEntranceHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        // Keep logging the raw GO! bytes (C2S format still not formally decoded â€” the transition works
        // without parsing it, but the body likely carries the encounter id for multi-encounter routing).
        _logger.LogInformation("EncounterParticipantRequestEntrance (GO! pressed) | body={hex}",
            Convert.ToHexString(reader.Span));

        // GO! -> ENTER: a REAL cross-world zone into the actual Frostfang Growler arena world, identified from
        // the client's own packs (2026-07-01): sg_random_encounter_clearing â€” the green grass clearing matching
        // the Sunrise video (its .gzne uses sg_grass/sg_stone/mv_grass, no snow). Real playable center (136,0,165)
        // r100 from sg_random_encounter_clearingAreas.xml. A cross-world load (unlike the earlier same-world hack)
        // applies the position + drops the player on real ground with WaitForZoneReadyPacket=false, the proven
        // !home pattern. BeginFrostfangEncounter marks the player so post-load ClientIsReady spawns the pack.
        if (connection.Player.Zone is Sanctuary.Game.Zones.StartingZone startingZone)
        {
            startingZone.BeginFrostfangEncounter(connection.Player);

            EnterWorld(connection,
                Sanctuary.Game.Zones.StartingZone.FrostfangArenaWorldName,
                startingZone.FrostfangArenaSpawn,
                Sanctuary.Game.Zones.StartingZone.FrostfangArenaZoneId);
        }

        return true;
    }

    // Reusable re-zone helper. For a CROSS-WORLD load (e.g. FabledRealms -> sg_random_encounter_clearing) the
    // client does a full geometry load and applies the position + drops the player on real ground; the earlier
    // fall-through happened only because the coords were outside the target world's terrain.
    public static void EnterWorld(GatewayConnection connection, string worldName, Vector4 spawn, int zoneId)
    {
        connection.Player.UpdatePosition(spawn, Quaternion.Identity); // keep server & client position in sync

        connection.SendTunneled(new PacketClientBeginZoning
        {
            Name = worldName,
            Sky = null,                      // let the world define its own sky (a bad sky string was rejected)
            Position = spawn,
            Rotation = Quaternion.Identity,
            Id = zoneId,
            // LIVE TEST 5 lesson: for a SAME-WORLD re-zone, WaitForZoneReadyPacket=true + instant GameStart
            // short-circuits the reload and the client NEVER APPLIES Position (player stays put while the
            // server thinks they moved â€” wolves then cluster at the phantom spot). !home proves false works:
            // the client teleports immediately. Re-introduce true + the GameStart gate when the arena becomes
            // a real separate world (geometry load is what makes the wait path apply the position).
            WaitForZoneReadyPacket = false,
        });

        _logger.LogInformation("ClientBeginZoning -> {world} at {pos} (WaitForZoneReady=true).", worldName, spawn);
    }
}
