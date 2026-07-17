using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Party;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseGroupPacketHandler
{
    private static ILogger _logger = null!;
    private static IPartyManager _partyManager = null!;
    private static IZoneManager _zoneManager = null!;

    private const short GroupInvite = 1;
    private const short GroupInviteReply = 2;
    private const short GroupLeave = 3;
    private const short GroupAccept = 4;
    private const short GroupKick = 6;

    private static readonly TimeSpan InviteDebounce = TimeSpan.FromSeconds(3);

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseGroupPacketHandler));

        _partyManager = serviceProvider.GetRequiredService<IPartyManager>();
        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short subOpCode))
            return false;

        _logger.LogInformation("GROUP packet sub={sub} from {player} | payload={hex}",
            subOpCode, connection.Player.Name, Convert.ToHexString(reader.Span));

        switch (subOpCode)
        {
            case GroupInvite:
                return HandleInvite(connection, reader.Span);

            case GroupLeave:
                LeaveParty(connection.Player);
                return true;

            case GroupAccept:
                AcceptInvite(connection.Player);
                return true;

            case GroupInviteReply:
                _partyManager.Decline(connection.Player);
                return true;

            case GroupKick:
                if (GroupPacketGroupKick.TryDeserialize(reader.Span, out var kick))
                    KickMemberByName(connection.Player, kick.TargetFullName);
                return true;

            default:
                return true;
        }
    }

    private static bool HandleInvite(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!GroupPacketGroupInvite.TryDeserialize(data, out var packet) || string.IsNullOrEmpty(packet.TargetName))
        {
            _logger.LogWarning("GROUP invite: failed to parse target name.");
            return true;
        }

        var inviter = connection.Player;

        if (DateTime.UtcNow - inviter.LastPartyInviteAt < InviteDebounce)
            return true;
        inviter.LastPartyInviteAt = DateTime.UtcNow;

        if (!_zoneManager.TryGetPlayer(packet.TargetName, out var target))
        {
            _logger.LogInformation("GROUP invite: target '{name}' not found/online.", packet.TargetName);
            return true;
        }

        if (target.Guid == inviter.Guid)
            return true;

        var party = _partyManager.Invite(inviter, target);
        if (party is null)
        {
            _logger.LogInformation("GROUP invite: {inviter} -> {target} refused (full/already grouped/not leader).",
                inviter.Name, target.Name);
            return true;
        }

        target.SendTunneled(new GroupPacketGroupInvite
        {
            InviterGuid = inviter.Guid,
            InviterName = inviter.Name,
        });

        SendSystem(target, $"{inviter.Name?.FullName ?? "A player"} invited you to their party. Type !paccept to join.");
        SendSystem(inviter, $"You invited {target.Name?.FullName ?? packet.TargetName} to your party.");

        _logger.LogInformation("GROUP invite recorded: {inviter} -> {target} (party leader {leader}).",
            inviter.Name, target.Name, party.LeaderGuid);
        return true;
    }

    public static void AcceptInvite(Player player)
    {
        var party = _partyManager.Accept(player);
        if (party is null)
        {
            SendSystem(player, "You have no pending party invite.");
            return;
        }

        foreach (var member in party.Members)
            SendSystem(member, $"{player.Name?.FullName ?? "A player"} joined the party. ({party.Count}/{Sanctuary.Game.Party.Party.MaxMembers})");

        PushRoster(party);
    }

    public static void LeaveParty(Player player)
    {
        var party = _partyManager.GetParty(player);
        if (party is null)
        {
            SendSystem(player, "You are not in a party.");
            return;
        }

        var membersBefore = party.Members;

        if (party.IsLeader(player))
        {
            _partyManager.DisbandParty(party);
            foreach (var member in membersBefore)
            {
                SendSystem(member, "The party has been disbanded.");
                ClearRoster(member);
            }
            return;
        }

        var stillStanding = _partyManager.RemoveMember(player);
        SendSystem(player, "You left the party.");
        ClearRoster(player);

        if (stillStanding is not null)
        {
            foreach (var member in stillStanding.Members)
                SendSystem(member, $"{player.Name?.FullName ?? "A player"} left the party.");
            PushRoster(stillStanding);
        }
        else
        {
            foreach (var member in membersBefore)
            {
                if (member.Guid == player.Guid) continue;
                SendSystem(member, "The party has been disbanded.");
                ClearRoster(member);
            }
        }
    }

    public static void KickMember(Player leader, ulong memberGuid)
    {
        var party = _partyManager.GetParty(leader);
        if (party is null || !party.IsLeader(leader) || memberGuid == leader.Guid)
            return;

        KickResolved(leader, party, party.Members.FirstOrDefault(m => m.Guid == memberGuid));
    }

    public static void KickMemberByName(Player leader, string targetFullName)
    {
        if (string.IsNullOrWhiteSpace(targetFullName))
            return;

        var party = _partyManager.GetParty(leader);
        if (party is null || !party.IsLeader(leader))
            return;

        var kicked = party.Members.FirstOrDefault(m =>
            m.Guid != leader.Guid &&
            string.Equals(m.Name?.FullName, targetFullName, StringComparison.OrdinalIgnoreCase));

        KickResolved(leader, party, kicked);
    }

    private static void KickResolved(Player leader, Sanctuary.Game.Party.Party party, Player? kicked)
    {
        if (kicked is null || kicked.Guid == leader.Guid)
            return;

        var membersBefore = party.Members;
        var stillStanding = _partyManager.RemoveMember(kicked);

        SendSystem(kicked, "You were removed from the party.");
        ClearRoster(kicked);

        if (stillStanding is not null)
        {
            foreach (var member in stillStanding.Members)
                SendSystem(member, $"{kicked.Name?.FullName ?? "A player"} was removed from the party.");
            PushRoster(stillStanding);
        }
        else
        {
            foreach (var member in membersBefore)
            {
                if (member.Guid == kicked.Guid) continue;
                SendSystem(member, "The party has been disbanded.");
                ClearRoster(member);
            }
        }
    }

    private static void ClearRoster(Player player) =>
        player.SendTunneled(new GroupPacketGroupLeave
        {
            Guid = player.Guid,
            Name = player.Name ?? new NameData(),
        });

    public static void PushRoster(Sanctuary.Game.Party.Party party)
    {
        var members = party.Members;

        var update = new GroupPacketGroupUpdate { LeaderGuid = party.LeaderGuid };
        foreach (var m in members)
        {
            update.Members.Add(new GroupPacketGroupUpdate.Member
            {
                Guid = m.Guid,
                Name = m.Name ?? new NameData(),
                ProfileId = m.ActiveProfileId,
                ProfileRank = GetLevel(m),
                Online = true,
            });
        }

        foreach (var m in members)
            m.SendTunneled(update);

        foreach (var recipient in members)
        {
            foreach (var subject in members)
            {
                try
                {
                    if (PacketPortraitDataRequestHandler.HasHeadshot(subject))
                    {
                        var img = PacketPortraitDataRequestHandler.BuildImageData(subject, "Headshot", includeAttachments: false);
                        _logger.LogInformation("PORTRAIT push (PNG) -> {to} for {subj} (guid={guid} png={png}B packet={pkt}B)",
                            recipient.Name?.FullName, subject.Name?.FullName, subject.Guid,
                            img.PngPayload?.Length ?? 0, img.Serialize().Length);
                        recipient.SendTunneled(img);
                    }
                    else
                    {
                        _logger.LogInformation("PORTRAIT render-trigger -> {to} for {subj} (guid={guid}, no PNG on disk)",
                            recipient.Name?.FullName, subject.Name?.FullName, subject.Guid);
                        recipient.SendTunneled(new PacketGeneratePortraitRequest
                        {
                            Guid = subject.Guid,
                            Provider = "Headshot",
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PORTRAIT fill failed for {subj} -> {to}",
                        subject.Name?.FullName, recipient.Name?.FullName);
                }
            }
        }
    }

    private static int GetLevel(Player player)
    {
        try { return player.ActiveProfile.Rank; }
        catch { return 1; }
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
