using System;

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
                // Format-independent: the sender leaves whatever party they're in.
                _partyManager.Leave(connection.Player);
                return true;

            case GroupInviteReply:
            case GroupAccept:
            case GroupKick:
                // TODO(party): accept/kick carry a target guid we still need to capture. Finalize the
                // parse from the logged payload, then drive _partyManager.Accept/Kick + roster push.
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

        // The native invite popup needs the S2C GroupInvite byte layout (RE in progress — the
        // serialize method isn't in the packet's vtable slots). INTERIM: surface the invite via a
        // System chat message the client already renders. The target joins with "!paccept".
        SendSystem(target, $"{inviter.Name?.FullName ?? "A player"} invited you to their party. Type !paccept to join.");
        SendSystem(inviter, $"You invited {target.Name?.FullName ?? packet.TargetName} to your party.");

        _logger.LogInformation("GROUP invite recorded: {inviter} -> {target} (party leader {leader}).",
            inviter.Name, target.Name, party.LeaderGuid);
        return true;
    }

    /// <summary>The target accepts a pending party invite (via the "!paccept" chat command until the
    /// native accept packet's byte format is captured). Announces the join to the whole party.</summary>
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
    }

    /// <summary>Leave the player's current party (via "!pleave").</summary>
    public static void LeaveParty(Player player)
    {
        var party = _partyManager.GetParty(player);
        if (party is null)
        {
            SendSystem(player, "You are not in a party.");
            return;
        }

        var others = party.Members;
        _partyManager.Leave(player);
        SendSystem(player, "You left the party.");
        foreach (var member in others)
        {
            if (member.Guid != player.Guid)
                SendSystem(member, $"{player.Name?.FullName ?? "A player"} left the party.");
        }
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
