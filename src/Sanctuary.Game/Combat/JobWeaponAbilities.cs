using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

/// <summary>
/// Job dispatch for the weapon-driven ability kits. Free Realms gives every combat job its
/// abilities through the EQUIPPED WEAPON ("of X" grants X); each job has its own kit class
/// (NinjaWeaponAbilities blades, ArcherWeaponAbilities bows) and this routes by the player's
/// active profile so zone-load / job-swap / weapon-equip / ability-press all stay job-agnostic.
/// </summary>
public static class JobWeaponAbilities
{
    /// <summary>True if the player's active job has a weapon-ability kit (drives whether the
    /// ability toolbar is sent/refreshed).</summary>
    public static bool HasKit(Player player) => player.ActiveProfileId
        is NinjaWeaponAbilities.NinjaProfileId
        or ArcherWeaponAbilities.ArcherProfileId;

    /// <summary>The active job's weapon-driven toolbar, or null when the job has no kit.</summary>
    public static AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        player.ActiveProfileId switch
        {
            NinjaWeaponAbilities.NinjaProfileId => NinjaWeaponAbilities.BuildToolbar(player, resources),
            ArcherWeaponAbilities.ArcherProfileId => ArcherWeaponAbilities.BuildToolbar(player, resources),
            _ => null,
        };

    /// <summary>Resolve the pressed slot against the active job's kit (slot 0 = basic, 1 = special).
    /// Jobs without a kit fall back to the ninja bare-hand strike, preserving old behavior.</summary>
    public static WeaponAbility ResolveAbility(Player player, int slot) =>
        player.ActiveProfileId == ArcherWeaponAbilities.ArcherProfileId
            ? ArcherWeaponAbilities.ResolveAbility(player, slot)
            : NinjaWeaponAbilities.ResolveAbility(player, slot);

    /// <summary>Auto-target reach for an unselected attack: bow range for archers, the
    /// capture-derived 7u melee envelope for everyone else (see the StartAbility handler's
    /// ground-truth notes on that value).</summary>
    public static float AutoTargetReach(Player player) =>
        player.ActiveProfileId == ArcherWeaponAbilities.ArcherProfileId
            ? ArcherWeaponAbilities.BowReach
            : 7f;

    /// <summary>
    /// Send the active job's toolbar AND warm the client's effect cache for it. Use this at every
    /// toolbar delivery point (zone-load, job swap, weapon equip). Returns false when the job has
    /// no kit (nothing sent).
    /// </summary>
    public static bool SendToolbarWithFxPreload(Player player, IResourceManager resources)
    {
        var toolbar = BuildToolbar(player, resources);
        if (toolbar is null)
            return false;

        player.SendTunneled(toolbar);
        PreloadAbilityEffects(player);
        return true;
    }

    /// <summary>
    /// FX CACHE WARM-UP: most composite-effect definitions are loadType=0 (load on demand), so the
    /// FIRST play of an effect only starts the asset stream and renders nothing — the visual only
    /// shows from the second cast on. Retail preloaded ability FX via the real ability definitions
    /// the bar referenced; our bar uses generic castable def ids, so nothing preloads. Fix: when the
    /// toolbar lands, play each of the equipped weapon's effects once ~400u BELOW the player — out
    /// of sight, but the client still instantiates the effect and pulls its assets into cache, so
    /// the first real cast renders immediately.
    /// </summary>
    public static void PreloadAbilityEffects(Player player)
    {
        var ids = new HashSet<int>();
        for (var slot = 0; slot <= 1; slot++) // the bar's 2 ability slots (retail ground truth)
        {
            var ability = ResolveAbility(player, slot);
            ids.Add(ability.EffectId);
            // LINGERING cast FX (projectile-trail loops, CastEffectStopMs > 0) must NOT be warmed:
            // an unattached loop has no stop, so it would sit under the map snowing forever
            // (user-sighted with the Bow of Blizzards trail). They cache on their first tag-play.
            if (ability.CastEffectStopMs == 0)
                ids.Add(ability.CastEffectId);
            ids.Add(ability.CasterEndEffectId);
            ids.Add(ability.EnemyExtraEffectId);
            ids.Add(ability.SwordEffectId);
        }

        var warmPos = new Vector4(player.Position.X, player.Position.Y - 400f, player.Position.Z, 1f);

        foreach (var id in ids)
        {
            if (id <= 0)
                continue;

            player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = 0, // world-positioned, not attached to an actor
                CompositeEffectId = id,
                Position = warmPos,
            });
        }
    }
}
