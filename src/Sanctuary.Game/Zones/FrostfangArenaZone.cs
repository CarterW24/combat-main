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

    // Encounter identity, shared by the details header ints + EncounterStatePacket + PlayerEnter
    // (the live server uses one [encounterId][instanceId] pair across all of them). 174 = the
    // Frostfang Growler activity id (ClientActivityDefinitions).
    public const int EncounterId = 174;
    public const int EncounterInstanceId = 1;

    // Client enum MINI_GAME_TYPE_COMBAT = 4 (IDA). The minigame status handler only shows the objective
    // (goals) pane for a COMBAT-type minigame — see the details packet below.
    private const int CombatMiniGameType = 4;

    private const int WolfModelId = 176;       // wolf.adr
    // Pack wolves are a low-HP SWARM (reference video: lots of wolves, each dies fast). ~5000 HP is a
    // basic hit or two — the special (~8300) one-shots, which is fine for trash. (We keep it a touch
    // above a single basic hit so the floating damage number / bar-drain still reads before it poofs.)
    private const int WolfHealth = 5000;
    // The Alpha does NOT die (video: he drops to very low HP then FLEES and the goal completes). Give him
    // a real health bar to drain, and flee once a hit takes him at/below the threshold. The threshold is
    // kept ABOVE a single max hit (~8300) so the triggering hit never also kills him.
    private const int AlphaHealth = 80000;
    private const int AlphaFleeThreshold = 12000;  // ~15% — "really low health" then runs away
    private const float AlphaScale = 1.6f;     // visibly bigger than the pack
    private const int SmokePoof = 21;          // PFX_smoke_black_explosion (death poof)

    // Wolf action animation-group ids — GROUND TRUTH from wolf.adr's anim table (extracted from
    // AssetsW_001.pack 2026-07-03) x AnimationGroups.xml. wolf.adr slots: com_death_01 -> group 1151,
    // com_death_static_01 -> 1152, com_h2h_attack_01 -> 1001, com_recoil_01 -> 1121, com_knock_down -> 1402
    // (1402 also appears in the live 2014 sub8 samples — cross-validates the table). Sent via op35/sub8.
    private const int WolfDeathAnimId = 1151;      // com_death_01 (falls over)
    private const int WolfDeathHoldMs = 1400;      // let the death clip play before the poof+despawn

    // Wave pacing (video: a constant swarm — many wolves alive at once, replaced as they die).
    private const int TotalSnarlers = 36;      // phase-1 wolf budget before the Alpha appears
    private const int InitialPack = 12;        // how many are alive/charging at once
    private const int RespawnDelayMs = 900;    // gap before a replacement runs in from the fog
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

    // Heart pickups (video: wolves drop pink hearts that heal +125 when the player walks over them).
    // Model 736 = powerup_health_buff.adr (Models.txt) — the COMBAT powerup family (damage/defense/health
    // buffs), i.e. the real encounter drop. LIVE TEST 2026-07-03 disproved the earlier guess 469
    // (sg_icon_health_pickup_anim_bbe = the DERBY track pickup — rendered as a flat ring decal, not a heart).
    private const int HeartModelId = 736;
    private const int HeartHeal = 125;            // the green "+125" the video shows
    private const float HeartPickupRange = 2.6f;  // walk-over radius
    private const int HeartDropPercent = 35;      // chance a scared-off wolf leaves a heart
    // Pickup FX 16324 = PFX_heal_health_red_sm_short_head (small short heal blip at the head — the pickup
    // pop). LIVE TEST 2026-07-03 disproved the earlier guess 51 (= PFX_poison_purple_hand-r_trail_LOOP —
    // wrong effect AND a loop that never ends; it drew a huge red/white cross over the player).
    private const int HeartHealFxId = 16324;
    private readonly List<Npc> _hearts = [];

    private readonly object _stateLock = new();
    private readonly List<Npc> _wolves = [];
    private readonly Dictionary<ulong, float> _engageAngle = [];
    private Npc? _alpha;
    private int _spawnedSnarlers;
    private int _killedSnarlers;
    private bool _alphaPhaseStarted;
    private bool _alphaFleeing;    // set once the Alpha hits the flee threshold (he runs, never dies)
    private int _encounterRun; // bumped every StartEncounter; stops stale AI loops

    // Goals tracker — GROUND TRUTH (2026-07-03, 04-01 capture): the real server DEFINES both objectives
    // INLINE in the launch details packet's ObjectiveData[], then ACTIVATES each by id (op45/sub1). It
    // NEVER uses op45/sub5 (ObjectiveAdd) — an activate for an id that isn't defined inline is dropped
    // (the goals panel never renders), which was our bug. These are the REAL captured ids + string ids
    // (12288/12642 with NameId/DescId from the Growler activity string family). Unknown string ids fall
    // back to "<OBJECTIVE n>" on the admin client — still proves the panel.
    // Knockout counter/limit — top-left combat HUD (op39/sub23 MiniGameKnockOut, Max=5 ground-truthed
    // from the 2014-04-01 burst idx 28043/28060/28071). This is the "knockouts remaining" star the user
    // wants top-left — it is NOT a Goals-window row. (Scales with party size on live; solo = 5.)
    private const int KnockoutLimit = 5;

    // THE Goals-window goal (video: the panel shows only this). id 12642 / NameId 104176 =
    // "Scare away the wolves!" (confirmed live 2026-07-03). The survival objective ("Don't get knocked
    // out 5 times!", NameId 2286) is NOT shown here — the knockout limit lives in the top-left counter.
    private const int GoalScareWolves = 12642;
    private const int GoalScareWolvesNameId = 104176;
    private const int GoalScareWolvesDescId = 104177;
    private const int WolfKillTarget = TotalSnarlers + 2; // pack budget + the Alpha's 2 escorts

    // PRIZES — the offer popup's reward list AND the victory loot-wheel slices (both render from the
    // details packet's PREVIEW reward bundle; see RewardEntry). GROUND TRUTH 2026-07-04: decoded verbatim
    // from the real 04-01 launch packet (idx 28053) — and that player was ALSO a ninja, so this IS the
    // correct job set for us (icons/names/ids cross-checked against ClientItemDefinitions.json).
    // Job dependence is server-side: live picks the set for the player's ACTIVE job and stamps
    // MiniGameInfo.ProfileType with the job CATEGORY (2 = combat jobs, Profiles.json Type). When more
    // jobs matter, select a different set per player.ActiveProfileId here.
    public const int CombatProfileType = 2;
    public static List<RewardEntry> NinjaPrizePreview() =>
    [
        new() { Hidden = true,  IconId = 2483, TintId = 234, NameId = 133217, ItemDefId = 76209 }, // Kusa Ninja Tabi Boots
        new() {                 IconId = 3717, TintId = 264, NameId = 131152, ItemDefId = 75408 }, // Ninja's Power Shard of Regeneration I
        new() {                 IconId = 3229, TintId = 247, NameId = 131975, ItemDefId = 75091 }, // Ninja's Training Sword of 1000 Storms
        new() {                 IconId = 1198, TintId = 0,   NameId = 131129, ItemDefId = 75385 }, // Ninja's Necklace of Vitality I
        new() { Hidden = true,  IconId = 973,  TintId = -1,  NameId = 6666,   ItemDefId = 10482 }, // Battle Item Mystery Pack
    ];
    // Real preview bundle values, IDA-verified 2026-07-04 (bundle U2 = Num Coins, U3 = Experience):
    // 10 coins, 0 XP. The encounter's XP (10) was granted by the GOAL's own reward bundle on live —
    // wire that up with the real XP/level system (backburner task, see STATUS.md).
    public const int PrizeCoins = 10;
    public const int PrizeXp = 0;

    // The goal, defined inline in the launch details packet (Status=1 -> "InProgress"). Only the
    // "Scare away the wolves!" objective is kept (the survival one was dropped per the user).
    private static IEnumerable<EncounterObjective> EncounterObjectives =>
    [
        new EncounterObjective
        {
            ObjectiveId = GoalScareWolves, NameId = GoalScareWolvesNameId, DescriptionId = GoalScareWolvesDescId,
            Status = 1, Count = 0, Total = WolfKillTarget, Unknown8 = 1,
        },
    ];

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
            foreach (var h in _hearts)
                h.Dispose();
            _hearts.Clear();
            _alpha?.Dispose();
            _alpha = null;
            _alphaFleeing = false;
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
                // The LAUNCH details packet (op41/sub114, flag=1). Type MUST be COMBAT (4): the client's
                // minigame status handler (minigamestatushandler.lua) only shows/populates the objective
                // pane (wndMinigameStatusObjPane @ top-right, bound to the BaseClient.MiniGameGoals data
                // source) when currentMiniGameType == COMBAT. Type 0 = TUTORIAL rendered the HUD frame but
                // type-gated the goals pane OFF (RE'd 2026-07-03 via IDA enum MINI_GAME_TYPE). Objectives
                // are DEFINED inline (Status=1 -> InProgress, real Totals baked) so the state carries them.
                EncounterDetailsResponsePacket MakeLaunch() => new()
                {
                    Unknown = EncounterId,          // live header ints = [encounterId][instanceId]
                    Unknown2 = EncounterInstanceId,
                    NameId = 93276,                 // "Frostfang Growler!" (ClientActivityDefinitions Id 174)
                    DescriptionId = 104171,
                    Difficulty = 1,
                    IconId = 1345,
                    MiniGameType = CombatMiniGameType,   // 4 = COMBAT — the goals-pane gate
                    // ZoneContext deliberately left 0. The 2026-07-03 red-name arc ended here: 6 = ARENA
                    // sets BaseClient.m_bIsInArena, which the ADD-PC apply uses to bake disposition
                    // HOSTILE before its SetProfileId re-runs the name color resolver (sub_966460:
                    // NameColor==0 + disposition 0 -> RED). LIVE TEST: Alpha name STAYED blue with 6 —
                    // the AddNpc (case-2-adjacent) apply path evidently does NOT run that arena-
                    // disposition branch (it was RE'd from the AddPc path). Reverted to 0 until the real
                    // AddNpc APPLY is traced (its unserialize is only reachable via a thunk @0x925EB0 —
                    // find its caller). Full notes: docs/STATUS.md "RED BOSS NAME".
                    Launch = true,
                    Objectives = [.. EncounterObjectives],
                    // Prizes + job category + activity id — all ground-truthed against the real 04-01
                    // launch packet (2026-07-04 decode; see NinjaPrizePreview). The preview bundle in the
                    // LAUNCH copy is what the victory score screen's loot wheel spins from.
                    PreviewRewards = NinjaPrizePreview(),
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

                // ★ EXACT REAL-SERVER ENTRY SEQUENCE (2014-04-01 capture idx 28053-28224). The critical
                // structure: the real server sends the LAUNCH details TWICE with a PlayerEnter BETWEEN them.
                //   1) LAUNCH  -> creates the MiniGameState with the goals (sub_9BB2D0) + fires
                //      "HandlerMiniGameStatus:...:Populate" — but the status UI handler isn't live yet.
                //   2) PlayerEnter (op41/sub2) -> sub_9B9500 -> sub_9B6AA0 "HUD:showMinigame": brings the
                //      minigame HUD / status handler up.
                //   3) LAUNCH again -> re-fires "HandlerMiniGameStatus:...:Populate", and NOW the status
                //      handler is live to catch it and populate the goals pane. (Our single-launch attempts
                //      fired Populate into the void before the handler existed -> empty pane.)
                //   4) op62 combat ruleset, then a second PlayerEnter with the real player guid.
                var launchBytes = MakeLaunch().Serialize();
                _logger.LogInformation("Frostfang arena: LAUNCH details ({len}B) = {hex}",
                    launchBytes.Length, Convert.ToHexString(launchBytes));

                // The ONLY visible goal (video: the Goals panel shows just "Scare away the wolves!").
                // id 12642 / NameId 104176 = "Scare away the wolves!" (confirmed live 2026-07-03). The
                // survival ("Don't get knocked out 5 times!") row + the knockout counter were dropped per
                // the user — the video's Goals panel never shows them.
                UiObjectiveAddPacket ScareWolvesRow() => new()
                {
                    ObjectiveId = GoalScareWolves,
                    NameId = GoalScareWolvesNameId,
                };

                // ★ ORDER IS LOAD-BEARING (RE'd 2026-07-03 evening from the decompiled UI Lua). The
                // top-right Goals window is Main.wndObjectives, fed by op47 into "BaseClient.ObjectiveHelper".
                // PlayerEnter -> "HUD:showMinigame" -> HUD:ShowMiniGameNormal -> ObjectiveWindow:
                // ObjectiveListPopulate(), which reads the CURRENT DS rows and HIDES the window if the DS
                // is empty (`if numRows<=0 then self:Hide(); return`). Script events (the live row-changed
                // callback) are only enabled once the window is shown. So the op47 row MUST already be in
                // the DS before the PlayerEnter that triggers showMinigame — otherwise ObjectiveListPopulate
                // hides the window and the later row lands unseen. This matches the real capture: op47
                // TaskAdd (idx 28049) precedes the first PlayerEnter (28058) and repeats after the second launch.
                // KNOCKOUT COUNTER (top-left HUD, NOT the Goals window). op39/sub23 MiniGameKnockOut,
                // Current=0/Max=5 -> the combat HUD's "knockouts remaining" star shows 5. The real burst
                // sent it 3x interleaved with the launches (idx 28043/28060/28071); showMinigame's
                // GroupHandler:SetKO reads this, so it must be stored before the PlayerEnter. This is the
                // knockout LIMIT/counter the user wants top-left — separate from the Goals objective rows.
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit)); // 28043
                player.SendTunneled(new ObjectiveActivatePacket    // op45 activate (announce)
                {
                    ObjectiveId = GoalScareWolves,
                    Total = WolfKillTarget,
                });
                // The goal row into the Goals window BEFORE any PlayerEnter (so ObjectiveListPopulate
                // sees it and shows the window instead of hiding it).
                player.SendTunneled(ScareWolvesRow());   // 28049 — op47 "Scare away the wolves!"
                player.SendTunneled(MakeLaunch());       // 28053 — create state + goals
                player.SendTunneled(MakeEnter(0));       // 28058 — PlayerEnter: showMinigame -> ObjectiveListPopulate (now sees the row)
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit)); // 28060
                player.SendTunneled(MakeLaunch());       // 28065 — LAUNCH again: re-fire Populate (handler now live)
                player.SendTunneled(ScareWolvesRow());   // 28069 — op47 row again (real server repeats here)
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit)); // 28071
                player.SendTunneled(PacketEncounterDataCommon.CreateCombatRules()); // 28122 — op62
                player.SendTunneled(MakeEnter(player.Guid)); // 28224 — PlayerEnter (player guid)

                // Our world-combat toggles + the running encounter state (kept from our combat wiring).
                player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = true });
                player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = true });
                player.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = EncounterId,
                    InstanceId = EncounterInstanceId,
                    State = 6,
                });

                _logger.LogInformation("Frostfang arena: real entry sequence delivered (goals row before PlayerEnter, post-load, run {run}).", run);
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

    /// <summary>Drop a heart pickup (model 469, animated health pickup) at a fallen wolf's spot.</summary>
    private void SpawnHeart(Player player, Vector4 pos)
    {
        if (!TryCreateNpc(out var heart))
            return;

        heart.ModelId = HeartModelId;
        heart.Name = null;            // no nameplate
        heart.NameId = 0;
        heart.Disposition = 1;        // neutral (not a combat target)
        heart.Scale = 1f;
        heart.IsInteractable = false; // auto-collected by walking over it, no click prompt
        heart.InteractRange = 0;
        heart.Visible = true;
        heart.MaxHealth = 0;          // not damageable
        heart.ShowHealthBar = false;
        heart.WalkAnimId = -1;
        heart.RunAnimId = -1;
        heart.StandAnimId = -1;
        heart.RiderGuid = ulong.MaxValue;
        heart.UpdatePosition(pos, Quaternion.Identity);

        player.OnAddVisibleNpcs(heart);
        heart.OnAddVisiblePlayers(player);
        SendNpcRelevance(player, heart);

        lock (_stateLock)
            _hearts.Add(heart);
    }

    /// <summary>Walk-over heart collection: within range → +125 heal number + green FX, remove the heart.</summary>
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
            // Green "+125" heal number over the player + a heal sparkle, then remove the heart.
            player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = player.Guid,
                CompositeEffectId = HeartHealFxId,
                Position = player.Position,
            });
            player.SendTunneled(new PlayerUpdatePacketHitPointModification
            {
                Guid = player.Guid,   // heal is self-sourced
                Guid2 = player.Guid,  // ...on the player
                Unknown = true,
                Unknown2 = 2500,      // player max HP (real pool is a TODO)
                Unknown3 = 2500,      // current after (cosmetic until HP is tracked)
                Unknown4 = HeartHeal, // +125 delta -> the green heal number
            });
            h.Dispose();
        }
    }

    private Npc? CreateWolf(Player player, string? name, int health, float scale, Vector4 pos,
        bool showHealthBar, string? textureAlias = null, int nameColor = 0, float nameScale = 0f)
    {
        if (!TryCreateNpc(out var npc))
            return null;

       // npc.NameColor = nameColor;  // static color DISABLED (test: disposition recolor may only run
                                      // when no NameColor is set — user theory 2026-07-03; the client's
                                      // color resolver sub_966460 CONFIRMS: NameColor==0 + hostile
                                      // disposition -> RED 0xFFFF0000 via SetOverHeadTextElementColor)
        npc.NameScale = nameScale; // >1 = bigger name letters (the video's boss); 0 = client default
        // HideNamePlate stays default FALSE — live-proven (builds 12 vs 13): sending true HIDES the plate.

        // ★ RED NAME (user-found 2026-07-03): a NON-DEFAULT ActiveProfile makes the client's AddNpc
        // apply actually call SetProfileId -> re-runs the color resolver AFTER disposition is set ->
        // hostile (disp 0) + NameColor unset = RED overhead name. Default(0) short-circuits the guard
        // and the name stays ctor-baked ally blue.
        npc.ActiveProfile = 1;

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

        // Make the client treat it as a HOSTILE (op35/sub28 — the AddNpc Disposition int is ignored;
        // ctor default = ally). Named hostiles (the Alpha) get the RED name via the color resolver
        // (sub_966460: NameColor==0 + disposition 0 -> Display.NameColorHostileNpc 0xFFFF0000).
        // Mark it hostile client-side (op35/sub28 UpdateDisposition — real packet, case 28; the AddNpc
        // Disposition int is ignored by the client and the character ctor defaults to ALLY). NOTE this
        // does NOT recolor the nameplate (color resolves once at spawn) — it's here for the client's
        // other disposition-driven logic. Red-name work is PAUSED; see docs/STATUS.md "RED BOSS NAME".
        player.SendTunneled(new PlayerUpdatePacketUpdateDisposition
        {
            Guid = npc.Guid,
            Disposition = 0,
        });

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

                    // Heart pickups: walk-over collection heals +125 (video).
                    CollectHearts(player);

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

                        // FLEEING ALPHA (video: at very low HP he turns and RUNS away, then the goal
                        // completes). Sprint directly away from the player and face that way; no biting.
                        if (_alphaFleeing && ReferenceEquals(wolf, _alpha))
                        {
                            var awayH = new Vector2(here.X - target.X, here.Z - target.Z);
                            var awayLen = awayH.Length();
                            var awayDir = awayLen > 0.01f ? awayH / awayLen : new Vector2(0f, 1f);
                            var fleeStep = MoveSpeed * 2.4f * (TickMs / 1000f); // sprint
                            var fleePos = new Vector4(
                                here.X + awayDir.X * fleeStep, here.Y, here.Z + awayDir.Y * fleeStep, wolf.Position.W);
                            var fleeRot = new Quaternion(awayDir.X, 0f, awayDir.Y, 0f);
                            wolf.UpdatePosition(fleePos, fleeRot);
                            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = wolf.Guid, Position = fleePos, Rotation = fleeRot, State = 0, Unknown = 0,
                            });
                            continue;
                        }

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

                                // The REAL per-hit packet (op32/sub7): wolf = attacker (plays the bite
                                // swing), player = target (incoming number/recoil). Mapping proven from
                                // the client struct reader — the earlier target-first guess made the
                                // PLAYER swing and go on melee cooldown from every bite.
                                player.SendTunneled(new CombatPacketAttackProcessed
                                {
                                    AttackerGuid = wolf.Guid,
                                    TargetGuid = player.Guid,
                                    Damage = BiteDamage,
                                    MaxHealth = 2500,        // player max HP (real player pool is a TODO)
                                    CompositeEffectId = 7,   // PFX_Hit_Flash on the target
                                    CurrentHealth = 2500,    // player pool TODO; client tracks HP - Damage itself
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

        // Death animation (wolf.adr com_death_01 = group 1151): the wolf falls over, holds a beat,
        // THEN the poof + despawn. Before this it vanished into the smoke on the kill tick.
        killer.SendTunneled(new PlayerUpdatePacketSetAnimation
        {
            Guid = npc.Guid,
            AnimationId = WolfDeathAnimId,
        });
        var deathPos = npc.Position;
        var dying = npc;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(WolfDeathHoldMs);
                killer.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                {
                    Guid = dying.Guid,
                    CompositeEffectId = SmokePoof,
                    Position = deathPos,
                });
                dying.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena: wolf death-anim despawn failed.");
            }
        });

        // Video: scared-off wolves sometimes leave a heart (+125) on the ground where they fell.
        if (killedCount > 0 && _rng.Next(100) < HeartDropPercent)
            SpawnHeart(killer, deathPos);

        // Tick the goal progress as wolves are scared off.
        if (killedCount > 0)
            killer.SendTunneled(new ObjectiveUpdatePacket { ObjectiveId = GoalScareWolves, Count = killedCount });

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
                // A/B TEST (2026-07-03 night): NameScale REVERTED 1.5 -> 0. The overhead plate (name+bar)
                // vanished starting with the deploy that introduced NameScale=1.5 and stayed gone after
                // IsBoss was removed — NameScale is the only remaining delta vs the last build where the
                // plate rendered. Client RE says m_fNameScale only feeds Display_EliteNameScale (size), so
                // if the plate returns at 0 the interaction is deeper (elite-path w/o elite bit 15?) —
                // either way this isolates it. Elite bit15 = the REAL "big plate" lever (virtual SetElite,
                // ProxiedCharacter vtbl slot 4) — its driving packet still untraced.
                _alpha = CreateWolf(killer, "Frostfang Alpha", AlphaHealth, AlphaScale, pos, showHealthBar: true,
                    nameColor: unchecked((int)0xFFFF0000), nameScale: 0f);
                if (_alpha is not null)
                {
                    _engageAngle[_alpha.Guid] = angle;

                    // NOTE: do NOT send UpdateCharacterState IsBoss here. LIVE TEST 2026-07-03: flagging
                    // the actor IsBoss (op35/sub20, bit 15) makes the client SUPPRESS the overhead
                    // nameplate entirely (name + overhead HP bar both vanished; only the top-center boss
                    // bar remained). The video wants the overhead RED name + green bar VISIBLE, so the
                    // Alpha must stay un-flagged. (Packet class kept — it's the vehicle for stun/frozen
                    // combat states later.)

                    // Boss plate: top-center boss health-bar data source (op32/sub9 -> AddBoss, RE'd).
                    killer.SendTunneled(new CombatPacketEnableBossDisplay { Guid = _alpha.Guid, Enable = true });
                }

                // Two anonymous escorts flank the boss (video shows adds fighting alongside the Alpha).
                _spawnedSnarlers = TotalSnarlers - 2;
                SpawnSnarler(killer);
                SpawnSnarler(killer);
            }

            // The Alpha's health bar drains as the player fights him; at AlphaFleeThreshold he FLEES
            // (OnNpcDamaged) rather than dying — matching the video. No goal row change here.
        }
        else if (victory)
        {
            // Fallback only: the Alpha normally FLEES (never dies), but if a hit somehow kills him,
            // still finish the encounter cleanly.
            WinEncounter(killer);
        }
    }

    /// <summary>The player has beaten the encounter (Alpha fled or, as a fallback, died): complete the
    /// goal, arm the loot wheel + score rows, and send everyone home after a beat.</summary>
    private void WinEncounter(Player player)
    {
        player.SendTunneled(new UiObjectiveCompletePacket { ObjectiveId = GoalScareWolves });

        // ★ LOOT WHEEL (real end flow, 04-01 capture + client RE — see MiniGameLootWheelPackets).
        // Pick the prize SERVER-SIDE (the spin is theater): uniform over the 5 preview items + the
        // coins slice. These two packets must go out while the MiniGameState is still alive (the
        // landing apply matches the prize NameId against the state's stored preview rows); the Lua
        // keeps the resolved index after our later state remove, so the player can spin any time.
        var prizes = NinjaPrizePreview();
        var slice = _rng.Next(prizes.Count + 1); // 0..4 = items, 5 = coins
        var wheel = new MiniGameLootWheelSetItemToLandOnPacket();
        if (slice < prizes.Count)
        {
            player.PendingWheelPrize = prizes[slice];
            player.PendingWheelCoins = 0;
            wheel.Entries.Add(prizes[slice]);
            _logger.LogInformation("Frostfang arena: wheel will land on {item} (def {def}).",
                prizes[slice].NameId, prizes[slice].ItemDefId);
        }
        else
        {
            player.PendingWheelPrize = null;
            player.PendingWheelCoins = PrizeCoins;
            wheel.Coins = PrizeCoins; // no entry + coins>0 -> the client resolves the COINS slice
            _logger.LogInformation("Frostfang arena: wheel will land on the COINS slice ({coins}).", PrizeCoins);
        }

        // Score rows (op39/sub47, live points model: 300/enemy, 5000 per knockout remaining).
        var enemies = _killedSnarlers;
        var knockoutsLeft = KnockoutLimit; // player HP pool is cosmetic for now -> never knocked out
        var score = new MiniGameGameEndScorePacket();
        score.Rows.Add(new MiniGameScoreRow { Name = "scoreEnemiesDefeated", Order = 0, Value = enemies, Points = enemies * 300 });
        score.Rows.Add(new MiniGameScoreRow { Name = "scorePlayerKnockouts", Order = 3, Value = knockoutsLeft, Max = KnockoutLimit, Points = knockoutsLeft * 5000 });
        score.Rows.Add(new MiniGameScoreRow { Name = "scoreTotalScore", Order = 4, Points = enemies * 300 + knockoutsLeft * 5000 });

        player.SendTunneled(wheel);
        player.SendTunneled(score);

        _logger.LogInformation("Frostfang arena: encounter won -> returning {name} home in 6s.", player.Name);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(6000);
                ReturnHome(player);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena victory return failed.");
            }
        });
    }

    /// <summary>Per-hit hook (IZone.OnNpcDamaged): once a hit drops the Alpha to the flee threshold he
    /// turns and runs (the AI loop sprints him outward), his boss plate clears, the goal completes, and
    /// after he reaches the fog he despawns and the player heads home — exactly like the reference video
    /// where the Alpha never actually dies.</summary>
    public override void OnNpcDamaged(Player player, Npc npc)
    {
        if (_alphaFleeing || !ReferenceEquals(npc, _alpha) || npc.Health > AlphaFleeThreshold)
            return;

        _alphaFleeing = true;
        _logger.LogInformation("Frostfang arena: Alpha at {hp} HP (<= {t}) -> FLEES; goal complete.",
            npc.Health, AlphaFleeThreshold);

        // Clear the boss health plate and complete the goal the instant he breaks.
        player.SendTunneled(new CombatPacketEnableBossDisplay { Guid = npc.Guid, Enable = false });

        var fleeing = _alpha;
        WinEncounter(player); // completes the goal + schedules the return home

        // Let him sprint to the fog (the AI loop drives the run), then remove him.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3500);
                if (fleeing is not null && ReferenceEquals(fleeing, _alpha))
                {
                    lock (_stateLock)
                    {
                        _alpha = null;
                        _engageAngle.Remove(fleeing.Guid);
                    }
                    fleeing.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena: Alpha flee-despawn failed.");
            }
        });
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
    public void EndEncounterForPlayer(Player player) => EndEncounterForPlayer(player, won: false);

    public void EndEncounterForPlayer(Player player, bool won)
    {
        // On a WIN, mark the run won (op39/sub18 GameOver, Won=true -> MiniGameState byte+99) IMMEDIATELY
        // before the state remove, so the end card the teardown triggers reads Won=true (win presentation,
        // not "TRY AGAIN!"). Sending it earlier (at the victory moment) made the card flash for ~0.5s —
        // live test 2026-07-03. A mid-run bail (!home) keeps won=false. NOTE the REAL server didn't use
        // GameOver at all: its end flow is MiniGameLootWheelSetItemToLandOn + MiniGameGameEndScore (named
        // score rows: scoreEnemiesDefeated, scorePlayerKnockouts) -> client shows scores + reward wheel ->
        // C2S LootWheelOnRotationStopped (~20s later) -> exit. (04-01 capture idx 37834/37838/38115.)
        // That full flow is the proper future implementation; GameOver-before-remove is the interim fix.
        if (won)
            player.SendTunneled(new MiniGameGameOverPacket(won: true));
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });
        player.SendTunneled(new UiObjectiveClearPacket()); // empty + hide the Goals window (op47/sub5)

        _logger.LogInformation("Frostfang arena: encounter released for {name} (state remove + default rules).",
            player.Name);
    }

    private void ReturnHome(Player player)
    {
        if (player.Zone != this)
            return; // already left

        EndEncounterForPlayer(player, won: true); // ReturnHome only runs on victory

        var home = _zoneManager.StartingZone;

        player.TeleportToZone(home, home.SpawnPosition, home.SpawnRotation, sky: null, geometryId: 0);
    }

    #endregion
}
