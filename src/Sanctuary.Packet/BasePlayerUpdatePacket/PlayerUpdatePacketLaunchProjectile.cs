using System.Numerics;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Packet;

public class PlayerUpdatePacketLaunchProjectile : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 62;

    public const int FlightTypeBeam = ProjectileParameters.FlightTypeBeam;
    public const int FlightTypeArc = ProjectileParameters.FlightTypeArc;
    public const int FlightTypeSeek = ProjectileParameters.FlightTypeSeek;
    public const int FlightTypeThrowCatch = ProjectileParameters.FlightTypeThrowCatch;
    public const int FlightTypeBoomerang = ProjectileParameters.FlightTypeBoomerang;

    public int ActorId;

    public int ProjectileId;

    public float Speed;
    public float Acceleration;

    public int FlightType = FlightTypeBeam;

    public int FireForward;

    public Vector4 Direction;
    public Vector4 StartPosition;

    public string ModelFileName = string.Empty;

    public Target Source = Target.CreateCharacterGuid(0);
    public Target Destination = Target.CreateCharacterGuid(0);

    public Vector4 SpinAxis;
    public float SpinRate;

    public bool VolleyGroupMode;

    public float ScaleStart = 1f;
    public float ScaleEnd = 1f;

    public int TrailCompositeEffectId;

    public int Unknown18;
    public int Unknown19;

    public float LaunchDelay;

    public float MaxLifetime = 10f;

    public int Unknown22;
    public int Unknown23;

    public float Unknown24;
    public float Unknown25;
    public float Unknown26;
    public float Unknown27;

    public string Unknown28 = string.Empty;

    public int CompositeEffectId;

    public PlayerUpdatePacketLaunchProjectile() : base(OpCode)
    {
    }

    private ProjectileParameters ToParameters() => new()
    {
        ActorId = ActorId,
        ProjectileId = ProjectileId,
        Speed = Speed,
        Acceleration = Acceleration,
        FlightType = FlightType,
        FireForward = FireForward,
        Direction = Direction,
        StartPosition = StartPosition,
        ModelFileName = ModelFileName,
        Source = Source,
        Destination = Destination,
        SpinAxis = SpinAxis,
        SpinRate = SpinRate,
        VolleyGroupMode = VolleyGroupMode,
        ScaleStart = ScaleStart,
        ScaleEnd = ScaleEnd,
        TrailCompositeEffectId = TrailCompositeEffectId,
        Unknown18 = Unknown18,
        Unknown19 = Unknown19,
        LaunchDelay = LaunchDelay,
        MaxLifetime = MaxLifetime,
        Unknown22 = Unknown22,
        Unknown23 = Unknown23,
        Unknown24 = Unknown24,
        Unknown25 = Unknown25,
        Unknown26 = Unknown26,
        Unknown27 = Unknown27,
        Unknown28 = Unknown28,
    };

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        ToParameters().Serialize(writer);

        writer.Write(CompositeEffectId);

        return writer.Buffer;
    }
}
