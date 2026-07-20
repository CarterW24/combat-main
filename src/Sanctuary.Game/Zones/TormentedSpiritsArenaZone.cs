using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class TormentedSpiritsArenaZone : CombatEncounterZone
{
    private sealed class TormentedSpiritsArenaDefinition : BaseZoneDefinition
    {
    }

    private const float GroundY = 2f;
    private const float CenterX = 141f;
    private const float CenterZ = 160f;

    private float _groundY = GroundY;

    public const int EncounterId = 146;
    public const int EncounterInstanceId = 1;

    public const int TitleNameId = 75999;
    public const int DescriptionId = 76363;
    public const int Difficulty = 2;
    public const int IconId = 1345;

    public const int EntryNpcNameId = 76190;

    private const int CombatMiniGameType = 4;

    private const int SpiritModelId = 10;
    private const int SpiritNameId = EntryNpcNameId;
    private const int SpiritActiveProfile = 151;

    private const int SpiritHealth = 1500;
    private const float SpiritAggroRange = 14f;

    private const int SpawnPoofFxId = 46;
    private const int DeathPoofFxId = 5017;
    private const int SpiritDeathHoldMs = 2000;

    private const int CharState_Baseline = 0x1;
    private const int CharState_Charging = 0x8001;

    private const int TombstoneNameId = 76373;
    private const int TombstoneHealth = 1500;
    private static readonly int[] TombstoneModelIds = [893, 894, 896];

    private static readonly Vector3[] SpiritSpawns =
    [
        new(120f, GroundY, 148f), new(158f, GroundY, 145f), new(130f, GroundY, 175f),
        new(152f, GroundY, 178f), new(115f, GroundY, 165f), new(165f, GroundY, 162f),
        new(138f, GroundY, 190f), new(125f, GroundY, 133f), new(155f, GroundY, 133f),
        new(170f, GroundY, 180f), new(112f, GroundY, 182f), new(147f, GroundY, 161f),
    ];

    private static readonly Vector3[] TombstoneSpawns =
    [
        new(125f, GroundY, 155f), new(155f, GroundY, 170f), new(140f, GroundY, 185f),
    ];

    private const int DoorModelId = 846;
    private const int DoorNameId = 4826;
    private const float DoorScale = 1.2f;
    private const int DoorInteractRange = 125;
    private const int DoorActiveProfile = 28;
    private const int DoorCursorId = 17;
    private const int DoorMinimapImageId = 186;
    private const int DoorBadgeType = 7;
    private const int DoorBadgeUnknown3 = 102;
    private static readonly Vector4 DoorSpawn = new(141f, GroundY, 148f, 1f);

    private const int CoinsModelId = 841;
    private const int CoinsNameId = 139649;
    private const int CoinsPopFxId = 5192;
    private const float CoinsKnockMagnitude = 0.0712f;
    private const int HeartModelId = 736;
    private const int HeartHeal = 125;
    private const float HeartPickupRange = 2.6f;
    private const int HeartDropPercent = 12;
    private const int HeartPickupFxId = 15032;
    private const int HealShowerFxId = 15921;
    private const int HealShowerMs = 15000;
    private int _healTagCounter = 300;
    private readonly List<Npc> _hearts = [];

    private const int WolfMovementTypePhysics = 2;

    private const int GoalBanishSpirits = 12646;
    private const int GoalBanishSpiritsNameId = 76354;

    public const int EncounterXp = 15;

    private sealed class SpiritState : EncounterMobState { }

    private readonly object _stateLock = new();
    private readonly List<Npc> _spirits = [];
    private readonly Dictionary<ulong, SpiritState> _spiritStates = [];
    private readonly List<Npc> _tombstones = [];
    private int _killedSpirits;
    private bool _won;
    private int _encounterRun;

    private readonly List<Player> _activePlayers = [];
    private Player? _anchor;

    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly Sanctuary.Game.Quests.IQuestManager _questManager;
    private readonly Random _rng = new();

    public TormentedSpiritsArenaZone(IServiceProvider serviceProvider)
        : base(CreateDefinition(), serviceProvider)
    {
        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _questManager = serviceProvider.GetRequiredService<Sanctuary.Game.Quests.IQuestManager>();
    }

    private static BaseZoneDefinition CreateDefinition() => new TormentedSpiritsArenaDefinition
    {
        Id = EncounterId,
        Name = "bs_random_encounter_01",
        TileSize = 64,
        StartLongitude = -2,
        EndLongitude = 8,
        StartLatitude = -2,
        EndLatitude = 8,
        Sky = null,
        SpawnPosition = new Vector4(141f, GroundY + 12f, 118f, 1f),
        SpawnRotation = Quaternion.Identity,
    };

    #region Zone lifecycle

    public override void OnClientIsReady(Player player)
    {
        EnterAtFullVitals(player);

        player.SendTunneled(new PacketZoneDoneSendingInitialData());
        player.SendTunneled(new ClientUpdatePacketDoneSendingPreloadCharacters());

        JobWeaponAbilities.SendToolbarWithFxPreload(player, _resourceManager);
    }

    public override void OnClientFinishedLoading(Player player)
    {
        base.OnClientFinishedLoading(player);

        ActivePlayers();

        bool first;
        lock (_stateLock)
        {
            if (!_activePlayers.Any(p => p.Guid == player.Guid))
                _activePlayers.Add(player);
            first = _activePlayers.Count == 1;
        }

        if (first)
        {
            _anchor = player;
            StartEncounter(player);
        }
        else
        {
            _logger.LogInformation("Spirit arena: {name} joined the party fight (member #{n}).",
                player.Name, _activePlayers.Count);
            DeliverEntrySequence(player, _encounterRun);
            PushLiveEncounterTo(player);
        }
    }

    protected override void Broadcast(ISerializablePacket packet)
    {
        foreach (var p in ActivePlayers())
            p.SendTunneled(packet);
    }

    private Player[] ActivePlayers()
    {
        lock (_stateLock)
        {
            _activePlayers.RemoveAll(p => p.Zone != this);
            if (_anchor is not null && _anchor.Zone != this)
                _anchor = _activePlayers.Count > 0 ? _activePlayers[0] : null;
            return [.. _activePlayers];
        }
    }

    private void PushLiveEncounterTo(Player player)
    {
        List<Npc> live = [];
        lock (_stateLock)
        {
            live.AddRange(_spirits);
            live.AddRange(_tombstones);
            live.AddRange(_hearts);
            if (ExitDoor is { } exitDoor) live.Add(exitDoor);
        }
        foreach (var npc in live)
        {
            player.OnAddVisibleNpcs(npc);
            npc.OnAddVisiblePlayers(player);
            SendNpcRelevance(player, npc);
        }
    }

    #endregion

    #region Encounter

    private void StartEncounter(Player player)
    {
        lock (_stateLock)
        {
            foreach (var old in _spirits)
                old.Dispose();
            _spirits.Clear();
            _spiritStates.Clear();
            foreach (var t in _tombstones)
                t.Dispose();
            _tombstones.Clear();
            foreach (var h in _hearts)
                h.Dispose();
            _hearts.Clear();
            ExitDoor?.Dispose();
            SetExitDoor(null);
            _killedSpirits = 0;
            _won = false;
            _groundY = GroundY;
            _encounterRun++;

            var guids = new List<ulong>(SpiritSpawns.Length + TombstoneSpawns.Length);
            foreach (var pt in SpiritSpawns)
            {
                var spirit = CreateSpirit(player, new Vector4(pt.X, pt.Y, pt.Z, 1f), spawnFx: 0);
                if (spirit is null)
                    continue;
                _spirits.Add(spirit);
                _spiritStates[spirit.Guid] = new SpiritState { SlotAngle = (float)(_rng.NextDouble() * Math.Tau), Home = spirit.Position };
                guids.Add(spirit.Guid);
            }

            for (var i = 0; i < TombstoneSpawns.Length; i++)
            {
                var tomb = CreateTombstone(player, TombstoneModelIds[i % TombstoneModelIds.Length], TombstoneSpawns[i]);
                if (tomb is null)
                    continue;
                _tombstones.Add(tomb);
                guids.Add(tomb.Guid);
            }

            SendCombatMinimapMarkers(player, guids);
        }

        DeliverEntrySequence(player, _encounterRun);

        _logger.LogInformation(
            "Spirit arena: encounter start for {name} — {spirits} spirits + {tombs} tombstones pre-spawned.",
            player.Name, SpiritSpawns.Length, TombstoneSpawns.Length);

        var groundRun = _encounterRun;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000);

                if (player.Zone != this || groundRun != _encounterRun)
                    return;

                var measured = player.Position.Y;
                _logger.LogInformation("Spirit arena: ground adoption — player settled at y={y} (guess was {guess}).",
                    measured, GroundY);

                if (MathF.Abs(measured - GroundY) < 0.75f)
                    return;

                Npc[] spirits;
                Npc[] tombstones;
                lock (_stateLock)
                {
                    _groundY = measured;
                    spirits = [.. _spirits];
                    tombstones = [.. _tombstones];
                }

                foreach (var actor in spirits)
                {
                    bool idle;
                    lock (_stateLock)
                        idle = _spiritStates.TryGetValue(actor.Guid, out var s) && !s.Charging;
                    if (!idle)
                        continue;

                    var p = actor.Position;
                    var lifted = new Vector4(p.X, measured, p.Z, p.W);
                    actor.UpdatePosition(lifted, actor.Rotation);
                    Broadcast(new PlayerUpdatePacketUpdatePosition
                    {
                        Guid = actor.Guid, Position = lifted, Rotation = actor.Rotation, State = 1, Unknown = 0,
                    });
                }

                foreach (var tomb in tombstones)
                {
                    var p = tomb.Position;
                    var lifted = new Vector4(p.X, measured, p.Z, p.W);
                    tomb.UpdatePosition(lifted, tomb.Rotation);
                    Broadcast(new PlayerUpdatePacketUpdatePosition
                    {
                        Guid = tomb.Guid, Position = lifted, Rotation = tomb.Rotation, State = 1, Unknown = 0,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spirit arena: ground adoption failed.");
            }
        });

        StartSpiritAi(player, _encounterRun);
    }

    private void DeliverEntrySequence(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);

                if (player.Zone != this || run != _encounterRun)
                    return;

                EncounterDetailsResponsePacket MakeLaunch() => new()
                {
                    Unknown = EncounterId,
                    Unknown2 = EncounterInstanceId,
                    NameId = TitleNameId,
                    DescriptionId = DescriptionId,
                    Difficulty = Difficulty,
                    IconId = IconId,
                    MiniGameType = CombatMiniGameType,
                    Launch = true,
                    Objectives =
                    [
                        new EncounterObjective
                        {
                            ObjectiveId = GoalBanishSpirits, NameId = GoalBanishSpiritsNameId,
                            DescriptionId = GoalBanishSpiritsNameId,
                            Status = 1, Count = 0, Total = 1, Unknown8 = 0,
                        },
                    ],
                    PreviewRewards = FrostfangArenaZone.GetPrizePreviewFor(player),
                    PreviewCoins = FrostfangArenaZone.PrizeCoins,
                    PreviewXp = FrostfangArenaZone.PrizeXp,
                    ProfileType = FrostfangArenaZone.CombatProfileType,
                    ActivityId = EncounterId,
                };

                EncounterPacketPlayerEnter MakeEnter(ulong guid) => new()
                {
                    EncounterId = EncounterId,
                    InstanceId = EncounterInstanceId,
                    PlayerGuid = guid,
                };

                UiObjectiveAddPacket BanishRow() => new()
                {
                    ObjectiveId = GoalBanishSpirits,
                    NameId = GoalBanishSpiritsNameId,
                };

                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(new ObjectiveActivatePacket { ObjectiveId = GoalBanishSpirits, Total = 1 });
                player.SendTunneled(BanishRow());
                player.SendTunneled(MakeLaunch());
                player.SendTunneled(MakeEnter(0));
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(MakeLaunch());
                player.SendTunneled(BanishRow());
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(PacketEncounterDataCommon.CreateCombatRules());
                player.SendTunneled(MakeEnter(player.Guid));

                // NO IsFighting/OverworldCombat here: never sent in-encounter — IsFighting=true is
                // what flips the knockout window to the paid overworld variant (wrong-window bug).
                player.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = EncounterId,
                    InstanceId = EncounterInstanceId,
                    State = 6,
                });

                _logger.LogInformation("Spirit arena: entry sequence delivered to {name} (run {run}).",
                    player.Name, run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spirit arena: delayed encounter-state delivery failed.");
            }
        });
    }

    private Npc? CreateSpirit(Player player, Vector4 pos, int spawnFx)
    {
        if (!TryCreateNpc(out var npc))
            return null;

        npc.ModelId = SpiritModelId;
        npc.NameId = 0;
        npc.Name = null;
        // Common mobs: no overhead plate/bar (live recipe — the target frame shows their health).
        npc.HideNamePlate = true;
        npc.ShowHealthBar = false;
        npc.Scale = 1f;
        npc.Disposition = 0;
        npc.ActiveProfile = SpiritActiveProfile;
        npc.CompositeEffectId = spawnFx;
        npc.MaxHealth = SpiritHealth;
        npc.Health = SpiritHealth;
        npc.IsInteractable = false;
        npc.InteractRange = 100;
        npc.Visible = true;
        npc.CursorId = 11;

        npc.WalkAnimId = -1;
        npc.RunAnimId = -1;
        npc.StandAnimId = -1;
        npc.MovementType = WolfMovementTypePhysics;
        npc.Speed = 0f;
        npc.RiderGuid = ulong.MaxValue;

        npc.UpdatePosition(pos, Quaternion.Identity);

        foreach (var p in ActivePlayers())
        {
            p.OnAddVisibleNpcs(npc);
            npc.OnAddVisiblePlayers(p);

            p.SendTunneled(new PlayerUpdatePacketUpdateMana { Guid = npc.Guid });
            p.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
            {
                Guid = npc.Guid,
                Status = (CharacterStatus)CharState_Baseline,
            });
            SendNpcRelevance(p, npc);
            p.SendTunneled(new PlayerUpdatePacketUpdateDisposition { Guid = npc.Guid, Disposition = 0 });
        }

        return npc;
    }

    private Npc? CreateTombstone(Player player, int modelId, Vector3 pos)
    {
        if (!TryCreateNpc(out var npc))
            return null;

        npc.ModelId = modelId;
        npc.NameId = TombstoneNameId;
        npc.Name = null;
        npc.HideNamePlate = false;
        npc.ShowHealthBar = true;
        npc.Scale = 1f;
        npc.Disposition = 0;
        npc.ActiveProfile = 1;
        npc.MaxHealth = TombstoneHealth;
        npc.Health = TombstoneHealth;
        npc.IsInteractable = false;
        npc.InteractRange = 100;
        npc.Visible = true;
        npc.CursorId = 11;
        npc.Static = true;

        npc.WalkAnimId = -1;
        npc.RunAnimId = -1;
        npc.StandAnimId = -1;
        npc.MovementType = WolfMovementTypePhysics;
        npc.RiderGuid = ulong.MaxValue;

        npc.UpdatePosition(new Vector4(pos.X, pos.Y, pos.Z, 1f), Quaternion.Identity);

        foreach (var p in ActivePlayers())
        {
            p.OnAddVisibleNpcs(npc);
            npc.OnAddVisiblePlayers(p);

            p.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
            {
                Guid = npc.Guid,
                Status = (CharacterStatus)CharState_Baseline,
            });
            SendNpcRelevance(p, npc);
            p.SendTunneled(new PlayerUpdatePacketUpdateDisposition { Guid = npc.Guid, Disposition = 0 });
            SendNpcHealth(p, npc);
        }

        return npc;
    }

    private void SendCombatMinimapMarkers(Player player, IReadOnlyList<ulong> guids)
    {
        if (guids.Count == 0)
            return;

        var badge = new PlayerUpdatePacketAddNotifications();
        foreach (var guid in guids)
            badge.Notifications.Add(new NotificationInfo { Guid = guid, Combat = true, Type = 3, Unknown10 = true });
        Broadcast(badge);
    }

    private void StartSpiritAi(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Spirit arena: AI loop started (run {run}).", run);

            try
            {

                for (var elapsed = 0; elapsed < 15 * 60 * 1000; elapsed += TickMs)
                {
                    await Task.Delay(TickMs);

                    if (run != _encounterRun)
                    {
                        _logger.LogInformation("Spirit arena: AI loop exit — superseded by a new run (run {run}).", run);
                        return;
                    }

                    var players = ActivePlayers();
                    if (players.Length == 0)
                    {
                        _logger.LogInformation("Spirit arena: AI loop exit — all players left the zone (run {run}).", run);
                        return;
                    }

                    foreach (var p in players)
                        CollectHearts(p);

                    Npc[] pack;
                    lock (_stateLock)
                        pack = [.. _spirits];

                    if (pack.Length == 0)
                        continue;

                    var now = Environment.TickCount64;
                    var dt = TickMs / 1000f;

                    foreach (var spirit in pack)
                    {
                        if (!spirit.IsAlive)
                            continue;

                        SpiritState? state;
                        lock (_stateLock)
                            _spiritStates.TryGetValue(spirit.Guid, out state);
                        if (state is null)
                            continue;

                        var here = new Vector3(spirit.Position.X, spirit.Position.Y, spirit.Position.Z);

                        var tgt = NearestLivePlayer(here, players);
                        if (tgt is null)
                        {
                            TickMobReturnHome(spirit, state, dt);
                            continue;
                        }

                        var target = new Vector3(tgt.Position.X, tgt.Position.Y, tgt.Position.Z);

                        if (!state.Charging)
                        {
                            var dx = target.X - here.X;
                            var dz = target.Z - here.Z;
                            if (dx * dx + dz * dz > SpiritAggroRange * SpiritAggroRange)
                                continue;
                            BeginCharge(tgt, spirit, state);
                        }

                        TickMobCombat(spirit, state, tgt, target, now, dt);
                    }
                }

                _logger.LogInformation("Spirit arena: AI loop exit — 15min safety timeout (run {run}).", run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spirit arena AI failed (run {run}).", run);
            }
        });
    }

    private void BeginCharge(Player player, Npc spirit, SpiritState state)
    {
        state.Charging = true;
        state.NextAttackTicks = Environment.TickCount64 + 1000 + _rng.Next(1500);

        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = spirit.Guid, ExpectedSpeed = 3f });
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = spirit.Guid, ExpectedSpeed = MobChaseSpeed });
        Broadcast(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = spirit.Guid,
            Status = (CharacterStatus)CharState_Charging,
        });
    }

    public override void OnNpcDamaged(Player player, Npc npc)
    {
        lock (_stateLock)
        {
            if (_spiritStates.TryGetValue(npc.Guid, out var state) && !state.Charging)
                BeginCharge(player, npc, state);
        }
    }

    private void SpawnHeart(Player player, Vector4 pos)
    {
        if (!TryCreateNpc(out var heart))
            return;

        heart.ModelId = HeartModelId;
        heart.Name = null;
        heart.NameId = 5102381;
        heart.Disposition = 1;
        heart.Scale = 1f;
        heart.IsInteractable = false;
        heart.InteractRange = 0;
        heart.Visible = true;
        heart.MaxHealth = 0;
        heart.ShowHealthBar = false;
        heart.HideNamePlate = true;
        heart.ActiveProfile = 8;
        heart.WalkAnimId = -1;
        heart.RunAnimId = -1;
        heart.StandAnimId = -1;
        heart.MovementType = WolfMovementTypePhysics;
        heart.RiderGuid = ulong.MaxValue;
        heart.UpdatePosition(pos, Quaternion.Identity);

        player.OnAddVisibleNpcs(heart);
        heart.OnAddVisiblePlayers(player);

        lock (_stateLock)
            _hearts.Add(heart);
    }

    private void CollectHearts(Player player)
    {
        List<Npc>? collected = null;
        lock (_stateLock)
        {
            for (var i = _hearts.Count - 1; i >= 0; i--)
            {
                var h = _hearts[i];
                var dx = player.Position.X - h.Position.X;
                var dz = player.Position.Z - h.Position.Z;
                if (dx * dx + dz * dz > HeartPickupRange * HeartPickupRange)
                    continue;
                _hearts.RemoveAt(i);
                (collected ??= []).Add(h);
            }
        }

        if (collected is null)
            return;

        foreach (var h in collected)
        {
            player.SendTunneled(new PlayerUpdatePacketHitPointModification
            {
                Guid = player.Guid,
                Guid2 = player.Guid,
                ShowFloatingText = true,
                Unknown2 = 2500,
                Unknown3 = 2500,
                Unknown4 = HeartHeal,
            });

            var tagId = ++_healTagCounter;
            player.SendTunneled(new PlayerUpdatePacketAddEffectTagCompositeEffect
            {
                Guid = player.Guid,
                TagId = tagId,
                CompositeEffectId = HealShowerFxId,
                SourceGuid = h.Guid,
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(HealShowerMs);
                    player.SendTunneled(new PlayerUpdatePacketRemoveEffectTagCompositeEffect
                    {
                        Guid = player.Guid,
                        TagId = tagId,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Spirit arena: heal-shower stop failed.");
                }
            });

            h.GracefulRemoval = (false, 0, 5000, HeartPickupFxId, 1000);
            h.Dispose();
        }
    }

    public override void OnNpcKilled(Player killer, Npc npc)
    {
        bool wasTombstone;
        bool allClear;

        lock (_stateLock)
        {
            if (_tombstones.Remove(npc))
            {
                wasTombstone = true;
            }
            else if (_spirits.Remove(npc))
            {
                _spiritStates.Remove(npc.Guid);
                _killedSpirits++;
                wasTombstone = false;
            }
            else
            {
                return;
            }

            allClear = !_won && _tombstones.Count == 0 && _spirits.Count == 0;
        }

        Broadcast(new PlayerUpdatePacketRemoveNotifications { Entries = { new() { Guid = npc.Guid } } });
        var deathPos = npc.Position;

        if (wasTombstone)
        {
            npc.GracefulRemoval = (false, 0, 0, DeathPoofFxId, 1000);
            npc.Dispose();

            if (!_won)
            {
                var spirit = CreateSpirit(killer, deathPos, SpawnPoofFxId);
                if (spirit is not null)
                {
                    lock (_stateLock)
                    {
                        _spirits.Add(spirit);
                        var state = new SpiritState { SlotAngle = (float)(_rng.NextDouble() * Math.Tau), Home = spirit.Position };
                        _spiritStates[spirit.Guid] = state;
                        BeginCharge(killer, spirit, state);
                        allClear = false;
                    }
                    SendCombatMinimapMarkers(killer, [spirit.Guid]);
                    _logger.LogInformation("Spirit arena: tombstone destroyed -> a spirit materializes ({left} tombs left).",
                        _tombstones.Count);
                }
            }
        }
        else
        {
            npc.GracefulRemoval = (true, SpiritDeathHoldMs, 0, DeathPoofFxId, 1000);
            npc.Dispose();

            if (_rng.Next(100) < HeartDropPercent)
                SpawnHeart(killer, deathPos);
        }

        if (allClear)
            WinEncounter(killer, deathPos);
    }

    private void WinEncounter(Player player, Vector4 lastKillPos)
    {
        lock (_stateLock)
            _won = true;

        SpawnHeart(player, lastKillPos);
        SpawnCoinPop(player, lastKillPos);

        var enemies = _killedSpirits;
        var knockoutsLeft = KnockoutLimit;
        MiniGameGameEndScorePacket MakeScore()
        {
            var s = new MiniGameGameEndScorePacket();
            s.Rows.Add(new MiniGameScoreRow { Name = "scoreEnemiesDefeated", Order = 0, Value = enemies, Points = enemies * 300 });
            s.Rows.Add(new MiniGameScoreRow { Name = "scorePlayerKnockouts", Order = 3, Value = knockoutsLeft, Max = KnockoutLimit, Points = knockoutsLeft * 5000 });
            s.Rows.Add(new MiniGameScoreRow { Name = "scoreTotalScore", Order = 4, Points = enemies * 300 + knockoutsLeft * 5000 });
            return s;
        }

        foreach (var member in ActivePlayers())
        {
            member.SendTunneled(new ObjectiveCompletePacket { ObjectiveId = GoalBanishSpirits });
            member.SendTunneled(new UiObjectiveCompletePacket { ObjectiveId = GoalBanishSpirits });

            member.AwardXp(EncounterXp);
            member.SendTunneled(new RewardBundlePacket { Xp = EncounterXp });

            _questManager.OnEncounterComplete(member, EncounterId);

            var prizes = FrostfangArenaZone.GetPrizePreviewFor(member);
            var slice = _rng.Next(prizes.Count + 1);
            var wheel = new MiniGameLootWheelSetItemToLandOnPacket();
            if (slice < prizes.Count)
            {
                member.PendingWheelPrize = prizes[slice];
                member.PendingWheelCoins = 0;
                wheel.Entries.Add(prizes[slice]);
            }
            else
            {
                member.PendingWheelPrize = null;
                member.PendingWheelCoins = FrostfangArenaZone.PrizeCoins;
                wheel.Coins = FrostfangArenaZone.PrizeCoins;
            }

            member.SendTunneled(wheel);
            member.SendTunneled(MakeScore());
        }

        SpawnExitDoor(player);

        _logger.LogInformation("Spirit arena: encounter WON — wheel armed, exit door out ({kills} spirits).", enemies);
    }

    private void SpawnCoinPop(Player player, Vector4 pos)
    {
        if (!TryCreateNpc(out var coins))
            return;

        coins.ModelId = CoinsModelId;
        coins.NameId = CoinsNameId;
        coins.Name = null;
        coins.Disposition = 1;
        coins.Scale = 1f;
        coins.IsInteractable = false;
        coins.InteractRange = 0;
        coins.Visible = true;
        coins.MaxHealth = 0;
        coins.HideNamePlate = true;
        coins.ActiveProfile = 28;
        coins.WalkAnimId = -1;
        coins.RunAnimId = -1;
        coins.StandAnimId = -1;
        coins.MovementType = WolfMovementTypePhysics;
        coins.RiderGuid = ulong.MaxValue;
        coins.UpdatePosition(new Vector4(pos.X, pos.Y + 1.5f, pos.Z, 1f), Quaternion.Identity);

        var angle = (float)(_rng.NextDouble() * Math.Tau);
        foreach (var p in ActivePlayers())
        {
            p.OnAddVisibleNpcs(coins);
            coins.OnAddVisiblePlayers(p);
            p.SendTunneled(new PlayerUpdatePacketKnockback
            {
                Guid = coins.Guid,
                Position = coins.Position,
                Direction = new Vector4(MathF.Sin(angle), 0f, MathF.Cos(angle), 0f),
                Magnitude = CoinsKnockMagnitude,
            });
            p.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = coins.Guid,
                CompositeEffectId = CoinsPopFxId,
                Position = coins.Position,
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150);
                coins.GracefulRemoval = (false, 0, 0, 0, 1000);
                coins.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spirit arena: coin-pop removal failed.");
            }
        });
    }

    private void SpawnExitDoor(Player player)
    {
        if (!TryCreateNpc(out var door))
            return;

        door.ModelId = DoorModelId;
        door.NameId = DoorNameId;
        door.Name = null;
        door.Disposition = 0;
        door.Scale = DoorScale;
        door.IsInteractable = true;
        door.InteractRange = DoorInteractRange;
        door.Visible = true;
        door.MaxHealth = 0;
        door.ShowHealthBar = false;
        door.HideNamePlate = false;
        door.ActiveProfile = DoorActiveProfile;
        door.CursorId = DoorCursorId;
        door.WalkAnimId = -1;
        door.RunAnimId = -1;
        door.StandAnimId = -1;
        door.MovementType = WolfMovementTypePhysics;
        door.RiderGuid = ulong.MaxValue;
        door.UpdatePosition(new Vector4(DoorSpawn.X, _groundY, DoorSpawn.Z, 1f), Quaternion.Identity);

        var badge = new PlayerUpdatePacketAddNotifications();
        badge.Notifications.Add(new NotificationInfo
        {
            Guid = door.Guid,
            Combat = false,
            Type = DoorBadgeType,
            Unknown3 = DoorBadgeUnknown3,
            ImageId = DoorMinimapImageId,
            DescriptionId = 0,
            NameId = DoorNameId,
            SubTextId = -1,
            Unknown8 = true,
            CompositeEffectId = 0,
            Unknown10 = true
        });

        foreach (var p in ActivePlayers())
        {
            p.OnAddVisibleNpcs(door);
            door.OnAddVisiblePlayers(p);

            p.SendTunneled(new PlayerUpdatePacketUpdateDisposition { Guid = door.Guid, Disposition = 1 });
            p.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
            {
                Guid = door.Guid,
                Status = (CharacterStatus)CharState_Baseline,
            });
            SendNpcRelevance(p, door);
            p.SendTunneled(badge);
        }

        SetExitDoor(door);
    }

    protected override int FailEncounterId => EncounterId;
    protected override int FailInstanceId => EncounterInstanceId;
    protected override string EncounterLogName => "Spirit arena";

    protected override void ReturnHome(Player player)
    {
        if (player.Zone != this)
            return;

        bool won;
        lock (_stateLock)
            won = _won;

        EndEncounterForPlayer(player, won);

        var home = _zoneManager.StartingZone;

        var returnPos = player.EncounterReturnPosition ?? home.SpawnPosition;
        player.EncounterReturnPosition = null;

        player.TeleportToZone(home, returnPos, home.SpawnRotation, sky: null, geometryId: 0);
    }

    #endregion
}
