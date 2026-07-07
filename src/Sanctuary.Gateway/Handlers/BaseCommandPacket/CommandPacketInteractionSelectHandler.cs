using System;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CommandPacketInteractionSelectHandler
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;
    private static IInteractionManager _interactionManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketInteractionSelectHandler));


        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _interactionManager = serviceProvider.GetRequiredService<IInteractionManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketInteractionSelect.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketInteractionSelect));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CommandPacketInteractionSelect), packet);

        if (!_interactionManager.TryGet(packet.Id, out var interaction))
        {
            _logger.LogError("Invalid interaction. {interaction}", packet.Id);

            return true;
        }

        if (connection.Player.VisiblePlayers.TryGetValue(packet.Guid, out var player))
        {
            interaction.OnInteract(connection.Player, player);
        }
        else if (connection.Player.VisibleNpcs.TryGetValue(packet.Guid, out var npc))
        {
            // Enforce the NPC's interact range (this path had none, so a selection could land from
            // across the map). Matches CommandPacketInteractRequest/FreeInteractionNpc so the
            // "must be next to the NPC" rule holds no matter which interact packet the client sends.
            var playerPosition = new Vector3(connection.Player.Position.X, connection.Player.Position.Y, connection.Player.Position.Z);
            var npcPosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);

            if (Vector3.Distance(playerPosition, npcPosition) > npc.InteractRange)
                return true;

            interaction.OnInteract(connection.Player, npc);
        }
        else
        {
            _logger.LogWarning("Received interaction for unknown entity. {entity}", packet.Guid);

            return true;
        }

        return true;
    }
}