using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Party;

/// <summary>
/// Tracks the transient parties (client "groups") and each player's membership. A DI singleton;
/// every method takes the acting player so it holds no per-connection state.
/// </summary>
public interface IPartyManager
{
    /// <summary>The party the player is currently in, or null.</summary>
    Party? GetParty(Player player);

    /// <summary>
    /// The leader invites <paramref name="target"/>: creates the leader's party if they don't have
    /// one yet, records the pending invite, and returns the party. Null if the invite can't proceed
    /// (party full, target already grouped, inviter isn't the leader of an existing party).
    /// </summary>
    Party? Invite(Player inviter, Player target);

    /// <summary>The target accepts a pending invite from a party; returns the joined party or null.</summary>
    Party? Accept(Player target);

    /// <summary>The target declines a pending invite (clears it from whatever party invited them).</summary>
    void Decline(Player target);

    /// <summary>The player leaves their party (disbands it if it drops below two members).</summary>
    void Leave(Player player);

    /// <summary>The leader kicks a member by guid.</summary>
    void Kick(Player leader, ulong memberGuid);

    /// <summary>Remove one member's party mapping + membership. Returns the party if it still stands
    /// (2+ members), or null if it collapsed to fewer than two (caller should treat as disbanded).</summary>
    Party? RemoveMember(Player player);

    /// <summary>Disband the entire party — drop every member's mapping. Used when the LEADER leaves.</summary>
    void DisbandParty(Party party);
}
