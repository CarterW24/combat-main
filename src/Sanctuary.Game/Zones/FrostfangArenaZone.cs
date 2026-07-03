using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Zones;

// INSTANCE (Frostfang Fury): the REAL arena zone for the "Frostfang Growler!" combat encounter, done the
// proper way — a genuine server-side zone the player is TeleportToZone'd into, so tiles/visibility/NPC
// delivery all run through the normal engine pipeline.
//
// The world + coords come from the CLIENT'S OWN DATA (2026-07-01, see docs/STATUS.md):
//   world  = sg_random_encounter_clearing (green grass clearing; matches the Sunrise reference video)
//   center = (136, y≈14, 165) radius 100, from sg_random_encounter_clearingAreas.xml ("Bed" AreaDefinition)
//
// ENCOUNTER SPEC — from the Sunrise reference video (MB2zn8Um8g8), frame-audited 2026-07-02:
//   * dark "gloam" fog the whole fight (sky_shrouded_gloam.xml, sent at GO! teleport)
//   * phase 1 "Scare away the wolves!": continuous WAVES of wolves — ~5-7 alive at once, they run in
//     from the fog at the clearing edge; NO nameplate and NO overhead health bar on pack wolves
//   * then "Frostfang Alpha": red name + overhead health bar (bosses are the ONLY plated NPCs)
//   * Goal Complete! -> fog lifts -> door -> phase 2 (Shadow Fakes waves + boss "Taka the Shadow") -> exit
//     (phase 2 / doors / goals tracker: TODO — this class currently implements phase 1 + Alpha + return)
public sealed class FrostfangArenaZone : BaseZone
{
    private sealed class FrostfangArenaDefinition : BaseZoneDefinition
    {
    }

    // Real ground height: LIVE TEST 10 (2026-07-02 19:31) — the client settles the player to y≈0.0-0.6
    // across the clearing. (The old "y≈14" reading was wrong and left wolves hovering at canopy height.)
    private const float GroundY = 0.5f;

    private const int WolfModelId = 176;       // wolf.adr
    // HP pools sized against the ninja's ~8300-damage hits so fights read like the video (pack wolves
    // take a few hits, the Alpha is a real slugfest). One-shot kills removed the actor the same instant
    // as the hit, killing the floating damage number / recoil / bar movement with it.
    private const int WolfHealth = 30000;      // ~4 hits
    private const int AlphaHealth = 120000;    // ~15 hits
    private const float AlphaScale = 1.6f;     // visibly bigger than the pack
    private const int SmokePoof = 21;          // PFX_smoke_black_explosion (death poof)

    // Wave pacing (video: constant pressure, wolves replaced as they die, ~5-7 alive at once).
    private const int TotalSnarlers = 12;      // phase-1 wolf budget before the Alpha appears
    private const int InitialPack = 5;
    private const int RespawnDelayMs = 1300;   // gap before a replacement runs in from the fog
    private const float EdgeRadiusMin = 16f;   // replacements spawn out in the fog and run in
    private const float EdgeRadiusMax = 24f;

    // Chase-and-bite AI. Wolves surround the player (each owns a slot on a ring) instead of stacking.
    private const int TickMs = 300;
    private const float MoveSpeed = 7f;
    private const float YSpeed = 12f;          // vertical convergence to the player's REAL ground height
    private const float BiteRange = 2.6f;
    private const float EngageRadius = 1.9f;   // ring the wolves try to stand on around the player
    private const int BiteCooldownMs = 2200;
    private const int BiteDamage = 150;        // cosmetic vs the 2500-HP player; player HP pool is a TODO

    /// <summary>Optional spawn override pinned live via the "!arena set" chat command (fine-tuning).</summary>
    public static Vector4? SpawnOverride;

    // Client movement gate (OnPlayerUpdatePosition, RE'd): MovementType must be 1 (CONTROLLER —
    // ClientMovementManager interpolates to each sent position at ExpectedSpeed) or 2 (PHYSICS),
    // and the actor's rider must be the invalid-guid sentinel, else op125 updates are dropped.
    // Live 2014 capture: every walking NPC is type 2 (PHYSICS) — that path auto-plays locomotion.
    private const int WolfMovementTypePhysics = 2;

    // The reference video's phase-1 pack mixes colors — alternate the client's wolf material variants
    // (wolf.dma default / wolf_black / wolf_evil, extracted from the Assets packs).
    private static readonly string?[] PackSkins = [null, "wolf_black", null, "wolf_evil"];

    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly Random _rng = new();

    private readonly object _stateLock = new();
    private readonly List<Npc> _wolves = [];
    private readonly Dictionary<ulong, float> _engageAngle = [];
    private Npc? _alpha;
    private int _spawnedSnarlers;
    private int _killedSnarlers;
    private bool _alphaPhaseStarted;
    private int _encounterRun; // bumped every StartEncounter; stops stale AI loops

    // Goals tracker (op45 BaseObjectivePacket, RE'd — drafts/objective-goals-packets.md).
    // NameId 5698 is the only string id confirmed to resolve client-side (server-fed table caveat);
    // on the admin client an unknown id falls back to "<OBJECTIVE n>" — still proves the panel.
    private const int GoalWolves = 1;
    private const int GoalAlpha = 2;
    private const int GoalNameId = 5698;
    private const int WolfKillTarget = TotalSnarlers + 2; // pack budget + the Alpha's 2 escorts

    public FrostfangArenaZone(IServiceProvider serviceProvider)
        : base(CreateDefinition(), serviceProvider)
    {
        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    private static BaseZoneDefinition CreateDefinition() => new FrostfangArenaDefinition
    {
        Id = 174, // the Frostfang Growler activity id (traceability; the runtime zone Id is assigned by the manager)
        Name = "sg_random_encounter_clearing",
        TileSize = 64,
        // The clearing is tiny (playable r100 around (136,165)); pad the tile grid generously around it.
        StartLongitude = -2,
        EndLongitude = 8,
        StartLatitude = -2,
        EndLatitude = 8,
        Sky = null, // the GO! teleport sends sky_shrouded_gloam.xml (encounter mood); world default otherwise
        SpawnPosition = new Vector4(136f, GroundY + 2f, 165f, 1f), // small settle-drop onto real ground
        SpawnRotation = Quaternion.Identity,
    };

    /// <summary>Where GO! drops the player (the pinned override, if the user set one, else the real center).</summary>
    public Vector4 EffectiveSpawn => SpawnOverride ?? SpawnPosition;

    #region Zone lifecycle

    public override void OnClientIsReady(Player player)
    {
        // Finish the client's zone-in (same tail the starting zone sends): vitals + "zone data done".
        // Do NOT spawn NPCs here: the client sends ClientIsReady ~0.35s after BeginZoning, while the load
        // screen is still up, and discards every AddNpc sent then (LIVE TESTS 8+9, 2026-07-02).
        player.SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = 2500, MaxHitpoints = 2500 });
        player.SendTunneled(new ClientUpdatePacketMana { CurrentMana = 100, MaxMana = 100 });

        player.SendTunneled(new PacketZoneDoneSendingInitialData());
        player.SendTunneled(new ClientUpdatePacketDoneSendingPreloadCharacters());

        // Keep the weapon-driven ability toolbar alive in the arena.
        if (player.ActiveProfileId == NinjaWeaponAbilities.NinjaProfileId)
            player.SendTunneled(NinjaWeaponAbilities.BuildToolbar(player, _resourceManager));
    }

    // The load screen has actually dropped (this is the handler that flips Player.Visible=true), so the
    // client accepts AddNpc from here on. This is the encounter's true start line.
    public override void OnClientFinishedLoading(Player player)
    {
        StartEncounter(player);
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
            _engageAngle.Clear();
            _alpha?.Dispose();
            _alpha = null;
            _spawnedSnarlers = 0;
            _killedSnarlers = 0;
            _alphaPhaseStarted = false;
            _encounterRun++;

            for (var i = 0; i < InitialPack; i++)
                SpawnSnarler(player);
        }

        // THE COMBAT GATE (RE'd — see PacketEncounterDataCommon) + Goals. LIVE TEST 12: sending these
        // in the same instant as ClientFinishedLoading did NOT take effect (no combat camera/bars/
        // numbers/goals), while the SAME packets sent mid-session (exit path) worked — the client's
        // zone-in tail evidently resets encounter/UI state right after FinishedLoading. So deliver the
        // encounter state a beat AFTER the load settles.
        var run = _encounterRun;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);

                if (player.Zone != this || run != _encounterRun)
                    return;

                // MASTER GATE (RE'd 2026-07-02, client case 114 @0xaa3dcf): the LAUNCH form of the
                // details packet creates the client's MiniGameState (ClientMiniGameManager::sub_9BB2D0).
                // While m_MiniGameStates is empty, EVERY op45 objective packet is silently dropped
                // (goals panel never renders) and IsInMiniGame() stays false. The offer popup's state
                // does not reliably survive the zone-in, so re-launch it here before the goal packets.
                player.SendTunneled(new EncounterDetailsResponsePacket
                {
                    NameId = 93276,        // "Frostfang Growler!" (ClientActivityDefinitions Id 174)
                    DescriptionId = 104171,
                    Difficulty = 1,
                    IconId = 1345,
                    Launch = true,
                });

                player.SendTunneled(PacketEncounterDataCommon.CreateCombatRules());
                player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = true });
                player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = true });

                // Goals panel: phase-1 objective ("Scare away the wolves!" in the reference video).
                player.SendTunneled(new ObjectiveAddPacket
                {
                    ObjectiveId = GoalWolves,
                    NameId = GoalNameId,
                    Status = 2,
                    Count = 0,
                    Total = WolfKillTarget,
                });

                _logger.LogInformation("Frostfang arena: combat ruleset + goal delivered (post-load, run {run}).", run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena: delayed encounter-state delivery failed.");
            }
        });

        _logger.LogInformation("Frostfang arena: encounter start — {n} wolves inbound for {name} (budget {total}).",
            InitialPack, player.Name, TotalSnarlers);

        StartWolfAi(player, _encounterRun);
    }

    // Spawns one pack wolf out in the fog and registers it (caller holds _stateLock).
    private void SpawnSnarler(Player player)
    {
        if (_spawnedSnarlers >= TotalSnarlers)
            return;

        var angle = (float)(_rng.NextDouble() * Math.Tau);
        var radius = EdgeRadiusMin + (float)_rng.NextDouble() * (EdgeRadiusMax - EdgeRadiusMin);
        var pos = new Vector4(
            player.Position.X + MathF.Sin(angle) * radius,
            player.Position.Y,
            player.Position.Z + MathF.Cos(angle) * radius,
            1f);

        // Pack wolves are anonymous (reference video: no nameplate) and color-mixed
        // (gray + dark variants run together in the video's pack).
        var skin = PackSkins[_spawnedSnarlers % PackSkins.Length];

        // showHealthBar TRUE: the video's arena wolves DO carry the normal green nameplate bar (the
        // BOSS gets the distinct op32/sub9 boss display instead) — user-confirmed from the reference.
        var wolf = CreateWolf(player, name: null, WolfHealth, 1f, pos, showHealthBar: true, skin);
        if (wolf is null)
            return;

        _spawnedSnarlers++;
        _wolves.Add(wolf);
        // Spread the pack around the player: each wolf owns a slot on the engage ring.
        _engageAngle[wolf.Guid] = angle;
    }

    private Npc? CreateWolf(Player player, string? name, int health, float scale, Vector4 pos,
        bool showHealthBar, string? textureAlias = null, int nameColor = 0)
    {
        if (!TryCreateNpc(out var npc))
            return null;

        npc.NameColor = nameColor;

        npc.ModelId = WolfModelId;
        npc.TextureAlias = textureAlias; // wolf material variant (wolf_black/wolf_evil/... or null=default)
        npc.Name = name;                // null => no nameplate (pack wolves); bosses carry their name
        npc.NameId = 0;
        npc.Disposition = 0;            // hostile — a combat target
        npc.EnemyStatus = true;         // AddNpc bool38: 1 on every live attackable hostile (red-name flag)
        npc.Scale = scale;
        npc.IsInteractable = false;
        npc.InteractRange = 0;          // no interact = no "press X to talk" prompt on hostiles
        npc.Visible = true;
        npc.CursorId = 11;              // crossed-swords attack cursor
        npc.MaxHealth = health;
        npc.Health = health;
        npc.ShowHealthBar = showHealthBar;

        // GROUND TRUTH (2014-03-25 capture, all 370 live AddNpc packets): the real server sends
        // Walk/Run/StandAnimId = -1 on every NPC — locomotion clips come from the model itself.
        // Overriding them with small ids replaces the wolf's run clip with an invalid one = SLIDING.
        npc.WalkAnimId = -1;
        npc.RunAnimId = -1;
        npc.StandAnimId = -1;

        // THE MOVEMENT FIX (client OnPlayerUpdatePosition RE, 2026-07-02): without these three, the
        // client parses onward op125 updates and silently drops them -> wolves frozen at spawn.
        // MovementType 2 = PHYSICS: what every walking NPC in the live capture uses; that path runs
        // the character through ProxiedCharacter physics which drives locomotion animation itself.
        npc.MovementType = WolfMovementTypePhysics;
        npc.Speed = MoveSpeed;          // feeds the client's ExpectedSpeed for the actor
        npc.RiderGuid = ulong.MaxValue; // "no rider" invalid-guid sentinel gate

        npc.UpdatePosition(pos, Quaternion.Identity);

        // push directly so the player sees it immediately (the tile system covers everyone else)
        player.OnAddVisibleNpcs(npc);
        npc.OnAddVisiblePlayers(player);
        SendNpcRelevance(player, npc);
        SendNpcHealth(player, npc);

        // Belt-and-suspenders: also set the actor's expected speed explicitly (sub23 packet works live).
        player.SendTunneled(new PlayerUpdatePacketExpectedSpeed
        {
            Guid = npc.Guid,
            ExpectedSpeed = MoveSpeed,
        });

        return npc;
    }

    // Chase-the-player AI: position tick + client interpolation; bites use HitPointModification
    // (source=wolf, victim=player, NEGATIVE amount -> floating damage number, no phantom player swing).
    private void StartWolfAi(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Frostfang arena: AI loop started (run {run}).", run);

            try
            {
                var nextBiteMs = new Dictionary<ulong, int>();
                var diagCountdown = 0;

                for (var elapsed = 0; elapsed < 15 * 60 * 1000; elapsed += TickMs)
                {
                    await Task.Delay(TickMs);

                    if (player.Zone != this)
                    {
                        _logger.LogInformation("Frostfang arena: AI loop exit — player left the zone (run {run}).", run);
                        return;
                    }

                    if (run != _encounterRun)
                    {
                        _logger.LogInformation("Frostfang arena: AI loop exit — superseded by a new run (run {run}).", run);
                        return;
                    }

                    Npc[] pack;
                    lock (_stateLock)
                        pack = _alpha is { } alpha ? [.. _wolves, alpha] : [.. _wolves];

                    if (pack.Length == 0)
                        continue; // between waves (respawn pending) or encounter done

                    var target = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);

                    // TEMP DIAGNOSTIC (invisible/floating-wolf hunt): prove the AI is ticking and show
                    // where the client should be drawing the pack. Remove once movement is confirmed.
                    if (--diagCountdown <= 0)
                    {
                        diagCountdown = 20; // every ~6s
                        _logger.LogInformation(
                            "Frostfang arena AI: alive={n} wolf0=({wx:F1},{wy:F1},{wz:F1}) player=({px:F1},{py:F1},{pz:F1})",
                            pack.Length, pack[0].Position.X, pack[0].Position.Y, pack[0].Position.Z,
                            target.X, target.Y, target.Z);
                    }

                    foreach (var wolf in pack)
                    {
                        if (!wolf.IsAlive)
                            continue;

                        var here = new Vector3(wolf.Position.X, wolf.Position.Y, wolf.Position.Z);

                        // Each wolf converges on ITS OWN slot around the player, so the pack surrounds
                        // rather than stacking on one point.
                        var slotAngle = _engageAngle.GetValueOrDefault(wolf.Guid);
                        var slot = target + new Vector3(MathF.Sin(slotAngle), 0f, MathF.Cos(slotAngle)) * EngageRadius;

                        var toPlayerH = new Vector2(target.X - here.X, target.Z - here.Z);
                        var distToPlayerH = toPlayerH.Length();

                        // op125 ROTATION = a normalized FACING DIRECTION vector (x, 0, z), NOT a
                        // quaternion — confirmed against the real 2014 SOE captures (every live op125
                        // rotation is a unit XZ vector; client reader sub_8E5940 reads 3 raw floats into
                        // m_vRotation and sets W=0). PacketWriter.Write(Quaternion, limited:true) emits
                        // X,Y,Z, so pack the facing there. (The old CreateFromYawPitchRoll sent
                        // (0, sin(yaw/2), 0) — a near-zero up-vector = the "sliding, not facing" bug.)
                        var face = distToPlayerH > 0.01f ? toPlayerH / distToPlayerH : new Vector2(0f, 1f);
                        var rot = new Quaternion(face.X, 0f, face.Y, 0f);

                        // Always converge vertically to the player's real ground height — the server has
                        // no heightmap, and a fixed spawn Y left wolves hovering at treetop level.
                        var dt = TickMs / 1000f;
                        var newY = MoveToward(here.Y, target.Y, YSpeed * dt);

                        if (distToPlayerH > BiteRange)
                        {
                            var toSlot = new Vector2(slot.X - here.X, slot.Z - here.Z);
                            var distToSlot = toSlot.Length();
                            var step = MathF.Min(MoveSpeed * dt, distToSlot);
                            var dir = distToSlot > 0.01f ? toSlot / distToSlot : Vector2.Zero;

                            var newPos = new Vector4(here.X + dir.X * step, newY, here.Z + dir.Y * step, wolf.Position.W);

                            wolf.UpdatePosition(newPos, rot);
                            // State bit0 SET means "no speed" client-side (RE'd) -> send 0 while MOVING.
                            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = wolf.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
                            });

                            // Locomotion is the client's job now: PHYSICS movement type + AnimIds -1
                            // (live-server ground truth). sub8 SetAnimation is only for one-off action
                            // clips (bites/howls), never run/stand — sending clip ids here fought the
                            // model's own locomotion and caused the sliding.
                        }
                        else
                        {
                            var newPos = new Vector4(here.X, newY, here.Z, wolf.Position.W);

                            wolf.UpdatePosition(newPos, rot);
                            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = wolf.Guid, Position = newPos, Rotation = rot, State = 1, Unknown = 0,
                            });

                            var next = nextBiteMs.GetValueOrDefault(wolf.Guid);
                            if (elapsed >= next)
                            {
                                nextBiteMs[wolf.Guid] = elapsed + BiteCooldownMs;

                                // The REAL per-hit packet (op32/sub7), field order locked to the live 2014
                                // dump: Guid1 = Guid2 = TARGET, Guid3 = ATTACKER, Int5 = HP after the hit.
                                player.SendTunneled(new CombatPacketAttackProcessed
                                {
                                    Guid1 = player.Guid,    // target (numbers render when target == me)
                                    Guid2 = player.Guid,
                                    Guid3 = wolf.Guid,      // attacker
                                    Int1 = BiteDamage,
                                    Int2 = 2500,            // player max HP (bar %; real HP pool is a TODO)
                                    Int3 = 7,               // PFX_Hit_Flash on the target
                                    Int5 = 2500,            // current HP after hit (player pool TODO)
                                });
                            }
                        }
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

    private static float MoveToward(float current, float goal, float maxDelta)
    {
        var delta = goal - current;
        if (MathF.Abs(delta) <= maxDelta)
            return goal;
        return current + MathF.Sign(delta) * maxDelta;
    }

    public override void OnNpcKilled(Player killer, Npc npc)
    {
        var startAlpha = false;
        var victory = false;

        var killedCount = 0;

        lock (_stateLock)
        {
            if (_wolves.Remove(npc))
            {
                _engageAngle.Remove(npc.Guid);
                killedCount = ++_killedSnarlers;

                if (_spawnedSnarlers < TotalSnarlers)
                {
                    // Waves: a replacement runs in from the fog after a short beat.
                    ScheduleSnarlerRespawn(killer, _encounterRun);
                }
                else if (_wolves.Count == 0 && _alpha is null && !_alphaPhaseStarted)
                {
                    _alphaPhaseStarted = true;
                    startAlpha = true;
                }
            }
            else if (ReferenceEquals(npc, _alpha))
            {
                _alpha = null;
                victory = true;
            }
            else
            {
                return; // not an encounter NPC
            }
        }

        killer.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = npc.Guid,
            CompositeEffectId = SmokePoof,
            Position = npc.Position,
        });
        npc.Dispose();

        // Tick the Goals panel (wolf kills advance goal 1; the Alpha completes goal 2).
        if (killedCount > 0)
            killer.SendTunneled(new ObjectiveUpdatePacket { ObjectiveId = GoalWolves, Count = killedCount });
        else if (victory)
            killer.SendTunneled(new ObjectiveUpdatePacket { ObjectiveId = GoalAlpha, Count = 1 });

        if (startAlpha)
        {
            _logger.LogInformation("Frostfang arena: pack budget cleared -> the Frostfang Alpha emerges.");

            lock (_stateLock)
            {
                // The boss is the ONLY plated NPC: nameplate + overhead health bar (reference video).
                var angle = (float)(_rng.NextDouble() * Math.Tau);
                var pos = new Vector4(
                    killer.Position.X + MathF.Sin(angle) * EdgeRadiusMin,
                    killer.Position.Y,
                    killer.Position.Z + MathF.Cos(angle) * EdgeRadiusMin,
                    1f);

                // RED boss name: live hostiles carry NameColor 0xFFFF0000 (int ARGB) in their FIRST
                // AddNpc — set it before CreateWolf pushes the packet (the old re-push-after was too late,
                // and the packet field was a float that mangled the bits until 2026-07-02).
                _alpha = CreateWolf(killer, "Frostfang Alpha", AlphaHealth, AlphaScale, pos, showHealthBar: true,
                    nameColor: unchecked((int)0xFFFF0000));
                if (_alpha is not null)
                {
                    _engageAngle[_alpha.Guid] = angle;

                    // Boss plate: boss health display (op32/sub9 -> AddBoss, RE'd).
                    killer.SendTunneled(new CombatPacketEnableBossDisplay { Guid = _alpha.Guid, Enable = true });
                }

                // Two anonymous escorts flank the boss (video shows adds fighting alongside the Alpha).
                _spawnedSnarlers = TotalSnarlers - 2;
                SpawnSnarler(killer);
                SpawnSnarler(killer);
            }

            // Second objective appears with the boss (video: the Goals panel grows per phase).
            killer.SendTunneled(new ObjectiveAddPacket
            {
                ObjectiveId = GoalAlpha,
                NameId = GoalNameId,
                Status = 2,
                Count = 0,
                Total = 1,
            });
        }
        else if (victory)
        {
            _logger.LogInformation("Frostfang arena: ALPHA DOWN -> victory; returning {name} home in 6s.", killer.Name);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(6000);
                    ReturnHome(killer);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Frostfang arena victory return failed.");
                }
            });
        }
    }

    private void ScheduleSnarlerRespawn(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RespawnDelayMs);

                if (player.Zone != this || run != _encounterRun)
                    return;

                lock (_stateLock)
                    SpawnSnarler(player);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena wolf respawn failed.");
            }
        });
    }

    /// <summary>Release the client from the encounter (RE'd exit protocol): remove the minigame
    /// state (op39/sub19 — full client-side teardown incl. combat exit for combat-type games) and
    /// restore the default combat ruleset (op62) + clear the transient fighting state. Without this
    /// the client stays InCombat forever (can't change jobs after leaving — LIVE TEST 11 bug).</summary>
    public void EndEncounterForPlayer(Player player)
    {
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });

        _logger.LogInformation("Frostfang arena: encounter released for {name} (state remove + default rules).",
            player.Name);
    }

    private void ReturnHome(Player player)
    {
        if (player.Zone != this)
            return; // already left

        EndEncounterForPlayer(player);

        var home = _zoneManager.StartingZone;

        player.TeleportToZone(home, home.SpawnPosition, home.SpawnRotation, sky: null, geometryId: 0);
    }

    #endregion
}
