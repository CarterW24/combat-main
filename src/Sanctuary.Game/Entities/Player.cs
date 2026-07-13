using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using Sanctuary.Core.Collections;
using Sanctuary.Core.IO;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Leveling;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Chat;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.Game.Entities;

public sealed class Player : ClientPcData, IEntity
{
    private readonly UdpConnection _connection;
    private readonly IResourceManager _resourceManager;

    public bool Visible { get; set; }
    public DateTime? SpawnedAt { get; set; }
    public ulong LastInteractNpcGuid { get; set; }
    public DateTime LastInteractAt { get; set; }

    /// <summary>
    /// When the player last accepted a quest. Used to ignore a spurious CommandPacketQuestAbandon
    /// (26/23) that the client can fire in the moments right after accepting - without this guard
    /// that stray packet would immediately drop the quest the player just took.
    /// </summary>
    public DateTime LastQuestAcceptedAt { get; set; }

    /// <summary>PARTY: last time a group-invite C2S was acted on. The client re-sends GroupInvite
    /// ~6x/sec while the invite UI is up (like FreeInteractionNpc), so the handler debounces on this
    /// to fire the invite once per burst.</summary>
    public DateTime LastPartyInviteAt { get; set; }

    /// <summary>True once the login-only zone-in burst (Welcome screen etc.) has been sent this
    /// session. The overworld's OnClientIsReady runs on EVERY zone-in — including the return from a
    /// combat instance — and re-sending PacketLoadWelcomeScreen there re-opens the client's Welcome
    /// popup (Main.wndWelcomeHandler) ON TOP of the encounter's victory screen (live bug 2026-07-04).</summary>
    public bool LoginBurstSent { get; set; }

    /// <summary>Set once the Hero's Journal has been repopulated this session. The client keeps the
    /// journal across a re-zone (e.g. the Frostfang arena round-trip), so re-sending QuestAdd on every
    /// overworld entry APPENDS duplicate rows the client never dedupes - and completion can only clear
    /// one, leaving finished quests stuck in the helper. Gate the restore to login only.</summary>
    public bool JournalRestored { get; set; }

    /// <summary>LOOT WHEEL: the prize the victory wheel was told to land on (set when the encounter
    /// sends MiniGameLootWheelSetItemToLandOn; consumed by the C2S LootWheelOnRotationStopped handler,
    /// which grants it). Null = no spin pending. A null prize with PendingWheelCoins &gt; 0 = the
    /// COINS slice.</summary>
    public Sanctuary.Packet.RewardEntry? PendingWheelPrize { get; set; }
    public int PendingWheelCoins { get; set; }

    /// <summary>Where the exit door returns the player after a combat instance: the overworld spot
    /// they stood on when GO! teleported them out (set by the entrance handler, consumed + cleared by
    /// the arena's ReturnHome). Null = fall back to the zone spawn.</summary>
    public System.Numerics.Vector4? EncounterReturnPosition { get; set; }

    public IZone Zone { get; set; }
    public ZoneTile ZoneTile { get; private set; } = ZoneTile.Empty;
    public ConcurrentDictionary<ulong, Npc> VisibleNpcs { get; } = [];
    public ConcurrentDictionary<ulong, Player> VisiblePlayers { get; } = [];

    private int ZoneAreaId { get; set; }

    public int ChatBubbleForegroundColor { get; set; }
    public int ChatBubbleBackgroundColor { get; set; }
    public int ChatBubbleSize { get; set; }

    public ClientPcProfile ActiveProfile => Profiles.Single(x => x.Id == ActiveProfileId);

    public Mount? Mount { get; set; }

    public List<FriendData> Friends { get; set; } = [];
    public List<IgnoreData> Ignores { get; set; } = [];

    public ConcurrentDictionary<ChatChannel, bool> ChatChannelStatus { get; set; } = [];

    public int StationCash { get; set; }
    public List<CoinStoreTransactionRecord> CoinStoreTransactions { get; set; } = [];

    public int TimezoneOffset { get; set; }

    public Dictionary<int, Dictionary<int, int>> ActionBarItemGuids { get; set; } = new();

    public new int TemporaryAppearance { get; set; }
    public DateTimeOffset? TemporaryAppearanceExpiresAt { get; set; }
    private int _temporaryAppearanceEffectId;

    private record PendingCooldown(int ActionBarId, int SlotIndex, int IconId, int NameId, int Count, int CooldownMs, DateTimeOffset StartedAt);
    private readonly ConcurrentDictionary<(int, int), PendingCooldown> _pendingCooldowns = new();

    public bool IsDead { get; set; }

    /// <summary>Where the player fell (set on Knockout) — the "Revive here" respawn option returns them here.</summary>
    public System.Numerics.Vector4 DeathPosition { get; set; }
    public int CurrentHitpoints { get; set; } = 2500;
    public int CurrentMana { get; set; } = 100;

    public ConcurrentSet<ulong> IncomingFriendRequests { get; } = [];

    public ulong CharacterId { get; set; }
    public bool InCombat { get; set; }
    public ulong CombatTargetGuid { get; set; }
    public ulong ActiveMerchantGuid { get; set; }
    public ulong CurrentHouseGuid { get; set; }
    public DateTime LastCombatTime { get; set; }

    public Pet? Pet { get; set; }

    /// <summary>
    /// QuestId -> Completed. Presence in the dictionary means the quest has been accepted.
    /// </summary>
    public Dictionary<int, bool> Quests { get; } = new();

    /// <summary>
    /// QuestId -> number of goals completed (goals tick off in order). The active goal is at this index.
    /// Absent = 0 goals done. Persisted alongside the quest so multi-goal progress survives relog.
    /// </summary>
    public Dictionary<int, int> QuestGoalProgress { get; } = new();

    /// <summary>
    /// QuestId -> current collect count for the quest's ACTIVE Collect goal (how many pickups gathered so
    /// far, 0..RequiredCount). In-memory only: a relog restarts the in-progress collect goal from 0 (the
    /// shared collectibles respawn), while completed goals persist via <see cref="QuestGoalProgress"/>.
    /// Cleared when the collect goal ticks off.
    /// </summary>
    public Dictionary<int, int> QuestCollectProgress { get; } = new();

    /// <summary>
    /// The quest the player currently has selected/tracked in the quest helper (set on accept and when
    /// they pick one in the journal). The tracker arrow and the "Take Me There" breadcrumb point at THIS
    /// quest's objective, not just the first active quest. 0 = none selected.
    /// </summary>
    public int ActiveQuestId { get; set; }

    /// <summary>
    /// Deferred quest turn-in finalization: set when a quest end screen is shown, invoked (once)
    /// when the client sends QuestEndReplyPacket (the player clicked "Complete").
    /// </summary>
    public System.Action? PendingQuestEndAction { get; set; }

    public void Disconnect() => _connection.Disconnect();

    public Vector4 StartingZonePosition { get; set; }
    public Quaternion StartingZoneRotation { get; set; }

    public Player(BaseZone zone, UdpConnection connection, IResourceManager resourceManager)
    {
        Zone = zone;

        _connection = connection;
        _resourceManager = resourceManager;
    }

    #region Connection

    public void Send(ISerializablePacket packet)
    {
        var data = packet.Serialize();

        _connection.Send(UdpChannel.Reliable1, data);
    }

    public void SendToVisible(ISerializablePacket packet, bool sendToSelf = false)
    {
        var visiblePlayers = VisiblePlayers.ToFrozenDictionary();

        foreach (var visiblePlayer in visiblePlayers)
            visiblePlayer.Value.Send(packet);

        if (sendToSelf)
            Send(packet);
    }

    public void SendTunneled(ISerializablePacket packet)
    {
        var packetTunneled = new PacketTunneledClientPacket
        {
            Payload = packet.Serialize()
        };

        Send(packetTunneled);
    }

    [Obsolete]
    public void SendTunneled(byte[] buffer)
    {
        var packetTunneled = new PacketTunneledClientPacket
        {
            Payload = buffer
        };

        Send(packetTunneled);
    }

    public void SendTunneledToVisible(ISerializablePacket packet, bool sendToSelf = false)
    {
        var visiblePlayers = VisiblePlayers.ToFrozenDictionary();

        foreach (var visiblePlayer in visiblePlayers)
            visiblePlayer.Value.SendTunneled(packet);

        if (sendToSelf)
            SendTunneled(packet);
    }

    #endregion

    #region Update

    public void UpdateEveryTick()
    {
        // Drive the overworld in-combat state here (10 Hz, single thread) so the op41 enter/exit packets are
        // never sent from the async auto-fire tasks — which raced and latched the client "in combat" (menu
        // stuck). See WorldCombatStateTick.
        WorldCombatStateTick();

        if (TemporaryAppearanceExpiresAt.HasValue &&
            TemporaryAppearanceExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            RemoveTemporaryAppearance();
        }
    }

    public void UpdateEverySecond()
    {
        RegenTick();

        var now = DateTimeOffset.UtcNow;
        foreach (var (key, cooldown) in _pendingCooldowns)
        {
            int elapsed = (int)(now - cooldown.StartedAt).TotalMilliseconds;
            bool expired = elapsed >= cooldown.CooldownMs;
            SendTunneled(BuildCooldownSlotPacket(cooldown, expired ? cooldown.CooldownMs : elapsed, expired));
            if (expired)
                _pendingCooldowns.TryRemove(key, out _);
        }
    }

    /// <summary>Out-of-combat window (seconds): HP won't regen until this long after the last hit taken.
    /// Matches the ability handler's world-combat decay so "in combat" means the same thing on both sides.</summary>
    private const int OutOfCombatSeconds = 6;

    /// <summary>When the player last took combat damage — gates HP regen so it doesn't fight incoming hits.</summary>
    public DateTime LastCombatDamageAt { get; set; } = DateTime.MinValue;

    /// <summary>Regenerates HP (and, for non-combat jobs, mana) toward their maximums.</summary>
    private void RegenTick()
    {
        if (IsDead)
            return;

        if (!Stats.TryGetValue(CharacterStatId.MaxHealth, out var maxHpStat) ||
            !Stats.TryGetValue(CharacterStatId.MaxMana, out var maxManaStat))
            return; // stats not initialized yet

        int maxHp = maxHpStat.Int;
        int maxMana = maxManaStat.Int;

        // COMBAT: don't regen HP while actively fighting (a hit within the out-of-combat window). The old
        // behavior raced incoming enemy damage and made the health bar visibly jitter up and down mid-fight.
        bool inCombat = DateTime.UtcNow - LastCombatDamageAt < TimeSpan.FromSeconds(OutOfCombatSeconds);

        bool hpChanged = false;
        if (!inCombat && CurrentHitpoints < maxHp)
        {
            int regen = Stats.TryGetValue(CharacterStatId.HitPointRegen, out var hr) ? hr.Int : 25;
            CurrentHitpoints = Math.Min(maxHp, CurrentHitpoints + Math.Max(1, regen));
            hpChanged = true;
        }

        // STAMINA: combat jobs' stamina bar is owned ENTIRELY by the ability handler's energy system
        // (0-100, drains on specials, +4/sec). RegenTick must NOT also drive it with the level-scaled
        // CurrentMana, or the two systems fight over the same bar — that flicker was the "stamina bar
        // glitching" AND it re-enabled the special slot client-side mid-cooldown (the "ability #2 spam").
        bool usesCombatEnergy =
            ActiveProfileId == Combat.NinjaWeaponAbilities.NinjaProfileId ||
            ActiveProfileId == Combat.ArcherWeaponAbilities.ArcherProfileId;

        bool manaChanged = false;
        if (!usesCombatEnergy && CurrentMana < maxMana)
        {
            int regen = Stats.TryGetValue(CharacterStatId.ManaRegen, out var mr) ? mr.Int : 4;
            CurrentMana = Math.Min(maxMana, CurrentMana + Math.Max(1, regen));
            manaChanged = true;
        }

        if (hpChanged)
        {
            SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = CurrentHitpoints, MaxHitpoints = maxHp });
            SendTunneledToVisible(new PlayerUpdatePacketUpdateHitpoints
            {
                Guid = Guid,
                Hitpoints = CurrentHitpoints,
                MaxHitpoints = maxHp
            }, sendToSelf: true);
        }

        if (manaChanged)
        {
            SendTunneled(new ClientUpdatePacketMana { CurrentMana = CurrentMana, MaxMana = maxMana });
            SendTunneledToVisible(new PlayerUpdatePacketUpdateMana
            {
                Guid = Guid,
                CurrentMana = CurrentMana,
                MaxMana = maxMana
            }, sendToSelf: true);
        }
    }

    public void StartActionBarCooldown(int actionBarId, int slotIndex, int iconId, int nameId, int count, int cooldownMs)
    {
        var cooldown = new PendingCooldown(actionBarId, slotIndex, iconId, nameId, count, cooldownMs, DateTimeOffset.UtcNow);
        _pendingCooldowns[(actionBarId, slotIndex)] = cooldown;
        SendTunneled(BuildCooldownSlotPacket(cooldown, 0, false));
    }

    private static ClientUpdatePacketUpdateActionBarSlot BuildCooldownSlotPacket(PendingCooldown cooldown, int elapsed, bool enabled)
    {
        var packet = new ClientUpdatePacketUpdateActionBarSlot { Data = { Id = cooldown.ActionBarId, Slot = cooldown.SlotIndex } };
        packet.Slot.IsEmpty = false;
        packet.Slot.IconId = cooldown.IconId;
        packet.Slot.NameId = cooldown.NameId;
        packet.Slot.Unknown5 = 1;
        packet.Slot.Unknown6 = 4;
        packet.Slot.Unknown7 = 15;
        packet.Slot.Enabled = enabled;
        packet.Slot.Unknown10 = elapsed;
        packet.Slot.TotalRefreshTime = cooldown.CooldownMs;
        packet.Slot.Unknown12 = elapsed;
        packet.Slot.Quantity = cooldown.Count;
        packet.Slot.ForceDismount = true;
        packet.Slot.Unknown15 = elapsed;
        return packet;
    }

    /// <summary>Knockout visual (this client renders NOTHING on its own at 0 HP): a hit-poof so the player
    /// and nearby people see the moment of defeat. Tunable.</summary>
    private const int KnockoutEffectId = 5017; // PFX death poof (same one dying NPCs use)

    /// <summary>Send a System-channel chat line to this player (the death/revive feedback, since there's no
    /// native death UI to show it).</summary>
    public void SendSystemMessage(string text)
    {
        SendTunneled(new PacketChat
        {
            Channel = Sanctuary.Packet.Common.Chat.ChatChannel.System,
            FromGuid = Guid,
            FromName = Name ?? new(),
            Message = text
        });
    }

    /// <summary>Revive burst played on respawn — a big flashy particle burst (the level-up FX), far
    /// flashier than a plain poof.</summary>
    private const int ReviveEffectId = 15117; // PFX_levelup_big (~2s one-shot burst)

    /// <summary>Resurrect/get-up animation played on revive (0 = none — the knocked-out state clear already
    /// stands the player up; set to a real resurrect clip id once confirmed).</summary>
    private const int ResurrectAnimId = 0;

    public void Respawn()
    {
        IsDead = false;

        var maxHp = Stats[CharacterStatId.MaxHealth].Int;
        CurrentHitpoints = maxHp;

        SendTunneled(new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = maxHp,
            MaxHitpoints = maxHp
        });

        // Clear the knocked-out/rooted state (stand up + movement restored).
        SendTunneledToVisible(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = Guid,
            Status = CharacterStatus.None,
        }, sendToSelf: true);

        // Clear the overworld "in combat" flags the knockout flow raised for the respawn window, and reset the
        // combat-state machine so it doesn't immediately re-enter combat on revive (the pre-death timestamp
        // could still be within the out-of-combat window). Otherwise the player stays wedged "in combat" after
        // reviving. WorldCombatStateTick resumes once alive.
        _worldCombatActive = false;
        _lastWorldCombatTicks = 0;
        SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });

        // Resurrect animation + revive FX at the player (visible to nearby players too).
        if (ResurrectAnimId > 0)
        {
            SendTunneledToVisible(new PlayerUpdatePacketSetAnimation
            {
                Guid = Guid,
                AnimationId = ResurrectAnimId,
            }, sendToSelf: true);
        }
        SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = Guid,
            CompositeEffectId = ReviveEffectId,
            Position = Position,
        }, sendToSelf: true);

        SendSystemMessage("You have been revived!");
    }

    public void TakeDamage(int amount, CombatNpc source) => TakeDamage(amount);

    /// <summary>Apply combat damage from any source (world CombatNpc, arena claw, etc.): drop HP, push the
    /// HP bar, and knock out at 0. No-op while already knocked out.</summary>
    public void TakeDamage(int amount)
    {
        if (IsDead)
            return;

        LastCombatDamageAt = DateTime.UtcNow; // gates HP regen so the bar doesn't jitter mid-fight

        CurrentHitpoints = Math.Max(0, CurrentHitpoints - amount);

        var hpPacket = new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = CurrentHitpoints,
            MaxHitpoints = Stats[CharacterStatId.MaxHealth].Int
        };
        SendTunneled(hpPacket);

        if (CurrentHitpoints <= 0)
            Knockout();
        else
            EnterWorldCombat(); // taking a hit puts you in combat too (weapon drawn, HP bars, damage text)
    }

    // --- Overworld "in combat" state (client op41 sub132 SetInWorldCombat + sub133 SetIsFighting) ---
    // These flags draw the weapon, show enemy HP bars + floating damage numbers, and put the client in its
    // combat mode. We enter on ANY overworld combat action — dealing damage, TAKING damage, or pressing an
    // attack — and drop out OutOfCombatSeconds after the last one.
    //
    // CRITICAL THREADING: the op41 enter/exit packets are sent ONLY from WorldCombatStateTick, which runs on
    // the single UpdateEveryTick loop. EnterWorldCombat (called from the async auto-fire tasks + the NPC-attack
    // tick, i.e. arbitrary threads) merely STAMPS a timestamp. Earlier we sent op41 straight from those async
    // callers, so an "enter" (true) from a fire task could land AFTER the decay's "exit" (false) and latch the
    // client ON forever — the client's combat mode locks the main menu, and once latched it never released
    // ("can't press anything even out of combat"). Funneling every send through one thread makes enter/exit a
    // clean, ordered state machine that can't cross. Instanced arenas own their own fighting-state, so this
    // no-ops there (a stale flag reconciles on return to the overworld: want=false -> exit sent).
    private long _lastWorldCombatTicks;
    private volatile bool _worldCombatActive;

    /// <summary>Stamp that a combat action just happened. Callable from ANY thread — it only writes the
    /// timestamp; the actual op41 packets are driven by <see cref="WorldCombatStateTick"/> on the tick thread
    /// so enter and exit can never race (see the threading note above).</summary>
    public void EnterWorldCombat()
    {
        if (Zone is not StartingZone)
            return; // arenas own their combat-state lifecycle
        _lastWorldCombatTicks = Environment.TickCount64;
    }

    /// <summary>Single-threaded combat-state driver (UpdateEveryTick, 10 Hz). Compares "had a combat action
    /// within OutOfCombatSeconds" against the current client state and sends the op41 enter/exit packets only
    /// on a transition. Every op41 send happens here on one thread, so the client can never get latched "in
    /// combat" with the menu stuck. Sends BOTH flags for the full indicator; arenas drive their own.</summary>
    private void WorldCombatStateTick()
    {
        if (Zone is not StartingZone)
            return; // arenas drive their own combat-state; a stale flag reconciles on return (want=false -> exit)

        // While knocked out, the death flow owns op41: OnPlayerKnockedOut raises it so the pay/safe respawn
        // WINDOW shows its buttons, and Respawn clears it (and resets _worldCombatActive). Don't let the decay
        // tear the window's combat-state down from under it.
        if (IsDead)
            return;

        bool want = _lastWorldCombatTicks != 0
            && Environment.TickCount64 - _lastWorldCombatTicks < OutOfCombatSeconds * 1000L;

        if (want == _worldCombatActive)
            return; // no transition

        _worldCombatActive = want;
        SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = want });
        SendTunneled(new EncounterPacketIsFighting { InWorldCombat = want });
    }

    /// <summary>DEATH: the player's HP reached 0 — they're knocked out. Marks them dead (blocks further
    /// damage + their own abilities), pins HP at 0, and hands off to the zone: the overworld leaves the
    /// client's knockout UI up for a respawn-in-place; a combat instance counts the KO and fails the
    /// encounter at the limit. The client shows its own knockout state when it receives 0 HP.</summary>
    public void Knockout()
    {
        if (IsDead)
            return;

        IsDead = true;
        CurrentHitpoints = 0;
        DeathPosition = Position; // where "Revive here" brings the player back

        SendTunneled(new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = 0,
            MaxHitpoints = Stats[CharacterStatId.MaxHealth].Int
        });

        // Put the actor into the KNOCKED-OUT + ROOTED state: the client plays its knockdown animation and
        // (IsRooted) stops the player from running around while down. Cleared on Respawn.
        SendTunneledToVisible(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = Guid,
            Status = CharacterStatus.IsKnockedOut | CharacterStatus.IsRooted,
        }, sendToSelf: true);

        // Also a death poof + message (belt-and-suspenders feedback).
        SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = Guid,
            CompositeEffectId = KnockoutEffectId,
            Position = Position,
        }, sendToSelf: true);

        SendSystemMessage("You have been knocked out!");

        Zone.OnPlayerKnockedOut(this);
    }

    /// <summary>
    /// Grants XP to the active job: accrues into the current level, levels up (and rescales stats +
    /// refills HP/mana) when the curve threshold is crossed, notifies the client, and updates the star
    /// meter. Persistence happens on the normal save path (DbProfile.Level / LevelXP).
    /// </summary>
    public void AwardXp(int xp)
    {
        if (xp <= 0)
            return;

        if (ActiveProfile.Rank >= JobLeveling.MaxLevel)
            return; // already max level - no more XP

        // Award XP per kill (immediately). This used to be deferred to combat-drop as a workaround for the
        // ranged-fire wedge — but the real culprits (the op41 combat-state flags + the ActivateProfile in the
        // XP flush) are now removed, so the XP feedback packets are safe to send on the kill.
        ApplyXp(xp);
    }

    /// <summary>Accrue XP, level up, and send the client-facing feedback.</summary>
    private void ApplyXp(int xp)
    {
        var profile = ActiveProfile;
        if (profile.Rank >= JobLeveling.MaxLevel)
            return;

        int startLevel = profile.Rank;
        profile.LevelXpRaw += xp;

        while (profile.Rank < JobLeveling.MaxLevel && profile.LevelXpRaw >= JobLeveling.XpForLevel(profile.Rank))
        {
            profile.LevelXpRaw -= JobLeveling.XpForLevel(profile.Rank);
            profile.Rank++;
            profile.StarsEarned++;   // one star per level
        }

        if (profile.Rank >= JobLeveling.MaxLevel)
            profile.LevelXpRaw = 0;

        profile.RankPercent = JobLeveling.RankPercent(profile.Rank, profile.LevelXpRaw);

        bool leveled = profile.Rank != startLevel;

        // Floating "+XP" feedback.
        SendTunneled(new ClientUpdatePacketUpdateProfileExperience
        {
            ProfileId = profile.Id,
            XpGained = xp,
            TotalXpInLevel = profile.LevelXpRaw,
            CurrentLevel = profile.Rank
        });

        // Native job XP bar + level-up: the ability-set experience (opcode 36/8). The client renders the
        // on-screen job XP bar from Progress/TotalForLevel and fires JobLevelUp when Level increases.
        SendTunneled(new AbilityPacketUpdateAbilityExperience { Experience = BuildJobAbilityExperience() });

        if (leveled)
        {
            SendTunneled(new ClientUpdatePacketUpdateProfileRank
            {
                ProfileId = profile.Id,
                NewRank = profile.Rank,
                ProfileIconId = profile.Icon,
                ProfileNameId = profile.NameId
            });
        }

        // Real-time job XP bar + level-up — on every kill, even mid-combat (retail shows XP as you earn it and
        // levels you up on the spot). The bar only redraws on a full profile re-send: on a level-up,
        // ApplyLevelUpEffects does that re-send via the JobLevelUp presentation (stat rescale + celebration +
        // full-screen UI) and moves the bar to the new level; otherwise a plain silent ActivateProfile moves
        // it. The incremental experience packets above move the +XP text/level number but NOT the bar — the
        // client outright ignores ClientUpdatePacketUpdateProfileExperience (op38/14: its handler has no case
        // for that sub-opcode). EITHER re-send clears the client's ability toolbar, so RestoreWeaponToolbar()
        // re-sends it right after — that restore is what keeps the ranged auto-fire alive across the re-send.
        // Both the per-kill AND the post-level-up firing wedges were a profile re-send with no toolbar restore.
        if (leveled)
            ApplyLevelUpEffects();
        else
            RefreshActiveProfile();

        RestoreWeaponToolbar();
    }

    /// <summary>Re-send the active job's weapon toolbar (op36/5 SetDefinition). A profile re-send
    /// (ActivateProfile / JobLevelUp) clears the client's ability slots, so this must follow any such re-send
    /// or the ranged auto-fire has no ability to repeat and wedges — the same toolbar restore a job-swap does.</summary>
    private void RestoreWeaponToolbar()
    {
        var toolbar = JobWeaponAbilities.BuildToolbar(this, _resourceManager);
        if (toolbar is not null)
            SendTunneled(toolbar);
    }

    /// <summary>The level-up presentation — stat rescale + HP/mana refill, the particle celebration, and the
    /// full-screen JobLevelUp UI. Runs on the spot when a kill levels you (retail behavior). It re-sends the
    /// profile, which clears the client's ability toolbar, so callers MUST follow it with
    /// <see cref="RestoreWeaponToolbar"/> or the ranged auto-fire wedges ("after leveling up I can't refire").</summary>
    private void ApplyLevelUpEffects()
    {
        RecalculateStats(refill: true);
        PlayLevelUpCelebration();

        // Full-screen job level-up UI (levelup_<job>.gfx) via the "JobLevelUp" client event — ClientUpdate
        // 38/15: the client reads one length-prefixed payload and parses it as the active profile.
        using var jluWriter = new PacketWriter();
        ActiveProfile.Serialize(jluWriter);
        SendTunneled(new ClientUpdatePacketJobLevelUp { Payload = jluWriter.Buffer });
    }

    /// <summary>Builds the active job's ability-set experience entry (drives the native job XP bar / level-up).</summary>
    private AbilityExperience BuildJobAbilityExperience()
    {
        var p = ActiveProfile;
        return new AbilityExperience
        {
            Unknown = 1,                 // non-zero = a present/valid entry (0 terminates the profile list)
            NameId = p.NameId,
            DescriptionId = p.DescriptionId,
            IconId = p.Icon,
            Level = p.Rank,
            Progress = p.LevelXpRaw,
            TotalForLevel = JobLeveling.XpForLevel(p.Rank),
        };
    }

    private const int LevelUpCompositeEffect = 15117; // PFX_levelup_big (retail level-up particle burst)

    /// <summary>
    /// Re-sends the active job's serialized profile (ClientUpdatePacketActivateProfile) so the client
    /// refreshes the Jobs panel level + XP bar from the authoritative Rank/RankPercent. An optional
    /// composite effect plays on the player (used for the level-up celebration).
    /// </summary>
    public void RefreshActiveProfile(int compositeEffect = 0)
    {
        using var writer = new PacketWriter();
        ActiveProfile.Serialize(writer);

        SendTunneled(new ClientUpdatePacketActivateProfile
        {
            Payload = writer.Buffer,
            Attachments = GetAttachments(),
            Animation = 0,
            CompositeEffect = compositeEffect
        });
    }

    /// <summary>Re-send the currently-equipped weapon (slot 7) — the ClientUpdatePacketEquipItem +
    /// PlayerUpdatePacketEquipItemChange pair the inventory equip flow sends. Manually re-equipping the bow
    /// is what players found un-freezes the ranged auto-fire after a kill: the weapon re-attach (WieldType)
    /// resets the client's wield/combat state WITHOUT the profile re-activation that itself froze firing.</summary>
    public void ResendEquippedWeapon()
    {
        if (!ActiveProfile.Items.TryGetValue(7, out var weaponProfileItem))
            return;

        var item = Items.SingleOrDefault(x => x.Id == weaponProfileItem.Id);
        if (item is null || !_resourceManager.ClientItemDefinitions.TryGetValue(item.Definition, out var def))
            return;
        if (!_resourceManager.ItemClasses.TryGetValue(def.Class, out var itemClass))
            return;

        var equip = new ClientUpdatePacketEquipItem
        {
            Guid = item.Id,
            ProfileId = ActiveProfileId,
            Equip = true,
        };
        equip.Attachment.ModelName = def.ModelName;
        equip.Attachment.TextureAlias = def.TextureAlias;
        equip.Attachment.TintAlias = def.TintAlias;
        equip.Attachment.TintId = item.Tint == 0 ? def.Icon.TintId : item.Tint;
        equip.Attachment.CompositeEffectId = def.CompositeEffectId;
        equip.Attachment.Slot = 7;
        SendTunneled(equip);

        SendTunneledToVisible(new PlayerUpdatePacketEquipItemChange
        {
            Guid = Guid,
            Id = item.Id,
            Attachment = equip.Attachment,
            ProfileId = ActiveProfileId,
            WieldType = itemClass.WieldType,
        }, sendToSelf: true);
    }

    /// <summary>
    /// Recomputes level-scaled character stats from the active job's Rank, pushes them to the client and
    /// caches them in <see cref="ClientPcData.Stats"/>. When <paramref name="refill"/> is set (login,
    /// level-up) current HP/mana are topped to the new maximum; otherwise they're only clamped down.
    /// </summary>
    public void RecalculateStats(bool refill = false)
    {
        int level = ActiveProfile.Rank;
        int maxHealth = JobLeveling.MaxHealth(level);
        int maxMana = JobLeveling.MaxMana(level);

        UpdateCharacterStats(
            new CharacterStat(CharacterStatId.MaxHealth, maxHealth),
            new CharacterStat(CharacterStatId.MaxMovementSpeed, 8f),
            new CharacterStat(CharacterStatId.WeaponRange, 5f),
            new CharacterStat(CharacterStatId.HitPointRegen, JobLeveling.HitPointRegen(level)),
            new CharacterStat(CharacterStatId.MaxMana, maxMana),
            new CharacterStat(CharacterStatId.ManaRegen, JobLeveling.ManaRegen(level)),
            new CharacterStat(CharacterStatId.MeleeChanceToHit, 100),
            new CharacterStat(CharacterStatId.MeleeWeaponDamageMultiplier, 1f),
            new CharacterStat(CharacterStatId.MeleeHandToHandDamage, 1),
            new CharacterStat(CharacterStatId.EquippedMeleeWeaponDamage, 1),
            new CharacterStat(CharacterStatId.MeleeAttackIntervalMs, 2000),
            new CharacterStat(CharacterStatId.DamageMultiplier, 1f),
            new CharacterStat(CharacterStatId.HealingMultiplier, 1f),
            new CharacterStat(CharacterStatId.AbilityCriticalHitMultiplier, 1f),
            new CharacterStat(CharacterStatId.HeadInflationPercent, 100),
            new CharacterStat(CharacterStatId.RangeMultiplier, 1f),
            new CharacterStat(CharacterStatId.FactoryProductionModifier, 1f),
            new CharacterStat(CharacterStatId.FactoryYieldModifier, 1f),
            new CharacterStat(CharacterStatId.InCombatHitPointRegen, 6),
            new CharacterStat(CharacterStatId.InCombatManaRegen, 4));

        if (refill || CurrentHitpoints > maxHealth || CurrentHitpoints <= 0)
            CurrentHitpoints = maxHealth;
        if (refill || CurrentMana > maxMana)
            CurrentMana = maxMana;

        SendHealthMana();
    }

    /// <summary>
    /// Pushes current HP and mana (with their level-scaled maximums) to the client. Sends both the
    /// self-HUD packets (ClientUpdate 38/1 hitpoints, 38/13 mana) AND the over-head bar packets
    /// (PlayerUpdate 35/5 hitpoints, 35/9 mana) so both the HUD and the bar over the character update.
    /// </summary>
    public void SendHealthMana()
    {
        int maxHealth = Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : CurrentHitpoints;
        int maxMana = Stats.TryGetValue(CharacterStatId.MaxMana, out var mm) ? mm.Int : CurrentMana;

        // Self HUD.
        SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = CurrentHitpoints, MaxHitpoints = maxHealth });
        SendTunneled(new ClientUpdatePacketMana { CurrentMana = CurrentMana, MaxMana = maxMana });

        // Over-head bars, visible to self + nearby players.
        SendTunneledToVisible(new PlayerUpdatePacketUpdateHitpoints
        {
            Guid = Guid,
            Hitpoints = CurrentHitpoints,
            MaxHitpoints = maxHealth
        }, sendToSelf: true);

        SendTunneledToVisible(new PlayerUpdatePacketUpdateMana
        {
            Guid = Guid,
            CurrentMana = CurrentMana,
            MaxMana = maxMana
        }, sendToSelf: true);
    }

    /// <summary>Plays the job level-up celebration effect on the player (visible to nearby players too).</summary>
    private void PlayLevelUpCelebration()
    {
        // A single PFX_levelup_big burst (~2s). We used to re-fire it several times over ~5s to "sustain" it,
        // but that read as the level-up effect playing 3-4 times in a row — and the full-screen JobLevelUp UI
        // is the real celebration, so one clean burst alongside it is enough.
        FireLevelUpBurst();
    }

    /// <summary>One level-up particle burst at the player's current position (guarded against post-logout sends).</summary>
    private void FireLevelUpBurst()
    {
        if (!Visible)
            return;

        SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = Guid,
            CompositeEffectId = LevelUpCompositeEffect,
            Position = Position
        }, sendToSelf: true);
    }

    public void UpdatePosition(Vector4 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;

        if (Visible)
        {
            UpdateZoneTile();

            UpdateZoneArea();
        }
    }

    private void UpdateZoneTile()
    {
        var newZoneTile = Zone.GetTileFromPosition(Position);

        if (newZoneTile == ZoneTile)
            return;

        Zone.UpdateEntityZoneTile(this, ZoneTile, newZoneTile);

        ZoneTile = newZoneTile;
    }

    public void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation)
    {
        // Preserve the original hardcoded values for existing (deep-mines test) callers.
        TeleportToZone(zone, position, rotation, "sky_deep_mines.xml", 214);
    }

    // INSTANCE (Frostfang Fury): overload with explicit sky/geometry so real zone transfers (e.g. the
    // sg_random_encounter_clearing arena) can use the destination world's own sky (null) instead of the
    // deep-mines test values. This is the PROPER server-side zone handoff — tiles/visibility rebuilt,
    // OverrideUpdateRadius=true (the client's case-31 handler feeds this to ActorManager::SetOverrideUpdateRadius;
    // without it NPCs in the new world get distance-culled -> the "invisible wolves" bug).
    public void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation, string? sky, int geometryId)
    {
        if (Zone == zone)
        {
            // Same-zone teleport: skip zone membership changes, just reset visibility and reposition.
            foreach (var visiblePlayer in VisiblePlayers)
                visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

            OnRemoveVisibleNpcs(VisibleNpcs.Values);
            OnRemoveVisiblePlayers(VisiblePlayers.Values);

            ZoneTile.Entities.Remove(Guid, out _);
            ZoneTile = ZoneTile.Empty;

            Visible = false;

            UpdatePosition(position, rotation);

            var sameZonePacket = new PacketClientBeginZoning
            {
                Name = Zone.Name,
                Position = position,
                Rotation = rotation,
                Sky = sky,               // honor the caller's sky (was hardcoded deep-mines) — the 3-arg
                Id = Zone.Id,            // overload still passes the deep-mines values for its old callers
                GeometryId = geometryId, // (was hardcoded 214)
                OverrideUpdateRadius = true
            };

            SendTunneled(sameZonePacket);
            return;
        }

        if (Zone is StartingZone)
        {
            StartingZonePosition = Position;
            StartingZoneRotation = Rotation;
        }

        if (Mount is not null)
            Mount.TeleportToZone(zone, position, rotation);

        // Alert/Remove visible entities
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        OnRemoveVisibleNpcs(VisibleNpcs.Values);
        OnRemoveVisiblePlayers(VisiblePlayers.Values);

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemovePlayer(Guid);

        // Add to new zone/zonetile
        zone.TryAddPlayer(this);

        // Teleport to new zone
        Visible = false;

        Zone = zone;

        ZoneTile = ZoneTile.Empty;

        UpdatePosition(position, rotation);

        var packetClientBeginZoning = new PacketClientBeginZoning
        {
            Name = Zone.Name,
            Position = position,
            Rotation = rotation,
            Sky = sky,
            Id = Zone.Id,
            GeometryId = geometryId,
            OverrideUpdateRadius = true
        };

        SendTunneled(packetClientBeginZoning);
    }

    private void UpdateZoneArea()
    {
        if (Zone is not StartingZone startingZone)
            return;

        var zoneAreaId = startingZone.GetZoneAreaId(Position);

        if (ZoneAreaId == zoneAreaId)
            return;

        ZoneAreaId = zoneAreaId;

        var packetPOIChangeMessage = new PacketPOIChangeMessage
        {
            ZoneId = zoneAreaId
        };

        SendTunneled(packetPOIChangeMessage);
    }

    public void UpdateCharacterStats(params CharacterStat[] characterStats)
    {
        var clientUpdatePacketUpdateStat = new ClientUpdatePacketUpdateStat
        {
            Guid = Guid
        };

        clientUpdatePacketUpdateStat.Stats.AddRange(characterStats);

        SendTunneled(clientUpdatePacketUpdateStat);

        foreach (var characterStat in characterStats)
        {
            Stats[characterStat.Id] = characterStat;

            if (characterStat.Id == CharacterStatId.MaxMovementSpeed)
            {
                var playerUpdatePacketExpectedSpeed = new PlayerUpdatePacketExpectedSpeed
                {
                    Guid = Guid,
                    ExpectedSpeed = characterStat.Float
                };

                SendTunneledToVisible(playerUpdatePacketExpectedSpeed);
            }
        }
    }

    #endregion

    #region Events

    public void OnAddVisibleNpcs(params IEnumerable<Npc> npcs)
    {
        foreach (var npc in npcs)
        {
            var playerUpdatePacketAddNpc = npc.GetAddNpcPacket();

            // Vendors bake a static badge into the AddNpc packet itself (npc.NotificationImageSetId).
            // Quest badges are per-player, so override that field per-recipient here - this is likely
            // the primary mechanism the client uses for the badge, not just the separate NotificationInfo packet.
            playerUpdatePacketAddNpc.NotificationImageSetId = GetNotificationImageId(npc);

            // EXPERIMENT: Unknown68 sits immediately next to NotificationImageSetId in the wire
            // format - testing whether it's the "quest this NPC offers" id, since that's the only
            // unexplored field adjacent to a field we've already confirmed matters.
            playerUpdatePacketAddNpc.Unknown68 = GetOfferedQuestId(npc);

            SendTunneled(playerUpdatePacketAddNpc);

            // Damageable hostiles (quest kill targets, world combat NPCs) need their attack cursor
            // (NpcRelevance) + health bar as soon as they come into view, not just at zone load.
            if (npc.IsDamageable)
            {
                Zone.SendNpcRelevance(this, npc);
                Zone.SendNpcHealth(this, npc);
            }
        }

        var playerUpdatePacketNpcRelevance = new PlayerUpdatePacketNpcRelevance();

        foreach (var npc in npcs)
        {
            if (npc.CursorId == 0)
                continue;

            var hasCursor = GetNotificationImageId(npc) != 0;

            playerUpdatePacketNpcRelevance.Entries.Add(new PlayerUpdatePacketNpcRelevance.Entry
            {
                Guid = npc.Guid,
                Unknown = true,
                CursorId = npc.CursorId,
                HasCursor = hasCursor
            });
        }

        if (playerUpdatePacketNpcRelevance.Entries.Count > 0)
            SendTunneled(playerUpdatePacketNpcRelevance);

        var notifications = new PlayerUpdatePacketAddNotifications();

        foreach (var npc in npcs)
        {
            // Combat-encounter "Battle Starter" badge (img-24): red crossed-swords over the head + red minimap
            // dot. Type 3 = combat category; Unknown3=7 / Unknown10=1 are the live 2014 combat-badge values.
            if (npc.CombatEncounterBadgeImageId != 0)
            {
                notifications.Notifications.Add(new NotificationInfo
                {
                    Guid = npc.Guid,
                    ImageId = npc.CombatEncounterBadgeImageId,
                    Type = 3,
                    Unknown3 = 7,
                    Unknown10 = true,
                });
                continue;
            }

            var imageId = GetNotificationImageId(npc);

            if (imageId == 0)
                continue;

            notifications.Notifications.Add(new NotificationInfo
            {
                Guid = npc.Guid,
                Combat = false,
                ImageId = imageId,
                NameId = npc.NameId,
                SubTextId = npc.SubTextNameId,
            });
        }

        if (notifications.Notifications.Count > 0)
            SendTunneled(notifications);

        foreach (var npc in npcs)
            VisibleNpcs.TryAdd(npc.Guid, npc);
    }

    /// <summary>
    /// Quest badges are per-player (unlike vendor badges, which are static on the Npc entity),
    /// since they depend on this player's own quest progress.
    /// </summary>
    public int GetNotificationImageId(Npc npc)
    {
        var quests = _resourceManager.Quests;

        // Giver: "!" if this NPC gives a quest the player can currently take.
        if (quests.ByGiver.TryGetValue(npc.Guid, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.IsOfferableFor(Quests))
                    return quest.NotificationAvailable;
            }
        }

        // Target: "?" if the player has an active (accepted, not completed) quest that turns in here.
        if (quests.ByTarget.TryGetValue(npc.Guid, out var targetQuestIds))
        {
            foreach (var questId in targetQuestIds)
            {
                if (Quests.TryGetValue(questId, out var completed) && !completed && quests.TryGet(questId, out var quest))
                    return quest.NotificationActive;
            }
        }

        return npc.NotificationImageSetId;
    }

    /// <summary>
    /// AddNpc.Unknown68 sits next to NotificationImageSetId; used to carry the "quest this NPC offers"
    /// id. Returns the first currently-offerable quest this NPC gives, else 0.
    /// </summary>
    public int GetOfferedQuestId(Npc npc)
    {
        var quests = _resourceManager.Quests;

        if (quests.ByGiver.TryGetValue(npc.Guid, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.IsOfferableFor(Quests))
                    return questId;
            }
        }

        return 0;
    }

    public void OnAddVisiblePlayers(params IEnumerable<Player> players)
    {
        foreach (var player in players)
        {
            var playerUpdatePacketAddPc = player.GetAddPcPacket();

            SendTunneled(playerUpdatePacketAddPc);
        }

        foreach (var player in players)
            VisiblePlayers.TryAdd(player.Guid, player);
    }

    public void OnRemoveVisibleNpcs(params IEnumerable<Npc> npcs)
    {
        foreach (var npc in npcs)
        {
            if (npc is Mount mount)
            {
                var playerUpdatePacketRemovePlayerGracefully = new PlayerUpdatePacketRemovePlayerGracefully();

                playerUpdatePacketRemovePlayerGracefully.Guid = npc.Guid;

                playerUpdatePacketRemovePlayerGracefully.Animate = false;
                playerUpdatePacketRemovePlayerGracefully.Delay = 0;
                playerUpdatePacketRemovePlayerGracefully.EffectDelay = 0;
                playerUpdatePacketRemovePlayerGracefully.CompositeEffectId = 46;
                playerUpdatePacketRemovePlayerGracefully.Duration = 1000;

                SendTunneled(playerUpdatePacketRemovePlayerGracefully);
            }
            else if (npc.GracefulRemoval is { } graceful)
            {
                // Live-server despawn (04-01 capture): the ONE graceful-remove packet carries the whole
                // death presentation — Animate=true plays the model's own death clip client-side, the
                // composite effect (5017 poof) fires and the actor despawns after Delay ms. No separate
                // SetAnimation / PlayCompositeEffect packets are needed (the real server sends none).
                var packet = new PlayerUpdatePacketRemovePlayerGracefully();

                packet.Guid = npc.Guid;

                packet.Animate = graceful.Animate;
                packet.Delay = graceful.Delay;
                packet.EffectDelay = graceful.EffectDelay;
                packet.CompositeEffectId = graceful.EffectId;
                packet.Duration = graceful.Duration;

                SendTunneled(packet);
            }
            else
            {
                var playerUpdatePacketRemovePlayer = new PlayerUpdatePacketRemovePlayer();

                playerUpdatePacketRemovePlayer.Guid = npc.Guid;

                SendTunneled(playerUpdatePacketRemovePlayer);
            }
        }

        foreach (var npc in npcs)
            VisibleNpcs.TryRemove(npc.Guid, out _);
    }

    public void OnRemoveVisiblePlayers(params IEnumerable<Player> players)
    {
        foreach (var player in players)
        {
            var playerUpdatePacketRemovePlayer = new PlayerUpdatePacketRemovePlayer();

            playerUpdatePacketRemovePlayer.Guid = player.Guid;

            SendTunneled(playerUpdatePacketRemovePlayer);
        }

        foreach (var player in players)
            VisiblePlayers.TryRemove(player.Guid, out _);
    }

    public void OnInteract(Player player)
    {
        var commandPacketInteractionList = new CommandPacketInteractionList();

        commandPacketInteractionList.List.Guid = Guid;

        commandPacketInteractionList.List.Interactions.Add(InspectInteraction.Data);

        if (Friends.Any(x => x.Guid == player.Guid))
        {
            commandPacketInteractionList.List.Interactions.Add(RemoveFriendInteraction.Data);
        }
        else
        {
            commandPacketInteractionList.List.Interactions.Add(AddFriendInteraction.Data);
        }

        if (player.Ignores.Any(x => x.Guid == Guid))
        {
            commandPacketInteractionList.List.Interactions.Add(StopIgnoringInteraction.Data);
        }
        else
        {
            commandPacketInteractionList.List.Interactions.Add(IgnoreInteraction.Data);
        }

        player.SendTunneled(commandPacketInteractionList);
    }

    #endregion

    public int GetFlairShardCompositeEffect()
    {
        const int FlairShardSlot = 13;

        if (ActiveProfile.Items.TryGetValue(FlairShardSlot, out var profileItem))
        {
            var clientItem = Items.FirstOrDefault(x => x.Id == profileItem.Id);

            if (clientItem is not null)
            {
                if (_resourceManager.ClientItemDefinitions.TryGetValue(clientItem.Definition, out var clientItemDefinition))
                    return clientItemDefinition.CompositeEffectId;
            }
        }

        return 0;
    }

    public List<CharacterAttachmentData> GetAttachments()
    {
        var list = new List<CharacterAttachmentData>();

        foreach (var profileItem in ActiveProfile.Items)
        {
            var attachment = GetAttachment(profileItem.Key);

            if (attachment is null)
                continue;

            list.Add(attachment);
        }

        return list;
    }

    // COMBAT WIP: the item-definition id of the weapon currently equipped in the weapon slot (7), or 0 if
    // none. Used to drive the ability toolbar off the equipped weapon (see Combat/NinjaWeaponAbilities).
    public int GetEquippedWeaponDefinitionId()
    {
        if (!ActiveProfile.Items.TryGetValue(7, out var profileItem))
            return 0;

        var clientItem = Items.FirstOrDefault(x => x.Id == profileItem.Id);

        return clientItem?.Definition ?? 0;
    }

    public CharacterAttachmentData? GetAttachment(int slot)
    {
        if (!ActiveProfile.Items.TryGetValue(slot, out var profileItem))
            return null;

        var clientItem = Items.FirstOrDefault(x => x.Id == profileItem.Id);

        if (clientItem is null)
            return null;

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(clientItem.Definition, out var clientItemDefinition))
            return null;

        var compositeEffectId = clientItemDefinition.CompositeEffectId;

        // Update the Weapon composite effect if we have a Flair Shard equipped.
        if (slot == 7)
        {
            var flairShardcompositeEffectId = GetFlairShardCompositeEffect();

            if (flairShardcompositeEffectId > 0)
                compositeEffectId = flairShardcompositeEffectId;
        }

        return new CharacterAttachmentData
        {
            ModelName = clientItemDefinition.ModelName,
            TextureAlias = clientItemDefinition.TextureAlias,
            TintAlias = clientItemDefinition.TintAlias,
            TintId = clientItem.Tint,
            CompositeEffectId = compositeEffectId,
            Slot = clientItemDefinition.Slot
        };
    }

    public PlayerUpdatePacketAddPc GetAddPcPacket()
    {
        var packet = new PlayerUpdatePacketAddPc
        {
            Guid = Guid,

            Name = Name,

            Model = Model,

            ChatBubbleForegroundColor = ChatBubbleForegroundColor,
            ChatBubbleBackgroundColor = ChatBubbleBackgroundColor,
            ChatBubbleSize = ChatBubbleSize,

            Position = Position,
            Rotation = Rotation,

            Attachments = GetAttachments(),

            Head = Head,
            Hair = Hair,

            HairColor = HairColor,
            EyeColor = EyeColor,

            SkinTone = SkinTone,

            FacePaint = FacePaint,
            ModelCustomization = ModelCustomization,

            MaxMovementSpeed = Stats[CharacterStatId.MaxMovementSpeed],

            IsUnderage = Age < 18,
            IsMember = MembershipStatus != 0,

            TemporaryAppearance = TemporaryAppearance,

            ActiveProfileId = ActiveProfileId,

            MountQueuePosition = -1,
            MountSeat = -1,
        };

        var activeTitle = Titles.FirstOrDefault(x => x.Id == ActiveTitle);

        if (activeTitle is not null)
            packet.Title = activeTitle;

        if (Mount is not null)
        {
            packet.MountGuid = Mount.Guid;
            packet.MountSeat = Mount.Seat;
            packet.MountQueuePosition = Mount.QueuePosition;

            packet.NameVerticalOffset = Mount.Definition.NameVerticalOffset;

            Debug.WriteLine($"AddPc: {Name} {Guid} | {Mount.Guid} {Mount.Seat} {Mount.QueuePosition}");
        }

        return packet;
    }


    public void ApplyTemporaryAppearance(int modelId, int durationMs, int effectId = 0)
    {
        TemporaryAppearance = modelId;
        _temporaryAppearanceEffectId = effectId;

        if (durationMs > 0)
            TemporaryAppearanceExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(durationMs);

        if (effectId != 0)
            SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect { Guid = Guid, CompositeEffectId = effectId, Position = Position }, true);

        SendTunneledToVisible(new PlayerUpdatePacketUpdateTemporaryAppearance { Guid = Guid, TemporaryAppearance = modelId }, true);
    }

    public void RemoveTemporaryAppearance()
    {
        TemporaryAppearance = 0;
        TemporaryAppearanceExpiresAt = null;

        if (_temporaryAppearanceEffectId != 0)
        {
            SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect { Guid = Guid, CompositeEffectId = _temporaryAppearanceEffectId, Position = Position }, true);
            _temporaryAppearanceEffectId = 0;
        }

        SendTunneledToVisible(new PlayerUpdatePacketRemoveTemporaryAppearance { Guid = Guid }, true);
    }

    #region Equatable

    public bool Equals(IEntity? other)
    {
        return Guid == other?.Guid;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is Player other)
            return Equals(other);

        return false;
    }

    public override int GetHashCode()
    {
        return Guid.GetHashCode();
    }

    public static bool operator ==(Player left, Player right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Player left, Player right)
    {
        return !(left == right);
    }

    #endregion

    public void Dispose()
    {
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        if (Mount is not null)
        {
            Mount.ZoneTile.Entities.Remove(Mount.Guid, out _);

            Zone.TryRemoveNpc(Mount.Guid);
            Mount = null;
        }

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemovePlayer(Guid);
    }
}
