using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Dungeons;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class EncounterArenaZone : CombatEncounterZone
{
    private sealed class EncounterArenaDefinition : BaseZoneDefinition { }

    public DungeonDefinition Dungeon { get; }
    public int EncounterId => Dungeon.ActivityId;
    private const int EncounterInstanceId = 1;

    private const int CombatMiniGameType = 4;

    private const int MobActiveProfile = 151;
    private const int SpawnPoofFxId = 46;
    private const int DeathPoofFxId = 5017;
    private const int DeathHoldMs = 1500;
    private const int CharState_Baseline = 0x1;
    private const int CharState_Charging = 0x8001;
    private const int MovementTypePhysics = 2;

    private const float AggroRange = 16f;

    private const int DoorModelId = 846;
    private const int DoorNameId = 4826;
    private const float DoorScale = 1.2f;
    private const int DoorInteractRange = 125;
    private const int DoorActiveProfile = 28;
    private const int DoorCursorId = 17;
    private const int DoorMinimapImageId = 186;
    private const int DoorBadgeType = 7;
    private const int DoorBadgeUnknown3 = 102;

    private sealed class MobState : EncounterMobState { }

    private readonly object _stateLock = new();
    private readonly List<Npc> _mobs = [];
    private readonly Dictionary<ulong, MobState> _mobStates = [];
    private int _killed;
    private bool _won;
    private int _encounterRun;
    private float _groundY;

    private readonly List<Player> _activePlayers = [];
    private Player? _anchor;

    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly Sanctuary.Game.Quests.IQuestManager _questManager;
    private readonly Random _rng = new();

    public EncounterArenaZone(DungeonDefinition dungeon, IServiceProvider serviceProvider)
        : base(CreateDefinition(dungeon), serviceProvider)
    {
        Dungeon = dungeon;
        _groundY = dungeon.GroundY;
        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _questManager = serviceProvider.GetRequiredService<Sanctuary.Game.Quests.IQuestManager>();
    }

    private const float SafeReach = 0.38f;

    private const float WalkThroughRadius = 120f;

    private static BaseZoneDefinition CreateDefinition(DungeonDefinition d)
    {
        const int tile = 64;
        const float pad = 96f;

        return new EncounterArenaDefinition
        {
            Id = d.ActivityId,
            Name = d.World,
            TileSize = tile,
            StartLongitude = (int)MathF.Floor((d.CenterX - d.Radius - pad) / tile),
            EndLongitude = (int)MathF.Ceiling((d.CenterX + d.Radius + pad) / tile),
            StartLatitude = (int)MathF.Floor((d.CenterZ - d.Radius - pad) / tile),
            EndLatitude = (int)MathF.Ceiling((d.CenterZ + d.Radius + pad) / tile),
            Sky = null,
            SpawnPosition = new Vector4(d.CenterX, d.GroundY + 20f, d.CenterZ, 1f),
            SpawnRotation = Quaternion.Identity,
        };
    }

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
            _logger.LogInformation("{dungeon}: {name} joined the party fight (member #{n}).",
                Dungeon.Comment, player.Name, _activePlayers.Count);
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
            live.AddRange(_mobs);
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
        var spawns = BuildDungeonSpawns();

        lock (_stateLock)
        {
            foreach (var old in _mobs)
                old.Dispose();
            _mobs.Clear();
            _mobStates.Clear();
            ExitDoor?.Dispose();
            SetExitDoor(null);
            _killed = 0;
            _won = false;
            _groundY = Dungeon.GroundY;
            _encounterRun++;

            var guids = new List<ulong>();
            var slot = 0;
            foreach (var group in Dungeon.Enemies)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    var pos = spawns[slot % spawns.Count];
                    slot++;
                    var mob = CreateMob(group, pos);
                    if (mob is null) continue;
                    _mobs.Add(mob);
                    _mobStates[mob.Guid] = new MobState { SlotAngle = (float)(_rng.NextDouble() * Math.Tau), Home = pos };
                    guids.Add(mob.Guid);
                }
            }

            SendCombatMinimapMarkers(guids);
        }

        DeliverEntrySequence(player, _encounterRun);

        _logger.LogInformation("{dungeon}: encounter start for {name} — {n} enemies pre-spawned in {world}.",
            Dungeon.Comment, player.Name, Dungeon.TotalEnemies, Dungeon.World);

        StartGroundAdoption(player, _encounterRun);
        StartAi(player, _encounterRun);
    }

    private List<Vector4> BuildDungeonSpawns()
    {
        var pts = new List<Vector4>(Math.Max(Dungeon.TotalEnemies, 1));
        var cx = Dungeon.CenterX;
        var cz = Dungeon.CenterZ;
        var gy = Dungeon.GroundY;

        if (Dungeon.Radius <= WalkThroughRadius)
        {
            var count = Math.Max(Dungeon.TotalEnemies, 1);
            for (var i = 0; i < count; i++)
            {
                var ring = i % 2;
                var radius = 22f + ring * 12f;
                var angle = (float)(i * Math.Tau / count) + ring * 0.4f;
                pts.Add(new Vector4(cx + MathF.Sin(angle) * radius, gy, cz + MathF.Cos(angle) * radius, 1f));
            }
            return pts;
        }

        var groups = Dungeon.Enemies;
        var ng = groups.Length;
        var reach = Dungeon.Radius * SafeReach;
        for (var g = 0; g < ng; g++)
        {
            var group = groups[g];
            var t = ng == 1 ? 0.5f : g / (float)(ng - 1);
            var stationZ = cz + (0.15f + 0.85f * t) * reach;
            var lateral = t >= 0.99f ? 0f : ((g % 2 == 0) ? -1f : 1f) * (reach * 0.35f);
            var stationX = cx + lateral;
            var c = Math.Max(group.Count, 1);
            var clusterR = 4f + (c > 6 ? 4f : 0f);
            for (var i = 0; i < c; i++)
            {
                var a = (float)(i * Math.Tau / c);
                pts.Add(new Vector4(stationX + MathF.Sin(a) * clusterR, gy, stationZ + MathF.Cos(a) * clusterR, 1f));
            }
        }
        return pts;
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
                    NameId = Dungeon.TitleNameId,
                    DescriptionId = Dungeon.DescriptionId,
                    Difficulty = Dungeon.Difficulty,
                    IconId = Dungeon.IconId,
                    MiniGameType = CombatMiniGameType,
                    Launch = true,
                    Objectives =
                    [
                        new EncounterObjective
                        {
                            ObjectiveId = EncounterId, NameId = Dungeon.DescriptionId,
                            DescriptionId = Dungeon.DescriptionId,
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

                UiObjectiveAddPacket GoalRow() => new()
                {
                    ObjectiveId = EncounterId,
                    NameId = Dungeon.DescriptionId,
                };

                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(new ObjectiveActivatePacket { ObjectiveId = EncounterId, Total = 1 });
                player.SendTunneled(GoalRow());
                player.SendTunneled(MakeLaunch());
                player.SendTunneled(MakeEnter(0));
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(MakeLaunch());
                player.SendTunneled(GoalRow());
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit));
                player.SendTunneled(PacketEncounterDataCommon.CreateCombatRules());
                player.SendTunneled(MakeEnter(player.Guid));

                player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = true });
                player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = true });
                player.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = EncounterId,
                    InstanceId = EncounterInstanceId,
                    State = 6,
                });

                _logger.LogInformation("{dungeon}: entry sequence delivered to {name} (run {run}).",
                    Dungeon.Comment, player.Name, run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{dungeon}: entry sequence delivery failed.", Dungeon.Comment);
            }
        });
    }

    private void StartGroundAdoption(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000);
                if (player.Zone != this || run != _encounterRun)
                    return;

                var measured = player.Position.Y;
                if (MathF.Abs(measured - Dungeon.GroundY) < 0.75f)
                    return;

                Npc[] mobs;
                lock (_stateLock)
                {
                    _groundY = measured;
                    mobs = [.. _mobs];
                }

                foreach (var actor in mobs)
                {
                    bool idle;
                    lock (_stateLock)
                        idle = _mobStates.TryGetValue(actor.Guid, out var s) && !s.Charging;
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{dungeon}: ground adoption failed.", Dungeon.Comment);
            }
        });
    }

    private Npc? CreateMob(DungeonEnemy group, Vector4 pos)
    {
        if (!TryCreateNpc(out var npc))
            return null;

        npc.ModelId = group.ModelId;
        npc.NameId = group.Boss ? Dungeon.TitleNameId : 0;
        npc.Name = null;
        npc.HideNamePlate = false;
        npc.ShowHealthBar = true;
        npc.Scale = group.Scale;
        npc.Disposition = 0;
        npc.ActiveProfile = MobActiveProfile;
        npc.CompositeEffectId = 0;
        npc.MaxHealth = group.Health;
        npc.Health = group.Health;
        npc.IsInteractable = false;
        npc.InteractRange = 100;
        npc.Visible = true;
        npc.CursorId = 11;
        npc.WalkAnimId = -1;
        npc.RunAnimId = -1;
        npc.StandAnimId = -1;
        npc.MovementType = MovementTypePhysics;
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
            if (group.Boss)
                SendNpcHealth(p, npc);
        }

        return npc;
    }

    private void SendCombatMinimapMarkers(IReadOnlyList<ulong> guids)
    {
        if (guids.Count == 0)
            return;
        var badge = new PlayerUpdatePacketAddNotifications();
        foreach (var guid in guids)
            badge.Notifications.Add(new NotificationInfo { Guid = guid, Combat = true, Type = 3, Unknown10 = true });
        Broadcast(badge);
    }

    #endregion

    #region AI

    private void StartAi(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var elapsed = 0; elapsed < 15 * 60 * 1000; elapsed += TickMs)
                {
                    await Task.Delay(TickMs);
                    if (run != _encounterRun)
                        return;

                    var players = ActivePlayers();
                    if (players.Length == 0)
                        return;

                    Npc[] pack;
                    lock (_stateLock)
                        pack = [.. _mobs];
                    if (pack.Length == 0)
                        continue;

                    var now = Environment.TickCount64;
                    var dt = TickMs / 1000f;

                    foreach (var mob in pack)
                    {
                        if (!mob.IsAlive)
                            continue;

                        MobState? state;
                        lock (_stateLock)
                            _mobStates.TryGetValue(mob.Guid, out state);
                        if (state is null)
                            continue;

                        var here = new Vector3(mob.Position.X, mob.Position.Y, mob.Position.Z);

                        var tgt = NearestLivePlayer(here, players);
                        if (tgt is null)
                        {
                            TickMobReturnHome(mob, state, dt);
                            continue;
                        }

                        var target = new Vector3(tgt.Position.X, tgt.Position.Y, tgt.Position.Z);

                        if (!state.Charging)
                        {
                            var dx = target.X - here.X;
                            var dz = target.Z - here.Z;
                            if (dx * dx + dz * dz > AggroRange * AggroRange)
                                continue;
                            BeginCharge(mob, state);
                        }

                        TickMobCombat(mob, state, tgt, target, now, dt);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{dungeon}: AI loop failed (run {run}).", Dungeon.Comment, run);
            }
        });
    }

    private void BeginCharge(Npc mob, MobState state)
    {
        state.Charging = true;
        state.NextAttackTicks = Environment.TickCount64 + 1000 + _rng.Next(1500);
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = mob.Guid, ExpectedSpeed = 3f });
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = mob.Guid, ExpectedSpeed = MobChaseSpeed });
        Broadcast(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = mob.Guid,
            Status = (CharacterStatus)CharState_Charging,
        });
    }

    public override void OnNpcDamaged(Player player, Npc npc)
    {
        lock (_stateLock)
        {
            if (_mobStates.TryGetValue(npc.Guid, out var state) && !state.Charging)
                BeginCharge(npc, state);
        }
    }

    #endregion

    #region Kills / victory

    public override void OnNpcKilled(Player killer, Npc npc)
    {
        bool allClear;
        lock (_stateLock)
        {
            if (!_mobs.Remove(npc))
                return;
            _mobStates.Remove(npc.Guid);
            _killed++;
            allClear = !_won && _mobs.Count == 0;
        }

        Broadcast(new PlayerUpdatePacketRemoveNotifications { Guids = { npc.Guid } });
        var deathPos = npc.Position;

        npc.GracefulRemoval = (true, DeathHoldMs, 0, DeathPoofFxId, 1000);
        npc.Dispose();

        if (allClear)
            WinEncounter(killer, deathPos);
    }

    protected override int FailEncounterId => EncounterId;
    protected override int FailInstanceId => EncounterInstanceId;
    protected override string EncounterLogName => Dungeon.Comment;

    private void WinEncounter(Player player, Vector4 lastKillPos)
    {
        lock (_stateLock)
            _won = true;

        var enemies = _killed;
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
            member.SendTunneled(new ObjectiveCompletePacket { ObjectiveId = EncounterId });
            member.SendTunneled(new UiObjectiveCompletePacket { ObjectiveId = EncounterId });

            member.AwardXp(Dungeon.Xp);
            member.SendTunneled(new RewardBundlePacket { Xp = Dungeon.Xp });

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
        _logger.LogInformation("{dungeon}: WON — wheel armed, exit door out ({kills} enemies).", Dungeon.Comment, enemies);
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
        door.MovementType = MovementTypePhysics;
        door.RiderGuid = ulong.MaxValue;
        var doorZ = Dungeon.Radius > WalkThroughRadius
            ? Dungeon.CenterZ + Dungeon.Radius * SafeReach
            : Dungeon.CenterZ - 12f;
        door.UpdatePosition(new Vector4(Dungeon.CenterX, _groundY, doorZ, 1f), Quaternion.Identity);

        var badge = new PlayerUpdatePacketAddNotifications();
        badge.Notifications.Add(new NotificationInfo
        {
            Guid = door.Guid, Combat = false, Type = DoorBadgeType, Unknown3 = DoorBadgeUnknown3,
            ImageId = DoorMinimapImageId, DescriptionId = 0, NameId = DoorNameId, SubTextId = -1,
            Unknown8 = true, CompositeEffectId = 0, Unknown10 = true,
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
