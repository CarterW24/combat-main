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

// PARTY (op40 group). Sub-opcodes are SHORTS. Ghidra-confirmed C2S classes: 1 GroupInvite,
// 4 GroupAccept, 3 GroupLeave, 6 GroupKick (2026-07-11 decode). OBSERVATION-FIRST: every op40
// sub-opcode is logged with its raw payload so the exact C2S field layouts can be finalized from
// real client captures — the methodology that cracked the quest packets. The format-INDEPENDENT
// actions (leave = "the sender leaves their party") run against the PartyManager now; the
// format-DEPENDENT ones (invite/accept/kick carry a target guid or name we must parse) are logged
// and left as TODO until the captured bytes confirm the layout.
[PacketHandler]
public static class BaseGroupPacketHandler
{
    private static ILogger _logger = null!;
    private static IPartyManager _partyManager = null!;
    private static IZoneManager _zoneManager = null!;

    // Sub-opcodes (from PacketReaderExtensions op40 table).
    private const short GroupInvite = 1;
    private const short GroupInviteReply = 2;
    private const short GroupLeave = 3;
    private const short GroupAccept = 4;
    private const short GroupKick = 6;

    /// <summary>The client re-sends GroupInvite ~6x/sec while the invite UI is up — collapse a burst.</summary>
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
        // reader is positioned AFTER the op40 short (the top-level dispatch already read it).
        if (!reader.TryRead(out short subOpCode))
            return false;

        // OBSERVE: log the full payload for every group packet so the wire formats can be finalized
        // from real captures. reader.Span returns the whole payload from byte 0 (codebase convention).
        _logger.LogInformation("GROUP packet sub={sub} from {player} | payload={hex}",
            subOpCode, connection.Player.Name, Convert.ToHexString(reader.Span));

        switch (subOpCode)
        {
            case GroupInvite:
                return HandleInvite(connection, reader.Span);

            case GroupLeave:
                // The "x" on your OWN portrait. LeaveParty disbands the whole party if you're the
                // leader, else just removes you + refreshes the remaining roster.
                LeaveParty(connection.Player);
                return true;

            case GroupAccept:
                // ★ NATIVE ✓ BUTTON (captured 2026-07-11): the invite popup's accept button sends
                // op40/sub4 with the leader's guid. The sender is the invitee accepting — join them to
                // whichever party invited them (PartyManager.Accept finds it), same as !paccept did.
                AcceptInvite(connection.Player);
                return true;

            case GroupInviteReply:
                // The popup's ✗ decline button (capture its exact payload to confirm); clear any
                // pending invite for the sender so a re-invite is possible.
                _partyManager.Decline(connection.Player);
                return true;

            case GroupKick:
                // The leader clicked the "x" on ANOTHER member's portrait. Captured wire format
                // (2026-07-11): the client sends the target's NAME (guid is 0), so kick BY NAME.
                if (GroupPacketGroupKick.TryDeserialize(reader.Span, out var kick))
                    KickMemberByName(connection.Player, kick.TargetFullName);
                return true;

            default:
                // Known-but-unimplemented (RenamePlayer/MapPing/InMinigame/...) or unknown — logged
                // above; swallow so the generic UNHANDLED path stays quiet.
                return true;
        }
    }

    // GroupInvite C2S (wire format captured 2026-07-11): header + 48-byte zero block + length-prefixed
    // target NAME + trailing noise. The client spams it ~6x/sec, so debounce per inviter.
    private static bool HandleInvite(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!GroupPacketGroupInvite.TryDeserialize(data, out var packet) || string.IsNullOrEmpty(packet.TargetName))
        {
            _logger.LogWarning("GROUP invite: failed to parse target name.");
            return true;
        }

        var inviter = connection.Player;

        // Debounce the ~6/sec re-send burst.
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

        // ★ NATIVE INVITE POPUP (RE experiment 2026-07-11): the S2C GroupInvite is the same packet
        // class the client serializes for C2S, so mirror that 97-byte shape back to the invitee with
        // the inviter's name/guid. If the client raises its invite popup, we've cracked the S2C format.
        target.SendTunneled(new GroupPacketGroupInvite
        {
            InviterGuid = inviter.Guid,
            InviterName = inviter.Name, // NameData — the "Group with <name>" popup label
        });

        // INTERIM fallback (until the native popup is confirmed): the System chat message + !paccept.
        SendSystem(target, $"{inviter.Name?.FullName ?? "A player"} invited you to their party. Type !paccept to join.");
        SendSystem(inviter, $"You invited {target.Name?.FullName ?? packet.TargetName} to your party.");

        _logger.LogInformation("GROUP invite recorded: {inviter} -> {target} (party leader {leader}).",
            inviter.Name, target.Name, party.LeaderGuid);
        return true;
    }

    /// <summary>The target accepts a pending party invite (native ✓ button = op40/sub4, or the
    /// "!paccept" fallback). Announces the join + pushes the live roster to every member.</summary>
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

    /// <summary>Leave the player's current party — the "x" on your OWN portrait. If you're the LEADER,
    /// the whole party disbands; otherwise just you leave and the remaining roster refreshes.</summary>
    public static void LeaveParty(Player player)
    {
        var party = _partyManager.GetParty(player);
        if (party is null)
        {
            SendSystem(player, "You are not in a party.");
            return;
        }

        // Snapshot the members BEFORE removal (so we can notify + clear everyone on a disband).
        var membersBefore = party.Members;

        if (party.IsLeader(player))
        {
            // ★ LEADER LEAVES -> DISBAND THE ENTIRE PARTY.
            _partyManager.DisbandParty(party);
            foreach (var member in membersBefore)
            {
                SendSystem(member, "The party has been disbanded.");
                ClearRoster(member);
            }
            return;
        }

        // A non-leader member leaves.
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
            // The party collapsed to one member — tell + clear the leftover leader.
            foreach (var member in membersBefore)
            {
                if (member.Guid == player.Guid) continue;
                SendSystem(member, "The party has been disbanded.");
                ClearRoster(member);
            }
        }
    }

    /// <summary>The leader kicks a member — the "x" on ANOTHER member's portrait (op40/sub6). Removes
    /// that member and refreshes everyone's roster.</summary>
    public static void KickMember(Player leader, ulong memberGuid)
    {
        var party = _partyManager.GetParty(leader);
        if (party is null || !party.IsLeader(leader) || memberGuid == leader.Guid)
            return;

        KickResolved(leader, party, party.Members.FirstOrDefault(m => m.Guid == memberGuid));
    }

    /// <summary>Kick BY NAME — the captured op40/sub6 payload identifies the target by name (guid 0).</summary>
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

    /// <summary>Close a player's group/roster window. Sends op40/sub3 GroupLeave — the client's group
    /// processor (FUN_0093daf0 case 3) frees its group state and hides the window on this. An empty sub-8
    /// GroupUpdate does NOT close the window, which is why a disbanded/left party's UI used to linger.</summary>
    private static void ClearRoster(Player player) =>
        player.SendTunneled(new GroupPacketGroupLeave
        {
            Guid = player.Guid,
            Name = player.Name ?? new NameData(),
        });

    /// <summary>★ Push the live GroupUpdate (sub-8 roster) to every party member — this is what fills
    /// the group/combat-group window (Frida-verified 2026-07-11: {guid, NameData} per member; the
    /// client resolves job/level from its own player cache). Call whenever membership changes.</summary>
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
                ProfileId = m.ActiveProfileId,      // int0 — job
                ProfileRank = GetLevel(m),          // int1 — the active job's real level
                Online = true,
            });
        }

        foreach (var m in members)
            m.SendTunneled(update);

        // Fill each member's roster headshot on every other member's client. A same-zone member's portrait
        // cache entry is a HIT, so the client never sends a PortraitDataRequest for it — without an unsolicited
        // push/trigger the entry stays an empty stub (silhouette). Frida-verified 2026-07-11: the group row
        // looks up the member's player guid (e.g. 0x1a1) and never fetches it.
        //
        // TWO ways to fill it, and picking the wrong one BLANKS the slot:
        //   * PNG on disk  -> push PlayerImageData (sub3) carrying the bytes. Proven to render.
        //   * no PNG       -> DON'T push. A PlayerImageData with an empty PngPayload doesn't merely fail to
        //                     render, it fills the Headshot slot with nothing and blanks it — which is what we
        //                     were doing for every member, since no client ever uploads its headshot and the
        //                     Images/ folder is normally empty. Send GeneratePortraitRequest (sub1) instead:
        //                     the client renders the 70x70 portrait ITSELF for whatever guid we name, so each
        //                     client draws its team-mates locally with no server-side PNG at all.
        foreach (var recipient in members)
        {
            foreach (var subject in members)
            {
                try
                {
                    if (PacketPortraitDataRequestHandler.HasHeadshot(subject))
                    {
                        // ★ Provider MUST be "Headshot" — the client's Fotomat receive handler (FUN_00bd4a50)
                        // fills the Headshot portrait slot ONLY when the provider string matches "Headshot"
                        // (the group roster reads that slot). A null/empty provider is silently discarded.
                        var img = PacketPortraitDataRequestHandler.BuildImageData(subject, "Headshot", includeAttachments: false);
                        _logger.LogInformation("PORTRAIT push (PNG) -> {to} for {subj}",
                            recipient.Name?.FullName, subject.Name?.FullName);
                        recipient.SendTunneled(img);
                    }
                    else
                    {
                        _logger.LogInformation("PORTRAIT render-trigger -> {to} for {subj} (no PNG on disk)",
                            recipient.Name?.FullName, subject.Name?.FullName);
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

    /// <summary>The active job's level (Rank), guarded — ActiveProfile throws if there's no active profile.</summary>
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
