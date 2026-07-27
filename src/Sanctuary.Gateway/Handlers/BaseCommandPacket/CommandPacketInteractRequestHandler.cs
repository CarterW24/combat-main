using System;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
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

        if (player.SpawnedAt is { } spawnedAt && DateTime.UtcNow - spawnedAt < TimeSpan.FromSeconds(2))
            return true;

        if (player.Zone is FrostfangArenaZone arena && arena.IsExitDoor(packet.Guid))
        {
            arena.UseExitDoor(player);
            return true;
        }

        if (player.Zone is TormentedSpiritsArenaZone spiritArena && spiritArena.IsExitDoor(packet.Guid))
        {
            spiritArena.UseExitDoor(player);
            return true;
        }

        if (player.Zone is EncounterArenaZone encounterArena && encounterArena.IsExitDoor(packet.Guid))
        {
            encounterArena.UseExitDoor(player);
            return true;
        }

        if (!player.Zone.TryGetEntity(packet.Guid, out var entity))
            return true;

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

        if (player.Zone is StartingZone startingZone
            && startingZone.GrowlerWolf is { } growler
            && growler.Guid == packet.Guid)
        {
            _logger.LogInformation("InteractRequest: Frostfang Growler ({guid}) clicked -> sending offer popup.",
                packet.Guid);

            EncounterParticipantRequestEntranceHandler.SendEncounterOffer(player, FrostfangArenaZone.EncounterId);

            return true;
        }

        if (player.Zone is StartingZone spiritZone
            && spiritZone.SpiritEntranceGuid != 0
            && packet.Guid == spiritZone.SpiritEntranceGuid)
        {
            _logger.LogInformation("InteractRequest: Tormented Spirit ({guid}) clicked -> sending offer popup.",
                packet.Guid);

            EncounterParticipantRequestEntranceHandler.SendEncounterOffer(player, TormentedSpiritsArenaZone.EncounterId);

            return true;
        }

        entity.OnInteract(player);

        return true;
    }
}
