using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class AbilityPacketClientRequestStartAbilityHandler
{
    private static ILogger _logger = null!;

    private const float CastSeconds = 0.5f; // delay before damage resolves (lets the swing anim play)

    // COMBAT WIP: the ability is resolved from the pressed slot + the EQUIPPED WEAPON (see Sanctuary.Game.
    // Combat.NinjaWeaponAbilities): slot 0 = common melee, slot 1 = the weapon's "of X" special. Damage /
    // swing animation 1099 / hit composite effect all come from that table.

    // COMBAT WIP: live animation probe. When set via "!anim <id>", EVERY ability key-press plays this
    // animation instead of the ability's own — so you can spam your ability keys (no chat flood) to find the
    // right per-ability move and see it replay in sequence. null = abilities use their own anim. "!anim 0"
    // (or "!anim" with no id) clears it.
    public static int? DebugAnimationOverride;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(AbilityPacketClientRequestStartAbilityHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!AbilityPacketClientRequestStartAbility.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}. ( Raw: {raw} )",
                nameof(AbilityPacketClientRequestStartAbility), Convert.ToHexString(data));
            return false;
        }

        // COMBAT WIP: capture the live client->server StartAbility fields so we can map
        // action-bar slots to abilities and implement real resolution. Remove/lower once mapped.
        _logger.LogInformation(
            "StartAbility: ActionBar.Id={id} Slot={slot} Target={target} Guid={guid} Pos=({px},{py},{pz},{pw}) Raw={raw}",
            packet.Data.Id, packet.Data.Slot, packet.Target, packet.Guid,
            packet.Position.X, packet.Position.Y, packet.Position.Z, packet.Position.W,
            Convert.ToHexString(data));

        var player = connection.Player;
        var zone = player.Zone;

        // Resolve the ability's target. When the player has the dummy selected the client sends its
        // guid; with nothing selected it sends the player's own guid. Fall back to the nearest live
        // hostile damageable NPC so testing works even without an explicit target selection.
        Npc? targetNpc = null;

        if (packet.Guid != 0 && zone.TryGetNpc(packet.Guid, out var selected) && selected.IsDamageable && selected.IsAlive)
            targetNpc = selected;
        else
            targetNpc = zone.Npcs.FirstOrDefault(n => n.IsHostile && n.IsDamageable && n.IsAlive);

        var targetGuid = targetNpc?.Guid ?? (packet.Guid != 0 ? packet.Guid : player.Guid);

        // Resolve the ability from the pressed slot + equipped weapon (slot 0 = melee, slot 1 = weapon special).
        var ability = NinjaWeaponAbilities.ResolveAbility(player, packet.Data.Slot);

        // COMBAT WIP: respond to an ability press with a real StartCasting (proven to render a cast bar
        // + play the caster's animation) instead of the AbilityPacketFailed stub.
        var startCasting = new AbilityPacketStartCasting
        {
            Unknown = player.Guid,            // caster
            Unknown2 = targetGuid,            // target
            CompositeEffectId = ability.CastEffectId, // FX on the caster during the cast (projectile/aura/ground-AoE)
            Animation = DebugAnimationOverride ?? ability.Animation, // override via !anim for live probing
            AbilityId = packet.Data.Slot + 1, // cast identifier (not visual-critical)
            ActionTime = CastSeconds,
            HasActionProgress = false,        // no cast/progress bar for a basic melee swing
        };

        connection.SendTunneled(startCasting);

        // COMBAT WIP: weapon-empowering specials (Mysticism / Mystical Blade) bind their FX to the SWORD
        // (item slot 7) instead of the body — the effect rides on the weapon. (SlotCompositeEffectOverride
        // op35/sub31: Guid + slot + composite effect.)
        if (ability.SwordEffectId > 0)
        {
            connection.SendTunneled(new PlayerUpdatePacketSlotCompositeEffectOverride
            {
                Guid = player.Guid,
                Slot = NinjaWeaponAbilities.WeaponSlot, // 7 = the equipped weapon
                CompositeEffect = ability.SwordEffectId,
            });
        }

        // COMBAT WIP: Shadow Army (any special with SummonCount>0) spawns temporary shadow-clone NPCs
        // around the caster (using the caster's model), then they poof away after a few seconds.
        if (ability.SummonCount > 0 && zone is StartingZone summonZone)
            summonZone.SummonShadowClones(player, ability.SummonCount, 12);

        if (targetNpc is null)
        {
            _logger.LogInformation("StartAbility: no damageable target found (slot {slot}).", packet.Data.Slot);
            return true;
        }

        _logger.LogInformation("Ability slot {slot} = '{name}' (dmg {dmg}, anim {anim}, fx {fx})",
            packet.Data.Slot, ability.Name, ability.Damage, ability.Animation, ability.EffectId);

        ResolveDamageAfterCast(player, targetNpc, ability.Damage, ability.EffectId,
            ability.CasterEndEffectId, ability.EnemyExtraEffectId);

        return true;
    }

    // World-combat state tracker: first hit sends the enter pair (sub132 + sub133 true); 6 seconds
    // after the LAST hit the decay loop sends the exit pair (false) — job/equipment changes unlock.
    private const int OutOfCombatSeconds = 6;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTime> _lastCombatHit = new();

    private static void EnterWorldCombat(Player player)
    {
        var alreadyFighting = _lastCombatHit.ContainsKey(player.Guid);
        _lastCombatHit[player.Guid] = DateTime.UtcNow;

        if (alreadyFighting)
            return;

        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = true });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = true });

        _ = Task.Run(async () =>
        {
            try
            {
                while (_lastCombatHit.TryGetValue(player.Guid, out var last))
                {
                    var remaining = TimeSpan.FromSeconds(OutOfCombatSeconds) - (DateTime.UtcNow - last);
                    if (remaining <= TimeSpan.Zero)
                        break;

                    await Task.Delay(remaining);
                }

                _lastCombatHit.TryRemove(player.Guid, out _);

                // The Frostfang arena owns its combat state for the whole encounter (its exit sequence
                // releases it) — don't let an overworld decay stomp it mid-fight.
                if (player.Zone is FrostfangArenaZone)
                    return;

                player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
                player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "World-combat decay loop failed.");
            }
        });
    }

    // COMBAT WIP: after the cast bar completes, apply damage to the target, play a hit effect, push its
    // updated health bar, and kill/respawn it at 0 HP. Runs off-thread so the cast time elapses first.
    private static void ResolveDamageAfterCast(Player player, Npc target, int damage, int effectId,
        int casterEndEffectId = 0, int enemyExtraEffectId = 0)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay((int)(CastSeconds * 1000));

                // Real-game behavior: landing a hit puts you in world-combat (sub132 SetInWorldCombat →
                // m_bIsFighting + NPC hp-bar mode, sub133 SetIsFighting → m_bInCombatArea). This is what
                // opens the client's floating-damage-number gate (BaseClient::sub_8BB0B0: CanUseAbilities
                // || IsFighting || ...). It also job-locks while fighting — released by the 6s decay below,
                // exactly like live FR's combat indicator.
                EnterWorldCombat(player);

                var killed = target.ApplyDamage(damage);

                // COMBAT WIP: FX that should land at the END of the animation (after the cast delay):
                //  - CasterEndEffectId plays on the CASTER's position/feet (e.g. Dragonstrike's land FX).
                //  - EnemyExtraEffectId plays an ADDITIONAL effect on the target on top of the hit FX
                //    (e.g. Soul Power's purple ring around the enemy).
                if (casterEndEffectId > 0)
                {
                    player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = player.Guid,
                        CompositeEffectId = casterEndEffectId,
                        Position = player.Position,
                    });
                }

                if (enemyExtraEffectId > 0)
                {
                    player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = target.Guid,
                        CompositeEffectId = enemyExtraEffectId,
                        Position = target.Position,
                    });
                }

                // The real combat packet — semantics PROVEN from the client struct reader (the first
                // two wire guids are one duplicated ATTACKER field; the third is the TARGET, who gets
                // the number/bar/recoil). See CombatPacketAttackProcessed header comment.
                var attackProcessed = new CombatPacketAttackProcessed
                {
                    AttackerGuid = player.Guid,
                    TargetGuid = target.Guid,
                    Damage = damage,
                    MaxHealth = target.MaxHealth,
                    CompositeEffectId = effectId,   // per-ability hit composite effect
                    CurrentHealth = target.Health,  // HP after the hit -> bar position
                };

                player.SendTunneled(attackProcessed);

                _logger.LogInformation(
                    "Ability hit {name} ({guid}) for {dmg} -> {hp}/{max} HP (killed={killed})",
                    target.Name, target.Guid, damage, target.Health, target.MaxHealth, killed);

                // Route the kill to the zone (IZone.OnNpcKilled): the starting zone resets its training
                // dummy; the Frostfang arena advances the encounter (pack -> Alpha -> victory + return).
                if (killed)
                    player.Zone.OnNpcKilled(player, target);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ability damage resolution failed.");
            }
        });
    }
}