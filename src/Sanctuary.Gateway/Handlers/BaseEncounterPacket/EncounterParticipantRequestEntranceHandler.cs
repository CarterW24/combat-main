using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Dungeons;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class EncounterParticipantRequestEntranceHandler
{
    // Global.Text 634 = "Your job has been switched because you are entering a combat area."
    private const int JobSwitchedStringId = 634;

    private const int InviteTimeoutSeconds = 30;

    private sealed class GroupLaunch
    {
        public required GatewayConnection Leader;
        public required int EncounterId;
        public readonly Dictionary<ulong, GatewayConnection?> Awaiting = [];
        public Timer? Timer;
        public bool Launched;
        public readonly object Lock = new();
    }

    private static readonly ConcurrentDictionary<ulong, GroupLaunch> _launchByLeader = new();
    private static readonly ConcurrentDictionary<ulong, GroupLaunch> _launchByMember = new();

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

        RequestLaunch(connection, encounterId);

        return true;
    }

    public static void EnterEncounterArena(GatewayConnection connection, int activityId) =>
        RequestLaunch(connection, activityId);

    public static void EnterFrostfangArena(GatewayConnection connection) =>
        RequestLaunch(connection, FrostfangArenaZone.EncounterId);

    public static void EnterSpiritArena(GatewayConnection connection) =>
        RequestLaunch(connection, TormentedSpiritsArenaZone.EncounterId);

    // GO! pressed. Solo (or non-leader) players enter immediately; a party leader first invites the
    // party (op41/102 confirm panel) and everyone enters together once all have answered or the
    // invite timer runs out.
    private static void RequestLaunch(GatewayConnection connection, int encounterId)
    {
        // An invited member pressing GO on their offer screen is their accept — never a fresh launch.
        if (_launchByMember.ContainsKey(connection.Player.Guid))
        {
            RecordReply(connection, accepted: true);
            return;
        }

        var leader = connection.Player;
        var party = _partyManager.GetParty(leader);

        if (party is null || !party.IsLeader(leader))
        {
            EnterSelf(connection, encounterId);
            return;
        }

        if (_launchByLeader.ContainsKey(leader.Guid))
        {
            _logger.LogInformation("GO! ignored: {name} already has a pending group launch.", leader.Name);
            return;
        }

        var invitees = party.Members
            .Where(m => m.Guid != leader.Guid && m.Zone is not CombatEncounterZone)
            .ToList();

        if (invitees.Count == 0)
        {
            EnterSelf(connection, encounterId);
            return;
        }

        var launch = new GroupLaunch { Leader = connection, EncounterId = encounterId };
        foreach (var member in invitees)
            launch.Awaiting[member.Guid] = null;

        _launchByLeader[leader.Guid] = launch;
        foreach (var member in invitees)
            _launchByMember[member.Guid] = launch;

        var titleNameId = TitleNameIdFor(encounterId);

        foreach (var member in invitees)
        {
            // The offer/details screen carries the encounter info AND the Change Jobs button — invited
            // members get it too, so they can pick a combat job before accepting. Their GO = accept,
            // their X (op41/124) = decline.
            SendEncounterOffer(member, encounterId);

            member.SendTunneled(new EncounterInvitationRequestPacket(encounterId, 0)
            {
                InviterGuid = leader.Guid,
                NameStringId = titleNameId,
                TimeoutSeconds = InviteTimeoutSeconds,
            });

            _logger.LogInformation("Invite sent: {leader} -> {member} for encounter {id} (title {title}, {timeout}s).",
                leader.Name, member.Name, encounterId, titleNameId, InviteTimeoutSeconds);
        }

        SendSystem(leader, "Waiting for your party to accept...");

        launch.Timer = new Timer(_ => OnLaunchTimeout(launch), null,
            TimeSpan.FromSeconds(InviteTimeoutSeconds + 2), Timeout.InfiniteTimeSpan);
    }

    public static bool HandleInvitationResponse(GatewayConnection connection, PacketReader reader)
    {
        reader.TryRead(out int _);
        reader.TryRead(out int _);
        reader.TryRead(out ulong _);
        reader.TryRead(out byte acceptedByte);

        _logger.LogInformation("Invite response from {name}: accepted={accepted}.",
            connection.Player.Name, acceptedByte != 0);

        RecordReply(connection, acceptedByte != 0);

        return true;
    }

    // A member closing the pending-encounter panel (op41/124) counts as a decline.
    public static bool TryHandleInviteDecline(GatewayConnection connection)
    {
        if (!_launchByMember.ContainsKey(connection.Player.Guid))
            return false;

        _logger.LogInformation("Invite declined (panel dismissed) by {name}.", connection.Player.Name);
        RecordReply(connection, accepted: false);
        return true;
    }

    private static void RecordReply(GatewayConnection connection, bool accepted)
    {
        var member = connection.Player;

        if (!_launchByMember.TryRemove(member.Guid, out var launch))
            return;

        var launchNow = false;

        lock (launch.Lock)
        {
            if (launch.Launched)
                return;

            if (!launch.Awaiting.Remove(member.Guid))
                return;

            if (accepted)
                launch.Awaiting[member.Guid] = connection;

            launchNow = !launch.Awaiting.Values.Contains(null);
        }

        AnnounceReply(member, accepted);

        if (!accepted)
            SendSystem(launch.Leader.Player, $"{member.Name?.FullName ?? "A party member"} declined.");

        if (launchNow)
            LaunchAll(launch);
    }

    private static void OnLaunchTimeout(GroupLaunch launch)
    {
        List<ulong> unanswered;

        lock (launch.Lock)
        {
            if (launch.Launched)
                return;
            unanswered = launch.Awaiting.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList();
        }

        foreach (var guid in unanswered)
            _launchByMember.TryRemove(guid, out _);

        _logger.LogInformation("Group launch timeout for leader {name}: {count} unanswered invite(s).",
            launch.Leader.Player.Name, unanswered.Count);

        LaunchAll(launch);
    }

    private static void LaunchAll(GroupLaunch launch)
    {
        List<GatewayConnection> entrants;

        lock (launch.Lock)
        {
            if (launch.Launched)
                return;
            launch.Launched = true;

            entrants = launch.Awaiting.Values.Where(c => c is not null).Cast<GatewayConnection>().ToList();
        }

        launch.Timer?.Dispose();
        _launchByLeader.TryRemove(launch.Leader.Player.Guid, out _);
        foreach (var guid in launch.Awaiting.Keys)
            _launchByMember.TryRemove(guid, out _);

        try
        {
            EnterSelf(launch.Leader, launch.EncounterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Group launch: leader entry failed.");
        }

        foreach (var entrant in entrants)
        {
            try
            {
                EnterSelf(entrant, launch.EncounterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Group launch: member entry failed for {name}.", entrant.Player.Name);
            }
        }

        _logger.LogInformation("Group launch: {count} player(s) entered encounter {id} together.",
            entrants.Count + 1, launch.EncounterId);
    }

    private static void EnterSelf(GatewayConnection connection, int encounterId)
    {
        var player = connection.Player;

        EnsureCombatJob(connection);

        if (encounterId == TormentedSpiritsArenaZone.EncounterId)
        {
            var arena = _zoneManager.GetOrCreateSpiritArena();
            player.EncounterReturnPosition = player.Position;
            player.TeleportToZone(arena, arena.SpawnPosition, arena.SpawnRotation, sky: null, geometryId: 0);
        }
        else if (DungeonCatalog.ByActivity.ContainsKey(encounterId))
        {
            var arena = _zoneManager.GetOrCreateEncounterArena(encounterId);
            player.EncounterReturnPosition = player.Position;
            player.TeleportToZone(arena, arena.SpawnPosition, arena.SpawnRotation, sky: null, geometryId: 0);
        }
        else
        {
            var arena = _zoneManager.GetOrCreateFrostfangArena();
            player.TeleportToZone(arena, arena.EffectiveSpawn, arena.SpawnRotation, sky: null, geometryId: 0);
        }

        player.SendTunneled(new MiniGameGameStartPacket(0, -1, -1));

        _logger.LogInformation("GO! -> {name} entered encounter {id} in {zone}.",
            player.Name, encounterId, player.Zone.GetType().Name);
    }

    // Nobody fights in a non-combat job: switch to the first combat job the player owns and show the
    // retail "job has been switched" toast (op41/120, string 634). Called at the offer screen AND at
    // entry (members who accept an invite never see an offer screen).
    public static void EnsureCombatJob(GatewayConnection connection)
    {
        var player = connection.Player;

        if (JobKits.Has(player.ActiveProfileId))
            return;

        var combatProfile = player.Profiles.FirstOrDefault(x => JobKits.Has(x.Id));
        if (combatProfile is null)
        {
            _logger.LogWarning("Combat-job gate: {name} owns NO combat profile — staying on profile {id}.",
                player.Name, player.ActiveProfileId);
            return;
        }

        _logger.LogInformation("Combat-job gate: switching {name} from profile {old} to combat profile {new}.",
            player.Name, player.ActiveProfileId, combatProfile.Id);

        CommandPacketSetProfileHandler.ActivateProfile(connection, combatProfile);

        player.SendTunneled(new EncounterParticipantMessagePacket
        {
            MessageStringId = JobSwitchedStringId,
            PlayerGuid = player.Guid,
            FallbackName = player.Name?.FullName ?? string.Empty,
        });
    }

    private static void AnnounceReply(Player responder, bool accepted)
    {
        var party = _partyManager.GetParty(responder);
        if (party is null)
            return;

        var reply = new GroupPacketAnnounceEncounterReply
        {
            MemberGuid = responder.Guid,
            Accepted = accepted,
        };

        foreach (var member in party.Members)
            member.SendTunneled(reply);
    }

    // The full offer/details burst (states 2-4 + details + delayed zone-ready/state 5) — the same
    // screen the leader gets from the NPC interaction. The delayed follow-ups check the player's
    // offer generation so an X (op41/124) in the gap can't reopen an already-dismissed offer.
    public static void SendEncounterOffer(Player player, int encounterId)
    {
        int instanceId;
        int nameId;
        int descriptionId;
        int difficulty;
        int iconId;

        if (encounterId == TormentedSpiritsArenaZone.EncounterId)
        {
            instanceId = TormentedSpiritsArenaZone.EncounterInstanceId;
            nameId = TormentedSpiritsArenaZone.TitleNameId;
            descriptionId = TormentedSpiritsArenaZone.DescriptionId;
            difficulty = TormentedSpiritsArenaZone.Difficulty;
            iconId = TormentedSpiritsArenaZone.IconId;
        }
        else if (DungeonCatalog.ByActivity.TryGetValue(encounterId, out var dungeon))
        {
            instanceId = 1;
            nameId = dungeon.TitleNameId;
            descriptionId = dungeon.DescriptionId;
            difficulty = dungeon.Difficulty;
            iconId = dungeon.IconId;
        }
        else
        {
            instanceId = FrostfangArenaZone.EncounterInstanceId;
            nameId = FrostfangArenaZone.TitleNameId;
            descriptionId = 104171;
            difficulty = 1;
            iconId = 1345;
        }

        foreach (var state in new[] { 2, 3, 4 })
        {
            player.SendTunneled(new EncounterStatePacket
            {
                EncounterId = encounterId,
                InstanceId = instanceId,
                State = state,
            });
        }

        player.SendTunneled(new EncounterDetailsResponsePacket
        {
            Unknown = encounterId,
            Unknown2 = instanceId,
            NameId = nameId,
            DescriptionId = descriptionId,
            Difficulty = difficulty,
            IconId = iconId,
            MiniGameType = 4,
            PreviewRewards = FrostfangArenaZone.GetPrizePreviewFor(player),
            PreviewCoins = FrostfangArenaZone.PrizeCoins,
            PreviewXp = FrostfangArenaZone.PrizeXp,
            ProfileType = FrostfangArenaZone.CombatProfileType,
            ActivityId = encounterId,
        });

        var offerGeneration = player.EncounterOfferGeneration;
        _ = Task.Run(async () =>
        {
            await Task.Delay(600);
            if (player.EncounterOfferGeneration != offerGeneration)
                return;
            player.SendTunneled(new EncounterZoneIsReadyPacket());
            player.SendTunneled(new EncounterStatePacket
            {
                EncounterId = encounterId,
                InstanceId = instanceId,
                State = 5,
            });
        });
    }

    private static int TitleNameIdFor(int encounterId)
    {
        if (encounterId == TormentedSpiritsArenaZone.EncounterId)
            return TormentedSpiritsArenaZone.TitleNameId;

        if (DungeonCatalog.ByActivity.TryGetValue(encounterId, out var dungeon))
            return dungeon.TitleNameId;

        return FrostfangArenaZone.TitleNameId;
    }

    private static void SendSystem(Player player, string message) =>
        player.SendTunneled(new PacketChat
        {
            Channel = ChatChannel.System,
            FromGuid = 0,
            FromName = new NameData(),
            Message = message,
        });
}
