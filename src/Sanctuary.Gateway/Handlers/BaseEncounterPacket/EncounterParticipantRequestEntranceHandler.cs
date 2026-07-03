using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

// INSTANCE (Frostfang Fury): handles EncounterParticipantRequestEntrancePacket (op41/sub108, C2S) — the GO!
// button on the adventure offer popup. Wire format (client ctor sub_8B6E70):
//   [op41][sub108][int encounterId][int unk2][ulong playerGuid]
// NOTE it arrives on the WORLD tunnel (PacketTunneledClientWorldPacket), not the client tunnel.
//
// GO! -> ENTER: a REAL server-side zone transfer (Player.TeleportToZone) into the FrostfangArenaZone —
// world sg_random_encounter_clearing, identified from the client's own pack data (see the zone class +
// docs/STATUS.md). The proper transfer rebuilds tiles/visibility and sets OverrideUpdateRadius=true, which
// is what makes arena NPCs actually render (the earlier client-only fake zoning left them invisible).
[PacketHandler]
public static class EncounterParticipantRequestEntranceHandler
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EncounterParticipantRequestEntranceHandler));

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        // Keep logging the raw GO! bytes (the body carries the encounter id — useful once there are
        // multiple encounters to route).
        _logger.LogInformation("EncounterParticipantRequestEntrance (GO! pressed) | body={hex}",
            Convert.ToHexString(reader.Span));

        EnterFrostfangArena(connection);

        return true;
    }

    /// <summary>The one true GO!-&gt;arena entry: proper server-side zone transfer + the minigame
    /// GameStart ack (op39/sub17) that drives the client's minigame state machine.</summary>
    public static void EnterFrostfangArena(GatewayConnection connection)
    {
        var arena = _zoneManager.GetOrCreateFrostfangArena();

        // Reference video: the whole encounter runs under a dark fog ("gloam") that lifts only after
        // victory. The arena world is an sg_ (Shrouded Glade) world, and the client ships a matching
        // mood sky: sky_shrouded_gloam.xml (always-night, heavy fog 10..600).
        connection.Player.TeleportToZone(arena, arena.EffectiveSpawn, arena.SpawnRotation,
            sky: "sky_shrouded_gloam.xml", geometryId: 0);

        // MiniGame lifecycle: the game has started (StateId<=0 targets the client's current MiniGameState —
        // the one our offer popup created). Also the packet that clears m_bWaitForZoneReadyPacket if set.
        connection.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));

        _logger.LogInformation("GO! -> TeleportToZone {zone} ({id}) at {pos}.", arena.Name, arena.Id, arena.EffectiveSpawn);
    }
}
