using System;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CommandPacketInteractRequestHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketInteractRequestHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketInteractRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketInteractRequest));
            return false;
        }

        var player = connection.Player;

        // Same guards as FreeInteractionNpc: the client can fire this on zone entry / from UI without a
        // deliberate click. Ignore interacts within the spawn grace window.
        if (player.SpawnedAt is { } spawnedAt && DateTime.UtcNow - spawnedAt < TimeSpan.FromSeconds(2))
            return true;

        if (!player.Zone.TryGetEntity(packet.Guid, out var entity))
            return true;

        // Enforce the NPC's interact range here too (this path resolves by guid and would otherwise
        // let a click land from any distance), so the "must be next to the NPC" rule holds regardless
        // of which interact packet the client sends.
        if (entity is Npc npc)
        {
            var playerPosition = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);
            var npcPosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);

            if (Vector3.Distance(playerPosition, npcPosition) > npc.InteractRange)
                return true;
        }

        if (packet.Guid == player.LastInteractNpcGuid && DateTime.UtcNow - player.LastInteractAt < TimeSpan.FromSeconds(3))
            return true;

        player.LastInteractNpcGuid = packet.Guid;
        player.LastInteractAt = DateTime.UtcNow;

        entity.OnInteract(player);

        return true;
    }
}
