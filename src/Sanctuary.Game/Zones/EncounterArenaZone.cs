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

// GENERIC data-driven combat dungeon (battle instance). One class runs ANY dungeon defined in
// DungeonCatalog: it reuses the proven Frostfang/Tormented-Spirits pipeline (world entry + ground
// adoption, the combat gate + goals burst, the chase/claw pack AI, death -> win, loot wheel + score +
// exit door, party co-op) but takes the world, arena center, enemy roster, text and XP from a
// DungeonDefinition. Goal is always "defeat every enemy". See DungeonDefinition.cs for the data.
public sealed class EncounterArenaZone : CombatEncounterZone
{
    private sealed class EncounterArenaDefinition : BaseZoneDefinition { }

    public DungeonDefinition Dungeon { get; }
    public int EncounterId => Dungeon.ActivityId;
    private const int EncounterInstanceId = 1;

    private const int CombatMiniGameType = 4; // client MINI_GAME_TYPE_COMBAT — the goals-pane gate
    // KnockoutLimit + the knockout/fail/revive lifecycle now live in CombatEncounterZone.

    // Enemy recipe (Frostfang pack-wolf / spirit recipe).
    private const int MobActiveProfile = 151;
    private const int SpawnPoofFxId = 46;
    private const int DeathPoofFxId = 5017;
    private const int DeathHoldMs = 1500;
    private const int CharState_Baseline = 0x1;
    private const int CharState_Charging = 0x8001;
    private const int MovementTypePhysics = 2;

    // Chase/claw tuning (from the spirit AI).
    private const int TickMs = 300;
    private const float YSpeed = 12f;
    private const float ClawRange = 2.6f;
    private const float EngageRadius = 1.9f;
    private const float ChaseSpeed = 5f;
    private const float AggroRange = 16f;
    private const int ClawCooldownMs = 4000;
    private const int ClawGlobalGapMs = 1200;
    private const int ClawDamage = 150;
    private const int ClawCritDamage = 300;
    private const int ClawCritPercent = 10;
    private const int ClawFxId = 5409;
    private const int ClawCritFxId = 5622;

    // Exit door (Frostfang/Spirits recipe).
    private const int DoorModelId = 846;
    private const int DoorNameId = 4826;
    private const float DoorScale = 1.2f;
    private const int DoorInteractRange = 125;
    private const int DoorActiveProfile = 28;
    private const int DoorCursorId = 17;
    private const int DoorMinimapImageId = 186;
    private const int DoorBadgeType = 7;
    private const int DoorBadgeUnknown3 = 102;

    private sealed class MobState
    {
        public bool Charging;
        public long NextClawTicks;
        public float SlotAngle;
        public Vector4 Home;    // spawn post — mobs walk back here and idle while the player is knocked down
        public bool Idling;     // true once parked at Home (so we broadcast the idle stop only once)
        public bool Planted;    // true once stopped in attack range — stop re-broadcasting position every tick
                                // (that bobbed the model + fought the swing animation = the attack jitter)
    }

    private readonly object _stateLock = new();
    private readonly List<Npc> _mobs = [];
    private readonly Dictionary<ulong, MobState> _mobStates = [];
    private Npc? _exitDoor;
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

    // How far from center (as a fraction of the Bed radius) we place the FAR end of the walk-through. Kept
    // conservative because the Bed sphere is a loose bounding volume — the real walkable cave is smaller,
    // so staying well inside it keeps enemies + the exit on actual floor rather than in a wall / the void.
    private const float SafeReach = 0.38f;

    // Maps with Radius above this are the big real dungeon worlds -> walk-through layout (enemies spread
    // north from the centered spawn). At or below, it's a small scattered-encounter arena -> tight ring.
    private const float WalkThroughRadius = 120f;

    private static BaseZoneDefinition CreateDefinition(DungeonDefinition d)
    {
        const int tile = 64;
        const float pad = 96f; // extra margin so entities near the edge always have a tile

        // Scale the tile grid to the ACTUAL map bounds (center +/- radius). The old fixed -2..8 grid
        // (coords -128..512) only fit the tiny arenas; the real dungeon worlds have centers up to ~670 and
        // radii up to ~600, so their entities fell outside the grid and never rendered. longitude = X,
        // latitude = Z.
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
            // SPAWN AT THE BED CENTER (dropped ~20u so the client settles onto the floor via ground
            // adoption). The client stores NO player-spawn point for these worlds, and the Bed sphere is
            // only the bounding volume — an edge offset (my earlier south-edge spawn) lands OUTSIDE the
            // actual cave geometry and the player falls through ("way below the map"). The center is the
            // one point guaranteed to be inside the room, so we spawn there and keep enemies within a
            // safe fraction of the radius. Per-dungeon spawns can be refined by measuring in-game.
            SpawnPosition = new Vector4(d.CenterX, d.GroundY + 20f, d.CenterZ, 1f),
            SpawnRotation = Quaternion.Identity,
        };
    }

    #region Zone lifecycle

    public override void OnClientIsReady(Player player)
    {
        // Enter at the player's REAL max HP (full) so the bar matches what the real-damage claw reports
        // (Stats[MaxHealth]) — a fixed 2500 here made the bar jump on the first hit.
        var startHp = player.Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh0) ? mh0.Int : 2500;
        player.CurrentHitpoints = startHp;
        player.SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = startHp, MaxHitpoints = startHp });
        player.SendTunneled(new ClientUpdatePacketMana { CurrentMana = 100, MaxMana = 100 });
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

    private void Broadcast(ISerializablePacket packet)
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
            if (_exitDoor is not null) live.Add(_exitDoor);
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
            _exitDoor?.Dispose();
            _exitDoor = null;
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

    /// <summary>Enemy spawn points, ordered to match the group iteration in <see cref="StartEncounter"/>
    /// (group 0's enemies first, then group 1's, ...).
    /// <para>Small arenas (Radius &lt;= WalkThroughRadius): the original tight two-ring cluster at center —
    /// an in-place arena brawl.</para>
    /// <para>Big dungeon worlds: each enemy GROUP is a "station" spread along the path from the entrance
    /// (south edge) to the far end (north), so the player walks through fighting cluster after cluster,
    /// with the last group (usually the boss) waiting at the far end. Enemies only aggro within
    /// <see cref="AggroRange"/>, so distant clusters stay put until you reach them.</para></summary>
    private List<Vector4> BuildDungeonSpawns()
    {
        var pts = new List<Vector4>(Math.Max(Dungeon.TotalEnemies, 1));
        var cx = Dungeon.CenterX;
        var cz = Dungeon.CenterZ;
        var gy = Dungeon.GroundY;

        // Small arena: concentric rings around center (unchanged behavior for the scattered encounters).
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

        // Walk-through: spawn is at CENTER, so lay the stations out NORTH of it within SafeReach, closest
        // group just ahead of the spawn and the last group (usually the boss) at the far end. You fight
        // forward from the middle of the room toward the boss; distant clusters stay dormant until you
        // reach them (AggroRange). Everything stays inside the safe radius so it lands on real floor.
        var groups = Dungeon.Enemies;
        var ng = groups.Length;
        var reach = Dungeon.Radius * SafeReach;
        for (var g = 0; g < ng; g++)
        {
            var group = groups[g];
            var t = ng == 1 ? 0.5f : g / (float)(ng - 1);        // 0 = nearest .. 1 = far end
            var stationZ = cz + (0.15f + 0.85f * t) * reach;     // north of center, within reach
            // zig-zag the mid stations left/right so it isn't a straight line; boss station centered.
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
        npc.NameId = Dungeon.TitleNameId; // plate hidden for mobs; bosses show it as the dungeon name
        npc.Name = null;
        npc.HideNamePlate = !group.Boss;
        npc.ShowHealthBar = group.Boss;
        npc.Scale = group.Scale;
        npc.Disposition = 0;              // hostile
        npc.ActiveProfile = MobActiveProfile;
        npc.CompositeEffectId = 0;
        npc.MaxHealth = group.Health;
        npc.Health = group.Health;
        npc.IsInteractable = true;
        npc.InteractRange = 100;
        npc.Visible = true;
        npc.CursorId = 11;                // attack cursor
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
                var lastPackClaw = 0L;
                for (var elapsed = 0; elapsed < 15 * 60 * 1000; elapsed += TickMs)
                {
                    await Task.Delay(TickMs);
                    if (player.Zone != this || run != _encounterRun)
                        return;

                    Npc[] pack;
                    lock (_stateLock)
                        pack = [.. _mobs];
                    if (pack.Length == 0)
                        continue;

                    var now = Environment.TickCount64;
                    var target = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);
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

                        // Player is knocked down: DISENGAGE — amble back to the home post and idle there until
                        // they revive. Reset Charging so the mob re-engages cleanly on revive.
                        if (player.IsDead)
                        {
                            state.Charging = false;
                            state.Planted = false;
                            var toHome = new Vector2(state.Home.X - here.X, state.Home.Z - here.Z);
                            var distHome = toHome.Length();
                            if (distHome > 0.6f)
                            {
                                state.Idling = false;
                                var stepH = MathF.Min(ChaseSpeed * dt, distHome);
                                var dirH = toHome / distHome;
                                var nyH = MoveToward(here.Y, state.Home.Y, YSpeed * dt);
                                var npH = new Vector4(here.X + dirH.X * stepH, nyH, here.Z + dirH.Y * stepH, mob.Position.W);
                                var frotH = new Quaternion(dirH.X, 0f, dirH.Y, 0f);
                                mob.UpdatePosition(npH, frotH);
                                Broadcast(new PlayerUpdatePacketUpdatePosition { Guid = mob.Guid, Position = npH, Rotation = frotH, State = 0, Unknown = 0 });
                            }
                            else if (!state.Idling)
                            {
                                state.Idling = true; // arrived — plant it idle once (State 1 = standing)
                                Broadcast(new PlayerUpdatePacketUpdatePosition { Guid = mob.Guid, Position = mob.Position, Rotation = mob.Rotation, State = 1, Unknown = 0 });
                            }
                            continue;
                        }

                        if (!state.Charging)
                        {
                            var dx = target.X - here.X;
                            var dz = target.Z - here.Z;
                            if (dx * dx + dz * dz > AggroRange * AggroRange)
                                continue;
                            BeginCharge(mob, state);
                        }

                        var slot = target + new Vector3(MathF.Sin(state.SlotAngle), 0f, MathF.Cos(state.SlotAngle)) * EngageRadius;
                        var toPlayerH = new Vector2(target.X - here.X, target.Z - here.Z);
                        var distToPlayerH = toPlayerH.Length();
                        var face = distToPlayerH > 0.01f ? toPlayerH / distToPlayerH : new Vector2(0f, 1f);
                        var rot = new Quaternion(face.X, 0f, face.Y, 0f);
                        var newY = MoveToward(here.Y, target.Y, YSpeed * dt);

                        if (distToPlayerH > ClawRange)
                        {
                            state.Planted = false;
                            var toSlot = new Vector2(slot.X - here.X, slot.Z - here.Z);
                            var distToSlot = toSlot.Length();
                            var step = MathF.Min(ChaseSpeed * dt, distToSlot);
                            var dir = distToSlot > 0.01f ? toSlot / distToSlot : Vector2.Zero;
                            var newPos = new Vector4(here.X + dir.X * step, newY, here.Z + dir.Y * step, mob.Position.W);
                            mob.UpdatePosition(newPos, rot);
                            Broadcast(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = mob.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
                            });
                        }
                        else
                        {
                            // In attack range: plant ONCE (stop + face), then just claw. Re-broadcasting the
                            // position every tick (with the per-tick Y-lerp) bobbed the model and fought the
                            // swing clip — that was the attack jitter. The claw's AttackProcessed drives the
                            // swing without moving the actor.
                            if (!state.Planted)
                            {
                                state.Planted = true;
                                var newPos = new Vector4(here.X, newY, here.Z, mob.Position.W);
                                mob.UpdatePosition(newPos, rot);
                                Broadcast(new PlayerUpdatePacketUpdatePosition
                                {
                                    Guid = mob.Guid, Position = newPos, Rotation = rot, State = 1, Unknown = 0,
                                });
                            }

                            if (now >= state.NextClawTicks && now - lastPackClaw >= ClawGlobalGapMs && !player.IsDead)
                            {
                                state.NextClawTicks = now + ClawCooldownMs;
                                lastPackClaw = now;
                                var crit = _rng.Next(100) < ClawCritPercent;
                                var dmg = crit ? ClawCritDamage : ClawDamage;

                                // REAL damage now (was cosmetic 2500/2500): drop the player's HP and knock
                                // them out at 0 -> OnPlayerKnockedOut runs the KO-counter / fail flow.
                                player.TakeDamage(dmg);
                                var maxHp = player.Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : 2500;

                                Broadcast(new CombatPacketAttackProcessed
                                {
                                    AttackerGuid = mob.Guid,
                                    TargetGuid = player.Guid,
                                    Damage = dmg,
                                    MaxHealth = maxHp,
                                    CompositeEffectId = crit ? ClawCritFxId : ClawFxId,
                                    CurrentHealth = player.CurrentHitpoints,
                                });

                                // Boss models whose default contact event doesn't animate (Abominable
                                // Snowman) need an explicit swing clip so they don't claw while frozen.
                                if (Sanctuary.Game.Entities.CombatNpc.ExplicitAttackAnimByModel.TryGetValue(mob.ModelId, out var swingAnimId))
                                    Broadcast(new PlayerUpdatePacketSetAnimation { Guid = mob.Guid, AnimationId = swingAnimId });
                            }
                        }
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
        state.NextClawTicks = Environment.TickCount64 + 1000 + _rng.Next(1500);
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = mob.Guid, ExpectedSpeed = 3f });
        Broadcast(new PlayerUpdatePacketExpectedSpeed { Guid = mob.Guid, ExpectedSpeed = ChaseSpeed });
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

    // Knockout / fail / revive lifecycle lives in CombatEncounterZone — supply the encounter id + log label.
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
        // Arena: near center (by the spawn). Walk-through: at the FAR end, where the player finishes the
        // last cluster — a portal out at the end of the dungeon (the arena's 125u interact range wouldn't
        // reach the far end of a big map from center).
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

        lock (_stateLock)
            _exitDoor = door;
    }

    public bool IsExitDoor(ulong guid)
    {
        lock (_stateLock)
            return _exitDoor is { } door && door.Guid == guid;
    }

    public void UseExitDoor(Player player)
    {
        _logger.LogInformation("{dungeon}: {name} used the exit door.", Dungeon.Comment, player.Name);
        ReturnHome(player);
    }

    public void EndEncounterForPlayer(Player player, bool won)
    {
        if (won)
            player.SendTunneled(new MiniGameGameOverPacket(won: true));
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });
        player.SendTunneled(new UiObjectiveClearPacket());
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

    private static float MoveToward(float current, float goal, float maxDelta)
    {
        var delta = goal - current;
        if (MathF.Abs(delta) <= maxDelta)
            return goal;
        return current + MathF.Sign(delta) * maxDelta;
    }

    #endregion
}
