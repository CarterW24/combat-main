using System;
using System.Collections.Generic;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientPathBasePacketHandler
{
    private static ILogger _logger = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientPathBasePacketHandler));

        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        var fullBuffer = reader.Span;

        if (!reader.TryRead(out byte subOpCode))
            return false;

        return subOpCode switch
        {
            ClientPathRequestPacket.OpCode => HandlePathRequest(connection, fullBuffer),
            _ => false
        };
    }

    private static bool HandlePathRequest(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientPathRequestPacket.TryDeserialize(data, out var request))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientPathRequestPacket));
            return false;
        }

        var player = connection.Player;

        var destination = request.End;
        if (_questManager.TryGetActiveObjectiveTarget(player, out var targetPosition))
            destination = new Vector4(targetPosition, 1f);

        var path = BuildPath(request.Start, destination);

        var trail = new ClientPathReplyPacket { RequestId = request.RequestId, ResultType = 1 };
        trail.Path.AddRange(path);
        player.SendTunneled(trail);

        if (request.Mode == 2)
        {
            var walk = new ClientPathReplyPacket { RequestId = request.RequestId, ResultType = 2 };
            walk.Path.AddRange(path);
            player.SendTunneled(walk);
        }

        _logger.LogInformation("[Path] {kind} for {name}: {a} -> {b} ({n} nodes)",
            request.Mode == 2 ? "Take-Me-There walk" : "trail refresh", player.Name, request.Start, destination, path.Count);
        return true;
    }

    private static List<Vector4> BuildPath(Vector4 start, Vector4 destination)
    {
        return new List<Vector4> { start, destination };
    }
}
