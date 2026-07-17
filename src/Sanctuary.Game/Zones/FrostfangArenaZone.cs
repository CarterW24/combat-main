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

public sealed class FrostfangArenaZone : CombatEncounterZone
{
    private sealed class FrostfangArenaDefinition : BaseZoneDefinition
    {
    }

    private const float GroundY = 0.5f;

    public const int EncounterId = 174;
    public const int EncounterInstanceId = 1;

    private const int CombatMiniGameType = 4;

    private const int SnowWolfModelId = 176;
    private const int EvilWolfModelId = 177;
    private const int SnarlerSnowNameId = 104067;
    private const int SnarlerEvilNameId = 116023;
    private const int RoamerNameId = 115837;
    private const int AlphaNameId = 423045;
    private const int PackActiveProfile = 151;
    private const int AlphaActiveProfile = 152;

    private const int WolfHealth = 760;
    private const int AlphaHealth = 3800;
    private const float AlphaScale = 1.7f;

    private const float RoamSpeed = 3f;
    private const int RoamerHowlAnimId = 1111;
    private const int RoamerHowlFxId = 15226;
    private const int RoamerHowlHoldMs = 1500;
    private const float RoamerAggroRange = 6f;
    private const int AggroDelayMs = 2200;
    private const int SpawnPoofFxId = 46;
    private const int DeathPoofFxId = 5017;
    private const int AlphaFleeMs = 10000;
    private const int WolfDeathHoldMs = 2000;

    private const int CharState_Baseline = 0x1;
    private const int CharState_Charging = 0x8001;

    private static readonly int[] WaveSizes = [6, 9, 10, 10];
    private const int EvilPerWave = 2;
    private const int NextWaveDelayMs = 1000;

    private static readonly Vector3[] SpawnPoints =
    [
        new(166.08f, 0.35f, 197.34f), new(139.08f, 1.64f, 113.27f), new(122.40f, 0.56f, 114.63f),
        new(160.33f, 1.69f, 137.58f), new(102.58f, 0.73f, 203.86f), new(104.37f, 1.56f, 131.93f),
        new( 99.96f, 0.78f, 138.62f), new(157.86f, 0.72f, 127.60f), new(197.86f, 2.03f, 173.21f),
        new(102.50f, 1.44f, 131.17f), new(169.48f, 0.63f, 153.74f), new(120.73f, 1.59f, 111.62f),
        new(140.46f, 1.76f, 113.18f), new( 97.46f, 0.24f, 173.99f), new(101.83f, -0.09f, 190.66f),
        new(111.27f, 0.80f, 125.00f), new(170.80f, 0.48f, 191.63f), new(111.35f, -0.19f, 210.22f),
        new(158.58f, 1.58f, 137.10f), new(136.48f, -0.36f, 209.45f), new( 97.55f, 1.72f, 160.75f),
        new(138.88f, 2.02f, 115.44f), new( 97.03f, 0.26f, 174.44f), new(183.62f, 1.10f, 183.02f),
        new(108.96f, 0.02f, 209.94f), new(183.20f, 0.85f, 163.49f), new(118.60f, 0.94f, 118.90f),
    ];

    private static readonly Vector4 RoamerSpawn = new(129.33f, GroundY, 171.81f, 1f);
    private static readonly Vector4 AlphaSpawn = new(154.32f, 1.96f, 209.35f, 1f);
    private static readonly Vector4 DoorSpawn = new(145.0f, 0.0f, 173.35f, 1f);

    private const int DoorModelId = 846;
    private const int DoorNameId = 4826;
    private const float DoorScale = 1.2f;
    private const int DoorInteractRange = 125;
    private const int DoorActiveProfile = 28;
    private const int DoorCursorId = 17;
    private const int DoorMinimapImageId = 186;
    private const int DoorBadgeType = 7;
    private const int DoorBadgeUnknown3 = 102;

    private const int CoinsModelId = 841;
    private const int CoinsNameId = 139649;
    private const int CoinsPopFxId = 5192;
    private const float CoinsKnockMagnitude = 0.0712f;

    protected override float MobChaseSpeed => 6f;

    private const float FleeSpeed = 9f;
    private const float FleeDespawnRadius = 90f;

    public static Vector4? SpawnOverride;

    private const int WolfMovementTypePhysics = 2;

    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly Random _rng = new();

    private const int HeartModelId = 736;
    private const int HeartHeal = 125;
    private const float HeartPickupRange = 2.6f;
    private const int HeartDropPercent = 12;
    private const int HeartPickupFxId = 15032;
    private const int HealShowerFxId = 15921;
    private const int HealShowerMs = 15000;
    private int _healTagCounter = 300;
    private readonly List<Npc> _hearts = [];
    private const int HeartBuffIconId = 17348;
    private const uint HeartBuffNameId = 5102381;
    private const int HeartBuffAbilityId = 6211;
    private const float HeartBuffMult = 1.33f;

    private enum PowerupKind { Energy, Damage, FlameWave, Quake, Shield }

    private const int EnergyPowerupModelId = 737;
    private const int DamagePowerupModelId = 746;
    private const int FlameWavePowerupModelId = 1949;
    private const int QuakePowerupModelId = 1950;
    private const int ShieldPowerupModelId = 1951;
    private const int PowerupDropPercent = 8;
    private const int PowerupNameId = 5102385;

    private const int DamageBuffBodyFxId = 5032;
    private const int DamageBuffMs = 15000;
    private const int DamageBuffPct = 133;
    private const int FlameWaveDamage = 500;
    private const float FlameWaveRadius = 10f;
    public static int FlameWaveFxId = 5591;
    public static int FlameWaveAnimId;
    private const int QuakeDamage = 350;
    private const float QuakeRadius = 10f;
    private const int QuakeStunMs = 3000;
    public static int QuakeFxId = 5388;
    public static int QuakeAnimId;
    private const int ShieldMs = 8000;
    public static int ShieldFxId = 5049;
    public static int ShieldAnimId;
    private const int WolfKnockDownAnimId = 1402;
    private const int WolfGetUpAnimId = 1403;
    private const int FlameWaveIconId = 26832;
    private const int QuakeIconId = 26835;
    private const int ShieldIconId = 26838;
    private const uint ShieldBuffNameId = 1352849523;
    private const float ShieldSpeedMult = 1.35f;

    private static readonly PowerupKind[] DroppablePowerups =
        [PowerupKind.Energy, PowerupKind.FlameWave, PowerupKind.Quake, PowerupKind.Shield];

    private readonly List<(Npc Npc, PowerupKind Kind)> _powerups = [];
    private PowerupKind? _heldPowerup;

    private sealed class WolfState : EncounterMobState
    {
        public long ChargeAtTicks;
        public long StunnedUntil;
        public bool IsRoamer;
        public bool Howled;
        public Vector2? WanderTarget;
        public long WanderPauseUntil;
    }

    private readonly object _stateLock = new();
    private readonly List<Npc> _wolves = [];
    private readonly Dictionary<ulong, WolfState> _wolfStates = [];
    private Npc? _alpha;
    private Npc? _fleeingAlpha;
    private long _alphaFleeUntilTicks;
    private int _waveIndex;
    private bool _waveScheduled;
    private bool _roamerEngaged;
    private int _killedSnarlers;
    private bool _won;
    private bool _victoryUiDone;
    private int _encounterRun;

    private readonly List<Player> _activePlayers = [];
    private Player? _anchor;

    private const int GoalScareWolves = 12642;
    private const int GoalScareWolvesNameId = 104176;
    private const int GoalKnockoutBudget = 12288; // "Don't get knocked out 5 times!" — activated, no Goals row (live)
    private const int GoalScareWolvesDescId = 104177;

    public const int CombatProfileType = 2;
    public static List<RewardEntry> NinjaPrizePreview() =>
    [
        new() { Hidden = true,  IconId = 2483, TintId = 234, NameId = 133217, ItemDefId = 76209 },
        new() {                 IconId = 3717, TintId = 264, NameId = 131152, ItemDefId = 75408 },
        new() {                 IconId = 3229, TintId = 247, NameId = 131975, ItemDefId = 75091 },
        new() {                 IconId = 1198, TintId = 0,   NameId = 131129, ItemDefId = 75385 },
        new() { Hidden = true,  IconId = 973,  TintId = -1,  NameId = 6666,   ItemDefId = 10482 },
    ];
    public const int PrizeCoins = 10;
    public const int PrizeXp = 0;

    public const int EncounterXp = 10;

    public const int ArcherProfileId = 35;
    public static List<RewardEntry> ArcherPrizePreview() =>
    [
        new() { Hidden = true,  IconId = 4939, TintId = 247, NameId = 132741, ItemDefId = 75733 },
        new() {                 IconId = 3721, TintId = 230, NameId = 130968, ItemDefId = 75224 },
        new() {                 IconId = 547,  TintId = 0,   NameId = 130924, ItemDefId = 75180 },
        new() {                 IconId = 3104, TintId = 228, NameId = 131884, ItemDefId = 75000 },
        new() { Hidden = true,  IconId = 973,  TintId = -1,  NameId = 6666,   ItemDefId = 10482 },
    ];

    public static List<RewardEntry> GetPrizePreviewFor(Player player) =>
        player.ActiveProfileId == ArcherProfileId ? ArcherPrizePreview() : NinjaPrizePreview();

    private static IEnumerable<EncounterObjective> EncounterObjectives =>
    [
        new EncounterObjective
        {
            ObjectiveId = GoalScareWolves, NameId = GoalScareWolvesNameId, DescriptionId = GoalScareWolvesDescId,
            Status = 1, Count = 0, Total = 1, Unknown8 = 0,
        },
    ];

    private readonly Sanctuary.Game.Quests.IQuestManager _questManager;

    public FrostfangArenaZone(IServiceProvider serviceProvider)
        : base(CreateDefinition(), serviceProvider)
    {
        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _questManager = serviceProvider.GetRequiredService<Sanctuary.Game.Quests.IQuestManager>();
    }

    private static BaseZoneDefinition CreateDefinition() => new FrostfangArenaDefinition
    {
        Id = 174,
        Name = "sg_random_encounter_clearing",
        TileSize = 64,
        StartLongitude = -2,
        EndLongitude = 8,
        StartLatitude = -2,
        EndLatitude = 8,
        Sky = null,
        SpawnPosition = new Vector4(130.11f, GroundY + 2f, 120.04f, 1f),
        SpawnRotation = Quaternion.Identity,
    };

    public Vector4 EffectiveSpawn => SpawnOverride ?? SpawnPosition;

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
            _logger.LogInformation("Frostfang arena: {name} joined the party fight (member #{n}).",
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

    private void PushLiveEncounterTo(Player player)
    {
        List<Npc> live = [];
        lock (_stateLock)
        {
            live.AddRange(_wolves);
            if (_alpha is not null) live.Add(_alpha);
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
            foreach (var old in _wolves)
                old.Dispose();
            _wolves.Clear();
            _wolfStates.Clear();
            foreach (var h in _hearts)
                h.Dispose();
            _hearts.Clear();
            foreach (var (pu, _) in _powerups)
                pu.Dispose();
            _powerups.Clear();
            _heldPowerup = null;
            _victoryUiDone = false;
            _alpha?.Dispose();
            _alpha = null;
            _fleeingAlpha?.Dispose();
            _fleeingAlpha = null;
            ExitDoor?.Dispose();
            SetExitDoor(null);
            _waveIndex = 0;
            _waveScheduled = false;
            _roamerEngaged = false;
            _killedSnarlers = 0;
            _won = false;
            _encounterRun++;

            SpawnRoamer(player);
        }

        DeliverEntrySequence(player, _encounterRun);

        _logger.LogInformation("Frostfang arena: encounter start for {name} — roamer out, {waves} waves queued.",
            player.Name, WaveSizes.Length);

        StartWolfAi(player, _encounterRun);
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
                    Unknown = EncounterId,          // live header ints = [encounterId][instanceId]
                    Unknown2 = EncounterInstanceId,
                    NameId = 93276,
                    DescriptionId = 104171,
                    Difficulty = 1,
                    IconId = 1345,
                    MiniGameType = CombatMiniGameType,
                    Launch = true,
                    Objectives = [.. EncounterObjectives],
                    PreviewRewards = GetPrizePreviewFor(player),
                    PreviewCoins = PrizeCoins,
                    PreviewXp = PrizeXp,
                    ProfileType = CombatProfileType,
                    ActivityId = EncounterId,
                };

                EncounterPacketPlayerEnter MakeEnter(ulong guid) => new()
                {
                    EncounterId = EncounterId,
                    InstanceId = EncounterInstanceId,
                    PlayerGuid = guid,
                };

                UiObjectiveAddPacket ScareWolvesRow() => new()
                {
                    ObjectiveId = GoalScareWolves,
                    NameId = GoalScareWolvesNameId,
                };

                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                // Live activates TWO objectives (capture 28045/28047): the knockout-budget goal
                // ("Don't get knocked out 5 times!", id 12288, total 5 — no Goals-window row) THEN
                // the scare-wolves goal.
                player.SendTunneled(new ObjectiveActivatePacket
                {
                    ObjectiveId = GoalKnockoutBudget,
                    Total = KnockoutLimit,
                });
                player.SendTunneled(new ObjectiveActivatePacket
                {
                    ObjectiveId = GoalScareWolves,
                    Total = 1,
                });
                player.SendTunneled(ScareWolvesRow());
                player.SendTunneled(MakeLaunch());
                player.SendTunneled(MakeEnter(0));
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(MakeLaunch());
                player.SendTunneled(ScareWolvesRow());
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                // Capture 28075: enable effect-tag composite rendering (live re-sends the login tags
                // right after — why the buff FX render in-encounter).
                player.SendTunneled(new PlayerUpdatePacketEffectTagCompositeEffectsEnable { Enable = true });
                // Capture 28116: the PLAYER's character state set to the baseline bit, right before op62.
                player.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
                {
                    Guid = player.Guid,
                    Status = (CharacterStatus)CharState_Baseline,
                });
                player.SendTunneled(PacketEncounterDataCommon.CreateCombatRules());
                player.SendTunneled(MakeEnter(player.Guid));

                player.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = EncounterId,
                    InstanceId = EncounterInstanceId,
                    State = 6,
                });

                _logger.LogInformation("Frostfang arena: entry sequence delivered to {name} (run {run}).",
                    player.Name, run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena: entry-sequence delivery failed.");
            }
        });
    }

    private void SpawnRoamer(Player player)
    {
        var roamer = CreateWolf(player, EvilWolfModelId, RoamerNameId, "evil", "evil_purple",
            WolfHealth, 1f, RoamerSpawn, showPlate: false, PackActiveProfile, spawnFx: 0, speed: RoamSpeed);
        if (roamer is null)
            return;

        _wolves.Add(roamer);
        _wolfStates[roamer.Guid] = new WolfState
        {
            IsRoamer = true,
            SlotAngle = (float)(_rng.NextDouble() * Math.Tau),
            Home = roamer.Position,
        };

        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = roamer.Guid, ExpectedSpeed = RoamSpeed });
        SendWolfMinimapMarkers(player, [roamer.Guid]);
    }

    private void SpawnWave(Player player)
    {
        if (_waveIndex >= WaveSizes.Length)
            return;

        var size = WaveSizes[_waveIndex];
        var isLastWave = _waveIndex == WaveSizes.Length - 1;
        _waveIndex++;
        _waveScheduled = false;

        var newGuids = new List<ulong>(size + 1);

        var points = new List<Vector3>(SpawnPoints);
        for (var i = 0; i < size; i++)
        {
            var pt = points.Count > 0
                ? points[_rng.Next(points.Count)]
                : SpawnPoints[_rng.Next(SpawnPoints.Length)];
            points.Remove(pt);

            var evil = i < EvilPerWave;
            var wolf = CreateWolf(player,
                evil ? EvilWolfModelId : SnowWolfModelId,
                evil ? SnarlerEvilNameId : SnarlerSnowNameId,
                evil ? "evil" : "snow",
                evil ? "evil_black" : "base_metal",
                WolfHealth, 1f, new Vector4(pt.X, pt.Y, pt.Z, 1f),
                showPlate: false, PackActiveProfile, SpawnPoofFxId, speed: 0f);
            if (wolf is null)
                continue;

            _wolves.Add(wolf);
            _wolfStates[wolf.Guid] = new WolfState
            {
                ChargeAtTicks = Environment.TickCount64 + AggroDelayMs,
                SlotAngle = (float)(_rng.NextDouble() * Math.Tau),
                Home = wolf.Position,
            };
            newGuids.Add(wolf.Guid);
        }

        if (isLastWave)
        {
            _alpha = CreateWolf(player, SnowWolfModelId, AlphaNameId, "snow", "snow_blue",
                AlphaHealth, AlphaScale, AlphaSpawn, showPlate: true, AlphaActiveProfile, spawnFx: 0, speed: 0f);
            if (_alpha is not null)
            {
                _wolves.Add(_alpha);
                _wolfStates[_alpha.Guid] = new WolfState
                {
                    ChargeAtTicks = Environment.TickCount64 + AggroDelayMs,
                    SlotAngle = (float)(_rng.NextDouble() * Math.Tau),
                    Home = _alpha.Position,
                };
                newGuids.Add(_alpha.Guid);
            }

            _logger.LogInformation("Frostfang arena: FINAL wave ({n} wolves) + the Frostfang Alpha.", size);
        }
        else
        {
            _logger.LogInformation("Frostfang arena: wave {w}/{total} — {n} wolves inbound.",
                _waveIndex, WaveSizes.Length, size);
        }

        SendWolfMinimapMarkers(player, newGuids);
    }

    private void SendWolfMinimapMarkers(Player player, IReadOnlyList<ulong> guids)
    {
        if (guids.Count == 0)
            return;

        var badge = new PlayerUpdatePacketAddNotifications();
        foreach (var guid in guids)
            badge.Notifications.Add(new NotificationInfo { Guid = guid, Combat = true, Type = 3, Unknown10 = true });
        Broadcast(badge);
    }

    private Npc? CreateWolf(Player player, int modelId, int nameId, string textureAlias, string tintAlias,
        int health, float scale, Vector4 pos, bool showPlate, int activeProfile, int spawnFx, float speed)
    {
        if (!TryCreateNpc(out var npc))
            return null;

        npc.ModelId = modelId;
        npc.NameId = showPlate ? nameId : 0;
        npc.Name = null;
        npc.TextureAlias = textureAlias;
        npc.TintAlias = tintAlias;
        npc.HideNamePlate = false;
        npc.ShowHealthBar = true;
        npc.Scale = scale;
        npc.Disposition = 0;
        npc.ActiveProfile = activeProfile;
        npc.CompositeEffectId = spawnFx;
        npc.MaxHealth = health;
        npc.Health = health;
        npc.IsInteractable = false;
        npc.InteractRange = 100;
        npc.Visible = true;
        npc.CursorId = 11;

        npc.WalkAnimId = -1;
        npc.RunAnimId = -1;
        npc.StandAnimId = -1;
        npc.MovementType = WolfMovementTypePhysics;
        npc.Speed = speed;
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

    private void StartWolfAi(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Frostfang arena: AI loop started (run {run}).", run);

            try
            {
                for (var elapsed = 0; elapsed < 15 * 60 * 1000; elapsed += TickMs)
                {
                    await Task.Delay(TickMs);

                    if (run != _encounterRun)
                    {
                        _logger.LogInformation("Frostfang arena: AI loop exit — superseded by a new run (run {run}).", run);
                        return;
                    }

                    var players = ActivePlayers();
                    if (players.Length == 0)
                    {
                        _logger.LogInformation("Frostfang arena: AI loop exit — all players left the zone (run {run}).", run);
                        return;
                    }

                    foreach (var p in players)
                    {
                        CollectHearts(p);
                        CollectPowerups(p);
                        CheckDoorProximity(p);
                    }

                    Npc[] pack;
                    Npc? fleeingAlpha;
                    lock (_stateLock)
                    {
                        pack = [.. _wolves];
                        fleeingAlpha = _fleeingAlpha;
                    }

                    if (pack.Length == 0 && fleeingAlpha is null)
                        continue;

                    var now = Environment.TickCount64;
                    var dt = TickMs / 1000f;

                    if (fleeingAlpha is not null)
                    {
                        var alphaHere = new Vector3(fleeingAlpha.Position.X, fleeingAlpha.Position.Y, fleeingAlpha.Position.Z);
                        TickFleeingAlpha(NearestLivePlayer(alphaHere, players) ?? players[0], fleeingAlpha, now, dt);
                    }

                    foreach (var wolf in pack)
                    {
                        if (!wolf.IsAlive)
                            continue;

                        WolfState? state;
                        lock (_stateLock)
                            _wolfStates.TryGetValue(wolf.Guid, out state);
                        if (state is null)
                            continue;

                        var here = new Vector3(wolf.Position.X, wolf.Position.Y, wolf.Position.Z);

                        var tgt = NearestLivePlayer(here, players);
                        if (tgt is null)
                        {
                            TickMobReturnHome(wolf, state, dt);
                            continue;
                        }

                        var target = new Vector3(tgt.Position.X, tgt.Position.Y, tgt.Position.Z);

                        if (state.IsRoamer && !state.Charging && !state.Howled)
                        {
                            var dxr = target.X - here.X;
                            var dzr = target.Z - here.Z;
                            if (dxr * dxr + dzr * dzr <= RoamerAggroRange * RoamerAggroRange)
                            {
                                _logger.LogInformation("Frostfang arena: player closed in on the roamer -> howl + wave 1.");
                                EngageRoamer(tgt, wolf, state);
                            }
                            else
                            {
                                TickRoamer(tgt, wolf, state, here, now, dt);
                                continue;
                            }
                        }

                        if (now < state.StunnedUntil)
                            continue;

                        if (!state.Charging)
                        {
                            if (now < state.ChargeAtTicks)
                                continue;
                            BeginCharge(tgt, wolf, state);
                        }

                        TickMobCombat(wolf, state, tgt, target, now, dt);
                    }
                }

                _logger.LogInformation("Frostfang arena: AI loop exit — 15min safety timeout (run {run}).", run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena wolf AI failed (run {run}).", run);
            }
        });
    }

    private void TickRoamer(Player player, Npc wolf, WolfState state, Vector3 here, long now, float dt)
    {
        if (state.WanderTarget is null)
        {
            if (now < state.WanderPauseUntil)
                return;

            var angle = (float)(_rng.NextDouble() * Math.Tau);
            var dist = 5f + (float)_rng.NextDouble() * 9f;
            state.WanderTarget = new Vector2(
                RoamerSpawn.X + MathF.Sin(angle) * dist,
                RoamerSpawn.Z + MathF.Cos(angle) * dist);
        }

        var wt = state.WanderTarget.Value;
        var to = new Vector2(wt.X - here.X, wt.Y - here.Z);
        var d = to.Length();

        if (d < 0.5f)
        {
            state.WanderTarget = null;
            state.WanderPauseUntil = now + 1500 + _rng.Next(2000);
            Broadcast(new PlayerUpdatePacketUpdatePosition
            {
                Guid = wolf.Guid, Position = wolf.Position, Rotation = new Quaternion(0f, 0f, 1f, 0f),
                State = 1, Unknown = 0,
            });
            return;
        }

        var dir = to / d;
        var step = MathF.Min(RoamSpeed * dt, d);
        var newPos = new Vector4(here.X + dir.X * step, MoveToward(here.Y, GroundY, MobYSpeed * dt),
            here.Z + dir.Y * step, wolf.Position.W);
        var rot = new Quaternion(dir.X, 0f, dir.Y, 0f);

        wolf.UpdatePosition(newPos, rot);
        Broadcast(new PlayerUpdatePacketUpdatePosition
        {
            Guid = wolf.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
        });
    }

    private void BeginCharge(Player player, Npc wolf, WolfState state)
    {
        state.Charging = true;
        state.NextAttackTicks = Environment.TickCount64 + 1000 + _rng.Next(1500);

        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = wolf.Guid, ExpectedSpeed = RoamSpeed });
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = wolf.Guid, ExpectedSpeed = MobChaseSpeed });

        if (!ReferenceEquals(wolf, _alpha))
        {
            Broadcast(new PlayerUpdatePacketUpdateCharacterState
            {
                Guid = wolf.Guid,
                Status = (CharacterStatus)CharState_Charging,
            });
        }
    }

    private void EngageRoamer(Player player, Npc roamer, WolfState state)
    {
        lock (_stateLock)
        {
            if (_roamerEngaged)
                return;
            _roamerEngaged = true;

            var facePlayer = new Vector2(player.Position.X - roamer.Position.X, player.Position.Z - roamer.Position.Z);
            var faceLen = facePlayer.Length();
            var faceDir = faceLen > 0.01f ? facePlayer / faceLen : new Vector2(0f, 1f);
            var howlRot = new Quaternion(faceDir.X, 0f, faceDir.Y, 0f);
            Broadcast(new PlayerUpdatePacketUpdatePosition
            {
                Guid = roamer.Guid, Position = roamer.Position, Rotation = howlRot, State = 1, Unknown = 0,
            });

            Broadcast(new PlayerUpdatePacketSetAnimation
            {
                Guid = roamer.Guid,
                AnimationId = RoamerHowlAnimId,
            });
            Broadcast(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = roamer.Guid,
                Unknown2 = player.Guid,
                CompositeEffectId = RoamerHowlFxId,
                EffectDelay = 0,
                Position = new Vector4(0f, 0f, 0f, 1f),
                Clear = true,
            });

            state.Howled = true;
            state.ChargeAtTicks = Environment.TickCount64 + RoamerHowlHoldMs;

            SpawnWave(player);
        }
    }

    private void TickFleeingAlpha(Player player, Npc alpha, long now, float dt)
    {
        var here = new Vector3(alpha.Position.X, alpha.Position.Y, alpha.Position.Z);

        var fromCenterX = here.X - 136f;
        var fromCenterZ = here.Z - 165f;
        var distFromCenter = MathF.Sqrt(fromCenterX * fromCenterX + fromCenterZ * fromCenterZ);

        if (now >= _alphaFleeUntilTicks || distFromCenter > FleeDespawnRadius)
        {
            lock (_stateLock)
            {
                if (!ReferenceEquals(_fleeingAlpha, alpha))
                    return;
                _fleeingAlpha = null;
            }
            Broadcast(new PlayerUpdatePacketRemoveNotifications { Guids = { alpha.Guid } });
            alpha.GracefulRemoval = (false, 0, 0, DeathPoofFxId, 1000);
            alpha.Dispose();
            _logger.LogInformation("Frostfang arena: the fled Alpha reached the fog -> despawned.");
            return;
        }

        var awayX = here.X - player.Position.X;
        var awayZ = here.Z - player.Position.Z;
        var len = MathF.Sqrt(awayX * awayX + awayZ * awayZ);
        var dir = len > 0.01f ? new Vector2(awayX / len, awayZ / len) : new Vector2(0f, 1f);
        var step = FleeSpeed * dt;
        var newPos = new Vector4(
            here.X + dir.X * step,
            MoveToward(here.Y, GroundY, MobYSpeed * dt),
            here.Z + dir.Y * step,
            alpha.Position.W);
        var rot = new Quaternion(dir.X, 0f, dir.Y, 0f);

        alpha.UpdatePosition(newPos, rot);
        Broadcast(new PlayerUpdatePacketUpdatePosition
        {
            Guid = alpha.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
        });
    }

    public override void OnNpcDamaged(Player player, Npc npc)
    {
        lock (_stateLock)
        {
            if (_wolfStates.TryGetValue(npc.Guid, out var state) && state.IsRoamer && !state.Charging)
            {
                _logger.LogInformation("Frostfang arena: the roamer was provoked -> howl + wave 1.");
                EngageRoamer(player, npc, state);
            }
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
            var maxHp = player.Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : 2500;
            player.CurrentHitpoints = Math.Min(maxHp, player.CurrentHitpoints + HeartHeal);
            player.SendTunneled(new ClientUpdatePacketHitpoints
            {
                CurrentHitpoints = player.CurrentHitpoints,
                MaxHitpoints = maxHp,
            });
            player.SendTunneled(new PlayerUpdatePacketHitPointModification
            {
                Guid = player.Guid,
                Guid2 = player.Guid,
                Unknown = true,
                Unknown2 = maxHp,
                Unknown3 = player.CurrentHitpoints,
                Unknown4 = HeartHeal,
            });

            StatusEffects.ApplyBuffTag(player, HeartBuffIconId, HeartBuffNameId, HealShowerMs,
                magnitude: HeartBuffMult, fxId: HealShowerFxId, abilityId: HeartBuffAbilityId,
                sourceGuid: h.Guid);

            h.GracefulRemoval = (false, 0, 5000, HeartPickupFxId, 1000);
            h.Dispose();
        }
    }

    private void SpawnPowerup(Player player, Vector4 pos, PowerupKind kind)
    {
        if (!TryCreateNpc(out var pu))
            return;

        pu.ModelId = kind switch
        {
            PowerupKind.Energy => EnergyPowerupModelId,
            PowerupKind.Damage => DamagePowerupModelId,
            PowerupKind.FlameWave => FlameWavePowerupModelId,
            PowerupKind.Quake => QuakePowerupModelId,
            _ => ShieldPowerupModelId,
        };
        pu.Name = null;
        pu.NameId = PowerupNameId;
        pu.Disposition = 1;
        pu.Scale = 1f;
        pu.IsInteractable = false;
        pu.InteractRange = 0;
        pu.Visible = true;
        pu.MaxHealth = 0;
        pu.ShowHealthBar = false;
        pu.HideNamePlate = true;
        pu.ActiveProfile = 8;
        pu.WalkAnimId = -1;
        pu.RunAnimId = -1;
        pu.StandAnimId = -1;
        pu.MovementType = WolfMovementTypePhysics;
        pu.RiderGuid = ulong.MaxValue;
        pu.UpdatePosition(pos, Quaternion.Identity);

        foreach (var p in ActivePlayers())
        {
            p.OnAddVisibleNpcs(pu);
            pu.OnAddVisiblePlayers(p);
        }

        lock (_stateLock)
            _powerups.Add((pu, kind));

        _logger.LogInformation("Frostfang arena: dropped a {kind} powerup.", kind);
    }

    private void CollectPowerups(Player player)
    {
        List<(Npc Npc, PowerupKind Kind)>? collected = null;
        lock (_stateLock)
        {
            for (var i = _powerups.Count - 1; i >= 0; i--)
            {
                var (p, k) = _powerups[i];
                var dx = player.Position.X - p.Position.X;
                var dz = player.Position.Z - p.Position.Z;
                if (dx * dx + dz * dz > HeartPickupRange * HeartPickupRange)
                    continue;
                if (k == PowerupKind.Energy && CombatBuffs.IsEnergyFull?.Invoke(player) == true)
                    continue;
                (collected ??= []).Add(_powerups[i]);
                _powerups.RemoveAt(i);
            }
        }

        if (collected is null)
            return;

        foreach (var (pu, kind) in collected)
        {
            switch (kind)
            {
                case PowerupKind.Energy:
                    CombatBuffs.RequestEnergyRefill(player);
                    break;

                case PowerupKind.Damage:
                    CombatBuffs.AddDamageBuff(player.Guid, DamageBuffPct, DamageBuffMs);
                    StatusEffects.ApplyBuffTag(player, 17351, PowerupNameId, DamageBuffMs,
                        magnitude: DamageBuffPct / 100f, fxId: DamageBuffBodyFxId, abilityId: 6213,
                        sourceGuid: pu.Guid);
                    break;

                default:
                    lock (_stateLock)
                        _heldPowerup = kind;
                    SendPowerupSlot(player);
                    break;
            }

            _logger.LogInformation("Frostfang arena: player collected a {kind} powerup.", kind);

            pu.GracefulRemoval = (false, 0, 5000, HeartPickupFxId, 1000);
            pu.Dispose();
        }
    }

    public bool UseHeldPowerup(Player player)
    {
        PowerupKind kind;
        lock (_stateLock)
        {
            if (_heldPowerup is not { } held)
                return false;
            kind = held;
            _heldPowerup = null;
        }

        var now = Environment.TickCount64;

        var useAnim = kind switch
        {
            PowerupKind.FlameWave => FlameWaveAnimId,
            PowerupKind.Quake => QuakeAnimId,
            _ => ShieldAnimId,
        };
        if (useAnim > 0)
        {
            player.SendTunneled(new AbilityPacketStartCasting
            {
                Unknown = player.Guid,
                Unknown2 = player.Guid,
                Animation = useAnim,
                AbilityId = 3,
                ActionTime = 0.4f,
                HasActionProgress = false,
            });
        }

        switch (kind)
        {
            case PowerupKind.FlameWave:
                Broadcast(new PlayerUpdatePacketPlayCompositeEffect
                {
                    Guid = player.Guid,
                    CompositeEffectId = FlameWaveFxId,
                    Position = player.Position,
                });
                DamageWolvesAround(player, FlameWaveRadius, FlameWaveDamage);
                break;

            case PowerupKind.Quake:
                Broadcast(new PlayerUpdatePacketPlayCompositeEffect
                {
                    Guid = player.Guid,
                    CompositeEffectId = QuakeFxId,
                    Position = player.Position,
                });
                DamageWolvesAround(player, QuakeRadius, QuakeDamage);
                List<Npc> stunned = [];
                lock (_stateLock)
                {
                    foreach (var wolf in _wolves)
                    {
                        var dx = wolf.Position.X - player.Position.X;
                        var dz = wolf.Position.Z - player.Position.Z;
                        if (dx * dx + dz * dz <= QuakeRadius * QuakeRadius &&
                            _wolfStates.TryGetValue(wolf.Guid, out var state))
                        {
                            state.StunnedUntil = now + QuakeStunMs;
                            stunned.Add(wolf);
                        }
                    }
                }
                foreach (var wolf in stunned)
                {
                    StatusEffects.Apply(wolf, StatusEffectKind.Stun, QuakeStunMs,
                        baseline: (CharacterStatus)CharState_Charging, source: player);
                    Broadcast(new PlayerUpdatePacketSetAnimation
                    {
                        Guid = wolf.Guid,
                        AnimationId = WolfKnockDownAnimId,
                    });
                }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(QuakeStunMs);
                        foreach (var wolf in stunned)
                            if (wolf.IsAlive)
                                Broadcast(new PlayerUpdatePacketSetAnimation
                                {
                                    Guid = wolf.Guid,
                                    AnimationId = WolfGetUpAnimId,
                                });
                    }
                    catch {  }
                });
                break;

            case PowerupKind.Shield:
                player.InvulnerableUntilTicks = now + ShieldMs;
                StatusEffects.ApplyBuffTag(player, ShieldIconId, ShieldBuffNameId, ShieldMs,
                    magnitude: ShieldSpeedMult, fxId: ShieldFxId);

                var baseSpeed = player.Stats.TryGetValue(CharacterStatId.MaxMovementSpeed, out var speedStat)
                    ? speedStat.Float
                    : 8f;
                player.UpdateCharacterStats(new CharacterStat(CharacterStatId.MaxMovementSpeed, baseSpeed * ShieldSpeedMult));
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(ShieldMs);
                        if (player.Zone == this)
                            player.UpdateCharacterStats(new CharacterStat(CharacterStatId.MaxMovementSpeed, baseSpeed));
                    }
                    catch {  }
                });
                break;
        }

        _logger.LogInformation("Frostfang arena: player used the held {kind} powerup.", kind);
        SendPowerupSlot(player);
        return true;
    }

    private void DamageWolvesAround(Player player, float radius, int damage)
    {
        List<Npc> victims = [];
        lock (_stateLock)
        {
            foreach (var wolf in _wolves)
            {
                if (!wolf.IsAlive || !wolf.IsDamageable)
                    continue;
                var dx = wolf.Position.X - player.Position.X;
                var dz = wolf.Position.Z - player.Position.Z;
                if (dx * dx + dz * dz <= radius * radius)
                    victims.Add(wolf);
            }
        }

        foreach (var wolf in victims)
        {
            var killed = wolf.ApplyDamage(damage);
            Broadcast(new PlayerUpdatePacketHitPointModification
            {
                Guid = player.Guid,
                Guid2 = wolf.Guid,
                Unknown = true,
                Unknown2 = wolf.MaxHealth,
                Unknown3 = wolf.Health,
                Unknown4 = -damage,
            });
            if (killed)
                OnNpcKilled(player, wolf);
        }
    }

    public bool GrantPowerup(Player player, string kind)
    {
        switch (kind)
        {
            case "energy":
                CombatBuffs.RequestEnergyRefill(player);
                return true;
            case "flame":
                lock (_stateLock) _heldPowerup = PowerupKind.FlameWave;
                break;
            case "quake" or "earth":
                lock (_stateLock) _heldPowerup = PowerupKind.Quake;
                break;
            case "shield":
                lock (_stateLock) _heldPowerup = PowerupKind.Shield;
                break;
            default:
                return false;
        }

        SendPowerupSlot(player);
        return true;
    }

    public static bool TrySetPowerupFx(string kind, int fxId, int? animId = null)
    {
        switch (kind)
        {
            case "flame":
                FlameWaveFxId = fxId;
                if (animId is { } fa) FlameWaveAnimId = fa;
                return true;
            case "quake" or "earth":
                QuakeFxId = fxId;
                if (animId is { } qa) QuakeAnimId = qa;
                return true;
            case "shield":
                ShieldFxId = fxId;
                if (animId is { } sa) ShieldAnimId = sa;
                return true;
            default:
                return false;
        }
    }

    private void SendPowerupSlot(Player player)
    {
        AbilityPacketSetDefinition.Slot? slot = null;
        lock (_stateLock)
        {
            slot = _heldPowerup switch
            {
                PowerupKind.FlameWave => JobWeaponAbilities.MakePowerupSlot(FlameWaveIconId, PowerupNameId),
                PowerupKind.Quake => JobWeaponAbilities.MakePowerupSlot(QuakeIconId, PowerupNameId),
                PowerupKind.Shield => JobWeaponAbilities.MakePowerupSlot(ShieldIconId, PowerupNameId),
                _ => null,
            };
        }

        player.SendTunneled(JobWeaponAbilities.BuildToolbar(player, _resourceManager, slot));
    }

    public override void OnNpcKilled(Player killer, Npc npc)
    {
        var alphaDown = false;
        var scheduleWave = false;

        lock (_stateLock)
        {
            if (ReferenceEquals(npc, _alpha))
            {
                _alpha = null;
                _wolves.Remove(npc);
                _wolfStates.Remove(npc.Guid);
                _killedSnarlers++;
                alphaDown = true;
            }
            else if (_wolves.Remove(npc))
            {
                _wolfStates.Remove(npc.Guid);
                _killedSnarlers++;

                if (_wolves.Count <= 1 && _waveIndex < WaveSizes.Length && !_waveScheduled && !_won)
                {
                    _waveScheduled = true;
                    scheduleWave = true;
                }
            }
            else
            {
                return;
            }
        }

        Broadcast(new PlayerUpdatePacketRemoveNotifications { Guids = { npc.Guid } });

        if (!alphaDown)
        {
            npc.GracefulRemoval = (true, WolfDeathHoldMs, 0, DeathPoofFxId, 1000);
            var deathPos = npc.Position;
            npc.Dispose();

            if (_rng.Next(100) < HeartDropPercent)
                SpawnHeart(killer, deathPos);
            else if (_rng.Next(100) < PowerupDropPercent)
                SpawnPowerup(killer, deathPos, DroppablePowerups[_rng.Next(DroppablePowerups.Length)]);

            if (scheduleWave)
                ScheduleNextWave(killer, _encounterRun);
            return;
        }

        _logger.LogInformation("Frostfang arena: the Alpha is DEFEATED -> he flees to the fog; encounter won.");

        var alphaPos = npc.Position;
        npc.Invulnerable = true;
        lock (_stateLock)
        {
            _fleeingAlpha = npc;
            _alphaFleeUntilTicks = Environment.TickCount64 + AlphaFleeMs;
        }
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = npc.Guid, ExpectedSpeed = FleeSpeed });

        WinEncounter(killer, alphaPos);
    }

    private void ScheduleNextWave(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(NextWaveDelayMs);

                if (player.Zone != this || run != _encounterRun)
                    return;

                lock (_stateLock)
                    SpawnWave(player);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena wave spawn failed.");
            }
        });
    }

    private void WinEncounter(Player player, Vector4 alphaPos)
    {
        List<Npc> stragglers;
        lock (_stateLock)
        {
            _won = true;
            stragglers = [.. _wolves];
            _wolves.Clear();
            _wolfStates.Clear();
        }
        foreach (var straggler in stragglers)
        {
            Broadcast(new PlayerUpdatePacketRemoveNotifications { Guids = { straggler.Guid } });
            straggler.GracefulRemoval = (true, WolfDeathHoldMs, 0, DeathPoofFxId, 1000);
            straggler.Dispose();
        }

        SpawnHeart(player, alphaPos);
        SpawnCoinPop(player, alphaPos);

        foreach (var member in ActivePlayers())
        {
            member.SendTunneled(new ObjectiveCompletePacket { ObjectiveId = GoalScareWolves });
            member.SendTunneled(new UiObjectiveCompletePacket { ObjectiveId = GoalScareWolves });

            member.AwardXp(EncounterXp);
            member.SendTunneled(new RewardBundlePacket { Xp = EncounterXp });

            _questManager.OnEncounterComplete(member, EncounterId);
        }

        SpawnExitDoor(player);

        _logger.LogInformation("Frostfang arena: encounter WON — exit door out ({kills} kills).", _killedSnarlers);
    }

    private void SendVictoryWheelAndScore()
    {
        var enemies = _killedSnarlers;
        var knockoutsLeft = Math.Max(0, KnockoutLimit - KnockoutsUsed(0));
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
            var prizes = GetPrizePreviewFor(member);
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
                member.PendingWheelCoins = PrizeCoins;
                wheel.Coins = PrizeCoins;
            }

            member.SendTunneled(wheel);
            member.SendTunneled(MakeScore());
            EndEncounterForPlayer(member, won: true);
        }

        _logger.LogInformation("Frostfang arena: victory wheel + score delivered at the door.");
    }

    public override void UseExitDoor(Player player)
    {
        bool first;
        lock (_stateLock)
        {
            first = _won && !_victoryUiDone;
            if (first)
                _victoryUiDone = true;
        }

        if (first)
        {
            SendVictoryWheelAndScore();
            return;
        }

        base.UseExitDoor(player);
    }

    private const float DoorProximityRange = 3f;

    private void CheckDoorProximity(Player player)
    {
        bool near;
        lock (_stateLock)
        {
            if (!_won || _victoryUiDone || ExitDoor is not { } door)
                return;
            var dx = player.Position.X - door.Position.X;
            var dz = player.Position.Z - door.Position.Z;
            near = dx * dx + dz * dz <= DoorProximityRange * DoorProximityRange;
            if (near)
                _victoryUiDone = true;
        }

        if (near)
            SendVictoryWheelAndScore();
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

        player.OnAddVisibleNpcs(coins);
        coins.OnAddVisiblePlayers(player);

        var angle = (float)(_rng.NextDouble() * Math.Tau);
        player.SendTunneled(new PlayerUpdatePacketKnockback
        {
            Guid = coins.Guid,
            Position = coins.Position,
            Direction = new Vector4(MathF.Sin(angle), 0f, MathF.Cos(angle), 0f),
            Magnitude = CoinsKnockMagnitude,
        });
        player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = coins.Guid,
            CompositeEffectId = CoinsPopFxId,
            Position = coins.Position,
        });

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
                _logger.LogError(ex, "Frostfang arena: coin-pop removal failed.");
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
        door.Disposition = 1;
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
        door.UpdatePosition(new Vector4(DoorSpawn.X, GroundY, DoorSpawn.Z, 1f), Quaternion.Identity);

        var badge = new PlayerUpdatePacketAddNotifications();
        badge.Notifications.Add(new NotificationInfo
        {
            Guid = door.Guid,
            Combat = false,
            Type = DoorBadgeType,
            Unknown3 = DoorBadgeUnknown3,   // live: 102
            ImageId = DoorMinimapImageId,
            DescriptionId = 0,
            NameId = DoorNameId,
            SubTextId = -1,
            Unknown8 = true,                // live: minimap-only (no floating icon over the door)
            CompositeEffectId = 0,
            Unknown10 = true                // constant 1 across all live samples
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

    protected override void ReturnHome(Player player)
    {
        if (player.Zone != this)
            return;

        bool won, tornDown;
        lock (_stateLock)
        {
            won = _won;
            tornDown = _victoryUiDone;
        }

        if (!tornDown)
            EndEncounterForPlayer(player, won);

        var home = _zoneManager.StartingZone;

        player.TeleportToZone(home, home.SpawnPosition, home.SpawnRotation, sky: null, geometryId: 0);
    }

    protected override int FailEncounterId => EncounterId;
    protected override int FailInstanceId => EncounterInstanceId;
    protected override string EncounterLogName => "Frostfang arena";

    #endregion
}
