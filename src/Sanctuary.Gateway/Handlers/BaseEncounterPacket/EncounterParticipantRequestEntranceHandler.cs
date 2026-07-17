using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class EncounterParticipantRequestEntranceHandler
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;
    private static Sanctuary.Game.Party.IPartyManager _partyManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(EncounterParticipantRequestEntranceHandler));

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _partyManager = serviceProvider.GetRequiredService<Sanctuary.Game.Party.IPartyManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        _logger.LogInformation("EncounterParticipantRequestEntrance (GO! pressed) | body={hex}",
            Convert.ToHexString(reader.Span));

        reader.TryRead(out int encounterId);

        if (encounterId == TormentedSpiritsArenaZone.EncounterId)
            EnterSpiritArena(connection);
        else if (Sanctuary.Game.Dungeons.DungeonCatalog.ByActivity.ContainsKey(encounterId))
            EnterEncounterArena(connection, encounterId);
        else
            EnterFrostfangArena(connection);

        return true;
    }

    public static void EnterEncounterArena(GatewayConnection connection, int activityId)
    {
        var arena = _zoneManager.GetOrCreateEncounterArena(activityId);

        void Enter(Player player)
        {
            player.EncounterReturnPosition = player.Position;
            player.TeleportToZone(arena, arena.SpawnPosition, arena.SpawnRotation, sky: null, geometryId: 0);
            player.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));
            _logger.LogInformation("GO! -> TeleportToZone {zone} ({id}) for {name} (activity {a}).",
                arena.Name, arena.Id, player.Name, activityId);
        }

        EnterWithParty(connection.Player, Enter);
    }

    public static void EnterFrostfangArena(GatewayConnection connection)
    {
        var arena = _zoneManager.GetOrCreateFrostfangArena();

        void Enter(Player player)
        {
            player.TeleportToZone(arena, arena.EffectiveSpawn, arena.SpawnRotation, sky: null, geometryId: 0);
            player.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));
            _logger.LogInformation("GO! -> TeleportToZone {zone} ({id}) for {name} at {pos}.",
                arena.Name, arena.Id, player.Name, arena.EffectiveSpawn);
        }

        EnterWithParty(connection.Player, Enter);
    }

    public static void EnterSpiritArena(GatewayConnection connection)
    {
        var arena = _zoneManager.GetOrCreateSpiritArena();

        void Enter(Player player)
        {
            player.EncounterReturnPosition = player.Position;
            player.TeleportToZone(arena, arena.SpawnPosition, arena.SpawnRotation, sky: null, geometryId: 0);
            player.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));
            _logger.LogInformation("GO! -> TeleportToZone {zone} ({id}) for {name} at {pos}.",
                arena.Name, arena.Id, player.Name, arena.SpawnPosition);
        }

        EnterWithParty(connection.Player, Enter);
    }

    private static void EnterWithParty(Player leader, Action<Player> enter)
    {
        enter(leader);

        var party = _partyManager.GetParty(leader);
        if (party is null || !party.IsLeader(leader))
            return;

        foreach (var member in party.Members)
        {
            if (member.Guid == leader.Guid)
                continue;
            _logger.LogInformation("GO! -> pulling party member {name} into the arena.", member.Name);
            enter(member);
        }
    }
}
