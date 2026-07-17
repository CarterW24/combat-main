using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class MiniGameEndPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MiniGameEndPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!MiniGameEndPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(MiniGameEndPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(MiniGameEndPacket), packet);

        var miniGameLeavePacket = new MiniGameLeavePacket(packet.StateId);

        connection.SendTunneled(miniGameLeavePacket);

        var player = connection.Player;

        if (player.Zone is CombatEncounterZone encounter)
        {
            _logger.LogInformation("Leave button pressed in {zone} — returning {name} to the overworld.", encounter.Name, player.Name);
            encounter.LeaveEncounter(player);
        }

        return true;
    }
}
