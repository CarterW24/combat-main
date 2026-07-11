using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
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
    private static Sanctuary.Game.Party.IPartyManager _partyManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EncounterParticipantRequestEntranceHandler));

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _partyManager = serviceProvider.GetRequiredService<Sanctuary.Game.Party.IPartyManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        _logger.LogInformation("EncounterParticipantRequestEntrance (GO! pressed) | body={hex}",
            Convert.ToHexString(reader.Span));

        // The body's first int is the encounter/activity id ([int encounterId][int unk2][ulong
        // playerGuid], client ctor sub_8B6E70) — route to the right arena. Unparseable/unknown ids
        // fall back to Frostfang (the pre-routing behavior).
        reader.TryRead(out int encounterId);

        if (encounterId == TormentedSpiritsArenaZone.EncounterId)
            EnterSpiritArena(connection);
        else
            EnterFrostfangArena(connection);

        return true;
    }

    /// <summary>The one true GO!-&gt;arena entry: proper server-side zone transfer + the minigame
    /// GameStart ack (op39/sub17) that drives the client's minigame state machine. CO-OP: the leader's
    /// whole party is pulled into the Frostfang instance (which has the multi-player encounter
    /// lifecycle — see FrostfangArenaZone).</summary>
    public static void EnterFrostfangArena(GatewayConnection connection)
    {
        var arena = _zoneManager.GetOrCreateFrostfangArena();

        void Enter(Player player)
        {
            // Sky = null so the world's natural bright-green daytime renders (VIDEO GROUND TRUTH
            // 2026-07-03; the old gloam sky was too dark).
            player.TeleportToZone(arena, arena.EffectiveSpawn, arena.SpawnRotation, sky: null, geometryId: 0);
            player.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));
            _logger.LogInformation("GO! -> TeleportToZone {zone} ({id}) for {name} at {pos}.",
                arena.Name, arena.Id, player.Name, arena.EffectiveSpawn);
        }

        EnterWithParty(connection.Player, Enter);
    }

    /// <summary>GO! -&gt; the Tormented Spirits graveyard arena (same transfer recipe as Frostfang).
    /// CO-OP: the leader's whole party is pulled in (the spirit arena now has the same multi-player
    /// encounter lifecycle as Frostfang).</summary>
    public static void EnterSpiritArena(GatewayConnection connection)
    {
        var arena = _zoneManager.GetOrCreateSpiritArena();

        void Enter(Player player)
        {
            // Stash the overworld spot so the exit door returns each member to where THEY were standing
            // in the Blackspore graveyard (not the world spawn).
            player.EncounterReturnPosition = player.Position;
            player.TeleportToZone(arena, arena.SpawnPosition, arena.SpawnRotation, sky: null, geometryId: 0);
            player.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));
            _logger.LogInformation("GO! -> TeleportToZone {zone} ({id}) for {name} at {pos}.",
                arena.Name, arena.Id, player.Name, arena.SpawnPosition);
        }

        EnterWithParty(connection.Player, Enter);
    }

    /// <summary>Enter the leader, then pull every other party member through the same enter action
    /// (co-op). For a soloist this is just the single enter.</summary>
    private static void EnterWithParty(Player leader, Action<Player> enter)
    {
        enter(leader);

        var party = _partyManager.GetParty(leader);
        if (party is null || !party.IsLeader(leader))
            return;

        foreach (var member in party.Members)
        {
            if (member.Guid == leader.Guid)
                continue;
            _logger.LogInformation("GO! -> pulling party member {name} into the arena.", member.Name);
            enter(member);
        }
    }
}
