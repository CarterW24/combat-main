using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Party;

public sealed class Party
{
    public const int MaxMembers = 4;

    private readonly object _lock = new();
    private readonly List<Player> _members = [];

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

    public bool Remove(Player player)
    {
        lock (_lock)
        {
            _members.RemoveAll(m => m.Guid == player.Guid);
            if (_members.Count > 0 && LeaderGuid == player.Guid)
                LeaderGuid = _members[0].Guid;
            return _members.Count <= 1;
        }
    }
}
