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

            foreach (var state in new[] { 2, 3, 4 })
            {
                connection.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = FrostfangArenaZone.EncounterId,
                    InstanceId = FrostfangArenaZone.EncounterInstanceId,
                    State = state,
                });
            }

            connection.SendTunneled(new EncounterDetailsResponsePacket
            {
                // Header ints = [EncounterId][InstanceId] on the live wire (details + state + PlayerEnter
                // all share them).
                Unknown = FrostfangArenaZone.EncounterId,
                Unknown2 = FrostfangArenaZone.EncounterInstanceId,
                NameId = 93276,
                DescriptionId = 104171,
                Difficulty = 1,
                IconId = 1345,
                MiniGameType = 4,
                PreviewRewards = FrostfangArenaZone.GetPrizePreviewFor(player),
                PreviewCoins = FrostfangArenaZone.PrizeCoins,
                PreviewXp = FrostfangArenaZone.PrizeXp,
                ProfileType = FrostfangArenaZone.CombatProfileType,
                ActivityId = FrostfangArenaZone.EncounterId,
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                connection.SendTunneled(new EncounterZoneIsReadyPacket());
                connection.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = FrostfangArenaZone.EncounterId,
                    InstanceId = FrostfangArenaZone.EncounterInstanceId,
                    State = 5,
                });
            });

            return true;
        }

        if (player.Zone is StartingZone spiritZone
            && spiritZone.SpiritEntranceGuid != 0
            && packet.Guid == spiritZone.SpiritEntranceGuid)
        {
            _logger.LogInformation("InteractRequest: Tormented Spirit ({guid}) clicked -> sending offer popup.",
                packet.Guid);

            foreach (var state in new[] { 2, 3, 4 })
            {
                connection.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = TormentedSpiritsArenaZone.EncounterId,
                    InstanceId = TormentedSpiritsArenaZone.EncounterInstanceId,
                    State = state,
                });
            }

            connection.SendTunneled(new EncounterDetailsResponsePacket
            {
                Unknown = TormentedSpiritsArenaZone.EncounterId,
                Unknown2 = TormentedSpiritsArenaZone.EncounterInstanceId,
                NameId = TormentedSpiritsArenaZone.TitleNameId,
                DescriptionId = TormentedSpiritsArenaZone.DescriptionId,
                Difficulty = TormentedSpiritsArenaZone.Difficulty,
                IconId = TormentedSpiritsArenaZone.IconId,
                MiniGameType = 4,
                PreviewRewards = FrostfangArenaZone.GetPrizePreviewFor(player),
                PreviewCoins = FrostfangArenaZone.PrizeCoins,
                PreviewXp = FrostfangArenaZone.PrizeXp,
                ProfileType = FrostfangArenaZone.CombatProfileType,
                ActivityId = TormentedSpiritsArenaZone.EncounterId,
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                connection.SendTunneled(new EncounterZoneIsReadyPacket());
                connection.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = TormentedSpiritsArenaZone.EncounterId,
                    InstanceId = TormentedSpiritsArenaZone.EncounterInstanceId,
                    State = 5,
                });
            });

            return true;
        }

        entity.OnInteract(player);

        return true;
    }
}
