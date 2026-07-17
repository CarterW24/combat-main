using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

/// <summary>The wiki's live status-effect roster (Sleep, Silence, Stun, Root, Fear, Confuse, Freeze,
/// Poison, Berserk). Knockout stays its own system (the KO/respawn flow in FrostfangArenaZone).</summary>
public enum StatusEffectKind
{
    Stun,
    Sleep,
    Silence,
    Root,
    Fear,
    Confuse,
    Freeze,
    Berserk,
    Poison,
}

/// <summary>Timed status effects on any character (player or NPC), driving all three client channels:
///  1. CharacterState flags (op35/20) — the client gates ability presses with its own built-in
///     messages (NoCastStunned/Asleep/Silenced/Afraid/Frozen...) and halts the movement controller
///     for Stun/Sleep/Root/Freeze. Silence blocks the ability bars only — items stay usable.
///  2. A buff-bar effect tag (op38/16, players only) — icon + label + duration pie; the tag itself
///     also carries the looping body FX (verified live: the heart's ×1.33 tag shipped its heal
///     shower in the tag's composite field).
///  3. AddEffectTagCompositeEffect (op35/41, broadcast) so OTHER players see the loop too.
/// Expiry reverts all three. Poison has no state flag — it is a damage-over-time tick (NPC targets;
/// on players it is presentation-only until zones expose a generic HP pool).
/// Server-side enforcement: zones' AI should check IsImmobilized(); the StartAbility handler checks
/// BlocksAbilities()/IsSilenced().</summary>
public static class StatusEffects
{
    private record Meta(CharacterStatus Flag, int IconId, uint NameId, int FxId);

    // Icons = the client's dedicated status_* imageset family; labels = locale ids (write as uint —
    // verified renderable); FX = the client's dedicated status composites
    // (ActorCompositeEffectDefinitions.xml).
    private static readonly Dictionary<StatusEffectKind, Meta> _meta = new()
    {
        [StatusEffectKind.Stun] = new(CharacterStatus.IsStunned, 1137, 2454861836, 2),        // stars loop
        [StatusEffectKind.Sleep] = new(CharacterStatus.IsAsleep, 1133, 2207336499, 3),        // Zzz loop
        [StatusEffectKind.Silence] = new(CharacterStatus.IsSilenced, 1132, 2633742197, 14),   // shush puff
        [StatusEffectKind.Root] = new(CharacterStatus.IsRooted, 1131, 1827650078, 15734),     // vines
        [StatusEffectKind.Fear] = new(CharacterStatus.IsAfraid, 1123, 4014129776, 0),
        [StatusEffectKind.Confuse] = new(CharacterStatus.IsConfused, 1119, 3985642884, 5441), // "?" swirl
        [StatusEffectKind.Freeze] = new(CharacterStatus.IsFrozen, 1134, 3584332239, 5337),    // ice cube
        [StatusEffectKind.Berserk] = new(CharacterStatus.IsBerserk, 1115, 2806066909, 0),
        [StatusEffectKind.Poison] = new(CharacterStatus.None, 1130, 1838987417, 5220),        // green cog loop
    };

    private const int PoisonTickMs = 2000;
    private const int PoisonTickDamage = 50;
    private const int PoisonHitFlashFxId = 15578; // green poison hit flash per tick

    private class ActiveEffect
    {
        public int TagId;
        public int Seq;
    }

    private class TargetState
    {
        public CharacterStatus Baseline;
        public readonly Dictionary<StatusEffectKind, ActiveEffect> Effects = new();
    }

    private static readonly ConcurrentDictionary<ulong, TargetState> _targets = new();

    // Tag instance ids must not collide with the other tag users (arena heal tags, ability buff tags
    // at 600+) — statuses get their own range.
    private static int _tagCounter = 900;

    public static bool TryParse(string name, out StatusEffectKind kind)
    {
        kind = name.ToLowerInvariant() switch
        {
            "stun" or "stunned" => StatusEffectKind.Stun,
            "sleep" or "asleep" => StatusEffectKind.Sleep,
            "silence" or "silenced" => StatusEffectKind.Silence,
            "root" or "rooted" or "snare" => StatusEffectKind.Root,
            "fear" or "afraid" => StatusEffectKind.Fear,
            "confuse" or "confused" => StatusEffectKind.Confuse,
            "freeze" or "frozen" or "ice" => StatusEffectKind.Freeze,
            "berserk" => StatusEffectKind.Berserk,
            "poison" or "poisoned" => StatusEffectKind.Poison,
            _ => (StatusEffectKind)(-1),
        };
        return kind >= 0;
    }

    /// <summary>Apply (or refresh) a status for durationMs. <paramref name="baseline"/> = the target's
    /// resting CharacterState bits, OR'd back in on every update so mob flags (e.g. the wolves'
    /// charging state) survive; pass it on the FIRST apply for that target. <paramref name="source"/>
    /// = the caster — shows as the tag's source guid (live tags carry it) and attributes poison ticks.</summary>
    public static void Apply(IEntity target, StatusEffectKind kind, int durationMs,
        CharacterStatus baseline = CharacterStatus.None, Player? source = null)
    {
        var meta = _meta[kind];
        var state = _targets.GetOrAdd(target.Guid, _ => new TargetState());
        int tagId, seq;

        lock (state)
        {
            if (baseline != CharacterStatus.None)
                state.Baseline = baseline;

            if (!state.Effects.TryGetValue(kind, out var effect))
                state.Effects[kind] = effect = new ActiveEffect { TagId = ++_tagCounter };

            tagId = effect.TagId;
            seq = ++effect.Seq; // refresh invalidates the previous expiry task
        }

        SendState(target, state);

        // Live pairing (the heart buff): the looping FX goes out as op35/41 to EVERYONE — including
        // the tag's owner — and THEN the tag itself, which references the same composite. The client
        // keys both by tag id (no double render).
        if (meta.FxId > 0)
        {
            Send(target, new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = target.Guid,
                TagId = tagId,
                CompositeEffectId = meta.FxId,
                SourceGuid = source?.Guid ?? target.Guid,
            });
        }

        if (target is Player player)
        {
            // Buff-bar entry. Field template = the live heart-buff tag (type 2, magnitude/duration,
            // the composite id echoed in the tag, source guid in Guid).
            player.SendTunneled(new ClientUpdatePacketAddEffectTag
            {
                Tag = new EffectTag
                {
                    InstanceId = tagId,
                    EffectId = tagId,
                    TypeId = 2,
                    Duration = durationMs / 1000,
                    Guid = source?.Guid ?? target.Guid,
                    CompositeEffectId = meta.FxId,
                    Unknown16 = 0, // the live in-combat tags (heart) ship 0 here; login-time buffs ship 3
                    IconId = meta.IconId,
                    NameId = unchecked((int)meta.NameId),
                },
            });
        }

        if (kind == StatusEffectKind.Poison && target is Npc npc)
            RunPoisonTicks(npc, state, seq, durationMs, source);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs);
                ExpireIfCurrent(target, kind, seq);
            }
            catch { /* target gone — nothing to revert */ }
        });
    }

    /// <summary>A timed BUFF tag (buff-bar icon + label + duration pie, optional looping body FX and
    /// magnitude) that self-removes — for powerups/hearts rather than the status roster. Field
    /// template = the live heart buff (type 2, FX riding the tag's composite field). Others see the
    /// FX via op35/41. Returns the tag id (pass to RemoveBuffTag for an early cancel).</summary>
    public static int ApplyBuffTag(Player player, int iconId, uint nameId, int durationMs,
        float magnitude = 0f, int fxId = 0, int abilityId = 0, ulong sourceGuid = 0)
    {
        var tagId = ++_tagCounter;

        // Live order: the op35/41 FX first (to everyone, self included), then the tag echoing the
        // same composite — the client keys both by tag id.
        if (fxId > 0)
        {
            var fx = new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = tagId,
                CompositeEffectId = fxId,
                SourceGuid = sourceGuid != 0 ? sourceGuid : player.Guid,
            };
            player.SendTunneled(fx);
            player.SendTunneledToVisible(fx);
        }

        player.SendTunneled(new ClientUpdatePacketAddEffectTag
        {
            Tag = new EffectTag
            {
                InstanceId = tagId,
                EffectId = tagId,
                TypeId = 2,
                Magnitude = magnitude,
                Duration = durationMs / 1000,
                Guid = sourceGuid != 0 ? sourceGuid : player.Guid,
                CompositeEffectId = fxId,
                Unknown16 = 0, // live in-combat tag template (heart) ships 0 here
                IconId = iconId,
                NameId = unchecked((int)nameId),
                AbilityId = abilityId,
            },
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs);
                RemoveBuffTag(player, tagId, fxId > 0);
            }
            catch { /* player gone */ }
        });

        return tagId;
    }

    public static void RemoveBuffTag(Player player, int tagId, bool hadFx = true)
    {
        player.SendTunneled(new ClientUpdatePacketRemoveEffectTag { InstanceId = tagId });
        if (hadFx)
        {
            player.SendTunneledToVisible(new PlayerUpdatePacketRemoveEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = tagId,
            });
        }
    }

    /// <summary>Remove one status now (early cleanse).</summary>
    public static void Clear(IEntity target, StatusEffectKind kind)
    {
        if (_targets.TryGetValue(target.Guid, out var state))
        {
            int seq;
            lock (state)
                seq = state.Effects.TryGetValue(kind, out var e) ? e.Seq : -1;
            if (seq >= 0)
                ExpireIfCurrent(target, kind, seq);
        }
    }

    /// <summary>Remove every active status (e.g. leaving an encounter).</summary>
    public static void ClearAll(IEntity target)
    {
        if (_targets.TryGetValue(target.Guid, out var state))
        {
            List<StatusEffectKind> kinds;
            lock (state)
                kinds = new List<StatusEffectKind>(state.Effects.Keys);
            foreach (var kind in kinds)
                Clear(target, kind);
        }
    }

    /// <summary>True while ANY ability press must be rejected server-side (the client already blocks
    /// these with its NoCast* messages — this is the authority check behind it).</summary>
    public static bool BlocksAbilities(ulong guid) =>
        HasAny(guid, StatusEffectKind.Stun, StatusEffectKind.Sleep, StatusEffectKind.Fear, StatusEffectKind.Freeze);

    /// <summary>Silence blocks the ability bars only — item/potion slots stay usable.</summary>
    public static bool IsSilenced(ulong guid) => HasAny(guid, StatusEffectKind.Silence);

    /// <summary>For zone AI: a stunned/sleeping/rooted/frozen mob must not move or attack (the flags
    /// only halt the CLIENT's controller — our server AI is the movement authority for NPCs).</summary>
    public static bool IsImmobilized(ulong guid) =>
        HasAny(guid, StatusEffectKind.Stun, StatusEffectKind.Sleep, StatusEffectKind.Root, StatusEffectKind.Freeze);

    private static bool HasAny(ulong guid, params StatusEffectKind[] kinds)
    {
        if (!_targets.TryGetValue(guid, out var state))
            return false;
        lock (state)
        {
            foreach (var kind in kinds)
                if (state.Effects.ContainsKey(kind))
                    return true;
        }
        return false;
    }

    private static void ExpireIfCurrent(IEntity target, StatusEffectKind kind, int seq)
    {
        if (!_targets.TryGetValue(target.Guid, out var state))
            return;

        int tagId;
        lock (state)
        {
            if (!state.Effects.TryGetValue(kind, out var effect) || effect.Seq != seq)
                return; // refreshed or already cleared
            tagId = effect.TagId;
            state.Effects.Remove(kind);
        }

        SendState(target, state);

        if (target is Player player)
            player.SendTunneled(new ClientUpdatePacketRemoveEffectTag { InstanceId = tagId });

        if (_meta[kind].FxId > 0)
        {
            Send(target, new PlayerUpdatePacketRemoveEffectTagCompositeEffect
            {
                Guid = target.Guid,
                TagId = tagId,
            });
        }
    }

    /// <summary>Poison = damage over time. NPC targets tick real damage (green hit flash + damage
    /// number to everyone watching); the flagless tag/FX half is handled by Apply like any status.</summary>
    private static void RunPoisonTicks(Npc npc, TargetState state, int seq, int durationMs, Player? source)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var elapsed = PoisonTickMs; elapsed <= durationMs; elapsed += PoisonTickMs)
                {
                    await Task.Delay(PoisonTickMs);

                    lock (state)
                    {
                        if (!state.Effects.TryGetValue(StatusEffectKind.Poison, out var e) || e.Seq != seq)
                            return; // cleansed or refreshed (the refresh runs its own ticker)
                    }

                    if (!npc.IsAlive || !npc.IsDamageable)
                        return;

                    npc.ApplyDamage(PoisonTickDamage);
                    foreach (var watcher in npc.VisiblePlayers.Values)
                    {
                        watcher.SendTunneled(new PlayerUpdatePacketHitPointModification
                        {
                            Guid = source?.Guid ?? npc.Guid,
                            Guid2 = npc.Guid,
                            Unknown = true,
                            Unknown2 = npc.MaxHealth,
                            Unknown3 = npc.Health,
                            Unknown4 = -PoisonTickDamage,
                        });
                        watcher.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                        {
                            Guid = npc.Guid,
                            CompositeEffectId = PoisonHitFlashFxId,
                            Position = npc.Position,
                        });
                    }
                }
            }
            catch { /* target despawned mid-tick */ }
        });
    }

    /// <summary>Recompute the flag union (baseline + all active statuses) and push op35/20.</summary>
    private static void SendState(IEntity target, TargetState state)
    {
        CharacterStatus flags;
        lock (state)
        {
            flags = state.Baseline;
            foreach (var kind in state.Effects.Keys)
                flags |= _meta[kind].Flag;
        }

        Send(target, new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = target.Guid,
            Status = flags,
        });
    }

    private static void Send(IEntity target, ISerializablePacket packet, bool skipSelf = false)
    {
        if (target is Player player)
        {
            if (!skipSelf)
                player.SendTunneled(packet);
            player.SendTunneledToVisible(packet);
        }
        else
        {
            foreach (var watcher in target.VisiblePlayers.Values)
                watcher.SendTunneled(packet);
        }
    }
}
