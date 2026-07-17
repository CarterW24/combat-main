using System;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseEncounterPacketHandler
{
    private const short EncounterParticipantRequestEntrance = 108;
    private const short EncounterRequestExit = 109;
    private const short EncounterParticipantResume = 122;
    private const short EncounterCancelPending = 124;

    private const int ReviveHereCost = 100;

    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseEncounterPacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short subOpCode))
        {
            _logger.LogError("Failed to read encounter sub-opcode. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        _logger.LogInformation("BaseEncounterPacket sub-opcode={sub} | remaining bytes={hex}",
            subOpCode, Convert.ToHexString(reader.Span));

        return subOpCode switch
        {
            EncounterParticipantRequestEntrance => EncounterParticipantRequestEntranceHandler.HandlePacket(connection, reader),
            EncounterRequestExit => HandleRequestExit(connection),
            EncounterParticipantResume => HandleResume(connection, reader),
            EncounterCancelPending => HandleCancelPending(connection),
            _ => true
        };
    }

    private static bool HandleCancelPending(GatewayConnection connection)
    {
        _logger.LogInformation("Dungeon start panel closed by {name} — tearing down the encounter lobby.", connection.Player.Name);
        var player = connection.Player;
        player.SendTunneled(new CommandPacketQuestDialogComplete());
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        return true;
    }

    private static bool HandleResume(GatewayConnection connection, PacketReader reader)
    {
        reader.TryRead(out int _);
        reader.TryRead(out int _);
        reader.TryRead(out byte option);

        var player = connection.Player;
        if (!player.IsDead)
            return true;

        if (player.Zone is EncounterArenaZone or FrostfangArenaZone or TormentedSpiritsArenaZone)
        {
            _logger.LogInformation("Revive button in {zone} for {name}.", player.Zone.GetType().Name, player.Name);
            player.Zone.OnPlayerRespawn(player);
            return true;
        }

        if (option == 1)
        {
            if (player.Coins >= ReviveHereCost && TrySpendCoins(connection, ReviveHereCost))
            {
                _logger.LogInformation("Revive HERE ({cost} coins) for {name}.", ReviveHereCost, player.Name);
                var pos = player.DeathPosition;
                player.Respawn();
                TeleportTo(player, pos);
                return true;
            }
            _logger.LogInformation("Revive here declined (insufficient coins) for {name} — safe revive instead.", player.Name);
        }

        var safe = NearestTownSpawn(player.DeathPosition);
        _logger.LogInformation("Revive at SAFE location (free) for {name}.", player.Name);
        player.Respawn();
        TeleportTo(player, safe);
        return true;
    }

    private static Vector4 NearestTownSpawn(Vector4 from)
    {
        var best = from;
        var bestDist = float.MaxValue;
        foreach (var poi in _resourceManager.PointOfInterests.Values)
        {
            if (poi.NotificationType != 7)
                continue;
            var target = poi.SpawnPosition != default ? poi.SpawnPosition : poi.Position;
            var dx = target.X - from.X;
            var dz = target.Z - from.Z;
            var d = dx * dx + dz * dz;
            if (d < bestDist)
            {
                bestDist = d;
                best = target;
            }
        }
        return best;
    }

    private static void TeleportTo(Game.Entities.Player player, Vector4 target)
    {
        player.UpdatePosition(target, player.Rotation);
        player.SendTunneled(new ClientUpdatePacketUpdateLocation
        {
            Position = target,
            Rotation = player.Rotation,
            Teleport = true,
        });
    }

    private static bool TrySpendCoins(GatewayConnection connection, int coins)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbCharacter = dbContext.Characters.SingleOrDefault(x => x.Id == GuidHelper.GetPlayerId(connection.Player.Guid));
        if (dbCharacter is null || dbCharacter.Coins < coins)
            return false;

        dbCharacter.Coins -= coins;
        dbContext.SaveChanges();

        connection.Player.Coins = dbCharacter.Coins;
        connection.SendTunneled(new ClientUpdatePacketCoinCount { Coins = connection.Player.Coins });
        return true;
    }

    private static bool HandleRequestExit(GatewayConnection connection)
    {
        var player = connection.Player;

        if (player.Zone is CombatEncounterZone encounter)
        {
            _logger.LogInformation("Leave button (RequestExit) in {zone} — returning {name} to the overworld.", encounter.Name, player.Name);
            encounter.LeaveEncounter(player);
        }

        return true;
    }
}
