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

        // COMBAT WIP: combat/"fighting" state is what opens the client gate for floating damage numbers,
        // but it also LOCKS job/equipment changes. Auto-entering it on attack is disabled for now so we can
        // freely swap gear/jobs while building the ability system. Use "!fight 1" to turn numbers on,
        // "!fight 0" to unlock changes again. (Re-enable here later once cooldowns/combat flow are in.)

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

                // The real combat packet: one message drives the target's health bar, the hit composite
                // effect, and the recoil animation (CONFIRMED live). The floating damage NUMBER it also
                // contains is gated client-side behind combat/"fighting" state (sub_8BB0B0) — TODO: set
                // that state so numbers show. Bar/effect/recoil work without it.
                var attackProcessed = new CombatPacketAttackProcessed
                {
                    Guid1 = player.Guid,            // attacker
                    Guid2 = target.Guid,            // target
                    Guid3 = target.Guid,
                    Int1 = damage,
                    Int2 = target.MaxHealth,        // max HP (bar %)
                    Int3 = effectId,                // per-ability hit composite effect
                    Bool1 = false,
                    Bool2 = false,
                    Int4 = 0,
                    Int5 = target.MaxHealth,        // fallback start HP (used when client HP unknown)
                };

                player.SendTunneled(attackProcessed);

                _logger.LogInformation(
                    "Ability hit {name} ({guid}) for {dmg} -> {hp}/{max} HP (killed={killed})",
                    target.Name, target.Guid, damage, target.Health, target.MaxHealth, killed);

                // Route the kill to the zone: training dummy resets; Frostfang arena wolves die/despawn
                // and drive the encounter (pack cleared -> Alpha spawns; Alpha dead -> victory + return).
                if (killed && player.Zone is StartingZone startingZone)
                    startingZone.OnNpcKilled(player, target);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ability damage resolution failed.");
            }
        });
    }
}