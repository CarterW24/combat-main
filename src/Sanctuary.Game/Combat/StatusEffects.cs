using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

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

public static class StatusEffects
{
    private record Meta(CharacterStatus Flag, int IconId, uint NameId, int FxId);

    private static readonly Dictionary<StatusEffectKind, Meta> _meta = new()
    {
        [StatusEffectKind.Stun] = new(CharacterStatus.IsStunned, 1137, 2454861836, 2),
        [StatusEffectKind.Sleep] = new(CharacterStatus.IsAsleep, 1133, 2207336499, 3),
        [StatusEffectKind.Silence] = new(CharacterStatus.IsSilenced, 1132, 2633742197, 14),
        [StatusEffectKind.Root] = new(CharacterStatus.IsRooted, 1131, 1827650078, 15734),
        [StatusEffectKind.Fear] = new(CharacterStatus.IsAfraid, 1123, 4014129776, 0),
        [StatusEffectKind.Confuse] = new(CharacterStatus.IsConfused, 1119, 3985642884, 5441),
        [StatusEffectKind.Freeze] = new(CharacterStatus.IsFrozen, 1134, 3584332239, 5337),
        [StatusEffectKind.Berserk] = new(CharacterStatus.IsBerserk, 1115, 2806066909, 0),
        [StatusEffectKind.Poison] = new(CharacterStatus.None, 1130, 1838987417, 5220),
    };

    private const int PoisonTickMs = 2000;
    private const int PoisonTickDamage = 50;
    private const int PoisonHitFlashFxId = 15578;

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
            seq = ++effect.Seq;
        }

        SendState(target, state);

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
            player.SendTunneled(new ClientUpdatePacketAddEffectTag
            {
                Tag = new EffectTag
                {
                    InstanceId = tagId,
                    EffectId = tagId,
                    TypeId = 2,
                    Duration = durationMs / 1000,
                    Guid = source?.Guid ?? target.Guid,
                    CompositeEffectId = 0, // FX rides op35/41 -> 35/42 only, never in the tag (see ApplyBuffTag)
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
            catch {  }
        });
    }

    private sealed class ActiveBuffTag
    {
        public int TagId;
        public int Seq;
        public bool HasFx;
    }

    // Player guid -> buff identity (icon id) -> live tag. ONE buff-bar entry per buff kind:
    // re-applying (a second heart) reuses the tag id and restarts the duration pie instead of
    // stacking a new icon. The registry also lets a zone change force-expire everything.
    private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<int, ActiveBuffTag>> _activeBuffTags = new();

    // HEART/BUFF FX RECIPE (2026-07-18, decided after the harness mixup): the only PLAY-VERIFIED
    // recipe is the 2026-07-15 build (fork `combat` branch): FX = op35/41 add + op35/42 stop, and
    // the FX id NEVER rides inside the 38/16 tag. The tag-era recipe (FX-in-tag, deployed 07-16
    // 19:20 to BOTH trees) loops the shower forever on the owner's screen — seen on harness 1
    // itself on 07-18. Tag here = buff-bar icon/label/pie ONLY.
    public static int ApplyBuffTag(Player player, int iconId, uint nameId, int durationMs,
        float magnitude = 0f, int fxId = 0, int abilityId = 0, ulong sourceGuid = 0)
    {
        var buffs = _activeBuffTags.GetOrAdd(player.Guid, _ => new());
        var isRefresh = buffs.TryGetValue(iconId, out var buff);
        if (!isRefresh || buff is null)
        {
            buff = new ActiveBuffTag { TagId = ++_tagCounter };
            buffs[iconId] = buff;
            isRefresh = false;
        }

        int tagId, seq;
        lock (buff)
        {
            tagId = buff.TagId;
            seq = ++buff.Seq;
            buff.HasFx = fxId > 0;
        }

        // On a refresh the first application's FX loop is still running under this tag id — the
        // client skips an add for an id it already tracks, so only start it once.
        if (fxId > 0 && !isRefresh)
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

        // Refresh: drop the old bar entry first so the re-add restarts the duration pie.
        if (isRefresh)
            player.SendTunneled(new ClientUpdatePacketRemoveEffectTag { InstanceId = tagId });

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
                CompositeEffectId = 0,
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
                lock (buff)
                {
                    // A newer application refreshed this buff — its own task owns the removal now.
                    if (buff.Seq != seq)
                        return;
                }
                RemoveBuffTag(player, iconId);
            }
            catch {  }
        });

        return tagId;
    }

    public static void RemoveBuffTag(Player player, int buffIconId)
    {
        if (!_activeBuffTags.TryGetValue(player.Guid, out var buffs)
            || !buffs.TryRemove(buffIconId, out var buff))
            return;

        player.SendTunneled(new ClientUpdatePacketRemoveEffectTag { InstanceId = buff.TagId });
        if (buff.HasFx)
        {
            player.SendTunneledToVisible(new PlayerUpdatePacketRemoveEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = buff.TagId,
            }, sendToSelf: true);
        }
    }

    // Expire every active buff tag NOW, while the packets can still reach the client — called
    // before a zone teleport. The scheduled Task.Delay removals still run later but no-op
    // (already out of the registry, and a duplicate remove is harmless to the client).
    public static void FlushBuffTags(Player player)
    {
        if (!_activeBuffTags.TryGetValue(player.Guid, out var buffs))
            return;
        foreach (var key in buffs.Keys)
            RemoveBuffTag(player, key);
    }

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

    public static bool BlocksAbilities(ulong guid) =>
        HasAny(guid, StatusEffectKind.Stun, StatusEffectKind.Sleep, StatusEffectKind.Fear, StatusEffectKind.Freeze);

    public static bool IsSilenced(ulong guid) => HasAny(guid, StatusEffectKind.Silence);

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
                return;
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
                            return;
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
                            ShowFloatingText = true,
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
            catch {  }
        });
    }

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
