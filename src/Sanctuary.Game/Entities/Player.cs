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

    public DateTime LastQuestAcceptedAt { get; set; }

    public DateTime LastPartyInviteAt { get; set; }

    public bool LoginBurstSent { get; set; }

    public bool JournalRestored { get; set; }

    public Sanctuary.Packet.RewardEntry? PendingWheelPrize { get; set; }
    public int PendingWheelCoins { get; set; }

    public System.Numerics.Vector4? EncounterReturnPosition { get; set; }

    public IZone Zone { get; set; }
    public ZoneTile ZoneTile { get; private set; } = ZoneTile.Empty;
    public ConcurrentDictionary<ulong, Npc> VisibleNpcs { get; } = [];
    public ConcurrentDictionary<ulong, Player> VisiblePlayers { get; } = [];

    private int ZoneAreaId { get; set; }

    public int ChatBubbleForegroundColor { get; set; }
    public int ChatBubbleBackgroundColor { get; set; }
    public int ChatBubbleSize { get; set; }

    public bool IsAdmin { get; set; }
    public bool IsMod { get; set; }
    public DateTimeOffset? MutedUntil { get; set; }

    public ClientPcProfile ActiveProfile =>
        Profiles.FirstOrDefault(x => x.Id == ActiveProfileId) ?? Profiles.First();

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

    public long InvulnerableUntilTicks { get; set; }
    public bool IsInvulnerable => Environment.TickCount64 < InvulnerableUntilTicks;

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

    public Dictionary<int, bool> Quests { get; } = new();

    public Dictionary<int, int> QuestGoalProgress { get; } = new();

    public Dictionary<int, int> QuestCollectProgress { get; } = new();

    public int ActiveQuestId { get; set; }

    public System.Action? PendingQuestEndAction { get; set; }

    private readonly ConcurrentQueue<(DateTimeOffset SendAt, ISerializablePacket Packet, bool SendToSelf)> _delayedPackets = new();

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

    public void SendTunneledToVisibleDelayed(ISerializablePacket packet, int delayMs, bool sendToSelf = false)
    {
        _delayedPackets.Enqueue((DateTimeOffset.UtcNow.AddMilliseconds(delayMs), packet, sendToSelf));
    }

    public bool IsMuted()
    {
        DateTimeOffset currentTime = DateTimeOffset.UtcNow;
        DateTimeOffset? mutedUntil = MutedUntil;
        return mutedUntil.HasValue && mutedUntil > currentTime;
    }

    public void Disconnect()
    {
        _connection.Disconnect();
    }

    #endregion

    #region Update

    public void UpdateEveryTick()
    {
        WorldCombatStateTick();

        LevelUpBurstTick();

        if (TemporaryAppearanceExpiresAt.HasValue &&
            TemporaryAppearanceExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            RemoveTemporaryAppearance();
        }

        while (_delayedPackets.TryPeek(out var delayed) && delayed.SendAt <= DateTimeOffset.UtcNow)
        {
            if (_delayedPackets.TryDequeue(out delayed))
                SendTunneledToVisible(delayed.Packet, delayed.SendToSelf);
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

    private const int OutOfCombatSeconds = 6;

    public DateTime LastCombatDamageAt { get; set; } = DateTime.MinValue;

    private void RegenTick()
    {
        if (IsDead)
            return;

        if (!Stats.TryGetValue(CharacterStatId.MaxHealth, out var maxHpStat) ||
            !Stats.TryGetValue(CharacterStatId.MaxMana, out var maxManaStat))
            return;

        int maxHp = maxHpStat.Int;
        int maxMana = maxManaStat.Int;

        bool inCombat = DateTime.UtcNow - LastCombatDamageAt < TimeSpan.FromSeconds(OutOfCombatSeconds);

        bool hpChanged = false;
        if (!inCombat && CurrentHitpoints < maxHp)
        {
            int regen = Stats.TryGetValue(CharacterStatId.HitPointRegen, out var hr) ? hr.Int : 25;
            CurrentHitpoints = Math.Min(maxHp, CurrentHitpoints + Math.Max(1, regen));
            hpChanged = true;
        }

        bool usesCombatEnergy = Combat.JobKits.Active(this)?.UsesCombatEnergy ?? false;

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

    private const int KnockdownAnimId = 1402;
    private const int GetUpAnimId = 1403;

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

    private const int ReviveEffectId = 15117;

    private const int ReviveInvulnMs = 3000;

    public void Respawn()
    {
        IsDead = false;
        InvulnerableUntilTicks = Environment.TickCount64 + ReviveInvulnMs;

        var maxHp = Stats[CharacterStatId.MaxHealth].Int;
        CurrentHitpoints = maxHp;

        SendTunneled(new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = maxHp,
            MaxHitpoints = maxHp
        });

        SendTunneled(new PlayerUpdatePacketHitPointModification
        {
            Guid = Guid,
            Guid2 = Guid,
            Unknown = true,
            Unknown2 = maxHp,
            Unknown3 = maxHp,
            Unknown4 = maxHp,
        });

        SendTunneledToVisible(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = Guid,
            Status = CharacterStatus.None,
        }, sendToSelf: true);

        _worldCombatActive = false;
        _lastWorldCombatTicks = 0;
        SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });

        SendTunneledToVisible(new PlayerUpdatePacketSetAnimation
        {
            Guid = Guid,
            AnimationId = GetUpAnimId,
        }, sendToSelf: true);
        SendTunneledToVisible(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = Guid,
            CompositeEffectId = ReviveEffectId,
            Position = Position,
        }, sendToSelf: true);

        SendSystemMessage("You have been revived!");
    }

    public void TakeDamage(int amount, CombatNpc source) => TakeDamage(amount);

    public void TakeDamage(int amount)
    {
        if (IsDead || IsInvulnerable)
            return;

        LastCombatDamageAt = DateTime.UtcNow;

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
            EnterWorldCombat();
    }

    public const int DodgeAnimId = 1406;

    public const int BaseDodgePercent = 5;

    public int DodgePercent()
    {
        var pct = BaseDodgePercent;
        if (Combat.ArcherWeaponAbilities.HasTrait(this, Combat.ArcherWeaponAbilities.ReflexesLevel))
            pct += Combat.ArcherWeaponAbilities.ReflexesDodgePercent;
        return pct;
    }

    public int ReduceIncomingDamage(int damage)
    {
        if (Combat.NinjaWeaponAbilities.HasTrait(this, Combat.NinjaWeaponAbilities.ShroudedArmorLevel))
            damage = (int)(damage * (1f - Combat.NinjaWeaponAbilities.ShroudedArmorDamageReduction));
        return Math.Max(1, damage);
    }

    public bool ForceDodgeDebug;

    public bool TryDodgeIncomingAttack(ulong attackerGuid)
    {
        if (IsDead)
            return false;
        if (!ForceDodgeDebug && Random.Shared.Next(100) >= DodgePercent())
            return false;

        SendTunneledToVisible(new CombatPacketAttackAttackerMissed { AttackerGuid = attackerGuid, TargetGuid = Guid }, sendToSelf: true);
        SendTunneledToVisible(new PlayerUpdatePacketSetAnimation { Guid = Guid, AnimationId = DodgeAnimId }, sendToSelf: true);
        return true;
    }

    private long _lastWorldCombatTicks;
    private volatile bool _worldCombatActive;

    public void EnterWorldCombat()
    {
        if (Zone is not StartingZone)
            return;
        _lastWorldCombatTicks = Environment.TickCount64;
    }

    private void WorldCombatStateTick()
    {
        if (Zone is not StartingZone)
            return;

        if (IsDead)
            return;

        bool want = _lastWorldCombatTicks != 0
            && Environment.TickCount64 - _lastWorldCombatTicks < OutOfCombatSeconds * 1000L;

        if (want == _worldCombatActive)
            return;

        _worldCombatActive = want;
        SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = want });
        SendTunneled(new EncounterPacketIsFighting { InWorldCombat = want });
    }

    public void Knockout()
    {
        if (IsDead)
            return;

        IsDead = true;
        CurrentHitpoints = 0;
        DeathPosition = Position;

        SendTunneled(new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = 0,
            MaxHitpoints = Stats[CharacterStatId.MaxHealth].Int
        });

        SendTunneledToVisible(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = Guid,
            Status = CharacterStatus.IsKnockedOut,
        }, sendToSelf: true);
        SendTunneledToVisible(new PlayerUpdatePacketSetAnimation
        {
            Guid = Guid,
            AnimationId = KnockdownAnimId,
        }, sendToSelf: true);

        SendSystemMessage("You have been knocked out!");

        Zone.OnPlayerKnockedOut(this);
    }

    public void AwardXp(int xp)
    {
        if (xp <= 0)
            return;

        if (ActiveProfile.Rank >= JobLeveling.MaxLevel)
            return;

        ApplyXp(xp);
    }

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
            profile.StarsEarned++;
        }

        if (profile.Rank >= JobLeveling.MaxLevel)
            profile.LevelXpRaw = 0;

        profile.RankPercent = JobLeveling.RankPercent(profile.Rank, profile.LevelXpRaw);

        bool leveled = profile.Rank != startLevel;

        SendTunneled(new ClientUpdatePacketUpdateProfileExperience
        {
            ProfileId = profile.Id,
            XpGained = xp,
            TotalXpInLevel = profile.LevelXpRaw,
            CurrentLevel = profile.Rank
        });

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

        if (leveled)
            ApplyLevelUpEffects();
        else
            RefreshActiveProfile();

        RestoreWeaponToolbar();
    }

    private void RestoreWeaponToolbar()
    {
        var toolbar = JobWeaponAbilities.BuildToolbar(this, _resourceManager);
        if (toolbar is not null)
            SendTunneled(toolbar);
    }

    private void ApplyLevelUpEffects()
    {
        RecalculateStats(refill: true);

        RefreshTraits();

        using var jluWriter = new PacketWriter();
        ActiveProfile.Serialize(jluWriter);
        SendTunneled(new ClientUpdatePacketJobLevelUp { Payload = jluWriter.Buffer });

        _levelUpBurstAtTicks = Environment.TickCount64 + LevelUpBurstDelayMs;
    }

    public void RefreshTraits()
    {
        var traits = Combat.JobKits.Active(this)?.BuildTraitEntries(ActiveProfile.Rank);
        if (traits is not null)
            ActiveProfile.AbilityExperiences = traits;
    }

    private AbilityExperience BuildJobAbilityExperience()
    {
        var p = ActiveProfile;
        return new AbilityExperience
        {
            Present = 1,
            NameId = p.NameId,
            DescriptionId = p.DescriptionId,
            IconId = p.Icon,
            Level = p.Rank,
            Progress = p.LevelXpRaw,
            TotalForLevel = JobLeveling.XpForLevel(p.Rank),
        };
    }

    private const int LevelUpCompositeEffect = 15117;

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

    public void RecalculateStats(bool refill = false)
    {
        int level = ActiveProfile.Rank;
        int maxHealth = JobLeveling.MaxHealth(level);
        int maxMana = JobLeveling.MaxMana(level);

        float moveSpeed = 8f;
        if (Combat.ArcherWeaponAbilities.HasTrait(this, Combat.ArcherWeaponAbilities.ReflexesLevel))
            moveSpeed *= Combat.ArcherWeaponAbilities.ReflexesSpeedMultiplier;
        if (Combat.NinjaWeaponAbilities.HasTrait(this, Combat.NinjaWeaponAbilities.NinjasGraceLevel))
            moveSpeed *= Combat.NinjaWeaponAbilities.NinjasGraceSpeedMultiplier;

        UpdateCharacterStats(
            new CharacterStat(CharacterStatId.MaxHealth, maxHealth),
            new CharacterStat(CharacterStatId.MaxMovementSpeed, moveSpeed),
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

    public void SendHealthMana()
    {
        int maxHealth = Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : CurrentHitpoints;
        int maxMana = Stats.TryGetValue(CharacterStatId.MaxMana, out var mm) ? mm.Int : CurrentMana;

        SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = CurrentHitpoints, MaxHitpoints = maxHealth });
        SendTunneled(new ClientUpdatePacketMana { CurrentMana = CurrentMana, MaxMana = maxMana });

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

    private const int LevelUpBurstDelayMs = 300;

    private long _levelUpBurstAtTicks;

    private void LevelUpBurstTick()
    {
        if (_levelUpBurstAtTicks == 0 || Environment.TickCount64 < _levelUpBurstAtTicks)
            return;

        _levelUpBurstAtTicks = 0;
        FireLevelUpBurst();
    }

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

    public void UpdatePosition(Vector4 position, Quaternion rotation, bool updateZoneArea = true)
    {
        Position = position;
        Rotation = rotation;

        Mount?.UpdatePosition(position, rotation, updateZoneArea);

        if (Visible)
        {
            UpdateZoneTile();

            if (updateZoneArea)
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
        TeleportToZone(zone, position, rotation, "sky_deep_mines.xml", 214);
    }

    public void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation, string? sky, int geometryId)
    {
        if (Zone == zone)
        {
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
                Sky = sky,
                Id = Zone.Id,
                GeometryId = geometryId,
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

        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        OnRemoveVisibleNpcs(VisibleNpcs.Values);
        OnRemoveVisiblePlayers(VisiblePlayers.Values);

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemovePlayer(Guid);

        zone.TryAddPlayer(this);

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
            if (npc is Mount)
                continue;

            var playerUpdatePacketAddNpc = npc.GetAddNpcPacket();

            playerUpdatePacketAddNpc.NotificationImageSetId = GetNotificationImageId(npc);

            // EXPERIMENT: Unknown68 sits immediately next to NotificationImageSetId in the wire
            // format - testing whether it's the "quest this NPC offers" id, since that's the only
            // unexplored field adjacent to a field we've already confirmed matters.
            playerUpdatePacketAddNpc.Unknown68 = GetOfferedQuestId(npc);

            SendTunneled(playerUpdatePacketAddNpc);

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

    public int GetNotificationImageId(Npc npc)
    {
        var quests = _resourceManager.Quests;

        if (quests.ByGiver.TryGetValue(npc.Guid, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.IsOfferableFor(Quests))
                    return quest.NotificationAvailable;
            }
        }

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
            if (player.Mount is not null)
            {
                var addPc = player.GetAddPcPacket();
                addPc.MountGuid = 0;
                addPc.MountSeat = -1;
                addPc.MountQueuePosition = -1;
                addPc.NameVerticalOffset = 0;

                SendTunneled(addPc);
                SendTunneled(player.Mount.GetAddNpcPacket());
                SendTunneled(player.Mount.GetMountResponsePacket());
            }
            else
                SendTunneled(player.GetAddPcPacket());
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
            SendTunneled(new PlayerUpdatePacketRemovePlayer { Guid = player.Guid });

            if (player.Mount is not null)
                SendTunneled(new PlayerUpdatePacketRemovePlayer { Guid = player.Mount.Guid });
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
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        if (Mount is not null)
        {
            Mount.ZoneTile.Entities.Remove(Mount.Guid, out _);

            Zone.TryRemoveNpc(Mount.Guid);
            Mount = null;
        }

        ZoneTile.Entities.Remove(Guid, out _);

        ZoneTile.Entities.Remove(Guid, out _);
        Zone.TryRemovePlayer(Guid);
    }
}
