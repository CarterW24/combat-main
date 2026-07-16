using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Party;

// Tracks the transient parties (client "groups") and each player's membership. A DI singleton;
// every method takes the acting player so it holds no per-connection state.
public interface IPartyManager
{
    // The party the player is currently in, or null.
    Party? GetParty(Player player);

    // The leader invites target: creates the leader's party if they don't have
    // one yet, records the pending invite, and returns the party. Null if the invite can't proceed
    // (party full, target already grouped, inviter isn't the leader of an existing party).
    Party? Invite(Player inviter, Player target);

    // The target accepts a pending invite from a party; returns the joined party or null.
    Party? Accept(Player target);

    // The target declines a pending invite (clears it from whatever party invited them).
    void Decline(Player target);

    // The player leaves their party (disbands it if it drops below two members).
    void Leave(Player player);

    // The leader kicks a member by guid.
    void Kick(Player leader, ulong memberGuid);

    // Remove one member's party mapping + membership. Returns the party if it still stands
    // (2+ members), or null if it collapsed to fewer than two (caller should treat as disbanded).
    Party? RemoveMember(Player player);

    // Disband the entire party — drop every member's mapping. Used when the LEADER leaves.
    void DisbandParty(Party party);
}
