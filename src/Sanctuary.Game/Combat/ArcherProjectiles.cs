using System.Numerics;
using System.Threading;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Game.Combat;

// op35/62 arrows for archer attacks (docs/projectile-wire-spec.md).
public static class ArcherProjectiles
{
    // Magical arrow: the FX is baked into the model variant; the composite id also rides
    // the packet's trail field as live did.
    public const string Model = "arrow_projectile_magical.adr";
    public const int TrailEffectId = 15484;

    public const float Speed = 90f;

    // Spawn at bow height, not at the feet.
    public const float MuzzleHeight = 1.6f;

    // Client impacts when the projectile gets within projectile_maxproximity (RE'd default 2.0u)
    // of the destination — the arrow never travels the last 2u.
    public const float ImpactProximity = 2f;

    // Safety cap so flight time can't stall combat (client hard-despawns at 10s anyway).
    public const float MaxFlightTime = 3f;

    private static int _nextProjectileId;

    // Damage lands when the arrow does.
    public static float FlightTimeTo(Player player, Npc target)
    {
        var dx = target.Position.X - player.Position.X;
        var dy = (target.Position.Y + 1f) - (player.Position.Y + MuzzleHeight);
        var dz = target.Position.Z - player.Position.Z;
        var distance = System.MathF.Max(0f, System.MathF.Sqrt(dx * dx + dy * dy + dz * dz) - ImpactProximity);

        return System.MathF.Min(distance / Speed, MaxFlightTime);
    }

    public static void FireAt(Player player, Npc target)
    {
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
            FlightType = PlayerUpdatePacketLaunchProjectile.FlightTypeBeam,
            Direction = direction,
            StartPosition = start,
            ModelFileName = Model,
            Source = Target.CreateCharacterGuid((long)player.Guid),
            Destination = Target.CreateCharacterGuid((long)target.Guid),
            SpinAxis = new Vector4(0f, 0f, 1f, 0f),
            TrailCompositeEffectId = TrailEffectId,
        };

        player.SendTunneledToVisible(packet, sendToSelf: true);

        if (CombatDebug.Verbose)
            player.SendSystemMessage($"dbg proj #{packet.ProjectileId} {Model} v={Speed} -> {target.Name}");
    }

    // Retail no-target shot: FireForward (client m_nNoTargetMode) spawns the projectile at the
    // shooter's own actor and, with the guid-0 Destination invalid, flies it out along Direction
    // (client substitutes a point 100u down that line). Cosmetic only — no damage rides it.
    public static void FireForward(Player player)
    {
        var forward = player.Forward;

        var packet = new PlayerUpdatePacketLaunchProjectile
        {
            ProjectileId = Interlocked.Increment(ref _nextProjectileId),
            Speed = Speed,
            FlightType = PlayerUpdatePacketLaunchProjectile.FlightTypeBeam,
            FireForward = 1,
            Direction = new Vector4(forward.X, 0f, forward.Z, 0f),
            StartPosition = new Vector4(
                player.Position.X,
                player.Position.Y + MuzzleHeight,
                player.Position.Z,
                1f),
            ModelFileName = Model,
            Source = Target.CreateCharacterGuid((long)player.Guid),
            SpinAxis = new Vector4(0f, 0f, 1f, 0f),
            TrailCompositeEffectId = TrailEffectId,
        };

        player.SendTunneledToVisible(packet, sendToSelf: true);

        if (CombatDebug.Verbose)
            player.SendSystemMessage($"dbg proj #{packet.ProjectileId} fire-forward {Model} v={Speed}");
    }
}
