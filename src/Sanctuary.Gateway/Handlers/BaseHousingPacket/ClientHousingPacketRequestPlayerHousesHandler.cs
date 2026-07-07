using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketRequestPlayerHousesHandler
{
    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketRequestPlayerHousesHandler));

        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketRequestPlayerHouses.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientHousingPacketRequestPlayerHouses));
            return false;
        }

        var playerId = GuidHelper.GetPlayerId(connection.Player.Guid);

        using var dbContext = _dbContextFactory.CreateDbContext();

        var houses = dbContext.Houses
            .Where(h => h.OwnerId == playerId)
            .ToList();

        var response = new HousingPacketInstanceList
        {
            PlayerGuid = connection.Player.Guid
        };

        foreach (var house in houses)
        {
            response.Instances.Add(new PlayerHousingInstanceInfo
            {
                OwnerGuid = connection.Player.Guid,
                InstanceGuid = house.Id,
                NameId = house.NameId,
                OwnerName = connection.Player.Name.FirstName,
                HouseName = house.CustomName,
                IconId = house.IconId,
                FixtureCount = 0,
                FurnitureScore = 0,
                LastVisited = house.LastVisited,
                WhenCreated = house.Created,
                IsLocked = house.IsLocked,
                IsMembersOnly = house.IsMembersOnly,
                IsFloraAllowed = house.IsFloraAllowed,
                Description = house.Description,
                KeywordList = house.KeywordList,
                Rating = house.Rating,
                Votes = house.Votes,
                HasRating = house.Votes > 0,
                CanVote = false,
                FactoryPlotId = 0
            });
        }

        connection.SendTunneled(response);

        _logger.LogInformation("Sent {count} houses to player {name}", houses.Count, connection.Player.Name.FirstName);

        return true;
    }
}
