using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.ChatCommands;

public class TestWeaponsChatCommand : IChatCommand
{
    private readonly IResourceManager _resourceManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;

    public string KeyWord => "testweapons";
    public string Usage => "[profileId|all]";
    public string Description => "Grants every weapon mapped in CombatJobs.json (one per mapping), for combat testing.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public TestWeaponsChatCommand(IResourceManager resourceManager, IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _resourceManager = resourceManager;
        _dbContextFactory = dbContextFactory;
    }

    public bool Handle(Player invoker, string[] args)
    {
        var profileFilter = args.Length > 0 && int.TryParse(args[0], out var profileId) ? profileId : 0;

        var itemDefinitionIds = new List<int>();

        foreach (var kit in _resourceManager.CombatJobs.Values)
        {
            if (profileFilter != 0 && kit.ProfileId != profileFilter)
                continue;

            foreach (var mapping in kit.Weapons)
            {
                if (mapping.WeaponDefIds.Count > 0)
                    itemDefinitionIds.Add(mapping.WeaponDefIds[0]);
            }
        }

        var characterId = GuidHelper.GetPlayerId(invoker.Guid);

        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbCharacter = dbContext.Characters
            .Include(character => character.Items)
            .SingleOrDefault(character => character.Id == characterId);

        if (dbCharacter is null)
            return true;

        var nextItemId = dbCharacter.Items.Select(item => item.Id).DefaultIfEmpty(0).Max() + 1;
        var granted = 0;

        foreach (var itemDefinitionId in itemDefinitionIds.Distinct())
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
                continue;

            if (dbCharacter.Items.Any(item => item.Definition == itemDefinitionId))
                continue;

            var dbItem = new DbItem
            {
                Id = nextItemId++,
                Definition = itemDefinitionId,
                Count = 1,
                Tint = 0
            };

            dbCharacter.Items.Add(dbItem);

            var clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Definition = dbItem.Definition,
                Count = dbItem.Count,
                Tint = dbItem.Tint
            };

            invoker.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);
            itemDefinition.Serialize(writer);

            invoker.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });

            granted++;
        }

        dbContext.SaveChanges();

        ChatHelper.SendSystemMessage(invoker, $"Granted {granted} test weapon(s) ({itemDefinitionIds.Distinct().Count()} mapped).");
        return true;
    }
}
