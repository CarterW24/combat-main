using System;
using System.Collections.Concurrent;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Combat;

public static class CombatBuffs
{
    private static readonly ConcurrentDictionary<ulong, (int Pct, long UntilTicks)> _damage = new();

    public static void AddDamageBuff(ulong playerGuid, int multiplierPct, int durationMs) =>
        _damage[playerGuid] = (multiplierPct, Environment.TickCount64 + durationMs);

    public static int ApplyDamage(ulong playerGuid, int damage)
    {
        if (!_damage.TryGetValue(playerGuid, out var buff))
            return damage;

        if (Environment.TickCount64 >= buff.UntilTicks)
        {
            _damage.TryRemove(playerGuid, out _);
            return damage;
        }

        return damage * buff.Pct / 100;
    }

    public static event Action<Player>? EnergyRefillRequested;

    public static void RequestEnergyRefill(Player player) => EnergyRefillRequested?.Invoke(player);

    public static Func<Player, bool>? IsEnergyFull;
}
