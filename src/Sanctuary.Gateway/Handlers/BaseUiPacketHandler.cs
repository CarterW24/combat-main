using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseUiPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseUiPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        var fullBuffer = reader.Span;

        if (!reader.TryRead(out byte subOpCode))
        {
            _logger.LogError("Failed to read sub-opcode from BaseUiPacket. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        switch (subOpCode)
        {
            case SelectQuestPacket.OpCode:
                return SelectQuestPacketHandler.HandlePacket(connection, fullBuffer);
            case 13:
                Console.WriteLine($"[BaseUiPacket] SelectedQuestLockedPacket from {connection.Player.Name}. Remaining bytes: {Convert.ToHexString(reader.RemainingSpan)}");
                break;
            case 6:
                _logger.LogInformation("[BaseUiPacket] SelectTaskRequest from {name}. Payload: {data}", connection.Player.Name, Convert.ToHexString(reader.RemainingSpan));
                break;
            default:
                Console.WriteLine($"[BaseUiPacket] sub-opcode {subOpCode} from {connection.Player.Name}. Remaining bytes: {Convert.ToHexString(reader.RemainingSpan)}");
                break;
        }

        return false;
    }
}
