using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Party;

// A transient in-memory party (the client's "group"). Unlike a guild it is NOT persisted — it
// exists only while it has members and is discarded when it empties. One member is the leader
// (the only one who can invite/kick); the leader passes to another member if the leader leaves.
public sealed class Party
{
    // Retail groups cap at 4 (the combatGroupWindow shows up to 4 member panes).
    public const int MaxMembers = 4;

    private readonly object _lock = new();
    private readonly List<Player> _members = [];

    // Guids the leader has invited who haven't accepted/declined yet.
    private readonly HashSet<ulong> _pendingInvites = [];

    public ulong LeaderGuid { get; private set; }

    public Party(Player leader)
    {
        LeaderGuid = leader.Guid;
        _members.Add(leader);
    }

    public IReadOnlyList<Player> Members
    {
        get { lock (_lock) return [.. _members]; }
    }

    public int Count
    {
        get { lock (_lock) return _members.Count; }
    }

    public bool IsFull
    {
        get { lock (_lock) return _members.Count >= MaxMembers; }
    }

    public bool IsLeader(Player player) => LeaderGuid == player.Guid;

    public bool Contains(Player player)
    {
        lock (_lock) return _members.Any(m => m.Guid == player.Guid);
    }

    public void AddPendingInvite(ulong guid)
    {
        lock (_lock) _pendingInvites.Add(guid);
    }

    public bool HasPendingInvite(ulong guid)
    {
        lock (_lock) return _pendingInvites.Contains(guid);
    }

    public bool TryAcceptInvite(Player player)
    {
        lock (_lock)
        {
            if (!_pendingInvites.Remove(player.Guid))
                return false;
            if (_members.Count >= MaxMembers || _members.Any(m => m.Guid == player.Guid))
                return false;
            _members.Add(player);
            return true;
        }
    }

    public void ClearInvite(ulong guid)
    {
        lock (_lock) _pendingInvites.Remove(guid);
    }

    // Remove a member; returns true if the party is now empty (caller disposes it). If the
    // leader left, leadership passes to the next remaining member.
    public bool Remove(Player player)
    {
        lock (_lock)
        {
            _members.RemoveAll(m => m.Guid == player.Guid);
            if (_members.Count > 0 && LeaderGuid == player.Guid)
                LeaderGuid = _members[0].Guid;
            return _members.Count <= 1; // a party of one isn't a party — caller tears it down
        }
    }
}
