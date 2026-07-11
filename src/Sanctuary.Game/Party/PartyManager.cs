using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Party;

public sealed class PartyManager : IPartyManager
{
    private readonly ILogger _logger;

    // player guid -> the party they belong to (or are the founding leader of).
    private readonly ConcurrentDictionary<ulong, Party> _partyByPlayer = new();

    public PartyManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PartyManager>();
    }

    public Party? GetParty(Player player)
        => _partyByPlayer.TryGetValue(player.Guid, out var party) ? party : null;

    public Party? Invite(Player inviter, Player target)
    {
        // Target already in a party -> can't invite.
        if (_partyByPlayer.ContainsKey(target.Guid))
        {
            _logger.LogInformation("Party invite refused: {target} is already grouped.", target.Name);
            return null;
        }

        var party = GetParty(inviter);
        if (party is null)
        {
            // First invite creates the inviter's party (they become the founding leader).
            party = new Party(inviter);
            _partyByPlayer[inviter.Guid] = party;
            _logger.LogInformation("Party created by leader {leader}.", inviter.Name);
        }
        else if (!party.IsLeader(inviter))
        {
            _logger.LogInformation("Party invite refused: {inviter} is not the leader.", inviter.Name);
            return null;
        }

        if (party.IsFull)
        {
            _logger.LogInformation("Party invite refused: party is full.");
            // If we just created an empty-ish party for a full check that can't happen; leave it.
            return null;
        }

        party.AddPendingInvite(target.Guid);
        _logger.LogInformation("Party invite: {inviter} -> {target}.", inviter.Name, target.Name);
        return party;
    }

    public Party? Accept(Player target)
    {
        // Find the party that invited this player.
        foreach (var party in _partyByPlayer.Values)
        {
            if (!party.HasPendingInvite(target.Guid))
                continue;

            if (!party.TryAcceptInvite(target))
                return null;

            _partyByPlayer[target.Guid] = party;
            _logger.LogInformation("Party accept: {target} joined (leader {leader}).", target.Name, party.LeaderGuid);
            return party;
        }

        return null;
    }

    public void Decline(Player target)
    {
        foreach (var party in _partyByPlayer.Values)
        {
            if (party.HasPendingInvite(target.Guid))
            {
                party.ClearInvite(target.Guid);
                _logger.LogInformation("Party decline: {target} declined.", target.Name);
                return;
            }
        }
    }

    public void Leave(Player player)
    {
        if (!_partyByPlayer.TryRemove(player.Guid, out var party))
            return;

        var disbanded = party.Remove(player);
        _logger.LogInformation("Party leave: {player} left.", player.Name);

        if (disbanded)
            Disband(party);
    }

    public void Kick(Player leader, ulong memberGuid)
    {
        var party = GetParty(leader);
        if (party is null || !party.IsLeader(leader) || leader.Guid == memberGuid)
            return;

        // Resolve the kicked member from the party roster.
        foreach (var member in party.Members)
        {
            if (member.Guid != memberGuid)
                continue;

            _partyByPlayer.TryRemove(memberGuid, out _);
            var disbanded = party.Remove(member);
            _logger.LogInformation("Party kick: {member} removed by {leader}.", member.Name, leader.Name);

            if (disbanded)
                Disband(party);
            return;
        }
    }

    public Party? RemoveMember(Player player)
    {
        if (!_partyByPlayer.TryRemove(player.Guid, out var party))
            return null;

        var collapsed = party.Remove(player); // true if <2 members remain
        _logger.LogInformation("Party: {player} removed.", player.Name);

        if (collapsed)
        {
            DisbandParty(party);
            return null;
        }
        return party;
    }

    public void DisbandParty(Party party)
    {
        foreach (var member in party.Members)
            _partyByPlayer.TryRemove(member.Guid, out _);
        _logger.LogInformation("Party disbanded (leader {leader}).", party.LeaderGuid);
    }

    /// <summary>Drop every remaining member's mapping (a sub-two party is torn down completely).</summary>
    private void Disband(Party party) => DisbandParty(party);
}
