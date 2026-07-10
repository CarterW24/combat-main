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

    /// <summary>Regenerates HP and mana toward their maximums using the level-scaled regen stats.</summary>
    private void RegenTick()
    {
        if (IsDead)
            return;

        if (!Stats.TryGetValue(CharacterStatId.MaxHealth, out var maxHpStat) ||
            !Stats.TryGetValue(CharacterStatId.MaxMana, out var maxManaStat))
            return; // stats not initialized yet

        int maxHp = maxHpStat.Int;
        int maxMana = maxManaStat.Int;
        bool changed = false;

        if (CurrentHitpoints < maxHp)
        {
            int regen = Stats.TryGetValue(CharacterStatId.HitPointRegen, out var hr) ? hr.Int : 25;
            CurrentHitpoints = Math.Min(maxHp, CurrentHitpoints + Math.Max(1, regen));
            changed = true;
        }

        if (CurrentMana < maxMana)
        {
            int regen = Stats.TryGetValue(CharacterStatId.ManaRegen, out var mr) ? mr.Int : 4;
            CurrentMana = Math.Min(maxMana, CurrentMana + Math.Max(1, regen));
            changed = true;
        }

        if (changed)
            SendHealthMana();
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

    public void Respawn()
    {
        IsDead = false;
        CurrentHitpoints = Stats[CharacterStatId.MaxHealth].Int;

        var hpPacket = new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = CurrentHitpoints,
            MaxHitpoints = CurrentHitpoints
        };
        SendTunneled(hpPacket);
    }

    public void TakeDamage(int amount, CombatNpc source)
    {
        if (IsDead)
            return;

        CurrentHitpoints = Math.Max(0, CurrentHitpoints - amount);

        var hpPacket = new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = CurrentHitpoints,
            MaxHitpoints = Stats[CharacterStatId.MaxHealth].Int
        };
        SendTunneled(hpPacket);

        if (CurrentHitpoints <= 0)
            IsDead = true;
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

        var profile = ActiveProfile;
        if (profile.Rank >= JobLeveling.MaxLevel)
            return; // already max level - no more XP

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
            // Level number + level-up display, then rescale HP/mana to the new rank.
            SendTunneled(new ClientUpdatePacketUpdateProfileRank
            {
                ProfileId = profile.Id,
                NewRank = profile.Rank,
                ProfileIconId = profile.Icon,
                ProfileNameId = profile.NameId
            });

            RecalculateStats(refill: true);
            PlayLevelUpCelebration(); // visible particle burst on the character

            // Full-screen job level-up UI (levelup_<job>.gfx) via the "JobLevelUp" client event. This is a
            // ClientUpdate 38/15 (NOT ability 36/15 - verified by live client trace): the client reads a single
            // length-prefixed payload and parses it as a profile, so we send the serialized active profile.
            using (var jluWriter = new PacketWriter())
            {
                profile.Serialize(jluWriter);
                SendTunneled(new ClientUpdatePacketJobLevelUp { Payload = jluWriter.Buffer });
            }
        }

        // Re-send the full profile so the Jobs panel's level + XP bar (RankPercent) reflect immediately.
        RefreshActiveProfile();
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
            CurrentHitpoints = CurrentHitpoints,
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
        // PFX_levelup_big is a ~2s one-shot, so a single play looked like it "disappeared". Re-fire it at the
        // player's CURRENT position a few times over ~5s: overlapping plays keep it sustained, and re-anchoring
        // on the live Position each time makes it track the player if they move during the celebration.
        FireLevelUpBurst();
        for (int i = 1; i <= 4; i++)
            System.Threading.Tasks.Task.Delay(i * 1000).ContinueWith(_ => FireLevelUpBurst());
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
                Sky = "sky_deep_mines.xml",
                Id = Zone.Id,
                GeometryId = 214,
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
            Sky = "sky_deep_mines.xml",
            Id = Zone.Id,
            GeometryId = 214,
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
            SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect { Guid = Guid, CompositeEffectId = effectId, Position = Position, Clear = false }, true);

        SendTunneledToVisible(new PlayerUpdatePacketUpdateTemporaryAppearance { Guid = Guid, TemporaryAppearance = modelId }, true);
    }

    public void RemoveTemporaryAppearance()
    {
        TemporaryAppearance = 0;
        TemporaryAppearanceExpiresAt = null;

        if (_temporaryAppearanceEffectId != 0)
        {
            SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect { Guid = Guid, CompositeEffectId = _temporaryAppearanceEffectId, Position = Position, Clear = false }, true);
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
        Mount?.Dispose();
        Mount = null;

        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemovePlayer(Guid);
    }
}
