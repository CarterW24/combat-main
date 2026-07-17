using System;
using System.Numerics;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Entities;

public sealed class EncounterEntryNpc : Npc
{
    public EncounterEntryNpc(IZone zone) : base(zone)
    {
    }

    public override int CombatEncounterBadgeImageId => 24;

    private const float WanderRadius = 7f;
    private const float WanderSpeed = 2.5f;

    private Vector4 _origin;
    private Vector4 _target;
    private Vector4 _lastSentPosition;
    private float _lastSentSpeed = -1f;
    private DateTime _pauseUntil;

    public void StartWander(Vector4 origin)
    {
        _origin = origin;
        _target = origin;
        _lastSentPosition = origin;
        _pauseUntil = DateTime.UtcNow.AddSeconds(Random.Shared.NextDouble() * 4);
    }

    public override void UpdateEveryTick()
    {
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
            SendExpectedSpeed(0f);
            BroadcastPosition(0);
            _lastSentPosition = Position;
            _pauseUntil = now.AddSeconds(2.0 + Random.Shared.NextDouble() * 3.0);
            PickTarget();
            return;
        }

        SendExpectedSpeed(WanderSpeed);

        var step = MathF.Min(WanderSpeed * 0.1f, dist);
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
            BroadcastPosition(2);
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
