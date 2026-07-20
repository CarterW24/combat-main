using System.Numerics;
using System.Threading;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Game.Combat;

// !proj tuning probe for op35/62 LaunchProjectile (docs/projectile-wire-spec.md).
// Fires on every archer attack so arrows can be eyeballed live before real wiring.
public static class ProjectileProbe
{
    public static bool Enabled = true;

    public static string Model = "arrow_projectile.adr";
    // 20 = user play-feel 2026-07-19 (50/60 read too fast); no live data exists for arrow speed.
    public static float Speed = 20f;
    public static float Acceleration;
    public static int FlightType = PlayerUpdatePacketLaunchProjectile.FlightTypeBeam;
    public static float SpinRate;
    public static int TrailEffectId;
    public static int CompositeEffectId;

    // Spawn at bow height, not at the feet.
    public static float MuzzleHeight = 1.6f;

    // Delay each victim's damage by its arrow's flight time (!proj sync on/off).
    public static bool SyncDamage = true;

    // Client impacts when the projectile gets within projectile_maxproximity (RE'd default 2.0u)
    // of the destination — the arrow never travels the last 2u.
    public const float ImpactProximity = 2f;

    // Safety cap so a bad speed value can't stall combat (client hard-despawns at 10s anyway).
    public const float MaxFlightTime = 3f;

    // `!trail` candidates. Model != "" swaps the ARROW MODEL (fx baked into the .adr —
    // guaranteed visible; live shipped these variants for exactly this). Id = composite fx
    // ALSO sent as the packet's trail field (attach path unproven on our probes so far —
    // model-backed entries are the reliable ones, marked ★ in the list).
    public static readonly (int Id, string Key, string Name, string Model)[] TrailCandidates =
    [
        (15479, "flaming",       "PRJ_flaming_orange_arrow",        "arrow_projectile_flaming.adr"),
        (15483, "green",         "PRJ_magical_green_arrow",         "arrow_projectile_magical_green.adr"),
        (15485, "purple",        "PRJ_magical_purple_arrow",        "arrow_projectile_magical_purple.adr"),
        (15484, "magical",       "PRJ_magical_yellow-green_arrow",  "arrow_projectile_magical.adr"),
        (0,     "laser",         "laser-red model",                 "arrow_projectile_laser-red.adr"),
        (0,     "laserorange",   "laser-orange model",              "arrow_projectile_laser-orange.adr"),
        (15334, "sonic",         "PRJ_soundwaves_blue",             "arrow_projectile_sonic.adr"),
        (16474, "heart",         "PRJ_hearts_red_trailing_med_loop","arrow_projectile_heart.adr"),
        (16066, "rocket",        "PRJ_rocket_trail_level-5",        "arrow_projectile_rocket.adr"),
        (0,     "birthday",      "birthday rocket model",           "arrow_projectile_rocket_birthday.adr"),
        (0,     "balloons",      "balloons model",                  "arrow_projectile_balloons.adr"),
        (0,     "boxing",        "punching-glove model",            "arrow_projectile_punchingglove.adr"),
        (0,     "dwarftech",     "dwarftech-bow model",             "arrow_projectile_dwarftech-bow.adr"),
        (16110, "freezing",      "PRJ_archer_freezing-shot_trail",  ""),
        (16056, "multishot",     "PRJ_archer_multishot_trail",      ""),
        (16214, "ricochet",      "PRJ_archer_ricochet_trail_red",   ""),
        (16050, "stunning",      "PRJ_archer_stunning-shot_trail",  ""),
        (15488, "split",         "PRJ_magical_blue_split-arrow",    ""),
        (15489, "multi",         "PRJ_magical_multi-arrow",         ""),
        (15501, "graybeam",      "PRJ_beam_gray_trail_arrow",       ""),
        (16420, "bluebeam",      "PRJ_beam_blue-orange_trail_arrow",""),
        (5479,  "fireball",      "PRJ_fireball_orange_1",           ""),
        (5483,  "fireball5",     "PRJ_fireball_orange_5",           ""),
        (15787, "redball",       "PRJ_fireball_red_medium",         ""),
        (15611, "blueball",      "PRJ_fireball_blue_small",         ""),
        (15613, "greenball",     "PRJ_fireball_green_small",        ""),
        (15615, "purpleball",    "PRJ_fireball_purple_small",       ""),
        (5606,  "mana",          "PRJ_mana-ball_blue_1",            ""),
        (5492,  "lightning",     "PRJ_lightning_ball_light-blue_1", ""),
        (16291, "chain",         "PRJ_lightning_wizard-chain",      ""),
        (16061, "arcane",        "PRJ_wizard_arcane-chain_level-5", ""),
        (15329, "snow",          "PRJ_ice_snowflakes",              ""),
        (15327, "stars",         "PRJ_stars_multi",                 ""),
        (16188, "sparkles",      "PRJ_sparkles_purple_trail_loop",  ""),
        (16341, "purplefire",    "PRJ_fire_purple_medium_trail",    ""),
        (15339, "poison",        "PRJ_poison_green",                ""),
        (15185, "meteor",        "PRJ_meteor_orange_small",         ""),
        (15494, "shuriken",      "PRJ_shuriken_trail_spin",         ""),
        (16013, "dragon",        "PRJ_ninja_dragonstrike",          ""),
        (15610, "water",         "PRJ_water_white_trail_pt_med_loop",""),
        (15332, "smoke",         "PRJ_smoke_brown",                 ""),
        (15326, "bubbles",       "PRJ_bubbles_white",               ""),
        (15328, "flies",         "PRJ_flies",                       ""),
        (15892, "tomato",        "PRJ_tomato",                      ""),
        (16975, "egg",           "PRJ_easteregg",                   ""),
        (15356, "cards",         "PRJ_cards_100_count",             ""),
        (15228, "icecream",      "PRJ_icecream_mint",               ""),
        (15314, "rock",          "PRJ_rock_brown_large",            ""),
        (16093, "firetrail",     "WFX_trail_special_fire_orange",   ""),
        (16094, "icetrail",      "WFX_trail_special_ice_white",     ""),
        (16095, "arcanetrail",   "WFX_trail_special_magic-arcane",  ""),
        (16096, "shadow",        "WFX_trail_special_magic-shadow",  ""),
        (16097, "nature",        "WFX_trail_special_nature_green",  ""),
        (16414, "candycane",     "WFX_trail_special_candycane",     ""),
        (16475, "heartstreak",   "WFX_trail_special_hearts_red",    ""),
        (15651, "orangetrail",   "WFX_trail_fire_orange",           ""),
        (15652, "prism",         "WFX_trail_prism_multi",           ""),
        (15653, "rainbow",       "WFX_trail_rainbow_multi",         ""),
        (15671, "bluelightning", "WFX_trail_lightning_blue",        ""),
        (15673, "web",           "WFX_trail_web_white",             ""),
    ];

    // Whether the current Model was set by a trail entry (so composite-only entries can
    // fall back to the plain arrow instead of keeping a stale themed model).
    private static bool _modelSetByTrail;

    public static int FindTrailIndexByKey(string key)
    {
        for (var i = 0; i < TrailCandidates.Length; i++)
            if (TrailCandidates[i].Key == key)
                return i;
        return -1;
    }

    public static string ApplyTrailEntry(int index)
    {
        var (id, key, name, model) = TrailCandidates[index];

        TrailIndex = index;
        TrailEffectId = id;

        if (model.Length > 0)
        {
            Model = model;
            _modelSetByTrail = true;
        }
        else if (_modelSetByTrail)
        {
            Model = "arrow_projectile.adr";
            _modelSetByTrail = false;
        }

        var star = model.Length > 0 ? "★" : "";
        return $"{star}{key} = {name}{(model.Length > 0 ? $" [{model}]" : $" (fx {id})")}";
    }

    public static void ClearTrail()
    {
        TrailEffectId = 0;
        TrailIndex = -1;
        if (_modelSetByTrail)
        {
            Model = "arrow_projectile.adr";
            _modelSetByTrail = false;
        }
    }

    // Index into TrailCandidates for !proj trail next/prev (-1 = not on the list).
    public static int TrailIndex = -1;

    private static int _nextProjectileId;

    public static float FlightTimeTo(Player player, Npc target)
    {
        if (!Enabled || !SyncDamage)
            return 0f;

        var dx = target.Position.X - player.Position.X;
        var dy = (target.Position.Y + 1f) - (player.Position.Y + MuzzleHeight);
        var dz = target.Position.Z - player.Position.Z;
        var distance = System.MathF.Max(0f, System.MathF.Sqrt(dx * dx + dy * dy + dz * dz) - ImpactProximity);

        if (Acceleration > 0.01f && Speed >= 0f)
        {
            var t = (System.MathF.Sqrt(Speed * Speed + 2f * Acceleration * distance) - Speed) / Acceleration;
            return System.MathF.Min(t, MaxFlightTime);
        }

        if (Speed <= 0.01f)
            return 0f;

        return System.MathF.Min(distance / Speed, MaxFlightTime);
    }

    public static void FireAt(Player player, Npc target)
    {
        if (!Enabled)
            return;

        var start = new Vector4(
            player.Position.X,
            player.Position.Y + MuzzleHeight,
            player.Position.Z,
            1f);

        // The client recomputes direction toward Destination each frame; this only seeds it.
        var direction = new Vector4(
            target.Position.X - start.X,
            target.Position.Y + 1f - start.Y,
            target.Position.Z - start.Z,
            0f);

        var packet = new PlayerUpdatePacketLaunchProjectile
        {
            ProjectileId = Interlocked.Increment(ref _nextProjectileId),
            Speed = Speed,
            Acceleration = Acceleration,
            FlightType = FlightType,
            Direction = direction,
            StartPosition = start,
            ModelFileName = Model,
            Source = Target.CreateCharacterGuid((long)player.Guid),
            Destination = Target.CreateCharacterGuid((long)target.Guid),
            SpinAxis = new Vector4(0f, 0f, 1f, 0f),
            SpinRate = SpinRate,
            TrailCompositeEffectId = TrailEffectId,
            CompositeEffectId = CompositeEffectId,
        };

        player.SendTunneledToVisible(packet, sendToSelf: true);

        if (CombatDebug.Verbose)
            player.SendSystemMessage(
                $"dbg proj #{packet.ProjectileId} {Model} v={Speed} a={Acceleration} type={FlightType} -> {target.Name}");
    }
}
