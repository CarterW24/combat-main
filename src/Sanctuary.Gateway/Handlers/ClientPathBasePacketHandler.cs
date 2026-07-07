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

/// <summary>
/// Opcode 98 - the "Take Me There" path family. On a ClientPathRequestPacket (sub 1, sent when the button
/// is clicked) we reply with a ClientPathReplyPacket (sub 2) whose waypoint list the client turns into the
/// green breadcrumb trail + auto-walk. The path runs from the client's start position to the tracked
/// quest's target NPC (falling back to the client-provided end point).
/// </summary>
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

        // Destination: the tracked quest's target NPC if we have one, otherwise the point the client asked for.
        var destination = request.End;
        if (_questManager.TryGetActiveObjectiveTarget(player, out var targetPosition))
            destination = new Vector4(targetPosition, 1f);

        var path = BuildPath(request.Start, destination);

        // The reply's ResultType routes it to a different client controller: 1 = the breadcrumb trail
        // (renders the green line), 2 = the character auto-move (pushes the path into the ProxiedCharacter's
        // movement so it actually walks). Send both so we get the trail AND the auto-walk.
        foreach (var resultType in new[] { 1, 2 })
        {
            var reply = new ClientPathReplyPacket { RequestId = request.RequestId, ResultType = resultType };
            reply.Path.AddRange(path);
            player.SendTunneled(reply);
        }

        _logger.LogInformation("[Path] Take-Me-There path for {name}: {a} -> {b} ({n} nodes)",
            player.Name, request.Start, destination, path.Count);
        return true;
    }

    /// <summary>
    /// Builds the path the client walks. We have no server-side navmesh, so we send only the endpoints and
    /// let the client's character steering find its way around obstacles between them - packing in dense
    /// intermediate waypoints instead pins the character to the straight line and walks it into walls.
    /// </summary>
    private static List<Vector4> BuildPath(Vector4 start, Vector4 destination)
    {
        return new List<Vector4> { start, destination };
    }
}
