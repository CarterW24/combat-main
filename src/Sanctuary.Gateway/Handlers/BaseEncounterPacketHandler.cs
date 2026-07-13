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

// INSTANCE WIP (Frostfang Fury): the C2S dispatcher for BaseEncounterPacket (op41). Previously NOTHING routed
// op41 inbound — clicking GO! on the adventure offer popup fell through unhandled. This reads the sub-opcode and
// dispatches. It also OBSERVE-LOGS every sub-opcode (like BaseAbilityPacketHandler) so we can see exactly what
// the offer popup's buttons send (sub108 EncounterParticipantRequestEntrance = GO!; sub109 RequestExit; etc.)
// and reconstruct their wire formats from the live bytes.
[PacketHandler]
public static class BaseEncounterPacketHandler
{
    // op41 sub-opcodes (from exports/packet-opcode-map.tsv).
    private const short EncounterParticipantRequestEntrance = 108; // C2S = the GO! / "Press to Teleport" button.
    private const short EncounterRequestExit = 109;                // C2S = the "Leave" button on the encounter UI.
    private const short EncounterParticipantResume = 122;          // C2S = the "Revive" button on the respawn window.
    private const short EncounterCancelPending = 124;              // C2S = closing the dungeon start/offer panel.

    /// <summary>"Revive here" coin cost (matches the window's displayed 100).</summary>
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
            // observe-only: don't hard-fail unknown/unmapped encounter sub-opcodes while we reverse them
            _ => true
        };
    }

    // CLOSE START PANEL (op41/sub124): closing the dungeon offer/start panel without pressing GO! leaves the
    // client in the encounter/minigame LOBBY state the offer put it in (EncounterState 2..5), which gates the
    // HUD + input. QuestDialogComplete alone only restored the camera — the game stayed input-locked until the
    // player pressed Escape. Tear the lobby down the same way a clean encounter exit does
    // (EncounterArenaZone.EndEncounterForPlayer): MiniGameStateRemove + the default encounter data drop the
    // gate so the player is free the instant they close the panel.
    private static bool HandleCancelPending(GatewayConnection connection)
    {
        _logger.LogInformation("Dungeon start panel closed by {name} — tearing down the encounter lobby.", connection.Player.Name);
        var player = connection.Player;
        player.SendTunneled(new CommandPacketQuestDialogComplete());
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        return true;
    }

    // REVIVE BUTTONS (op41/sub122 Resume): the overworld pay/safe respawn window. Wire format (live capture):
    //   [op41][sub122][int][int][byte option]  — option 1 = "Revive here" (paid), 0 = "Revive at safe" (free).
    private static bool HandleResume(GatewayConnection connection, PacketReader reader)
    {
        reader.TryRead(out int _);          // header int 1 (encounter id / unused for overworld)
        reader.TryRead(out int _);          // header int 2
        reader.TryRead(out byte option);    // 1 = Revive here (paid) · 0 = Revive at safe location (free)

        var player = connection.Player;
        if (!player.IsDead)
            return true;

        // In a combat instance the Revive button just revives you at your spot (no pay/safe choice) — that
        // flow lives in the zone's OnPlayerRespawn (revive at death position + FX).
        if (player.Zone is EncounterArenaZone or FrostfangArenaZone or TormentedSpiritsArenaZone)
        {
            _logger.LogInformation("Revive button in {zone} for {name}.", player.Zone.GetType().Name, player.Name);
            player.Zone.OnPlayerRespawn(player);
            return true;
        }

        // "Revive here" (paid): charge the coins and come back at the exact death spot. If they can't
        // afford it, fall back to the free safe revive rather than leaving them stuck.
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

        // "Revive at safe location" (free): back at the nearest town/warpstone.
        var safe = NearestTownSpawn(player.DeathPosition);
        _logger.LogInformation("Revive at SAFE location (free) for {name}.", player.Name);
        player.Respawn();
        TeleportTo(player, safe);
        return true;
    }

    /// <summary>Nearest town/warpstone POI (NotificationType 7) spawn to the given spot; falls back to the
    /// spot itself if none are loaded.</summary>
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

    /// <summary>Seamless in-world teleport (server position + client UpdateLocation), same recipe as atlas
    /// fast-travel.</summary>
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

    /// <summary>Deduct coins (DB + player + client counter). Returns false if the character row is missing.</summary>
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

    // LEAVE BUTTON (op41/sub109 RequestExit): bail out of a combat instance back to the overworld — the
    // same UseExitDoor -> ReturnHome the victory portal uses (no-ops if the player isn't in that zone,
    // e.g. this also fires as the client closes the encounter UI after a win, when already home).
    private static bool HandleRequestExit(GatewayConnection connection)
    {
        var player = connection.Player;
        switch (player.Zone)
        {
            case EncounterArenaZone arena:
                _logger.LogInformation("Leave button (RequestExit) in {zone} — returning {name} to the overworld.", arena.Name, player.Name);
                arena.UseExitDoor(player);
                break;
            case FrostfangArenaZone frostfang:
                frostfang.UseExitDoor(player);
                break;
            case TormentedSpiritsArenaZone spirits:
                spirits.UseExitDoor(player);
                break;
        }
        return true;
    }
}
