using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseMiniGamePacketHandler
{
    private const byte MiniGameStartGame = 5;
    private const byte LootWheelOnRotationStopped = 46;

    private const int MysteryPackDefId = 10482;
    private const int MysteryPackContentsCount = 3;
    private const int MysteryPackTableId = 636;
    private static readonly int[] MysteryPackSpherePool = [3011, 3013, 3015, 3025, 3074, 3089];
    private static readonly Random _rng = new();

    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseMiniGamePacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out byte subOpCode))
        {
            _logger.LogError("Failed to read minigame sub-opcode. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        _logger.LogInformation("BaseMiniGamePacket sub-opcode={sub} | bytes={hex}",
            subOpCode, Convert.ToHexString(reader.Span));

        return subOpCode switch
        {
            MiniGameStartGame => HandleStartGame(connection, reader),
            LootWheelOnRotationStopped => HandleLootWheelStopped(connection),
            MiniGameEndPacket.OpCode => MiniGameEndPacketHandler.HandlePacket(connection, reader.Span),
            MiniGamePayloadPacket.OpCode => MiniGamePayloadPacketHandler.HandlePacket(connection, reader.Span),
            _ => true
        };
    }

    private static bool HandleLootWheelStopped(GatewayConnection connection)
    {
        var player = connection.Player;
        var prize = player.PendingWheelPrize;
        var coins = player.PendingWheelCoins;
        player.PendingWheelPrize = null;
        player.PendingWheelCoins = 0;

        if (prize is null && coins <= 0)
        {
            _logger.LogInformation("LootWheelOnRotationStopped with no pending prize — ignoring.");
            return true;
        }

        if (prize is null)
        {
            GrantCoins(connection, coins);
            connection.SendTunneled(new RewardBundlePacket { Coins = coins, Unknown15 = 957 });
            _logger.LogInformation("Loot wheel payout: {coins} coins -> {name}.", coins, player.Name);
            return true;
        }

        if (prize.ItemDefId == MysteryPackDefId)
        {
            OpenMysteryPack(connection);
            // The wheel-prize banner itself (pack icon/name — live sent it AFTER the contents banner).
            connection.SendTunneled(new RewardBundlePacket { IconId = prize.IconId, NameId = prize.NameId, Unknown15 = 957 });
            return true;
        }

        var granted = GrantItem(connection, prize.ItemDefId, prize.Quantity);
        if (granted is not null)
            connection.SendTunneled(new RewardBundlePacket { IconId = prize.IconId, NameId = prize.NameId, Unknown15 = 957 });

        _logger.LogInformation("Loot wheel payout: item def {def} x{qty} -> {name} ({ok}).",
            prize.ItemDefId, prize.Quantity, player.Name, granted is not null ? "granted" : "FAILED");

        return true;
    }

    public static void OpenMysteryPack(GatewayConnection connection)
    {
        var sphereDefId = MysteryPackSpherePool[_rng.Next(MysteryPackSpherePool.Length)];
        var contents = GrantItem(connection, sphereDefId, MysteryPackContentsCount);
        if (contents is not null)
        {
            connection.SendTunneled(new RewardBundlePacket
            {
                Entries =
                [
                    new RewardEntry
                    {
                        IconId = contents.Definition?.Icon.Id ?? 0,
                        TintId = contents.Definition?.Icon.TintId ?? 0,
                        NameId = contents.Definition?.NameId ?? 0,
                        Quantity = MysteryPackContentsCount,
                        ItemDefId = sphereDefId,
                        TailItemGuid = contents.ItemGuid,
                    }
                ],
                Unknown15 = MysteryPackTableId, // live banner carried the pack's loot-table id (Param1)
            });
        }

        _logger.LogInformation("Mystery Pack -> {n}x sphere def {def} for {name} ({ok}).",
            MysteryPackContentsCount, sphereDefId, connection.Player.Name, contents is not null ? "granted" : "GRANT FAILED");
    }

    private sealed record GrantedItem(int ItemGuid, ClientItemDefinition? Definition);

    private static GrantedItem? GrantItem(GatewayConnection connection, int definitionId, int quantity)
    {
        if (!_resourceManager.ClientItemDefinitions.TryGetValue(definitionId, out var definition))
        {
            _logger.LogWarning("Loot wheel grant: unknown item definition {def}.", definitionId);
            return null;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var dbQuery = dbContext.Characters
            .Where(x => x.Id == GuidHelper.GetPlayerId(connection.Player.Guid))
            .Select(x => new
            {
                Character = x,
                Item = x.Items.SingleOrDefault(i => i.Definition == definition.Id && i.Tint == 0),
                NextId = x.Items.Max(i => i.Id)
            })
            .SingleOrDefault();

        if (dbQuery is null)
        {
            _logger.LogWarning("Loot wheel grant: character row missing for {guid}.", connection.Player.Guid);
            return null;
        }

        var dbItem = dbQuery.Item;

        if (dbItem is not null)
        {
            dbItem.Count += quantity;
        }
        else
        {
            dbItem = new DbItem
            {
                Id = dbQuery.NextId + 1,
                Definition = definition.Id,
                Tint = 0,
                Count = quantity
            };

            dbQuery.Character.Items.Add(dbItem);
        }

        if (dbContext.SaveChanges() <= 0)
        {
            _logger.LogWarning("Loot wheel grant: DB save failed for def {def}.", definitionId);
            return null;
        }

        var clientItem = connection.Player.Items.SingleOrDefault(x => x.Definition == definition.Id && x.Tint == 0);

        if (clientItem is not null)
        {
            clientItem.Count = dbItem.Count;

            connection.SendTunneled(new ClientUpdatePacketItemUpdate
            {
                ItemGuid = clientItem.Id,
                Count = clientItem.Count,
            });
        }
        else
        {
            clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Tint = dbItem.Tint,
                Count = dbItem.Count,
                Definition = dbItem.Definition
            };

            connection.Player.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);

            connection.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });
        }

        return new GrantedItem(clientItem.Id, definition);
    }

    private static void GrantCoins(GatewayConnection connection, int coins)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var dbCharacter = dbContext.Characters.SingleOrDefault(x => x.Id == GuidHelper.GetPlayerId(connection.Player.Guid));
        if (dbCharacter is null)
            return;

        dbCharacter.Coins += coins;
        dbContext.SaveChanges();

        connection.Player.Coins = dbCharacter.Coins;

        connection.SendTunneled(new ClientUpdatePacketCoinCount { Coins = connection.Player.Coins });
    }

    private static bool HandleStartGame(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out int stateId) || !reader.TryRead(out int groupId) || !reader.TryRead(out int gameId))
        {
            _logger.LogWarning("MiniGameStartGame: short body ( {hex} ) — acking anyway.", Convert.ToHexString(reader.Span));
            stateId = 0; groupId = -1; gameId = -1;
        }

        _logger.LogInformation("MiniGameStartGame (GO! pressed): StateId={state} GroupId={group} GameId={game}",
            stateId, groupId, gameId);

        EncounterParticipantRequestEntranceHandler.EnterFrostfangArena(connection);

        return true;
    }
}
