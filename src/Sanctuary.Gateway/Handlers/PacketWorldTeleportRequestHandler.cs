using System;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketWorldTeleportRequestHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketWorldTeleportRequestHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PacketWorldTeleportRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}. ( Raw: {raw} )",
                nameof(PacketWorldTeleportRequest), Convert.ToHexString(data));
            return false;
        }

        var player = connection.Player;

        var id = packet.Guid;
        _logger.LogInformation("WorldTeleportRequest: id={id} raw={raw}", id, Convert.ToHexString(data));

        var poi = _resourceManager.PointOfInterests.Values.FirstOrDefault(p =>
            (ulong)p.LocationId == id || (ulong)p.TeleportLocationId == id || (ulong)p.Id == id);

        if (poi is null)
        {
            _logger.LogWarning("WorldTeleportRequest: no POI matched id {id} — no teleport.", id);
            return true;
        }

        var target = poi.SpawnPosition != default ? poi.SpawnPosition : poi.Position;
        var rotation = new Quaternion(MathF.Sin(poi.Heading), 0f, MathF.Cos(poi.Heading), 0f);

        player.TeleportToZone(player.Zone, target, rotation, sky: null, geometryId: 0);

        _logger.LogInformation("WorldTeleportRequest: teleported {player} to POI id={id} (name {name}, atlas {atlas}) at {pos}.",
            player.Guid, poi.Id, poi.NameId, poi.AtlasName, target);

        return true;
    }
}
