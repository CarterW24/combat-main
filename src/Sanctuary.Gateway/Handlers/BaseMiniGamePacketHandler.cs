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

// INSTANCE WIP (Frostfang Fury): C2S dispatcher for BaseMiniGamePacket (op39) — ported from the team's
// `minigame` branch. LIVE TEST 1 (2026-07-01) taught us the GO! button does NOT send op41/sub108
// (EncounterParticipantRequestEntrance) as assumed — the only thing we logged was CommandPacket sub42
// ClosedMinigameEndScreen (the panel closing). The branch's flow says starting a minigame sends
// op39/sub5 MiniGameStartGame -> server acks with sub17 GameStart. This handler observe-logs EVERY op39
// sub-opcode and treats sub5 as the GO! press: ack + enter the Frostfang arena.
[PacketHandler]
public static class BaseMiniGamePacketHandler
{
    // op39 sub-opcodes (byte-sized!) from the minigame branch.
    private const byte MiniGameStartGame = 5;              // C2S — pressing GO!/start on a minigame offer panel
    private const byte LootWheelOnRotationStopped = 46;    // C2S — the victory wheel finished spinning (04-01 idx 38115)

    // The Battle Item Mystery Pack wheel prize: on live (04-01 idx 38142) it opened INSTANTLY into
    // battle items — 3x Flabbergast Sphere. We mirror the net effect (grant the contents, not the pack).
    private const int MysteryPackDefId = 10482;
    private const int FlabbergastSphereDefId = 3015;
    private const int MysteryPackContentsCount = 3;

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
            // observe-only: log-and-accept unknown minigame sub-opcodes while we reverse the family
            _ => true
        };
    }

    // ★ LOOT WHEEL PAYOUT (op39/sub46, body = base 3 ints only). The wheel finished spinning on the
    // prize WE preselected in FrostfangArenaZone.WinEncounter (SetItemToLandOn) — grant it now, exactly
    // like the live server did after 04-01 idx 38115: inventory add/update + RewardBundlePacket
    // (op50/sub1) grant banners. Mystery Pack opens into battle items (contents banner first, then the
    // prize banner — the live order).
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
            // COINS slice.
            GrantCoins(connection, coins);
            connection.SendTunneled(new RewardBundlePacket { Coins = coins, Unknown15 = 957 });
            _logger.LogInformation("Loot wheel payout: {coins} coins -> {name}.", coins, player.Name);
            return true;
        }

        if (prize.ItemDefId == MysteryPackDefId)
        {
            // Live behavior: the pack opens instantly — grant the CONTENTS (3x Flabbergast Sphere),
            // then the two grant banners (contents with the inventory-guid tail, then the pack banner).
            var contents = GrantItem(connection, FlabbergastSphereDefId, MysteryPackContentsCount);
            if (contents is not null)
            {
                connection.SendTunneled(new RewardBundlePacket
                {
                    Entries =
                    [
                        new RewardEntry
                        {
                            IconId = contents.Definition?.Icon.Id ?? 1899,
                            TintId = contents.Definition?.Icon.TintId ?? 0,
                            NameId = contents.Definition?.NameId ?? 0,
                            Quantity = MysteryPackContentsCount,
                            ItemDefId = FlabbergastSphereDefId,
                            TailItemGuid = contents.ItemGuid,
                        }
                    ],
                    Unknown15 = 636, // live value (38142); meaning unknown
                });
            }
            connection.SendTunneled(new RewardBundlePacket { IconId = prize.IconId, NameId = prize.NameId, Unknown15 = 957 });
            _logger.LogInformation("Loot wheel payout: Mystery Pack -> {n}x Flabbergast Sphere for {name}.",
                MysteryPackContentsCount, player.Name);
            return true;
        }

        // Plain item prize.
        var granted = GrantItem(connection, prize.ItemDefId, prize.Quantity);
        if (granted is not null)
            connection.SendTunneled(new RewardBundlePacket { IconId = prize.IconId, NameId = prize.NameId, Unknown15 = 957 });

        _logger.LogInformation("Loot wheel payout: item def {def} x{qty} -> {name} ({ok}).",
            prize.ItemDefId, prize.Quantity, player.Name, granted is not null ? "granted" : "FAILED");

        return true;
    }

    private sealed record GrantedItem(int ItemGuid, ClientItemDefinition? Definition);

    /// <summary>Add an item to the player's persistent inventory + live client state (same DB/packet
    /// flow as the coin-store buy handler, minus the cost). Returns the inventory item guid.</summary>
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
        // body: [int StateId][int GroupId][int GameId]
        if (!reader.TryRead(out int stateId) || !reader.TryRead(out int groupId) || !reader.TryRead(out int gameId))
        {
            _logger.LogWarning("MiniGameStartGame: short body ( {hex} ) — acking anyway.", Convert.ToHexString(reader.Span));
            stateId = 0; groupId = -1; gameId = -1;
        }

        _logger.LogInformation("MiniGameStartGame (GO! pressed): StateId={state} GroupId={group} GameId={game}",
            stateId, groupId, gameId);

        // Same entry as the sub108 GO! path: proper server-side zone transfer into the arena
        // (also sends the GameStart ack).
        EncounterParticipantRequestEntranceHandler.EnterFrostfangArena(connection);

        return true;
    }
}
