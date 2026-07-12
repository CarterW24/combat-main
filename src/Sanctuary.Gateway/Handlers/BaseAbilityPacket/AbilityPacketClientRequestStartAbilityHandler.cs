using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class AbilityPacketClientRequestStartAbilityHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, DateTimeOffset>> _itemCooldowns = new();

    /// <summary>Animation id that returns a player to their normal standing idle after a boombox dance
    /// (sent via SetAnimation flags=1); confirmed in-game with /anim.</summary>
    private const int BoomboxIdleAnimId = 1;

    /// <summary>How long a boombox stays out and plays (and its use cooldown).</summary>
    private const int BoomboxDurationMs = 120_000;

    /// <summary>Use cooldown for food-effect consumables (matches the boombox duration).</summary>
    private const int FoodEffectCooldownMs = 120_000;

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

    /// <summary>Unique effect-tag ids for the lingering cast-FX plays (start high to stay clear of
    /// the zones' heal-shower tag range).</summary>
    private static int _castFxTagCounter = 5000;

    // COMBAT WIP: live animation probe. When set via "!anim <id>", EVERY ability key-press plays this
    // animation instead of the ability's own — so you can spam your ability keys (no chat flood) to find the
    // right per-ability move and see it replay in sequence. null = abilities use their own anim. "!anim 0"
    // (or "!anim" with no id) clears it.
    public static int? DebugAnimationOverride;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(AbilityPacketClientRequestStartAbilityHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!AbilityPacketClientRequestStartAbility.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}. ( Raw: {raw} )",
                nameof(AbilityPacketClientRequestStartAbility), Convert.ToHexString(data));
            return false;
        }

        _logger.LogInformation("AbilityPacket: Id={Id} Slot={Slot}", packet.Data.Id, packet.Data.Slot);

        // DEATH: no acting while knocked out (can't swing/shoot/use items until you respawn).
        if (connection.Player.IsDead)
            return true;

        // Item bar (id 2) = consumables (boombox / cake / transform food); any other bar = combat ability.
        if (packet.Data.Id == 2)
            return HandleItemAbility(connection, packet);

        return HandleCombatAbility(connection, packet, data);
    }

    private static bool HandleItemAbility(GatewayConnection connection, AbilityPacketClientRequestStartAbility packet)
    {
        connection.Player.ActionBars.TryGetValue(2, out var actionBar);

        if (actionBar is null || !actionBar.Slots.TryGetValue(packet.Data.Slot, out var foundSlot) || foundSlot.IsEmpty)
        {
            SendFailure(connection, 3079);
            return true;
        }

        if (!connection.Player.ActionBarItemGuids.TryGetValue(2, out var actionBarItems) ||
            !actionBarItems.TryGetValue(packet.Data.Slot, out var itemGuid))
        {
            SendFailure(connection, 3079);
            return true;
        }

        var clientItem = connection.Player.Items.FirstOrDefault(x => x.Id == itemGuid);
        if (clientItem is null)
        {
            SendFailure(connection, 3079);
            return true;
        }

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(clientItem.Definition, out var clientItemDefinition))
        {
            SendFailure(connection, 3079);
            return true;
        }

        if (clientItemDefinition.ActivatableAbilityId == 0)
        {
            SendFailure(connection, 3079);
            return true;
        }

        bool isBoombox = _resourceManager.Consumables.Boomboxes.ContainsKey(clientItemDefinition.Id);
        bool isCake = _resourceManager.Consumables.Cakes.TryGetValue(clientItemDefinition.Id, out var cakeDef);

        if (isCake || isBoombox)
        {
            var playerCooldowns = _itemCooldowns.GetOrAdd(connection.Player.Guid, _ => new ConcurrentDictionary<int, DateTimeOffset>());

            if (playerCooldowns.TryGetValue(clientItemDefinition.Id, out var expiry) && DateTimeOffset.UtcNow < expiry)
            {
                SendFailure(connection, 3079);
                return true;
            }

            if (isCake)
                SpawnCakeNpc(connection, cakeDef!);
            else
                SpawnBoomboxNpc(connection, clientItemDefinition);

            int cooldownMs = isCake ? cakeDef!.CooldownMs : BoomboxDurationMs;
            playerCooldowns[clientItemDefinition.Id] = DateTimeOffset.UtcNow.AddMilliseconds(cooldownMs);
            connection.Player.StartActionBarCooldown(2, packet.Data.Slot, clientItemDefinition.Icon.Id, clientItemDefinition.NameId, clientItem.Count, cooldownMs);

            return true;
        }

        _logger.LogInformation("HandleItemAbility: itemDef={ItemDefId} abilityId={AbilityId}", clientItemDefinition.Id, clientItemDefinition.ActivatableAbilityId);

        // Random-transform foods (e.g. Jack-O-Lantern) roll one of their listed transformations
        // instead of using the item's fixed ActivatableAbilityId.
        var transformAbilityId = clientItemDefinition.ActivatableAbilityId;

        if (_resourceManager.Consumables.RandomTransformFoods.TryGetValue(clientItemDefinition.Id, out var randomFood) && randomFood.TransformAbilityIds.Length > 0)
            transformAbilityId = randomFood.TransformAbilityIds[Random.Shared.Next(randomFood.TransformAbilityIds.Length)];

        if (_resourceManager.Consumables.Transformations.TryGetValue(transformAbilityId, out var transform))
        {
            _logger.LogInformation("Transform match: modelId={ModelId} durationMs={Duration}", transform.ModelId, transform.DurationMs);

            var playerCooldowns = _itemCooldowns.GetOrAdd(connection.Player.Guid, _ => new ConcurrentDictionary<int, DateTimeOffset>());

            if (playerCooldowns.TryGetValue(clientItemDefinition.Id, out var expiry) && DateTimeOffset.UtcNow < expiry)
            {
                SendFailure(connection, 3079);
                return true;
            }

            if (connection.Player.TemporaryAppearance != 0)
            {
                SendFailure(connection, 3079);
                return true;
            }

            ApplyTransform(connection, transform.ModelId, transform.DurationMs, transform.CompositeEffectId);

            playerCooldowns[clientItemDefinition.Id] = DateTimeOffset.UtcNow.AddMilliseconds(transform.CooldownMs);

            var capturedSlot = packet.Data.Slot;
            var capturedItemDef = clientItemDefinition;
            var capturedCount = clientItem.Count;
            var cooldownMs = transform.CooldownMs;

            bool willHaveItemLeft = capturedCount > 1;

            if (clientItemDefinition.SingleUse)
                ConsumeItem(connection, clientItem, clientItemDefinition, capturedSlot);

            if (willHaveItemLeft)
                connection.Player.StartActionBarCooldown(2, capturedSlot, capturedItemDef.Icon.Id, capturedItemDef.NameId, capturedCount - 1, cooldownMs);

            return true;
        }

        // Food-effect consumables share the boombox-style item cooldown so they can't be spammed.
        if (_resourceManager.Consumables.FoodEffects.ContainsKey(clientItemDefinition.ActivatableAbilityId))
        {
            var playerCooldowns = _itemCooldowns.GetOrAdd(connection.Player.Guid, _ => new ConcurrentDictionary<int, DateTimeOffset>());

            if (playerCooldowns.TryGetValue(clientItemDefinition.Id, out var expiry) && DateTimeOffset.UtcNow < expiry)
            {
                SendFailure(connection, 3079);
                return true;
            }

            playerCooldowns[clientItemDefinition.Id] = DateTimeOffset.UtcNow.AddMilliseconds(FoodEffectCooldownMs);

            TriggerAbilityEffect(connection, clientItemDefinition);

            var capturedSlot = packet.Data.Slot;
            var capturedCount = clientItem.Count;

            bool willHaveItemLeft = !clientItemDefinition.SingleUse || capturedCount > 1;

            if (clientItemDefinition.SingleUse)
                ConsumeItem(connection, clientItem, clientItemDefinition, capturedSlot);

            if (willHaveItemLeft)
                connection.Player.StartActionBarCooldown(2, capturedSlot, clientItemDefinition.Icon.Id, clientItemDefinition.NameId,
                    clientItemDefinition.SingleUse ? capturedCount - 1 : capturedCount, FoodEffectCooldownMs);

            return true;
        }

        TriggerAbilityEffect(connection, clientItemDefinition);

        if (clientItemDefinition.SingleUse)
            return ConsumeItem(connection, clientItem, clientItemDefinition, packet.Data.Slot);

        return true;
    }

    // COMBAT (combat branch): an ability-bar press — resolve the target + the equipped weapon's ability,
    // play the cast, then resolve damage. See NinjaWeaponAbilities for the slot -> ability mapping.
    private static bool HandleCombatAbility(GatewayConnection connection, AbilityPacketClientRequestStartAbility packet, ReadOnlySpan<byte> data)
    {
        // COMBAT WIP: capture the live client->server StartAbility fields so we can map
        // action-bar slots to abilities and implement real resolution. Remove/lower once mapped.
        _logger.LogInformation(
            "StartAbility: ActionBar.Id={id} Slot={slot} Target={target} Guid={guid} Pos=({px},{py},{pz},{pw}) Raw={raw}",
            packet.Data.Id, packet.Data.Slot, packet.Target, packet.Guid,
            packet.Position.X, packet.Position.Y, packet.Position.Z, packet.Position.W,
            Convert.ToHexString(data));

        var player = connection.Player;
        var zone = player.Zone;

        // Pressing an attack puts you in world-combat immediately (weapon drawn + the client's ranged
        // auto-fire loop), even before the shot resolves and even if it whiffs. This is what lets the bow
        // keep firing at the next enemy after a kill instead of going inert.
        player.EnterWorldCombat();

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
            // Horizontal (X/Z) radius, height ignored. MELEE = 7 units ≈ a few body-lengths (player
            // capsule ~1.9 tall; wolves bite from ~2.6). GROUND-CHECK (04-01 capture, 37 player->enemy
            // hits): real hit distances ran 0.6–9.2, median 2.3, mean 2.7 — the bulk ≤ ~4 (basic
            // swings), the 5–9 tail almost certainly the AoE special. 7 sits inside SOE's envelope:
            // forgiving of the 300ms tick lag without grabbing far wolves. Tune toward ~5 if it feels
            // grabby. ARCHERS shoot at range — their reach is the bow envelope (JobWeaponAbilities).
            var attackReach = JobWeaponAbilities.AutoTargetReach(player);
            var reach2 = attackReach * attackReach;
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

        // Resolve the ability from the pressed slot + equipped weapon for the ACTIVE JOB's kit
        // (slot 0 = basic attack/shot, slot 1 = the weapon's named special).
        var ability = JobWeaponAbilities.ResolveAbility(player, packet.Data.Slot);

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

        // ENERGY GATE (non-basic slots): each ability drains its own EnergyCost (weapon specials =
        // the live-decoded full 100 bar; the archer level abilities = 50 each). Can't afford it =>
        // drop the press (no cast) — matches the real server, which server-gates the special.
        if (!isBasicMelee)
        {
            var cost = ability.EnergyCost;
            var energy = GetEnergy(player);
            if (energy < cost)
            {
                _logger.LogInformation("StartAbility: ability blocked — energy {e}/{max} < {cost}.",
                    energy, MaxEnergy, cost);
                return true;
            }

            var remaining = energy - cost;
            _energy[player.Guid] = remaining;
            SendEnergy(player, remaining);   // op38/sub13: bar drops by the cost
            StartEnergyRegen(player);        // begin the +4/sec refill
        }

        // LINGERING cast FX (CastEffectStopMs > 0 — projectile trails and other loops that never
        // self-terminate): play via an effect TAG on the caster and remove it after the window, so
        // the trail flashes with the shot instead of snowing on the player forever. One-shot cast
        // FX keep riding StartCasting's CompositeEffectId as before.
        var startCastingFx = ability.CastEffectId;
        if (startCastingFx > 0 && ability.CastEffectStopMs > 0)
        {
            startCastingFx = 0;

            var tagId = System.Threading.Interlocked.Increment(ref _castFxTagCounter);
            player.SendTunneled(new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = tagId,
                CompositeEffectId = ability.CastEffectId,
                SourceGuid = player.Guid,
            });
            var stopMs = ability.CastEffectStopMs;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(stopMs);
                    player.SendTunneled(new PlayerUpdatePacketRemoveEffectTagCompositeEffect
                    {
                        Guid = player.Guid,
                        TagId = tagId,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lingering cast-FX stop failed.");
                }
            });
        }

        // COMBAT WIP: respond to an ability press with a real StartCasting (proven to render a cast bar
        // + play the caster's animation) instead of the AbilityPacketFailed stub.
        var startCasting = new AbilityPacketStartCasting
        {
            Unknown = player.Guid,            // caster
            Unknown2 = targetGuid,            // target
            CompositeEffectId = startCastingFx, // one-shot FX on the caster during the cast
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

    private static bool ConsumeItem(GatewayConnection connection, Packet.Common.ClientItem clientItem,
        Packet.Common.ClientItemDefinition clientItemDefinition, int actionBarSlot)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var dbItem = dbContext.Items.SingleOrDefault(i => i.CharacterId == characterId && i.Id == clientItem.Id);

        if (dbItem is null)
        {
            SendFailure(connection, 3079);
            return true;
        }

        dbItem.Count--;

        var shouldDeleteItem = dbItem.Count <= 0;

        if (shouldDeleteItem)
            dbContext.Items.Remove(dbItem);

        if (dbContext.SaveChanges() <= 0)
        {
            SendFailure(connection, 3079);
            return true;
        }

        if (shouldDeleteItem)
        {
            connection.Player.Items.Remove(clientItem);
            connection.SendTunneled(new ClientUpdatePacketItemDelete { ItemGuid = clientItem.Id });

            var slotPacket = new ClientUpdatePacketUpdateActionBarSlot { Data = { Id = 2, Slot = actionBarSlot } };
            slotPacket.Slot.IsEmpty = true;

            if (connection.Player.ActionBarItemGuids.TryGetValue(2, out var trackedItems))
                trackedItems.Remove(actionBarSlot);

            connection.SendTunneled(slotPacket);
        }
        else
        {
            clientItem.Count--;

            connection.SendTunneled(new ClientUpdatePacketItemUpdate
            {
                ItemGuid = clientItem.Id,
                Count = clientItem.Count,
                ConsumedCount = clientItem.ConsumedCount,
                AbilityCount = clientItem.AbilityCount,
                RentalExpirationTime = 0
            });

            var slotPacket = new ClientUpdatePacketUpdateActionBarSlot { Data = { Id = 2, Slot = actionBarSlot } };
            slotPacket.Slot.IsEmpty = false;
            slotPacket.Slot.IconId = clientItemDefinition.Icon.Id;
            slotPacket.Slot.NameId = clientItemDefinition.NameId;
            slotPacket.Slot.Unknown5 = 1;
            slotPacket.Slot.Unknown6 = 4;
            slotPacket.Slot.Unknown7 = 15;
            slotPacket.Slot.Enabled = true;
            slotPacket.Slot.Unknown10 = 1000;
            slotPacket.Slot.TotalRefreshTime = 1000;
            slotPacket.Slot.Quantity = clientItem.Count;
            slotPacket.Slot.ForceDismount = true;
            slotPacket.Slot.Unknown15 = 1000;

            connection.SendTunneled(slotPacket);
        }

        return true;
    }

    private static void TriggerAbilityEffect(GatewayConnection connection, Packet.Common.ClientItemDefinition clientItemDefinition)
    {
        if (clientItemDefinition.ActivatableAbilityId == 0)
            return;

        int abilityId = clientItemDefinition.ActivatableAbilityId;

        _resourceManager.Consumables.FoodEffects.TryGetValue(abilityId, out var foodEffect);

        int effectId = foodEffect?.CompositeEffectId ?? clientItemDefinition.CompositeEffectId;
        int quickChatId = foodEffect?.QuickChatId ?? 0;
        int effectDelayMs = foodEffect?.EffectDelayMs ?? 0;

        if (quickChatId != 0)
        {
            var animPacket = new QuickChatSendChatToChannelPacket
            {
                Id = quickChatId,
                Guid = connection.Player.Guid,
                Name = connection.Player.Name ?? new Packet.Common.NameData(),
                Channel = Packet.Common.Chat.ChatChannel.WorldArea,
                AreaNameId = 0,
                GuildGuid = 0
            };
            connection.Player.SendTunneledToVisible(animPacket, true);
        }
        else
        {
            connection.Player.SendTunneledToVisible(new AbilityPacketExecuteClientLua { Script = string.Empty, Param1 = 0, Param2 = 0, Param3 = 0 }, true);
        }

        if (effectId != 0)
        {
            var effectPacket = new PlayerUpdatePacketPlayCompositeEffect { Guid = connection.Player.Guid, CompositeEffectId = effectId, Clear = true };

            if (effectDelayMs > 0)
                Task.Delay(effectDelayMs).ContinueWith(_ => connection.Player.SendTunneledToVisible(effectPacket, true));
            else
                connection.Player.SendTunneledToVisible(effectPacket, true);
        }
    }

    private static void SpawnCakeNpc(GatewayConnection connection, CakeItemDefinition cakeDef)
    {
        try
        {
            var zone = connection.Player.Zone;
            if (zone is not Game.Zones.StartingZone startingZone)
                return;

            if (!startingZone.TryCreateNpc(out var cakeNpc))
                return;

            cakeNpc.NameId = cakeDef.NameId;
            cakeNpc.ModelId = cakeDef.ModelId;
            cakeNpc.TextureAlias = "";
            cakeNpc.TintAlias = "";
            cakeNpc.Scale = 1.0f;
            cakeNpc.Animation = cakeDef.Animation;
            cakeNpc.HideNamePlate = false;
            cakeNpc.IsInteractable = true;
            cakeNpc.CursorId = (byte)cakeDef.CursorId;

            var forwardDirection = Vector3.Transform(new Vector3(0, 0, 1), connection.Player.Rotation);
            var spawnPosition = new Vector4(
                connection.Player.Position.X + forwardDirection.X * 1.5f,
                connection.Player.Position.Y + forwardDirection.Y * 1.5f,
                connection.Player.Position.Z + forwardDirection.Z * 1.5f,
                connection.Player.Position.W
            );

            cakeNpc.Visible = true;
            cakeNpc.UpdatePosition(spawnPosition, connection.Player.Rotation);

            if (cakeDef.Type == CakeItemType.BossCake)
            {
                cakeNpc.InteractAction = (interactingPlayer) =>
                {
                    int abilityId = cakeDef.TransformAbilityIds[Random.Shared.Next(cakeDef.TransformAbilityIds.Length)];
                    if (_resourceManager.Consumables.Transformations.TryGetValue(abilityId, out var transform))
                        ApplyTransform(interactingPlayer, transform.ModelId, transform.DurationMs, transform.CompositeEffectId);
                };
            }
            else
            {
                var scareActive = false;
                cakeNpc.InteractAction = (interactingPlayer) =>
                {
                    if (scareActive) return;
                    scareActive = true;

                    var cakePosition = cakeNpc.Position;

                    void Broadcast(ISerializablePacket pkt)
                    {
                        interactingPlayer.SendTunneled(pkt);
                        foreach (var p in interactingPlayer.VisiblePlayers.Values)
                            p.SendTunneled(pkt);
                    }

                    void SendEffect(int effectId) => Broadcast(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = cakeNpc.Guid,
                        CompositeEffectId = effectId,
                        Position = cakePosition,
                        Clear = true
                    });

                    // Roll across all outcomes: each scare group and each transform is equally likely.
                    var roll = Random.Shared.Next(cakeDef.ScareGroups.Length + cakeDef.TransformAbilityIds.Length);

                    if (roll < cakeDef.ScareGroups.Length)
                    {
                        foreach (var effectId in cakeDef.ScareGroups[roll])
                            SendEffect(effectId);
                    }
                    else
                    {
                        var abilityId = cakeDef.TransformAbilityIds[roll - cakeDef.ScareGroups.Length];
                        if (_resourceManager.Consumables.Transformations.TryGetValue(abilityId, out var scareTransform))
                            ApplyTransform(interactingPlayer, scareTransform.ModelId, scareTransform.DurationMs, scareTransform.CompositeEffectId);
                    }

                    Task.Delay(cakeDef.ScareCooldownMs).ContinueWith(_ => { scareActive = false; });
                };
            }

            var poofEffect = new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = cakeNpc.Guid,
                CompositeEffectId = cakeDef.SpawnPoofEffectId,
                Position = spawnPosition,
                Clear = false
            };

            connection.Player.SendTunneled(poofEffect);
            connection.Player.OnAddVisibleNpcs([cakeNpc]);
            foreach (var player in connection.Player.VisiblePlayers.Values)
            {
                player.SendTunneled(poofEffect);
                player.OnAddVisibleNpcs([cakeNpc]);
            }

            var capturedNpc = cakeNpc;

            Task.Delay(cakeDef.LifetimeMs).ContinueWith(_ =>
            {
                try
                {
                    var removePacket = new PlayerUpdatePacketRemovePlayerGracefully
                    {
                        Guid = capturedNpc.Guid,
                        Animate = false,
                        Delay = 0,
                        EffectDelay = 0,
                        CompositeEffectId = cakeDef.SpawnPoofEffectId,
                        Duration = 500
                    };

                    foreach (var player in startingZone.Players ?? [])
                        player.SendTunneled(removePacket);

                    capturedNpc.Dispose();
                }
                catch (Exception ex) { _logger.LogError(ex, "SpawnCakeNpc: error during NPC cleanup"); }
            });
        }
        catch (Exception ex) { _logger.LogError(ex, "SpawnCakeNpc: error during NPC spawn"); }
    }

    private static void SpawnBoomboxNpc(GatewayConnection connection, Packet.Common.ClientItemDefinition itemDef)
    {
        try
        {
            var zone = connection.Player.Zone;
            if (zone is not Game.Zones.StartingZone startingZone)
                return;

            if (!startingZone.TryCreateNpc(out var boomboxNpc))
                return;

            _resourceManager.Consumables.Boomboxes.TryGetValue(itemDef.Id, out var boomboxDef);
            int modelId = boomboxDef?.ModelId ?? 1062;
            int effectId = boomboxDef?.EffectId ?? 0;
            int[] danceSequence = boomboxDef?.DanceSequence ?? [3501, 3502, 3503, 3504, 3505];

            boomboxNpc.NameId = 0;
            boomboxNpc.ModelId = modelId;
            boomboxNpc.Name = "Boombox";
            boomboxNpc.TextureAlias = itemDef.TextureAlias ?? "";
            boomboxNpc.TintAlias = itemDef.TintAlias ?? "";
            boomboxNpc.Scale = 1.0f;
            boomboxNpc.Animation = 2100; // Bouncing animation
            boomboxNpc.CompositeEffectId = effectId; // Owned by the entity — client stops it when RemovePlayer is received
            boomboxNpc.HideNamePlate = true;
            boomboxNpc.IsInteractable = false;

            var leftDirection = Vector3.Transform(new Vector3(-1, 0, 0), connection.Player.Rotation);
            var spawnPosition = new Vector4(
                connection.Player.Position.X + leftDirection.X * 2.0f,
                connection.Player.Position.Y + leftDirection.Y * 2.0f,
                connection.Player.Position.Z + leftDirection.Z * 2.0f,
                connection.Player.Position.W
            );

            // Visible must be set before UpdatePosition so UpdateZoneTile() fires
            // and the zone tile system sends AddNpc to all players in range.
            boomboxNpc.Visible = true;
            boomboxNpc.UpdatePosition(spawnPosition, connection.Player.Rotation);

            var poofEffect = new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = boomboxNpc.Guid,
                CompositeEffectId = 21, // PFX_smoke_black_explosion
                Position = spawnPosition,
                Clear = false
            };

            // Zone tile system sent AddNpc to boomboxNpc.VisiblePlayers via OnAddVisibleNpcs.
            // Send poof after AddNpc so the entity exists on the client when the effect plays.
            var pktRecipients = boomboxNpc.VisiblePlayers.Values.ToList();
            if (!boomboxNpc.VisiblePlayers.ContainsKey(connection.Player.Guid))
            {
                // Spawner is outside zone tile range — send packets manually.
                connection.Player.SendTunneled(boomboxNpc.GetAddNpcPacket());
                pktRecipients.Insert(0, connection.Player);
            }

            foreach (var player in pktRecipients)
                player.SendTunneled(poofEffect);

            var capturedNpc = boomboxNpc;

            const float BoomboxRangeInMeters = 15.0f;
            var danceCenter = new Vector3(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);

            // Reset a single player to the normal standing idle (used when they leave range / it ends).
            static void ResetPlayer(Game.Entities.Player p) =>
                p.SendTunneledToVisible(new PlayerUpdatePacketSetAnimation
                {
                    Guid = p.Guid,
                    AnimationId = BoomboxIdleAnimId,
                    PlayType = 1 // bit0 = set as the entity's base/idle anim (ends the looping dance)
                }, sendToSelf: true);

            Task.Run(async () =>
            {
                // Players currently dancing to this boombox (so we can reset them on leave / end).
                var dancing = new HashSet<ulong>();
                try
                {
                    // Poll the crowd every TickMs so anyone who walks up starts dancing within ~1s, but only
                    // rotate to the next dance every SwitchMs. A dance is selected and played on the very first
                    // tick (no startup delay). On a rotation the whole crowd is re-synced (phase-locked);
                    // newcomers between rotations are started on the current dance without restarting everyone
                    // already dancing. The loop runs the full BoomboxDurationMs, and dances loop, so the crowd
                    // keeps dancing until the boombox despawns.
                    const int TickMs = 1000;
                    const int SwitchMs = 4000;
                    int sequenceIndex = 0;
                    int previousAnim = -1;
                    int currentAnim = 3501;
                    int sinceSwitch = SwitchMs; // >= SwitchMs so a dance is selected and played on the first tick

                    // Start `animId` in sync on `targets`, delivered to them + anyone who has one of them visible.
                    static void SyncDance(List<Game.Entities.Player> targets, int animId)
                    {
                        if (targets.Count == 0)
                            return;

                        var sync = new PlayerUpdatePacketSetSynchronizedAnimations();
                        foreach (var p in targets)
                            sync.Animations.Add(new PlayerUpdatePacketSetSynchronizedAnimations.Animation { Guid = p.Guid, AnimationId = animId });

                        var recipients = new HashSet<Game.Entities.Player>(targets);
                        foreach (var p in targets)
                            foreach (var vp in p.VisiblePlayers.Values)
                                recipients.Add(vp);

                        foreach (var r in recipients)
                        {
                            try { r.SendTunneled(sync); } catch (Exception ex) { _logger.LogError(ex, "SpawnBoomboxNpc: sync send failed for {Guid}", r.Guid); }
                        }
                    }

                    for (int elapsed = 0; elapsed < BoomboxDurationMs; elapsed += TickMs)
                    {
                        // Advance the dance rotation when due (and select the first dance on the first tick).
                        // Only flag a change when the id actually differs, so single-dance boomboxes don't
                        // re-blast (and hitch) the crowd every SwitchMs.
                        bool animChanged = false;
                        if (sinceSwitch >= SwitchMs)
                        {
                            int selected = danceSequence.Length > 0 ? danceSequence[sequenceIndex % danceSequence.Length] : 3501;
                            sequenceIndex++;
                            sinceSwitch = 0;
                            if (selected != previousAnim)
                            {
                                currentAnim = selected;
                                previousAnim = selected;
                                animChanged = true;
                            }
                        }

                        var players = startingZone.Players ?? [];
                        var inRange = players.Where(p =>
                            Vector3.Distance(new Vector3(p.Position.X, p.Position.Y, p.Position.Z), danceCenter) <= BoomboxRangeInMeters)
                            .ToList();
                        var inRangeGuids = inRange.Select(p => p.Guid).ToHashSet();

                        // Reset players who just left range.
                        foreach (var p in players.Where(p => dancing.Contains(p.Guid) && !inRangeGuids.Contains(p.Guid)).ToList())
                        {
                            try { ResetPlayer(p); } catch (Exception ex) { _logger.LogError(ex, "SpawnBoomboxNpc: reset failed for {Guid}", p.Guid); }
                        }

                        var newcomers = inRange.Where(p => !dancing.Contains(p.Guid)).ToList();
                        dancing = inRangeGuids;

                        // On a rotation, re-sync the whole crowd (phase-lock). Otherwise just start any late
                        // arrivals on the current dance so players already dancing don't hitch.
                        if (animChanged)
                            SyncDance(inRange, currentAnim);
                        else if (newcomers.Count > 0)
                            SyncDance(newcomers, currentAnim);

                        await Task.Delay(TickMs);
                        sinceSwitch += TickMs;
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "SpawnBoomboxNpc: unhandled error in dance loop"); }
                finally
                {
                    // Stop everyone still dancing when the boombox's dance ends.
                    foreach (var p in (startingZone.Players ?? []).Where(p => dancing.Contains(p.Guid)).ToList())
                    {
                        try { ResetPlayer(p); } catch { /* player may have disconnected */ }
                    }
                }
            });

            Task.Delay(BoomboxDurationMs).ContinueWith(_ =>
            {
                try
                {
                    // CompositeEffectId=21 plays the despawn poof on the way out.
                    var removePacket = new PlayerUpdatePacketRemovePlayerGracefully
                    {
                        Guid = capturedNpc.Guid,
                        Animate = false,
                        Delay = 0,
                        EffectDelay = 0,
                        CompositeEffectId = 21, // PFX_smoke_black_explosion despawn poof
                        Duration = 500
                    };

                    foreach (var player in startingZone.Players ?? [])
                        player.SendTunneled(removePacket);

                    capturedNpc.Dispose();
                }
                catch (Exception ex) { _logger.LogError(ex, "SpawnBoomboxNpc: error during NPC cleanup"); }
            });
        }
        catch (Exception ex) { _logger.LogError(ex, "SpawnBoomboxNpc: error during NPC spawn"); }
    }

    private static void SendFailure(GatewayConnection connection, int stringId)
        => connection.SendTunneled(new AbilityPacketFailed { StringId = stringId });

    internal static void ApplyTransform(GatewayConnection connection, int temporaryAppearance, int durationMs, int effectId = 0)
        => connection.Player.ApplyTemporaryAppearance(temporaryAppearance, durationMs, effectId);

    internal static void ApplyTransform(Game.Entities.Player player, int temporaryAppearance, int durationMs, int effectId = 0)
        => player.ApplyTemporaryAppearance(temporaryAppearance, durationMs, effectId);

    internal static void RemoveTransform(GatewayConnection connection)
        => connection.Player.RemoveTemporaryAppearance();

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
                // || IsFighting || ...). It also job-locks while fighting — released by the decay, exactly
                // like live FR's combat indicator. (Player owns the state machine so getting HIT enters it too.)
                player.EnterWorldCombat();

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

                    // IMPACT FX on the victim (the ability's EffectId — the explosive-arrow burst, the
                    // lightning strike, the basic-hit flash...). AttackProcessed used to carry this in
                    // its CompositeEffectId; the 2026-07-03 switch to HitPointModification (no effect
                    // field) silently dropped EVERY impact effect — play it explicitly instead.
                    if (effectId > 0)
                    {
                        player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = target.Guid,
                            CompositeEffectId = effectId,
                            Position = target.Position,
                        });
                    }

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
