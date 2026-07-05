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

    // Per the real 2014 capture (Packet-Protocol_Dump), the player fired the basic attack as fast as
    // 0.17s apart (44 of 97 presses under 0.5s) — it is essentially spammable, NO real cooldown. The
    // StartCasting ActionTime is what the client uses to lock the action-bar slot, so the basic melee
    // gets a tiny window and the specials keep a short wind-up.
    private const float MeleeActionTime = 0.15f;   // slot 0 basic attack — spammable, matches live cadence
    private const float SpecialActionTime = 0.4f;  // slot 1 named special — a real wind-up
    private const float MeleeDamageDelay = 0.15f;  // number pops as the fast swing lands
    private const float SpecialDamageDelay = 0.4f; // number pops at the end of the special's animation

    // ATTACK CADENCE (2026-07-03): the basic attack must resolve ONE swing per ANIMATION, not one per
    // key-press — the client can send StartAbility faster than the swing clip plays, so un-paced it pops a
    // damage number on every click. We removed the client melee-timer cooldown (that was the AttackProcessed
    // bug), so the pacing is now SERVER-side: gate basic-attack resolution to the swing cadence per player.
    // Presses inside the window are ignored (no cast, no number) so the animation plays fully and one number
    // lands per swing. VALUE = GROUND TRUTH: measured from the 2014-04-01 capture, the real server's
    // consecutive single-target player->enemy HitPointModification packets land ~0.66s apart (median 0.662s;
    // sub-0.1s bursts are AoE specials hitting the pack at once, excluded). Specials stay ungated here (their
    // rate is the energy/cost system, handled separately).
    private const int BasicSwingMs = 660;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, long> _nextBasicSwingTicks = new();

    // ENERGY / MANA (2026-07-03, SOLVED from the 2014-04-01 capture — player op38/sub13 timeline):
    //   * max = 100.
    //   * the special (slot 1) costs the WHOLE bar: energy went 100 -> 0 (delta -100) exactly when the
    //     special's AoE landed (23:21:31). So SpecialEnergyCost = 100.
    //   * regen = +4 per second, TIME-based (energy climbed 0 -> 100 in a steady +4/1s trickle over 25s,
    //     during AND after combat — NO kill-based chunks). Full recharge = 25s.
    // We report the player's energy on the same op38/sub13 (ClientUpdatePacketMana) the real server used.
    // The basic attack (slot 0) costs no energy; only slot-1 specials are gated.
    private const int MaxEnergy = 100;
    private const int SpecialEnergyCost = NinjaWeaponAbilities.SpecialEnergyCost; // 100 — shared with the toolbar's slot ManaCost (client grey-out)
    // AUTHENTIC live value = 4 (25s full refill, measured from the 04-01 capture — see the comment
    // block above). During testing this was temporarily cranked to 50 (~2s refill) so repeated
    // encounter runs weren't a slog; restored to 4 for the committed branch. Bump it back up locally
    // if you want faster energy while iterating.
    private const int EnergyRegenPerSec = 4;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, int> _energy = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, bool> _regenRunning = new();

    private static int GetEnergy(Player player) => _energy.TryGetValue(player.Guid, out var e) ? e : MaxEnergy;

    private static void SendEnergy(Player player, int energy) =>
        player.SendTunneled(new ClientUpdatePacketMana { CurrentMana = energy, MaxMana = MaxEnergy });

    // Time-based +4/sec regen loop, running only while the player's energy is below max (mirrors the real
    // server, which only streamed op38/sub13 while the bar was refilling).
    private static void StartEnergyRegen(Player player)
    {
        if (!_regenRunning.TryAdd(player.Guid, true))
            return; // already regenerating

        _ = Task.Run(async () =>
        {
            try
            {
                while (GetEnergy(player) < MaxEnergy)
                {
                    await Task.Delay(1000);
                    var next = Math.Min(MaxEnergy, GetEnergy(player) + EnergyRegenPerSec);
                    _energy[player.Guid] = next;
                    SendEnergy(player, next);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Energy regen loop failed.");
            }
            finally
            {
                _regenRunning.TryRemove(player.Guid, out _);
            }
        });
    }

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

        // Resolve the ability's target. When the player has an enemy SELECTED the client sends its
        // guid — always honor that. With nothing selected, swing at what the player is actually
        // FACING: the nearest live hostile within melee reach inside a forward cone. Nothing there =
        // the swing whiffs (StartCasting still plays, no damage) — real-game feel. (The old fallback
        // was zone.Npcs.FirstOrDefault(...): literally the first hostile in the zone LIST, anywhere —
        // the "I swing and a random wolf across the arena takes the hit" bug.)
        Npc? targetNpc = null;

        if (packet.Guid != 0 && zone.TryGetNpc(packet.Guid, out var selected) && selected.IsDamageable && selected.IsAlive)
        {
            targetNpc = selected;
        }
        else
        {
            // AUTO-TARGET for an unselected swing = the NEAREST live hostile within melee range. This
            // reconstructs what the real SOE server did: the client attacked with NO enemy selected
            // (04-01 capture: Target=0, Guid=self) and the SERVER chose the target — that logic is lost,
            // and "nearest in range" is the natural, predictable reconstruction. The range cap is what
            // stops the old "swing → a random wolf across the arena gets hit" bug; picking the closest
            // (not the first-in-list) makes it hit the wolf that's actually on you. No facing cone: the
            // client only sends the player's facing while MOVING, so a cone whiffs when you stand still
            // to fight the swarm (that was the "spotty" hit detection).
            // Horizontal (X/Z) radius, height ignored. 7 units ≈ a few body-lengths (player capsule
            // ~1.9 tall; wolves bite from ~2.6). GROUND-CHECK (04-01 capture, 37 player->enemy hits):
            // real hit distances ran 0.6–9.2, median 2.3, mean 2.7 — the bulk ≤ ~4 (basic swings), the
            // 5–9 tail almost certainly the AoE special. 7 sits inside SOE's envelope: forgiving of the
            // 300ms tick lag without grabbing far wolves. Tune toward ~5 if it feels grabby.
            const float meleeReach = 7f;
            var reach2 = meleeReach * meleeReach;
            var best2 = reach2;

            foreach (var n in zone.Npcs)
            {
                if (!n.IsHostile || !n.IsDamageable || !n.IsAlive)
                    continue;

                var dx = n.Position.X - player.Position.X;
                var dz = n.Position.Z - player.Position.Z;
                var d2 = dx * dx + dz * dz;
                if (d2 >= best2)
                    continue;

                best2 = d2;
                targetNpc = n;
            }
        }

        var targetGuid = targetNpc?.Guid ?? (packet.Guid != 0 ? packet.Guid : player.Guid);

        // Resolve the ability from the pressed slot + equipped weapon (slot 0 = melee, slot 1 = weapon special).
        var ability = NinjaWeaponAbilities.ResolveAbility(player, packet.Data.Slot);

        // Basic attack (slot 0) is fast/spammable; specials wind up. This controls both the client-side
        // slot lock (StartCasting.ActionTime) and when the damage number resolves.
        var isBasicMelee = packet.Data.Slot <= 0;
        var actionTime = isBasicMelee ? MeleeActionTime : SpecialActionTime;
        var damageDelay = isBasicMelee ? MeleeDamageDelay : SpecialDamageDelay;

        // Pace the basic attack to the swing animation: drop presses that arrive before the current swing
        // finishes so we get one swing + one damage number per animation, not one per key-press.
        if (isBasicMelee && BasicSwingMs > 0)
        {
            var now = Environment.TickCount64;
            if (_nextBasicSwingTicks.TryGetValue(player.Guid, out var next) && now < next)
                return true; // still mid-swing — ignore this extra click (no cast, no number)
            _nextBasicSwingTicks[player.Guid] = now + BasicSwingMs;
        }

        // ENERGY GATE (specials only): the slot-1 special costs the full 100 bar. If the player can't
        // afford it, drop the press (no cast) — matches the real server, which server-gates the special.
        if (!isBasicMelee)
        {
            var energy = GetEnergy(player);
            if (energy < SpecialEnergyCost)
            {
                _logger.LogInformation("StartAbility: special blocked — energy {e}/{max} < {cost}.",
                    energy, MaxEnergy, SpecialEnergyCost);
                return true;
            }

            var remaining = energy - SpecialEnergyCost;
            _energy[player.Guid] = remaining;
            SendEnergy(player, remaining);   // op38/sub13: bar drops to 0
            StartEnergyRegen(player);        // begin the +4/sec refill
        }

        // COMBAT WIP: respond to an ability press with a real StartCasting (proven to render a cast bar
        // + play the caster's animation) instead of the AbilityPacketFailed stub.
        var startCasting = new AbilityPacketStartCasting
        {
            Unknown = player.Guid,            // caster
            Unknown2 = targetGuid,            // target
            CompositeEffectId = ability.CastEffectId, // FX on the caster during the cast (projectile/aura/ground-AoE)
            Animation = DebugAnimationOverride ?? ability.Animation, // override via !anim for live probing
            AbilityId = packet.Data.Slot + 1, // cast identifier (not visual-critical)
            ActionTime = actionTime,
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

        // AOE specials (AoeRadius > 0) hit EVERY live hostile within the radius of the CASTER — the whole
        // pack, not just the selected target. Single-target abilities keep the resolved target.
        System.Collections.Generic.List<Npc> targets;
        if (ability.AoeRadius > 0)
        {
            var r2 = ability.AoeRadius * ability.AoeRadius;
            var c = player.Position;
            targets = zone.Npcs
                .Where(n => n.IsHostile && n.IsDamageable && n.IsAlive)
                .Where(n =>
                {
                    var dx = n.Position.X - c.X;
                    var dz = n.Position.Z - c.Z;
                    return dx * dx + dz * dz <= r2;
                })
                .ToList();
        }
        else
        {
            targets = targetNpc is null ? [] : [targetNpc];
        }

        if (targets.Count == 0)
        {
            _logger.LogInformation("StartAbility: no damageable target found (slot {slot}, aoe {radius}).",
                packet.Data.Slot, ability.AoeRadius);
            return true;
        }

        _logger.LogInformation("Ability slot {slot} = '{name}' (dmg {dmg}, anim {anim}, fx {fx}, targets {count})",
            packet.Data.Slot, ability.Name, ability.Damage, ability.Animation, ability.EffectId, targets.Count);

        ResolveDamageAfterCast(player, targets, ability.Damage, ability.EffectId, damageDelay,
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

    // COMBAT WIP: after the cast bar completes, apply damage to the target(s), play a hit effect, push
    // each updated health bar, and kill/respawn at 0 HP. Runs off-thread so the cast time elapses first.
    // AOE specials pass the whole in-radius pack — one HitPointModification per victim in a burst, which
    // is exactly how the real server's AoE reads in the 04-01 capture.
    private static void ResolveDamageAfterCast(Player player, System.Collections.Generic.IReadOnlyList<Npc> targets,
        int damage, int effectId, float damageDelay, int casterEndEffectId = 0, int enemyExtraEffectId = 0)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay((int)(damageDelay * 1000));

                // Real-game behavior: landing a hit puts you in world-combat (sub132 SetInWorldCombat →
                // m_bIsFighting + NPC hp-bar mode, sub133 SetIsFighting → m_bInCombatArea). This is what
                // opens the client's floating-damage-number gate (BaseClient::sub_8BB0B0: CanUseAbilities
                // || IsFighting || ...). It also job-locks while fighting — released by the 6s decay below,
                // exactly like live FR's combat indicator.
                EnterWorldCombat(player);

                // Caster-side end FX plays ONCE regardless of how many victims (e.g. Dragonstrike's land FX).
                if (casterEndEffectId > 0)
                {
                    player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = player.Guid,
                        CompositeEffectId = casterEndEffectId,
                        Position = player.Position,
                    });
                }

                foreach (var target in targets)
                {
                    if (!target.IsAlive)
                        continue; // e.g. died to an earlier hit this same tick

                    var killed = target.ApplyDamage(damage);

                    // EnemyExtraEffectId plays an ADDITIONAL effect on each victim on top of the hit FX
                    // (e.g. Soul Power's purple ring around the enemy).
                    if (enemyExtraEffectId > 0)
                    {
                        player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = target.Guid,
                            CompositeEffectId = enemyExtraEffectId,
                            Position = target.Position,
                        });
                    }

                    // COOLDOWN FIX (2026-07-03, ground-truthed against the 04-01 capture): the real server
                    // dealt the PLAYER's own hits via HitPointModification (op35/35), NOT AttackProcessed.
                    // AttackProcessed's handler (CombatProcessor::sub_A2BA40) resets the action-bar melee
                    // timer whenever the attacker == local player -> SetTimer(slot0, MELEEATTACKINTERVALMS
                    // default 1000ms), which is the [1] cooldown the user saw. HitPointModification produces
                    // the floating number + health bar + recoil and NEVER touches the action-bar timer.
                    //   Real wire order (04-01): Guid=SOURCE(player), Guid2=VICTIM(enemy), leading bool=01,
                    //   i2=maxHP, i3=curHP-after, i4=-damage (the delta = the floating number).
                    player.SendTunneled(new PlayerUpdatePacketHitPointModification
                    {
                        Guid = player.Guid,           // source / attacker
                        Guid2 = target.Guid,          // victim
                        Unknown = true,               // player->NPC sample had the leading bool = 01
                        Unknown2 = target.MaxHealth,  // max HP (bar denominator)
                        Unknown3 = target.Health,     // current HP AFTER the hit (bar position)
                        Unknown4 = -damage,           // delta = -damage -> the floating number
                    });

                    _logger.LogInformation(
                        "Ability hit {name} ({guid}) for {dmg} -> {hp}/{max} HP (killed={killed})",
                        target.Name, target.Guid, damage, target.Health, target.MaxHealth, killed);

                    // Route the kill to the zone (IZone.OnNpcKilled): the starting zone resets its training
                    // dummy; the Frostfang arena advances the encounter (pack -> Alpha -> victory + return).
                    // Non-fatal hits go to OnNpcDamaged so the zone can react to HP thresholds (the Alpha
                    // flees at low health instead of dying).
                    if (killed)
                        player.Zone.OnNpcKilled(player, target);
                    else
                        player.Zone.OnNpcDamaged(player, target);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ability damage resolution failed.");
            }
        });
    }
}