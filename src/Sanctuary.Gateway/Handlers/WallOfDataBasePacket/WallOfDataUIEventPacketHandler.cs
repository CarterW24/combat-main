using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class WallOfDataUIEventPacketHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(WallOfDataUIEventPacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        Console.WriteLine($"[DEBUG] WallOfDataUIEventPacketHandler.HandlePacket called! Data length: {data.Length}");

        if (!WallOfDataUIEventPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(WallOfDataUIEventPacket));
            Console.WriteLine("[DEBUG] WallOfDataUIEventPacket deserialization FAILED");
            return false;
        }

        Console.WriteLine($"[DEBUG] Packet deserialized successfully: TableName={packet.TableName}, Callback={packet.Callback}, Param={packet.Param}");
        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(WallOfDataUIEventPacket), packet);

        if (packet.TableName == "Atlas" && packet.Callback == "teleportPlayerToPointOfInterest"
            && int.TryParse(packet.Param, out var poiId))
        {
            return HandleAtlasTeleport(connection, poiId);
        }

        if (packet.Callback == "redeemCode" && !string.IsNullOrEmpty(packet.Param))
        {
            Console.WriteLine($"[DEBUG] Calling HandleClaimCode with code: {packet.Param}");
            return HandleClaimCode(connection, packet.Param);
        }

        if (packet.Callback is "ShowPets" or "GotoMyPets")
        {
            return HandleShowPets(connection);
        }

        if (packet.Callback is "ShowMounts" or "GotoMyMounts")
        {
            return HandleShowMounts(connection);
        }

        Console.WriteLine("[DEBUG] Packet processed but not a claim code redemption");
        return true;
    }

    private static bool HandleAtlasTeleport(GatewayConnection connection, int poiId)
    {
        var poi = _resourceManager.PointOfInterests.Values.FirstOrDefault(p => p.Id == poiId);
        if (poi is null)
        {
            _logger.LogWarning("Atlas teleport: POI id {id} not found.", poiId);
            return true;
        }

        var player = connection.Player;
        var target = poi.SpawnPosition != default ? poi.SpawnPosition : poi.Position;
        var rotation = new System.Numerics.Quaternion(MathF.Sin(poi.Heading), 0f, MathF.Cos(poi.Heading), 0f);

        player.UpdatePosition(target, rotation);

        player.SendTunneled(new ClientUpdatePacketUpdateLocation
        {
            Position = target,
            Rotation = rotation,
            Teleport = true
        });

        _logger.LogInformation("Atlas teleport -> POI {id} (name {name}, atlas {atlas}) at {pos}.",
            poi.Id, poi.NameId, poi.AtlasName, target);

        return true;
    }

    private static bool HandleShowMounts(GatewayConnection connection)
    {
        var packetMountList = new PacketMountList { Mounts = connection.Player.Mounts };

        connection.SendTunneled(packetMountList);

        _logger.LogInformation("Resent PacketMountList in response to Mounts UI event. TotalMountsCount={count}",
            connection.Player.Mounts.Count);

        return true;
    }

    private static bool HandleShowPets(GatewayConnection connection)
    {
        var petListPacket = new PetListPacket { Pets = connection.Player.Pets };

        var rawBytes = petListPacket.Serialize();

        Console.WriteLine($"[DEBUG] PetListPacket raw bytes ({rawBytes.Length}): {Convert.ToHexString(rawBytes)}");

        if (connection.Player.Pets.Count > 0)
        {
            using var entryWriter = new Sanctuary.Core.IO.PacketWriter();
            connection.Player.Pets[0].Serialize(entryWriter);
            var entryBytes = entryWriter.Buffer;

            Console.WriteLine($"[DEBUG] First pet entry raw bytes ({entryBytes.Length}): {Convert.ToHexString(entryBytes)}");
            Console.WriteLine($"[DEBUG] First pet entry: Id={connection.Player.Pets[0].Id}, Name='{connection.Player.Pets[0].Name}', NameId={connection.Player.Pets[0].NameId}, TintId={connection.Player.Pets[0].TintId}, TextureAlias='{connection.Player.Pets[0].TextureAlias}'");
        }

        connection.SendTunneled(petListPacket);

        _logger.LogInformation("Resent PetListPacket in response to Pets UI event. TotalPetsCount={count}",
            connection.Player.Pets.Count);

        return true;
    }

    private static bool HandleClaimCode(GatewayConnection connection, string code)
    {
        _logger.LogInformation("HandleClaimCode called with code: {Code}", code);

        if (connection.Player?.Zone is not Sanctuary.Game.Zones.BaseZone zone)
        {
            _logger.LogError("Player zone is not a BaseZone");
            SendRedemptionNotification(connection, false);
            return true;
        }

        var claimCode = zone.GetClaimCodes().FirstOrDefault(x =>
            string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));

        if (claimCode is null)
        {
            _logger.LogWarning("Invalid claim code: {Code}", code);
            SendRedemptionNotification(connection, false);
            return true;
        }

        var itemIds = zone.GetClaimCodeItemIds(code);
        var itemDefs = new List<Sanctuary.Packet.Common.ClientItemDefinition>();

        foreach (var itemId in itemIds)
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemId, out var def))
            {
                _logger.LogWarning("Claim code {Code} references missing item {ItemId} — skipping", code, itemId);
                continue;
            }
            itemDefs.Add(def);
        }

        if (itemDefs.Count == 0)
        {
            _logger.LogError("Claim code {Code} has no valid item definitions", code);
            SendRedemptionNotification(connection, false);
            return true;
        }

        if (connection.Player.Items.Any(x => itemDefs.Any(d => d.Id == x.Definition)))
        {
            _logger.LogInformation("Player {Guid} already redeemed code {Code}", connection.Player.Guid, code);
            SendRedemptionNotification(connection, false);
            return true;
        }

        var newItems = new List<ClientItem>();
        foreach (var def in itemDefs)
        {
            var newItem = new ClientItem { Definition = def.Id, Count = zone.GetClaimCodeItemCount(code, def.Id), Tint = 0 };

            if (!connection.SaveItemToDatabase(newItem))
            {
                _logger.LogError("Failed to save item {Definition} to database for code {Code}", def.Id, code);
                SendRedemptionNotification(connection, false);
                return true;
            }

            connection.Player.Items.Add(newItem);
            newItems.Add(newItem);
        }

        using var defWriter = new Core.IO.PacketWriter();
        defWriter.Write(itemDefs.ToArray());
        connection.SendTunneled(new PlayerUpdatePacketItemDefinitions { Payload = defWriter.Buffer });

        foreach (var item in newItems)
        {
            using var writer = new Core.IO.PacketWriter();
            item.Serialize(writer);
            connection.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });
        }

        SendRedemptionNotification(connection, true);
        connection.SendTunneled(new ExecuteScriptPacket { Script = "WelcomeHandler.close()" });

        _logger.LogInformation("Granted {Count} item(s) to player {Guid} via code {Code}",
            newItems.Count, connection.Player.Guid, code);

        return true;
    }

    private static void SendRedemptionNotification(GatewayConnection connection, bool success)
    {
        connection.SendTunneled(new KeyCodeRedemptionNotificationPacket { Success = success });
        connection.SendTunneled(new PromotionalBundleDataPacket());
        _logger.LogInformation("Sent KeyCodeRedemptionNotificationPacket (Success={Success})", success);
    }
}
