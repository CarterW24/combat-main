using System;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CommandPacketFreeInteractionNpcHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketFreeInteractionNpcHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketFreeInteractionNpc.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketFreeInteractionNpc));
            return false;
        }

        var player = connection.Player;

        if (player.SpawnedAt is { } spawnedAt && DateTime.UtcNow - spawnedAt < TimeSpan.FromSeconds(2))
            return true;

        var playerPosition = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);

        var target = player.VisibleNpcs.Values
            .Where(npc => npc.IsInteractable && npc.InteractAction is not null)
            .Select(npc => new
            {
                Npc = npc,
                Distance = Vector3.Distance(playerPosition, new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z))
            })
            .Where(x => x.Distance <= x.Npc.InteractRange)
            .OrderBy(x => x.Distance)
            .Select(x => x.Npc)
            .FirstOrDefault();

        if (target is null)
            return true;

        if (target.Guid == player.LastInteractNpcGuid && DateTime.UtcNow - player.LastInteractAt < TimeSpan.FromSeconds(3))
            return true;

        player.LastInteractNpcGuid = target.Guid;
        player.LastInteractAt = DateTime.UtcNow;

        target.OnInteract(player);

        return true;
    }
}
