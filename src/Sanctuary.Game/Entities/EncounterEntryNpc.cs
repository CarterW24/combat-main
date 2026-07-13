using System;
using System.Numerics;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Entities;

/// <summary>A peaceful, wandering overworld creature that is the ENTRY to a combat encounter — the retail
/// "Battle Starter". Clicking it opens the encounter's start panel (via its <see cref="Npc.InteractAction"/>);
/// unlike a <see cref="CombatNpc"/> it never aggros, never takes damage, and can't be attacked — it just ambles
/// around its spawn post so an encounter feels like a live creature you walk up to rather than a fixed marker.
///
/// Movement mirrors CombatNpc's grounded broadcast: MovementType=2 (PHYSICS), stream ExpectedSpeed so the
/// client interpolates a smooth run between the throttled position updates. Kept a slow amble inside a small
/// radius so it never wanders off its post (which would also complicate tile visibility).</summary>
public sealed class EncounterEntryNpc : Npc
{
    public EncounterEntryNpc(IZone zone) : base(zone)
    {
    }

    /// <summary>Show the combat-encounter "Battle Starter" badge (img-24, red crossed-swords + red minimap dot)
    /// over the head — the retail cue that this creature starts a battle.</summary>
    public override int CombatEncounterBadgeImageId => 24;

    /// <summary>How far from its post it roams, and how fast (a gentle amble, well under combat speed).</summary>
    private const float WanderRadius = 7f;
    private const float WanderSpeed = 2.5f;

    private Vector4 _origin;
    private Vector4 _target;
    private Vector4 _lastSentPosition;
    private float _lastSentSpeed = -1f;
    private DateTime _pauseUntil;

    /// <summary>Anchor the roam at the given post and stagger the first move so a field of them doesn't step
    /// in lockstep.</summary>
    public void StartWander(Vector4 origin)
    {
        _origin = origin;
        _target = origin;
        _lastSentPosition = origin;
        _pauseUntil = DateTime.UtcNow.AddSeconds(Random.Shared.NextDouble() * 4);
    }

    public override void UpdateEveryTick()
    {
        // No observers -> nothing to animate for (and moving unseen just drifts the post out of sync).
        if (!Visible || VisiblePlayers.IsEmpty)
            return;

        var now = DateTime.UtcNow;
        if (now < _pauseUntil)
            return;

        var dx = _target.X - Position.X;
        var dz = _target.Z - Position.Z;
        var dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist < 0.5f)
        {
            // Reached the spot — plant (speed 0 + idle-state position) and idle a beat before the next move.
            SendExpectedSpeed(0f);
            BroadcastPosition(0);
            _lastSentPosition = Position;
            _pauseUntil = now.AddSeconds(2.0 + Random.Shared.NextDouble() * 3.0);
            PickTarget();
            return;
        }

        SendExpectedSpeed(WanderSpeed);

        var step = MathF.Min(WanderSpeed * 0.1f, dist); // ~10 ticks/sec
        var nx = dx / dist;
        var nz = dz / dist;
        var frac = step / dist;
        var newPos = new Vector4(
            Position.X + nx * step,
            Position.Y + (_target.Y - Position.Y) * frac,
            Position.Z + nz * step,
            1f);

        var angle = MathF.Atan2(dx, dz);
        var rot = new Quaternion(0f, MathF.Sin(angle / 2f), 0f, MathF.Cos(angle / 2f));
        UpdatePosition(newPos, rot);

        var sdx = newPos.X - _lastSentPosition.X;
        var sdz = newPos.Z - _lastSentPosition.Z;
        if (MathF.Sqrt(sdx * sdx + sdz * sdz) >= 0.3f)
        {
            BroadcastPosition(2); // run state
            _lastSentPosition = newPos;
        }
    }

    private void PickTarget()
    {
        var angle = Random.Shared.NextSingle() * MathF.Tau;
        var radius = Random.Shared.NextSingle() * WanderRadius;
        _target = new Vector4(
            _origin.X + MathF.Cos(angle) * radius,
            _origin.Y,
            _origin.Z + MathF.Sin(angle) * radius,
            1f);
    }

    private void SendExpectedSpeed(float speed)
    {
        if (_lastSentSpeed == speed)
            return;
        _lastSentSpeed = speed;
        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(new PlayerUpdatePacketExpectedSpeed { Guid = Guid, ExpectedSpeed = speed });
    }

    private void BroadcastPosition(byte state)
    {
        var packet = new PlayerUpdatePacketUpdatePosition
        {
            Guid = Guid,
            Position = Position,
            Rotation = Rotation,
            State = state,
            Unknown = 0
        };
        foreach (var player in VisiblePlayers.Values)
            player.SendTunneled(packet);
    }
}
