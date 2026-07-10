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
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

// INSTANCE (Frostfang Fury): the REAL arena zone for the "Frostfang Growler!" combat encounter, done the
// proper way — a genuine server-side zone the player is TeleportToZone'd into, so tiles/visibility/NPC
// delivery all run through the normal engine pipeline.
//
// The world + coords come from the CLIENT'S OWN DATA (2026-07-01, see docs/STATUS.md):
//   world  = sg_random_encounter_clearing (green grass clearing; matches the Sunrise reference video)
//   center = (136, y≈0.5, 165) radius 100, from sg_random_encounter_clearingAreas.xml ("Bed" AreaDefinition)
//
// ★ ENCOUNTER SPEC — GROUND TRUTH (2026-07-05): the ENTIRE live encounter decoded from the 2014-04-01
// capture (idx 27735-38200; scripts in the session scratchpad, analysis in docs/STATUS.md):
//   * ONE "roamer" wolf pre-spawned before the player even loads (wolf_evil, tint evil_purple,
//     AddNpc Speed=3.0) — it wanders at walk speed and never charges; the video shows it idling
//     around as the player loads in.
//   * 4 WAVES of pack wolves: 6, 9, 10, 10 — each wave 2× wolf_evil (tex 'evil'/'evil_black') and
//     the rest wolf (tex 'snow'/'base_metal'). A wave spawns ~1s after the previous is (nearly)
//     cleared (live triggered at ≤1 alive). Total pack = 1+6+9+10+10 = 36. Max ~12 alive at once.
//   * spawn points are FIXED locations ringing the whole arena (bake of every live spawn below) —
//     wolves appear far away, idle ~2.2s, then get ExpectedSpeed 3.0 + 6.0 + CharacterState 0x8001
//     and CHARGE the player at 6.0. (No proximity aggro — the "roaming pack" look in the video is
//     just far-away wolves running in.)
//   * pack wolves: NameId 104067/116023 ("Frostfang Snarler"), HideNamePlate=1, healthBar=0 —
//     NO overhead plates (video-confirmed); they show as red dots on the minimap via the combat
//     notification. HP 760 (one basic hit). Bites are TINY on the live wire (3-5 vs a 7828-HP ninja).
//   * the ALPHA spawns WITH wave 4: model 176 + tex 'snow'/'snow_blue', scale 1.7, NameId 423045,
//     plate SHOWN + healthBar=1 (the video's floating red name + bar — live sends NO op32/sub9 boss
//     display for him). HP 3800. He charges and bites like the pack.
//   * the Alpha has NO flee threshold: at 0 HP he is "defeated" and the flee IS his death
//     presentation — RemovePlayerGracefully(Animate, Delay=10000) + ExpectedSpeed 6.0: the client
//     runs him off for 10s (video 1:25-1:35). Pack wolves use the same packet with Delay=2000
//     (death clip + 5017 poof).
//   * win moment (all at once): health heart (736) + coin pile (841, Knockback pop + fx, removed
//     ~instantly) at the Alpha's spot; op45/sub3 ObjectiveComplete (green-check "Goal Complete!",
//     id 12642) + op47/sub3 row complete; reward banners; the EXIT DOOR (846 sg_exit_door_01,
//     NameId 4826, scale 1.2, cursor 17, minimap badge ImageId 186) spawns at (145, 0, 173) —
//     NO auto-kick; the player leaves by clicking the door.
//   * the goal 12642 "Scare away the wolves!" is Total=1 with NO per-kill ticks on live.
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

    // ── Wolf identities: GROUND TRUTH, every field verbatim from the live AddNpc packets ────────────
    private const int SnowWolfModelId = 176;   // wolf.adr        tex 'snow' / tint 'base_metal'
    private const int EvilWolfModelId = 177;   // wolf_evil.adr   tex 'evil' / tint 'evil_black'
    private const int SnarlerSnowNameId = 104067; // "Frostfang Snarler" family string
    private const int SnarlerEvilNameId = 116023;
    private const int RoamerNameId = 115837;   // the pre-spawned evil_purple roamer's live NameId
    private const int AlphaNameId = 423045;    // "Frostfang Alpha"
    private const int PackActiveProfile = 151; // live ActiveProfile on every pack wolf (non-zero also
                                               // keeps the red hostile name resolve — see Npc.Disposition)
    private const int AlphaActiveProfile = 152;

    private const int WolfHealth = 760;        // live: player -2739 basic one-shots; video archer ~3 arrows
    private const int AlphaHealth = 3800;      // live max in the killing HitPointModification
    private const float AlphaScale = 1.7f;     // live AddNpc scale (was 1.6 guessed from video)

    private const float RoamSpeed = 3f;        // live ExpectedSpeed while ambling
    private const float ChaseSpeed = 6f;       // live ExpectedSpeed while charging (all wolves)
    // The roamer's fight-kickoff HOWL (live idx 28467-28469, both on the roamer, one tick before wave 1's
    // AddNpc burst): a rear-up cast pose + a "commanding shout" composite over its head. THIS is what
    // summons the pack — no wolf spawns until the howl fires. Trigger is PROXIMITY (the live capture shows
    // the player walking straight up to the roamer, 52u -> ~4u, and it howls at close range — not a timer).
    private const int RoamerHowlAnimId = 1111;   // AnimationGroup com_cast_01 (SetAnimation op35/8)
    private const int RoamerHowlFxId = 15226;    // PFX_moire-circles_multi_head_commanding-shout-level-1_loop
    private const int RoamerHowlHoldMs = 1500;   // plant + hold the howl pose (anim + fx) before charging
    private const float RoamerAggroRange = 6f;   // player-approach distance that fires the howl
    private const int AggroDelayMs = 2200;     // live: ES 3.0 + ES 6.0 + state 0x8001 land ~2.2s after AddNpc
    private const int SpawnPoofFxId = 46;      // AddNpc.CompositeEffectId on every live WAVE wolf (not the roamer)
    private const int DeathPoofFxId = 5017;    // the graceful-remove composite effect on every dying wolf
    private const int AlphaFleeMs = 10000;     // graceful-remove Delay on the defeated Alpha (he runs off)
    private const int WolfDeathHoldMs = 2000;  // graceful-remove Delay on pack wolves (death clip plays)

    // CharacterState 0x8001 = live "charging/in-combat" state (bit0 baseline + bit15). Every live wolf
    // toggles 1 -> 0x8001 at its charge moment; the PLAYER toggles the same pair with IsFighting.
    // NOTE our 2026-07-03 test showed bit15 AT SPAWN suppresses an overhead plate, so the Alpha
    // (plated) does NOT get this — video-first. Pack wolves have no plates to lose.
    private const int CharState_Baseline = 0x1;
    private const int CharState_Charging = 0x8001;

    // Waves (live): 6, 9, 10, 10 — two evil wolves in each, Alpha alongside the last wave.
    private static readonly int[] WaveSizes = [6, 9, 10, 10];
    private const int EvilPerWave = 2;
    private const int NextWaveDelayMs = 1000;  // live gap: last kill -> next wave ≈ 0.6-1.3s

    // Every live wolf spawn point (x, y, z), baked verbatim from the 04-01 AddNpc positions — fixed
    // locations ringing the arena (center ~(136,165)); the live server drew from these repeatedly.
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

    // Live one-off actor positions (roamer / Alpha / exit door), verbatim from the capture.
    private static readonly Vector4 RoamerSpawn = new(129.33f, GroundY, 171.81f, 1f);
    private static readonly Vector4 AlphaSpawn = new(154.32f, 1.96f, 209.35f, 1f);
    private static readonly Vector4 DoorSpawn = new(145.0f, 0.0f, 173.35f, 1f);

    // ── Exit door (846 = sg_exit_door_01.adr) — live fields from AddNpc idx 37181 + companions ──────
    private const int DoorModelId = 846;
    private const int DoorNameId = 4826;
    private const float DoorScale = 1.2f;
    private const int DoorInteractRange = 125;
    private const int DoorActiveProfile = 28;
    private const int DoorCursorId = 17;         // live NpcRelevance entry for the door
    private const int DoorMinimapImageId = 186;  // live AddNotifications badge (minimap exit icon)
    private const int DoorBadgeType = 7;
    private const int DoorBadgeUnknown3 = 102;

    // Coin-pile pop at the win (841 = loot_coins_01.adr): knocked outward and removed ~instantly.
    private const int CoinsModelId = 841;
    private const int CoinsNameId = 139649;
    private const int CoinsPopFxId = 5192;       // PlayCompositeEffect on the coins at the win moment
    private const float CoinsKnockMagnitude = 0.0712f; // live Knockback magnitude

    // Chase-and-bite AI. Wolves surround the player (each owns a slot on a ring) instead of stacking.
    private const int TickMs = 300;
    private const float YSpeed = 12f;          // vertical convergence to the player's REAL ground height
    private const float BiteRange = 2.6f;
    private const float EngageRadius = 1.9f;   // ring the wolves try to stand on around the player
    // Live bite pacing is SPARSE: ~30 bites over ~80s across up to 12 wolves (~1 per 2.7s pack-wide).
    private const int BiteCooldownMs = 4000;   // per-wolf
    private const int BiteGlobalGapMs = 1200;  // pack-wide minimum spacing
    // Live wire damage was 3-5 (rare 9/12 crits, crit fx 5622) — vs a leveled 7828-HP ninja. Our
    // player pool is still the cosmetic 2500, so keep the bite FELT like the video's L1 archer
    // (~4-6% per bite) and rescale when the real HP pool lands (STATUS.md task).
    private const int BiteDamage = 150;
    private const int BiteCritDamage = 300;
    private const int BiteCritPercent = 10;
    private const int BiteFxId = 5409;         // live hit composite effect on every normal bite
    private const int BiteCritFxId = 5622;     // live crit hit effect

    // Defeated-Alpha flee run (video 1:25-1:35: he turns and sprints off into the fog until he's gone).
    private const float FleeSpeed = 9f;        // a touch faster than the chase so he clearly gets away
    private const float FleeDespawnRadius = 90f; // ~arena edge from center (136,165), r100 playable

    /// <summary>Optional spawn override pinned live via the "!arena set" chat command (fine-tuning).</summary>
    public static Vector4? SpawnOverride;

    // Client movement gate (OnPlayerUpdatePosition, RE'd): MovementType must be 1 (CONTROLLER) or
    // 2 (PHYSICS), and the actor's rider must be the invalid-guid sentinel, else op125 updates are
    // dropped. Live: every walking NPC is type 2 (PHYSICS) — that path auto-plays locomotion.
    private const int WolfMovementTypePhysics = 2;

    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly Random _rng = new();

    // Heart pickups (video: +125 green heal on walk-over; model 736 = powerup_health_buff.adr, the
    // real drop — one is GUARANTEED at the Alpha's defeat spot on live; mid-fight drops are random).
    // (Live also dropped one 746 powerup_damage_buff mid-fight; damage buffs are a later task.)
    private const int HeartModelId = 736;
    private const int HeartHeal = 125;            // the green "+125" the video shows
    private const float HeartPickupRange = 2.6f;  // walk-over radius
    private const int HeartDropPercent = 12;      // random mid-fight drop chance per kill
    // Heart pickup FX: the live heart is removed gracefully with composite effect 15032 (the pickup
    // sparkle) — params verbatim from the capture remove (Animate=0, Delay=0, EffectDelay=5000).
    private const int HeartPickupFxId = 15032;
    // ★ WHAT THE HEART ACTUALLY IS (SOLVED 2026-07-05, wiki + 04-01 capture + video math). The health
    // powerup (model 736 powerup_health_buff — the ONLY combat "heart"; the sg_icon_* pickups are
    // Demolition Derby, unrelated) does TWO things on pickup:
    //   (1) a FLAT +125 heal — the wiki's "Small Heart: heals a low amount of your own health" (the
    //       green number). Video proof: archer at 417/500 -> +125 -> 542, seen as 542/665.
    //   (2) a TEMPORARY +33% MAX-HP BUFF for ~15s: MaxHealth ×1.33 (archer 500->665, ninja 7828->10411,
    //       both exactly ×1.33), healed to the new full, reverted ~15s later.
    // It is NOT heal-over-time — both parts land instantly; only the buff (and its FX) linger 15s. The
    // ninja showed no +125 float only because he grabbed it at full HP (flat heal on a full bar is
    // invisible; his visible gain was the buff fill). We do the flat +125 + the 15s looping shower now;
    // the real ×1.33 max-HP buff + revert needs the player HP pool (STATUS.md task).
    //
    // THE HEALING STATUS EFFECT (GROUND TRUTH, 04-01 idx 37215-37223): composite 15921 =
    // PFX_magic-heal_red_head_shower_lg_loop_raised (the LOOPING over-head heal shower + trail) is
    // attached to the player via an effect TAG (op35/sub41), held ~15s, then stopped (op35/sub42) —
    // NOT the one-shot 16324 blip we used before. The status-effect ICON under the portrait is driven
    // by the effect-tag entries (op38/sub16, 3 per pickup, server-defined effect ids 61401-61403);
    // that 97-byte format is complex/server-authored (embeds a float + source guid + effect refs) and
    // is a TODO — the looping composite below is the visible above-head heart/trail.
    private const int HealShowerFxId = 15921;  // looping over-head heal shower + trail
    private const int HealShowerMs = 15000;    // live buff duration (14.88s measured) — the ~15s shower
    private int _healTagCounter = 300;         // unique effect-tag ids for concurrent heart pickups
    private readonly List<Npc> _hearts = [];

    private sealed class WolfState
    {
        public bool Charging;
        public long ChargeAtTicks;   // Environment.TickCount64 when the charge kicks in
        public long NextBiteTicks;
        public float SlotAngle;
        // Roamer wander state
        public bool IsRoamer;
        public bool Howled;          // roamer has howled; standing in the pose until ChargeAtTicks, then charges
        public Vector2? WanderTarget;
        public long WanderPauseUntil;
    }

    private readonly object _stateLock = new();
    private readonly List<Npc> _wolves = [];
    private readonly Dictionary<ulong, WolfState> _wolfStates = [];
    private Npc? _alpha;
    // The DEFEATED Alpha while he RUNS AWAY (video: he never dies on screen — at 0 HP he turns and flees
    // to the fog). Kept OUT of _wolves so the normal chase/straggler-cleanup ignore him; the AI loop
    // drives his flee run and despawns him at the timeout / arena edge.
    private Npc? _fleeingAlpha;
    private long _alphaFleeUntilTicks;
    private Npc? _exitDoor;
    private int _waveIndex;        // next wave to spawn (0-based into WaveSizes)
    private bool _waveScheduled;
    private bool _roamerEngaged;   // set once the roamer has howled + spawned wave 1 (gates the kickoff)
    private int _killedSnarlers;
    private bool _won;
    private int _encounterRun; // bumped every StartEncounter; stops stale AI loops

    // Knockout counter/limit — top-left combat HUD (op39/sub23 MiniGameKnockOut, Max=5 ground-truthed
    // from the 2014-04-01 burst idx 28043/28060/28071). Solo = 5 on live.
    private const int KnockoutLimit = 5;

    // THE Goals-window goal (video: the panel shows only this). id 12642 / NameId 104176 =
    // "Scare away the wolves!" (confirmed live 2026-07-03). GROUND TRUTH (launch decode): the live
    // goal is Total=1 (a one-shot "deal with the pack" flag) — NO per-kill count ticks; it completes
    // in one go at the win via op45/sub3 + op47/sub3.
    private const int GoalScareWolves = 12642;
    private const int GoalScareWolvesNameId = 104176;
    private const int GoalScareWolvesDescId = 104177;

    // PRIZES — the offer popup's reward list AND the victory loot-wheel slices (both render from the
    // details packet's PREVIEW reward bundle; see RewardEntry). GROUND TRUTH 2026-07-04: decoded verbatim
    // from the real 04-01 launch packet (idx 28053) — and that player was ALSO a ninja, so this IS the
    // correct job set for us (icons/names/ids cross-checked against ClientItemDefinitions.json).
    // Job dependence is server-side: live picks the set for the player's ACTIVE job and stamps
    // MiniGameInfo.ProfileType with the job CATEGORY (2 = combat jobs, Profiles.json Type).
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
    // 10 coins, 0 XP. The encounter's XP was granted by the GOAL's own reward bundle on live — that's
    // EncounterXp below, granted for real in WinEncounter via the job XP/level system.
    public const int PrizeCoins = 10;
    public const int PrizeXp = 0;

    /// <summary>Job XP granted at the encounter win (live: 10, delivered by the goal's own reward
    /// bundle rather than the wheel preview — the popup preview correctly keeps showing 0 XP).</summary>
    public const int EncounterXp = 10;

    // ARCHER set — the REFERENCE VIDEO's ground truth (its player was an archer; popup frame at 0:09
    // shows exactly these three): Power Shard of Vitality I / Ring of Regeneration I / Bow of Volleys —
    // the mirror of the ninja structure (shard + training weapon + jewelry). The two HIDDEN slots
    // aren't visible in the video, so: the boots are INFERRED by tier index (ninja hidden boot = its
    // costume family's TIER 2, archer tier 2 = Hen Feather; 11 tiers in both families) and the
    // Mystery Pack slot is the shared consumable prize.
    public const int ArcherProfileId = 35; // Profiles.json "Archer" (Type 2 = combat category)
    public static List<RewardEntry> ArcherPrizePreview() =>
    [
        new() { Hidden = true,  IconId = 4939, TintId = 247, NameId = 132741, ItemDefId = 75733 }, // Hen Feather Archer Boots (inferred tier-2)
        new() {                 IconId = 3721, TintId = 230, NameId = 130968, ItemDefId = 75224 }, // Archer's Power Shard of Vitality I (video)
        new() {                 IconId = 547,  TintId = 0,   NameId = 130924, ItemDefId = 75180 }, // Archer's Ring of Regeneration I (video)
        new() {                 IconId = 3104, TintId = 228, NameId = 131884, ItemDefId = 75000 }, // Archer's Bow of Volleys (video)
        new() { Hidden = true,  IconId = 973,  TintId = -1,  NameId = 6666,   ItemDefId = 10482 }, // Battle Item Mystery Pack (shared slot)
    ];

    /// <summary>The reward set for the player's ACTIVE JOB — live behavior: the interact/launch packets
    /// carry no profile, the SERVER picks the set for the player's active job and stamps only the job
    /// CATEGORY (ProfileType=2, combat). Ninja = 04-01 capture ground truth; Archer = reference-video
    /// ground truth (3 visible) + tier-2 boot inference. Other combat jobs fall back to ninja until
    /// authored — the pattern per job: tier-2 costume boots (hidden) + Power Shard of X I + training
    /// weapon + jewelry of Y I + Mystery Pack, all in the job's 75xxx item block.
    /// The SAME set must be used at offer, launch, AND the win-time wheel packet — the client resolves
    /// the wheel's landing slice by matching NameId against the launch packet's stored preview rows.</summary>
    public static List<RewardEntry> GetPrizePreviewFor(Player player) =>
        player.ActiveProfileId == ArcherProfileId ? ArcherPrizePreview() : NinjaPrizePreview();

    // The goal, defined inline in the launch details packet. GROUND TRUTH: live 12642 ships
    // Status=1, Count=0, Total=1, Unknown8=0.
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
        Id = 174, // the Frostfang Growler activity id (traceability; the runtime zone Id is assigned by the manager)
        Name = "sg_random_encounter_clearing",
        TileSize = 64,
        // The clearing is tiny (playable r100 around (136,165)); pad the tile grid generously around it.
        StartLongitude = -2,
        EndLongitude = 8,
        StartLatitude = -2,
        EndLatitude = 8,
        Sky = null, // the GO! teleport sends sky_shrouded_gloam.xml (encounter mood); world default otherwise
        // Live player spawn (04-01 capture, first c2s position idx 28214): (130.11, 1.03, 120.04) — the
        // SOUTH edge of the clearing, ~52u from the roaming wolf up at z~172. The player walks that whole
        // stretch north before closing on the roamer (matches the video's long approach). Spawning at the
        // arena centre (136,165) put the player ~10u from the roamer — right on top of it. GroundY+2 keeps
        // the small settle-drop onto real ground.
        SpawnPosition = new Vector4(130.11f, GroundY + 2f, 120.04f, 1f),
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
            _wolfStates.Clear();
            foreach (var h in _hearts)
                h.Dispose();
            _hearts.Clear();
            _alpha?.Dispose();
            _alpha = null;
            _fleeingAlpha?.Dispose();
            _fleeingAlpha = null;
            _exitDoor?.Dispose();
            _exitDoor = null;
            _waveIndex = 0;
            _waveScheduled = false;
            _roamerEngaged = false;
            _killedSnarlers = 0;
            _won = false;
            _encounterRun++;

            // The lone ROAMER — live pre-spawns it before the player's launch burst; the video shows
            // it ambling around as the player loads in. It wanders at walk speed until attacked.
            SpawnRoamer(player);
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
                // Type MUST be COMBAT (4): the client's minigame status handler only shows/populates
                // the objective pane when currentMiniGameType == COMBAT (RE'd via IDA MINI_GAME_TYPE).
                EncounterDetailsResponsePacket MakeLaunch() => new()
                {
                    Unknown = EncounterId,          // live header ints = [encounterId][instanceId]
                    Unknown2 = EncounterInstanceId,
                    NameId = 93276,                 // "Frostfang Growler!" (ClientActivityDefinitions Id 174)
                    DescriptionId = 104171,
                    Difficulty = 1,
                    IconId = 1345,
                    MiniGameType = CombatMiniGameType,   // 4 = COMBAT — the goals-pane gate
                    // ZoneContext deliberately left 0 (the 2026-07-03 ARENA=6 red-name experiment
                    // failed; the AddNpc apply path doesn't run that arena-disposition branch).
                    Launch = true,
                    Objectives = [.. EncounterObjectives],
                    // Prizes + job category + activity id — all ground-truthed against the real 04-01
                    // launch packet (2026-07-04 decode; see NinjaPrizePreview). The preview bundle in the
                    // LAUNCH copy is what the victory score screen's loot wheel spins from. Set is picked
                    // for the player's ACTIVE JOB server-side (live behavior — no profile id on the wire).
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

                // ★ EXACT REAL-SERVER ENTRY SEQUENCE (2014-04-01 capture idx 28043-28224). The critical
                // structure: the real server sends the LAUNCH details TWICE with a PlayerEnter BETWEEN
                // them (the first Populate fires before the status handler exists; the PlayerEnter brings
                // the HUD up; the second launch re-fires Populate into the now-live handler). The op47
                // goal row must be in the DS BEFORE the PlayerEnter (ObjectiveListPopulate hides the
                // window if the DS is empty at show time). Full notes in docs/STATUS.md.
                UiObjectiveAddPacket ScareWolvesRow() => new()
                {
                    ObjectiveId = GoalScareWolves,
                    NameId = GoalScareWolvesNameId,
                };

                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit)); // 28043
                player.SendTunneled(new ObjectiveActivatePacket    // op45 activate (announce)
                {
                    ObjectiveId = GoalScareWolves,
                    Total = 1, // live goal total — a one-shot flag, not a wolf counter
                });
                player.SendTunneled(ScareWolvesRow());   // 28049 — op47 "Scare away the wolves!"
                player.SendTunneled(MakeLaunch());       // 28053 — create state + goals
                player.SendTunneled(MakeEnter(0));       // 28058 — PlayerEnter: showMinigame
                player.SendTunneled(new MiniGameKnockOutPacket(0, KnockoutLimit)); // 28060
                player.SendTunneled(MakeLaunch());       // 28065 — LAUNCH again: re-fire Populate
                player.SendTunneled(ScareWolvesRow());   // 28069 — op47 row again (real server repeats)
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

                // Wave 1 is NOT on a timer — it's gated on the roamer's howl, which fires when the player
                // walks up to the roamer (proximity, in the AI loop) or hits it (OnNpcDamaged). The lone
                // roamer ambles until then. Live order: howl packets -> wave-1 AddNpc, same tick.
                _logger.LogInformation("Frostfang arena: real entry sequence delivered (run {run}).", run);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena: delayed encounter-state delivery failed.");
            }
        });

        _logger.LogInformation("Frostfang arena: encounter start for {name} — roamer out, {waves} waves queued.",
            player.Name, WaveSizes.Length);

        StartWolfAi(player, _encounterRun);
    }

    // ── Spawning ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The pre-spawned lone roamer (live: wolf_evil + tint 'evil_purple', AddNpc Speed=3.0,
    /// NO spawn poof) — ambles around the mid-arena until attacked, then charges like the pack.</summary>
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
        };

        // The roamer walks from the start (live ES 3.0 shortly after spawn).
        player.SendTunneled(new PlayerUpdatePacketExpectedSpeed { Guid = roamer.Guid, ExpectedSpeed = RoamSpeed });
        SendWolfMinimapMarkers(player, [roamer.Guid]);
    }

    /// <summary>Spawn the next wave (caller holds _stateLock): live sizes 6/9/10/10, two wolf_evil in
    /// each, the Alpha alongside the final wave, all at the baked live spawn points.</summary>
    private void SpawnWave(Player player)
    {
        if (_waveIndex >= WaveSizes.Length)
            return;

        var size = WaveSizes[_waveIndex];
        var isLastWave = _waveIndex == WaveSizes.Length - 1;
        _waveIndex++;
        _waveScheduled = false;

        var newGuids = new List<ulong>(size + 1);

        // Pick distinct spawn points for this wave.
        var points = new List<Vector3>(SpawnPoints);
        for (var i = 0; i < size; i++)
        {
            var pt = points.Count > 0
                ? points[_rng.Next(points.Count)]
                : SpawnPoints[_rng.Next(SpawnPoints.Length)];
            points.Remove(pt);

            var evil = i < EvilPerWave; // live: exactly two wolf_evil per wave
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
            };
            newGuids.Add(wolf.Guid);
        }

        if (isLastWave)
        {
            // ★ THE ALPHA — spawns WITH the last wave on live (idx 35077, third actor of the burst):
            // big (1.7) snow_blue wolf, plate + health bar SHOWN (the video's floating red name + red
            // bar — the red comes from hostile disposition + the name resolver; live sends NO op32/sub9
            // boss display and no NameColor).
            _alpha = CreateWolf(player, SnowWolfModelId, AlphaNameId, "snow", "snow_blue",
                AlphaHealth, AlphaScale, AlphaSpawn, showPlate: true, AlphaActiveProfile, spawnFx: 0, speed: 0f);
            if (_alpha is not null)
            {
                // The Alpha rides the SAME AI list as the pack (the loop ticks _wolves) so he charges +
                // bites like the others — he was missing from _wolves before, so he just stood there.
                // OnNpcKilled special-cases him (defeat -> flee -> win) via the _alpha reference.
                _wolves.Add(_alpha);
                _wolfStates[_alpha.Guid] = new WolfState
                {
                    ChargeAtTicks = Environment.TickCount64 + AggroDelayMs,
                    SlotAngle = (float)(_rng.NextDouble() * Math.Tau),
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

    /// <summary>Live: one op35/sub10 AddNotifications per wave — a short "combat" entry per wolf,
    /// which is what paints the red enemy dots on the minimap.</summary>
    private static void SendWolfMinimapMarkers(Player player, IReadOnlyList<ulong> guids)
    {
        if (guids.Count == 0)
            return;

        var badge = new PlayerUpdatePacketAddNotifications();
        foreach (var guid in guids)
            badge.Notifications.Add(new NotificationInfo { Guid = guid, Combat = true, Type = 3, Unknown10 = true });
        player.SendTunneled(badge);
    }

    private Npc? CreateWolf(Player player, int modelId, int nameId, string textureAlias, string tintAlias,
        int health, float scale, Vector4 pos, bool showPlate, int activeProfile, int spawnFx, float speed)
    {
        if (!TryCreateNpc(out var npc))
            return null;

        // ★ Every field below mirrors the live AddNpc packets verbatim (04-01 capture decode
        // 2026-07-05). Pack wolves: NameId set but plate HIDDEN + no bar (no overhead UI at all —
        // video-confirmed); the NameId still feeds the target frame when clicked. The Alpha flips
        // showPlate: plate + bar visible -> the floating red name + red bar over his head.
        npc.ModelId = modelId;
        npc.NameId = nameId;
        npc.Name = null;
        npc.TextureAlias = textureAlias;
        npc.TintAlias = tintAlias;
        npc.HideNamePlate = !showPlate;
        npc.ShowHealthBar = showPlate;
        npc.Scale = scale;
        npc.Disposition = 0;            // hostile
        // Non-zero ActiveProfile makes the client's AddNpc apply re-run the name color resolver AFTER
        // disposition lands -> hostile + NameColor unset = RED (see Npc.Disposition notes). Live uses
        // the real job-profile ids 151 (pack) / 152 (alpha).
        npc.ActiveProfile = activeProfile;
        npc.CompositeEffectId = spawnFx; // 46 = the live spawn poof on wave wolves (0 on roamer/alpha)
        npc.MaxHealth = health;
        npc.Health = health;
        npc.IsInteractable = true;      // live: 1 / range 100 on every wolf
        npc.InteractRange = 100;
        npc.Visible = true;
        npc.CursorId = 11;              // crossed-swords attack cursor (delivered via NpcRelevance)

        // Locomotion: model's own clips (-1), PHYSICS movement, live wire values.
        npc.WalkAnimId = -1;
        npc.RunAnimId = -1;
        npc.StandAnimId = -1;
        npc.MovementType = WolfMovementTypePhysics;
        npc.Speed = speed;              // live: 0 on wave wolves (ExpectedSpeed drives them), 3.0 roamer
        npc.RiderGuid = ulong.MaxValue; // "no rider" invalid-guid sentinel gate

        npc.UpdatePosition(pos, Quaternion.Identity);

        // Push directly so the player sees it immediately (the tile system covers everyone else).
        player.OnAddVisibleNpcs(npc);
        npc.OnAddVisiblePlayers(player);

        // Live post-spawn burst, in order: UpdateMana(100,800,800) then CharacterState baseline.
        player.SendTunneled(new PlayerUpdatePacketUpdateMana { Guid = npc.Guid });
        player.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = npc.Guid,
            Status = (CharacterStatus)CharState_Baseline,
        });

        // Clickable attack target (cursor via relevance — same recipe as the training dummy).
        SendNpcRelevance(player, npc);

        // Belt-and-suspenders hostile mark (op35/sub28). NOTE: live does NOT send this for wolves
        // (disposition rides in the AddNpc), but our builds have always shipped it and the red-name
        // behavior is proven with it — keep until a live test confirms it's redundant.
        player.SendTunneled(new PlayerUpdatePacketUpdateDisposition
        {
            Guid = npc.Guid,
            Disposition = 0,
        });

        return npc;
    }

    // ── AI ───────────────────────────────────────────────────────────────────────────────────────────

    // Chase-the-player AI: position tick + client interpolation; bites use CombatPacketAttackProcessed
    // (live per-bite packet: wolf attacker, player target, fx 5409 / crit 5622).
    private void StartWolfAi(Player player, int run)
    {
        _ = Task.Run(async () =>
        {
            _logger.LogInformation("Frostfang arena: AI loop started (run {run}).", run);

            try
            {
                var lastPackBite = 0L;

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
                    Npc? fleeingAlpha;
                    lock (_stateLock)
                    {
                        pack = [.. _wolves];
                        fleeingAlpha = _fleeingAlpha;
                    }

                    if (pack.Length == 0 && fleeingAlpha is null)
                        continue; // between waves or encounter done

                    var now = Environment.TickCount64;
                    var target = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);
                    var dt = TickMs / 1000f;

                    // The defeated Alpha runs for the fog (kept out of _wolves so nothing else touches him).
                    if (fleeingAlpha is not null)
                        TickFleeingAlpha(player, fleeingAlpha, now, dt);

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

                        // ROAMER: amble between random waypoints at walk speed until the player closes in
                        // (proximity — the live trigger) or hits it (OnNpcDamaged). Either fires the howl
                        // via EngageRoamer. Scenery until then, matching the video's load-in wolf. Once it
                        // has howled it stops roaming (falls through to the hold+charge gate below).
                        if (state.IsRoamer && !state.Charging && !state.Howled)
                        {
                            var dxr = target.X - here.X;
                            var dzr = target.Z - here.Z;
                            if (dxr * dxr + dzr * dzr <= RoamerAggroRange * RoamerAggroRange)
                            {
                                _logger.LogInformation("Frostfang arena: player closed in on the roamer -> howl + wave 1.");
                                EngageRoamer(player, wolf, state);
                            }
                            else
                            {
                                TickRoamer(player, wolf, state, here, now, dt);
                                continue;
                            }
                        }

                        // Standing still: the roamer holding its howl pose, or a wave wolf in its ~2.2s
                        // post-spawn idle — either way, wait out ChargeAtTicks, then charge.
                        if (!state.Charging)
                        {
                            if (now < state.ChargeAtTicks)
                                continue;
                            BeginCharge(player, wolf, state);
                        }

                        // CHARGING: converge on an owned slot around the player at live chase speed.
                        var slot = target + new Vector3(MathF.Sin(state.SlotAngle), 0f, MathF.Cos(state.SlotAngle)) * EngageRadius;

                        var toPlayerH = new Vector2(target.X - here.X, target.Z - here.Z);
                        var distToPlayerH = toPlayerH.Length();

                        // op125 ROTATION = a normalized FACING DIRECTION vector (x, 0, z) — live-wire
                        // convention (client reader sub_8E5940 reads 3 raw floats; W unused).
                        var face = distToPlayerH > 0.01f ? toPlayerH / distToPlayerH : new Vector2(0f, 1f);
                        var rot = new Quaternion(face.X, 0f, face.Y, 0f);

                        // Converge vertically to the player's real ground height (no server heightmap).
                        var newY = MoveToward(here.Y, target.Y, YSpeed * dt);

                        if (distToPlayerH > BiteRange)
                        {
                            var toSlot = new Vector2(slot.X - here.X, slot.Z - here.Z);
                            var distToSlot = toSlot.Length();
                            var step = MathF.Min(ChaseSpeed * dt, distToSlot);
                            var dir = distToSlot > 0.01f ? toSlot / distToSlot : Vector2.Zero;

                            var newPos = new Vector4(here.X + dir.X * step, newY, here.Z + dir.Y * step, wolf.Position.W);

                            wolf.UpdatePosition(newPos, rot);
                            // State bit0 SET means "no speed" client-side (RE'd) -> send 0 while MOVING.
                            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = wolf.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
                            });
                        }
                        else
                        {
                            var newPos = new Vector4(here.X, newY, here.Z, wolf.Position.W);

                            wolf.UpdatePosition(newPos, rot);
                            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = wolf.Guid, Position = newPos, Rotation = rot, State = 1, Unknown = 0,
                            });

                            // Bites — live pacing is sparse (~1 per 2.7s across the whole pack), each
                            // one a CombatPacketAttackProcessed: wolf attacker (plays the bite clip),
                            // player target (incoming number/recoil), fx 5409 / crit 5622.
                            if (now >= state.NextBiteTicks && now - lastPackBite >= BiteGlobalGapMs)
                            {
                                state.NextBiteTicks = now + BiteCooldownMs;
                                lastPackBite = now;

                                var crit = _rng.Next(100) < BiteCritPercent;
                                player.SendTunneled(new CombatPacketAttackProcessed
                                {
                                    AttackerGuid = wolf.Guid,
                                    TargetGuid = player.Guid,
                                    Damage = crit ? BiteCritDamage : BiteDamage,
                                    MaxHealth = 2500,        // player max HP (real player pool is a TODO)
                                    CompositeEffectId = crit ? BiteCritFxId : BiteFxId,
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

    /// <summary>Wander tick: walk to a random waypoint near mid-arena at live walk speed (3.0),
    /// pause a beat, pick another. No biting — the roamer is scenery until provoked.</summary>
    private void TickRoamer(Player player, Npc wolf, WolfState state, Vector3 here, long now, float dt)
    {
        if (state.WanderTarget is null)
        {
            if (now < state.WanderPauseUntil)
                return;

            // New waypoint within ~14m of the roamer's home spot, kept inside the clearing.
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
            // Arrived — stand for 1.5-3.5s (send one stopped update so the client halts locomotion).
            state.WanderTarget = null;
            state.WanderPauseUntil = now + 1500 + _rng.Next(2000);
            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
            {
                Guid = wolf.Guid, Position = wolf.Position, Rotation = new Quaternion(0f, 0f, 1f, 0f),
                State = 1, Unknown = 0,
            });
            return;
        }

        var dir = to / d;
        var step = MathF.Min(RoamSpeed * dt, d);
        var newPos = new Vector4(here.X + dir.X * step, MoveToward(here.Y, GroundY, YSpeed * dt),
            here.Z + dir.Y * step, wolf.Position.W);
        var rot = new Quaternion(dir.X, 0f, dir.Y, 0f);

        wolf.UpdatePosition(newPos, rot);
        player.SendTunneled(new PlayerUpdatePacketUpdatePosition
        {
            Guid = wolf.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
        });
    }

    /// <summary>The live aggro burst, verbatim order: ExpectedSpeed 3.0 -> ExpectedSpeed 6.0 ->
    /// CharacterState 0x8001. The Alpha skips the 0x8001 (bit15 at spawn suppressed overhead plates in
    /// our 2026-07-03 live test, and his plate must stay visible — video-first).</summary>
    private void BeginCharge(Player player, Npc wolf, WolfState state)
    {
        state.Charging = true;
        state.NextBiteTicks = Environment.TickCount64 + 1000 + _rng.Next(1500);

        player.SendTunneled(new PlayerUpdatePacketExpectedSpeed { Guid = wolf.Guid, ExpectedSpeed = RoamSpeed });
        player.SendTunneled(new PlayerUpdatePacketExpectedSpeed { Guid = wolf.Guid, ExpectedSpeed = ChaseSpeed });

        if (!ReferenceEquals(wolf, _alpha))
        {
            player.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
            {
                Guid = wolf.Guid,
                Status = (CharacterStatus)CharState_Charging,
            });
        }
    }

    /// <summary>The roamer's fight-kickoff (live idx 28467-28471): it plants, rears into a commanding
    /// howl — SetAnimation com_cast_01 (1111) + PlayCompositeEffect 15226 (moire "commanding-shout" rings
    /// over its head), animation and FX FIRING TOGETHER (EffectDelay 0) — and the pack spawns. It holds
    /// the pose for RoamerHowlHoldMs (the AI loop then charges it via ChargeAtTicks) so the howl reads
    /// before the lunge. Fires exactly once — proximity or a hit on the roamer (both idempotent).</summary>
    private void EngageRoamer(Player player, Npc roamer, WolfState state)
    {
        lock (_stateLock)
        {
            if (_roamerEngaged)
                return;
            _roamerEngaged = true;

            // Plant it where it stands so the client stops its wander walk and plays the howl cleanly.
            var facePlayer = new Vector2(player.Position.X - roamer.Position.X, player.Position.Z - roamer.Position.Z);
            var faceLen = facePlayer.Length();
            var faceDir = faceLen > 0.01f ? facePlayer / faceLen : new Vector2(0f, 1f);
            var howlRot = new Quaternion(faceDir.X, 0f, faceDir.Y, 0f);
            player.SendTunneled(new PlayerUpdatePacketUpdatePosition
            {
                Guid = roamer.Guid, Position = roamer.Position, Rotation = howlRot, State = 1, Unknown = 0,
            });

            // The howl — animation and composite together (EffectDelay 0 keeps the FX in sync with the
            // pose; the live 2000 fired the rings ~2s late, which read as "the FX only went as he charged").
            player.SendTunneled(new PlayerUpdatePacketSetAnimation
            {
                Guid = roamer.Guid,
                AnimationId = RoamerHowlAnimId,
            });
            player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = roamer.Guid,
                Unknown2 = player.Guid,
                CompositeEffectId = RoamerHowlFxId,
                EffectDelay = 0,
                Position = new Vector4(0f, 0f, 0f, 1f),
                Clear = true,
            });

            // Hold the pose, THEN charge — the loop's charge gate waits out ChargeAtTicks.
            state.Howled = true;
            state.ChargeAtTicks = Environment.TickCount64 + RoamerHowlHoldMs;

            // The pack answers the call — not a moment before.
            SpawnWave(player);
        }
    }

    /// <summary>The defeated Alpha's flee run: sprint straight AWAY from the player (facing that way,
    /// no biting) until the flee timeout or he reaches the arena edge, then a small poof + despawn.
    /// Smooth server-driven movement — no death clip, no teleport.</summary>
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
                    return; // already handled
                _fleeingAlpha = null;
            }
            player.SendTunneled(new PlayerUpdatePacketRemoveNotifications { Guids = { alpha.Guid } });
            alpha.GracefulRemoval = (false, 0, 0, DeathPoofFxId, 1000); // quiet poof once he's in the fog
            alpha.Dispose();
            _logger.LogInformation("Frostfang arena: the fled Alpha reached the fog -> despawned.");
            return;
        }

        // Run directly away from the player and face that way.
        var awayX = here.X - player.Position.X;
        var awayZ = here.Z - player.Position.Z;
        var len = MathF.Sqrt(awayX * awayX + awayZ * awayZ);
        var dir = len > 0.01f ? new Vector2(awayX / len, awayZ / len) : new Vector2(0f, 1f);
        var step = FleeSpeed * dt;
        var newPos = new Vector4(
            here.X + dir.X * step,
            MoveToward(here.Y, GroundY, YSpeed * dt),
            here.Z + dir.Y * step,
            alpha.Position.W);
        var rot = new Quaternion(dir.X, 0f, dir.Y, 0f);

        alpha.UpdatePosition(newPos, rot);
        player.SendTunneled(new PlayerUpdatePacketUpdatePosition
        {
            Guid = alpha.Guid, Position = newPos, Rotation = rot, State = 0, Unknown = 0,
        });
    }

    /// <summary>Provoking the roamer (any damage) flips it into a normal charger.</summary>
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

    // ── Hearts ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Drop a heart pickup (736 = powerup_health_buff.adr) — live spawns one at the defeated
    /// Alpha's spot; mid-fight drops are random (the video's +125 heal at 1:05).</summary>
    private void SpawnHeart(Player player, Vector4 pos)
    {
        if (!TryCreateNpc(out var heart))
            return;

        heart.ModelId = HeartModelId;
        heart.Name = null;
        heart.NameId = 5102381;       // live heart NameId
        heart.Disposition = 1;        // neutral (not a combat target)
        heart.Scale = 1f;
        heart.IsInteractable = false; // auto-collected by walking over it, no click prompt
        heart.InteractRange = 0;
        heart.Visible = true;
        heart.MaxHealth = 0;          // not damageable
        heart.ShowHealthBar = false;
        heart.HideNamePlate = true;
        heart.ActiveProfile = 8;      // live heart AddNpc value
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

    /// <summary>Walk-over heart collection: within range → +125 heal number + green FX, remove the
    /// heart with the live pickup effect (graceful remove, fx 15032 — verbatim capture params).</summary>
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
            // Green "+125" heal number over the player.
            player.SendTunneled(new PlayerUpdatePacketHitPointModification
            {
                Guid = player.Guid,   // heal is self-sourced
                Guid2 = player.Guid,  // ...on the player
                Unknown = true,
                Unknown2 = 2500,      // player max HP (real pool is a TODO)
                Unknown3 = 2500,      // current after (cosmetic until HP is tracked)
                Unknown4 = HeartHeal, // +125 delta -> the green heal number
            });

            // ★ THE HEALING STATUS EFFECT (live-faithful): attach the LOOPING heal shower (15921) over
            // the player's head via an effect tag (op35/sub41) — the "heart above his head + healing
            // trail" from the video — then stop it after HealShowerMs (op35/sub42). Sourced from the
            // heart's guid, exactly like the capture.
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
                    _logger.LogError(ex, "Frostfang arena: heal-shower stop failed.");
                }
            });

            // Live heart removal: graceful, fx 15032 (the pickup sparkle), params verbatim.
            h.GracefulRemoval = (false, 0, 5000, HeartPickupFxId, 1000);
            h.Dispose();
        }
    }

    // ── Kills / waves / victory ─────────────────────────────────────────────────────────────────────

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

                // Live wave trigger: the next wave runs in when the field is (nearly) clear.
                if (_wolves.Count <= 1 && _waveIndex < WaveSizes.Length && !_waveScheduled && !_won)
                {
                    _waveScheduled = true;
                    scheduleWave = true;
                }
            }
            else
            {
                return; // not an encounter NPC
            }
        }

        // Clear the minimap combat marker, then the ONE live death packet: RemovePlayerGracefully
        // (Animate=true -> the client plays the wolf's own death clip, 5017 poof after Delay).
        killer.SendTunneled(new PlayerUpdatePacketRemoveNotifications { Guids = { npc.Guid } });

        if (!alphaDown)
        {
            npc.GracefulRemoval = (true, WolfDeathHoldMs, 0, DeathPoofFxId, 1000);
            var deathPos = npc.Position;
            npc.Dispose();

            // Random mid-fight heart drop (video: the +125 pickup mid-fight).
            if (_rng.Next(100) < HeartDropPercent)
                SpawnHeart(killer, deathPos);

            if (scheduleWave)
                ScheduleNextWave(killer, _encounterRun);
            return;
        }

        // ★ THE ALPHA IS DEFEATED — he FLEES, he does NOT die (video 1:25-1:35: at 0 HP he turns and
        //   runs off into the fog). We keep him alive-but-INVULNERABLE and drive a real run AWAY from
        //   the player in the AI loop (TickFleeingAlpha) until the timeout / arena edge, then poof.
        //   (The earlier build reused the pack "animate + poof" graceful-remove here, which made the
        //   client play his DEATH clip in place — he "died instead of fleeing", the user's report. The
        //   live graceful-remove(animate,delay=10000) is ambiguous; driving the run explicitly
        //   guarantees the video's flee.) He's out of _wolves already (above) + invulnerable, so he
        //   can't be hit while fleeing and nothing else moves him — no teleport stutter.
        _logger.LogInformation("Frostfang arena: the Alpha is DEFEATED -> he flees to the fog; encounter won.");

        var alphaPos = npc.Position;
        npc.Invulnerable = true;
        lock (_stateLock)
        {
            _fleeingAlpha = npc;
            _alphaFleeUntilTicks = Environment.TickCount64 + AlphaFleeMs;
        }
        killer.SendTunneled(new PlayerUpdatePacketExpectedSpeed { Guid = npc.Guid, ExpectedSpeed = FleeSpeed });

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

    /// <summary>The win moment — every beat verbatim from the live capture burst: the Alpha's parting
    /// drops (heart + coin pop), the goal completing (green ✓ "Goal Complete!"), the loot wheel + score
    /// rows, and the exit door. NO auto-return — the player leaves through the door.</summary>
    private void WinEncounter(Player player, Vector4 alphaPos)
    {
        // Clear any pack wolves still alive — on live the Alpha spawns WITH the final wave, so the
        // player can defeat him while stragglers remain, and the win burst removes them (04-01: wolves
        // 0x22/0x26 removed right after the Alpha at 37148). Without this they'd keep biting on the
        // victory screen. Scatter them off with the same graceful poof + clear their minimap dots.
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
            player.SendTunneled(new PlayerUpdatePacketRemoveNotifications { Guids = { straggler.Guid } });
            straggler.GracefulRemoval = (true, WolfDeathHoldMs, 0, DeathPoofFxId, 1000);
            straggler.Dispose();
        }

        // 1) The Alpha's parting drops at his EXACT defeat spot (live: heart + coin pile both at his
        //    last position, e.g. 116.85,-0.45,180.32 — heart at ground, coins popped up). The coin
        //    pile POPS outward (Knockback) with a burst effect and vanishes (pure theater; the actual
        //    coin grant is the reward banner / wheel). On live these two are born from a death-ability
        //    the Alpha "casts" (StartCasting self + LaunchAndLand + DetonateProjectile arc) — that
        //    projectile flourish is omitted here (would need 2 more packet classes for a cosmetic arc);
        //    spawning them directly at the death spot reproduces the on-screen result.
        SpawnHeart(player, alphaPos);
        SpawnCoinPop(player, alphaPos);

        // 2) Goal complete — BOTH live packets: op45/sub3 (the green-check "Goal Complete!" announce)
        //    + op47/sub3 (the Goals-window row flips to done). Live sends no per-kill ticks before this.
        player.SendTunneled(new ObjectiveCompletePacket { ObjectiveId = GoalScareWolves });
        player.SendTunneled(new UiObjectiveCompletePacket { ObjectiveId = GoalScareWolves });

        // 2b) Goal reward: the encounter XP (live: 10, from the goal's own reward bundle). AwardXp
        //     drives the ACTIVE job's real level bar (+ level-up celebration when it tips); the
        //     RewardBundlePacket is the coins/XP fly-in banner the live goal bundle produced.
        player.AwardXp(EncounterXp);
        player.SendTunneled(new RewardBundlePacket { Xp = EncounterXp });

        // 2c) Credit any quest whose active goal is "win THIS encounter" (EncounterComplete, id 174) -
        //     e.g. Brawler: Growler Encroachment. This is what makes the dungeon a quest objective.
        _questManager.OnEncounterComplete(player, EncounterId);

        // 3) ★ LOOT WHEEL (real end flow, 04-01 capture + client RE — see MiniGameLootWheelPackets).
        // Pick the prize SERVER-SIDE (the spin is theater): uniform over the 5 preview items + the
        // coins slice. These packets must go out while the MiniGameState is still alive (the landing
        // apply matches the prize NameId against the state's stored preview rows); the Lua keeps the
        // resolved index, so the player can spin any time. MUST be the same job set the launch packet
        // advertised (NameId matching — see GetPrizePreviewFor).
        var prizes = GetPrizePreviewFor(player);
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

        // 4) THE EXIT DOOR — replaces the old 6-second auto-kick. The player spins the wheel and
        //    leaves whenever they like by clicking the door (live cursor 17 + minimap exit badge).
        SpawnExitDoor(player);

        _logger.LogInformation("Frostfang arena: encounter WON — wheel armed, exit door out ({kills} kills).", enemies);
    }

    /// <summary>The live coin-pile pop: loot_coins_01 spawns at the Alpha's spot, gets a Knockback
    /// along a random direction + a burst effect, and is removed almost immediately.</summary>
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
        coins.ActiveProfile = 28;     // live coins AddNpc value
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
                await Task.Delay(150); // live removes the pile ~0.1s after the pop
                coins.GracefulRemoval = (false, 0, 0, 0, 1000);
                coins.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Frostfang arena: coin-pop removal failed.");
            }
        });
    }

    /// <summary>The live exit door (846 = sg_exit_door_01.adr at (145, 0, 173.35), scale 1.2): AddNpc
    /// hostile-then-SetDisposition(neutral) exactly like the wire, cursor 17, minimap exit badge.</summary>
    private void SpawnExitDoor(Player player)
    {
        if (!TryCreateNpc(out var door))
            return;

        door.ModelId = DoorModelId;
        door.NameId = DoorNameId;
        door.Name = null;
        door.Disposition = 0;           // live AddNpc ships 0, then flips neutral via sub28 below
        door.Scale = DoorScale;
        door.IsInteractable = true;
        door.InteractRange = DoorInteractRange;
        door.Visible = true;
        door.MaxHealth = 0;
        door.ShowHealthBar = false;
        door.HideNamePlate = false;     // live: plate shown (door name on approach)
        door.ActiveProfile = DoorActiveProfile;
        door.CursorId = DoorCursorId;   // live NpcRelevance cursor 17
        door.WalkAnimId = -1;
        door.RunAnimId = -1;
        door.StandAnimId = -1;
        door.MovementType = WolfMovementTypePhysics;
        door.RiderGuid = ulong.MaxValue;
        door.UpdatePosition(new Vector4(DoorSpawn.X, GroundY, DoorSpawn.Z, 1f), Quaternion.Identity);

        player.OnAddVisibleNpcs(door);
        door.OnAddVisiblePlayers(player);

        // Live companion burst: SetDisposition(neutral), baseline state, cursor, minimap badge.
        player.SendTunneled(new PlayerUpdatePacketUpdateDisposition { Guid = door.Guid, Disposition = 1 });
        // NO op35/sub9 vitals packet for the door: it RENDERS AN OVERHEAD BAR regardless of value (a full
        // 100/100/100 still shows a full bar — user-confirmed). The door is a static interactable with no
        // health, so it must not get vitals at all (the heart/coins don't either, and show no bar). The
        // live server did send it 100/100/100, but we skip it here — the door works purely off its
        // NpcRelevance cursor + the interact handler, and the user wants no bar on it.
        player.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = door.Guid,
            Status = (CharacterStatus)CharState_Baseline,
        });
        SendNpcRelevance(player, door);

        var badge = new PlayerUpdatePacketAddNotifications();
        badge.Notifications.Add(new NotificationInfo
        {
            Guid = door.Guid,
            Combat = false,
            Type = DoorBadgeType,           // live: 7
            Unknown3 = DoorBadgeUnknown3,   // live: 102
            ImageId = DoorMinimapImageId,   // live: 186 (minimap exit icon)
            DescriptionId = 0,
            NameId = DoorNameId,
            SubTextId = -1,
            Unknown8 = true,                // live: minimap-only (no floating icon over the door)
            CompositeEffectId = 0,
            Unknown10 = true                // constant 1 across all live samples
        });
        player.SendTunneled(badge);

        lock (_stateLock)
            _exitDoor = door;
    }

    /// <summary>True if the guid is the live exit door (interact routing).</summary>
    public bool IsExitDoor(ulong guid)
    {
        lock (_stateLock)
            return _exitDoor is { } door && door.Guid == guid;
    }

    /// <summary>Player clicked the exit door — release the encounter and send them home.</summary>
    public void UseExitDoor(Player player)
    {
        _logger.LogInformation("Frostfang arena: {name} used the exit door.", player.Name);
        ReturnHome(player);
    }

    /// <summary>Release the client from the encounter (RE'd exit protocol): remove the minigame
    /// state (op39/sub19 — full client-side teardown incl. combat exit for combat-type games) and
    /// restore the default combat ruleset (op62) + clear the transient fighting state. Without this
    /// the client stays InCombat forever (can't change jobs after leaving — LIVE TEST 11 bug).</summary>
    public void EndEncounterForPlayer(Player player) => EndEncounterForPlayer(player, won: false);

    public void EndEncounterForPlayer(Player player, bool won)
    {
        // On a WIN, mark the run won (op39/sub18 GameOver, Won=true) IMMEDIATELY before the state
        // remove, so the end card the teardown triggers reads Won=true. A mid-run bail keeps won=false.
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

        bool won;
        lock (_stateLock)
            won = _won;

        EndEncounterForPlayer(player, won);

        var home = _zoneManager.StartingZone;

        player.TeleportToZone(home, home.SpawnPosition, home.SpawnRotation, sky: null, geometryId: 0);
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
