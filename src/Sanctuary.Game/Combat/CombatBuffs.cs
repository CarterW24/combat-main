using System;
using System.Collections.Concurrent;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Combat;

// COMBAT: cross-assembly buff/energy glue. Zones (Sanctuary.Game) can't reach the Gateway ability
// handler's private state, so the shared pieces live here:
//  - the DAMAGE-BUFF registry (Mystical Blade / Enrage / the damage POWERUP all multiply outgoing
//    ability damage while unexpired — the handler applies it on every hit),
//  - an energy-refill request line (the ENERGY powerup refills the bar, whose state the handler owns —
//    it subscribes at startup).
public static class CombatBuffs
{
    private static readonly ConcurrentDictionary<ulong, (int Pct, long UntilTicks)> _damage = new();

    public static void AddDamageBuff(ulong playerGuid, int multiplierPct, int durationMs) =>
        _damage[playerGuid] = (multiplierPct, Environment.TickCount64 + durationMs);

    /// <summary>Outgoing ability damage with the player's active buff applied (expired buffs prune here).</summary>
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

    /// <summary>The ability handler (owner of the energy bar) subscribes to this; zones raise it when
    /// the player grabs an ENERGY powerup.</summary>
    public static event Action<Player>? EnergyRefillRequested;

    public static void RequestEnergyRefill(Player player) => EnergyRefillRequested?.Invoke(player);

    /// <summary>Set by the ability handler at startup. Zones use it to leave ENERGY pickups on the
    /// ground while the player's bar is already full (user design call 2026-07-15) — the pickup
    /// collects on a later walk-over once some energy has been spent. Null (not yet wired) = don't gate.</summary>
    public static Func<Player, bool>? IsEnergyFull;
}
