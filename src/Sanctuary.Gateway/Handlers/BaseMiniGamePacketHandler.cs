using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

// INSTANCE WIP (Frostfang Fury): C2S dispatcher for BaseMiniGamePacket (op39) — ported from the team's
// `minigame` branch. LIVE TEST 1 (2026-07-01) taught us the GO! button does NOT send op41/sub108
// (EncounterParticipantRequestEntrance) as assumed — the only thing we logged was CommandPacket sub42
// ClosedMinigameEndScreen (the panel closing). The branch's flow says starting a minigame sends
// op39/sub5 MiniGameStartGame -> server acks with sub17 GameStart. This handler observe-logs EVERY op39
// sub-opcode and treats sub5 as the GO! press: ack + enter the Frostfang arena.
[PacketHandler]
public static class BaseMiniGamePacketHandler
{
    // op39 sub-opcodes (byte-sized!) from the minigame branch.
    private const byte MiniGameStartGame = 5; // C2S — pressing GO!/start on a minigame offer panel

    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseMiniGamePacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out byte subOpCode))
        {
            _logger.LogError("Failed to read minigame sub-opcode. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        _logger.LogInformation("BaseMiniGamePacket sub-opcode={sub} | bytes={hex}",
            subOpCode, Convert.ToHexString(reader.Span));

        return subOpCode switch
        {
            MiniGameStartGame => HandleStartGame(connection, reader),
            // observe-only: log-and-accept unknown minigame sub-opcodes while we reverse the family
            _ => true
        };
    }

    private static bool HandleStartGame(GatewayConnection connection, PacketReader reader)
    {
        // body: [int StateId][int GroupId][int GameId]
        if (!reader.TryRead(out int stateId) || !reader.TryRead(out int groupId) || !reader.TryRead(out int gameId))
        {
            _logger.LogWarning("MiniGameStartGame: short body ( {hex} ) — acking anyway.", Convert.ToHexString(reader.Span));
            stateId = 0; groupId = -1; gameId = -1;
        }

        _logger.LogInformation("MiniGameStartGame (GO! pressed): StateId={state} GroupId={group} GameId={game}",
            stateId, groupId, gameId);

        // Same entry as the sub108 GO! path: proper server-side zone transfer into the arena
        // (also sends the GameStart ack).
        EncounterParticipantRequestEntranceHandler.EnterFrostfangArena(connection);

        return true;
    }
}
