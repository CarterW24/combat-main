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

        _logger.LogInformation("FOTOMAT recv sub={sub} | {data}", opCode, Convert.ToHexString(reader.Span));

        return opCode switch
        {
            PacketPortraitDataRequest.OpCode => PacketPortraitDataRequestHandler.HandlePacket(connection, reader.Span),

            PacketGeneratePortraitRequest.OpCode => PacketPortraitDataRequestHandler.HandlePacket(connection, reader.Span),

            _ => false
        };
    }
}
