using System;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketLeaveHouseHandler
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketLeaveHouseHandler));

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketLeaveHouse.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientHousingPacketLeaveHouse));
            return false;
        }

        connection.Player.CurrentHouseGuid = 0;

        var startingZone = _zoneManager.StartingZone;

        var position = connection.Player.StartingZonePosition;
        var rotation = connection.Player.StartingZoneRotation;

        var packetClientBeginZoning = new PacketClientBeginZoning
        {
            Name = startingZone.Name,
            Type = 0,
            Position = position,
            Rotation = rotation,
            Sky = string.Empty,
            Unknown = 0,
            Id = 0,
            GeometryId = 0,
            OverrideUpdateRadius = false
        };

        connection.SendTunneled(packetClientBeginZoning);

        _logger.LogInformation("Player {name} left house and returned to starting zone", connection.Player.Name.FirstName);

        return true;
    }
}
