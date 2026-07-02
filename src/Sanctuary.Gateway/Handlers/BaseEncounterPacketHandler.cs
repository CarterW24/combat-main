using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

// INSTANCE WIP (Frostfang Fury): the C2S dispatcher for BaseEncounterPacket (op41). Previously NOTHING routed
// op41 inbound â€” clicking GO! on the adventure offer popup fell through unhandled. This reads the sub-opcode and
// dispatches. It also OBSERVE-LOGS every sub-opcode (like BaseAbilityPacketHandler) so we can see exactly what
// the offer popup's buttons send (sub108 EncounterParticipantRequestEntrance = GO!; sub109 RequestExit; etc.)
// and reconstruct their wire formats from the live bytes.
[PacketHandler]
public static class BaseEncounterPacketHandler
{
    // op41 sub-opcodes (from exports/packet-opcode-map.tsv).
    private const short EncounterParticipantRequestEntrance = 108; // C2S = the GO! / "Press to Teleport" button.

    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseEncounterPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short subOpCode))
        {
            _logger.LogError("Failed to read encounter sub-opcode. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        _logger.LogInformation("BaseEncounterPacket sub-opcode={sub} | remaining bytes={hex}",
            subOpCode, Convert.ToHexString(reader.Span));

        return subOpCode switch
        {
            EncounterParticipantRequestEntrance => EncounterParticipantRequestEntranceHandler.HandlePacket(connection, reader),
            // observe-only: don't hard-fail unknown/unmapped encounter sub-opcodes while we reverse them
            _ => true
        };
    }
}
