using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseFotomatPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseFotomatPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        // Log EVERY Fotomat sub-opcode. We were silently dropping (and not even logging) everything except
        // sub2, which hid the fact that the client talks to us about portraits at all — its own
        // capture/upload path constructs and SENDS a GeneratePortraitRequest (sub1) to the server.
        _logger.LogInformation("FOTOMAT recv sub={sub} | {data}", opCode, Convert.ToHexString(reader.Span));

        return opCode switch
        {
            // sub2 PortraitDataRequest — the client asking for a portrait; we reply with PlayerImageData.
            PacketPortraitDataRequest.OpCode => PacketPortraitDataRequestHandler.HandlePacket(connection, reader.Span),

            // sub1 GeneratePortraitRequest — the CLIENT asking the SERVER to generate a portrait (sub1 is
            // bidirectional). We used to drop it. It has the same wire layout as sub2 (guid + provider), so
            // answer it the same way: serve the target's PlayerImageData.
            PacketGeneratePortraitRequest.OpCode => PacketPortraitDataRequestHandler.HandlePacket(connection, reader.Span),

            _ => false
        };
    }
}