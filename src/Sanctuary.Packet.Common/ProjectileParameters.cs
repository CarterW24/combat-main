using System.Numerics;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Packet.Common;

// The projectile descriptor the client reads via ProjectileParameters__Unserialize (0x8E8910).
// SHARED payload: op35/62 PlayerUpdateLaunchProjectile carries it directly (+ a trailing fx int),
// and op36/4 AbilityPacketLaunchAndLand embeds it as its final nested struct (verified in IDA:
// sub_A31F30's last call is this reader). Field order below = the play-verified op35/62 order.
// Full field semantics: docs/projectile-wire-spec.md.
public class ProjectileParameters : ISerializableType
{
    public const int FlightTypeBeam = 1;
    public const int FlightTypeArc = 2;
    public const int FlightTypeSeek = 4;
    public const int FlightTypeThrowCatch = 7;
    public const int FlightTypeBoomerang = 8;

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

    public void Serialize(PacketWriter writer)
    {
        writer.Write(ActorId);

        writer.Write(ProjectileId);

        writer.Write(Speed);
        writer.Write(Acceleration);

        writer.Write(FlightType);

        writer.Write(FireForward);

        writer.Write(Direction);
        writer.Write(StartPosition);

        writer.Write(ModelFileName);

        Source.Serialize(writer);
        Destination.Serialize(writer);

        writer.Write(SpinAxis);
        writer.Write(SpinRate);

        writer.Write(VolleyGroupMode);

        writer.Write(ScaleStart);
        writer.Write(ScaleEnd);

        writer.Write(TrailCompositeEffectId);

        writer.Write(Unknown18);
        writer.Write(Unknown19);

        writer.Write(LaunchDelay);

        writer.Write(MaxLifetime);

        writer.Write(Unknown22);
        writer.Write(Unknown23);

        writer.Write(Unknown24);
        writer.Write(Unknown25);
        writer.Write(Unknown26);
        writer.Write(Unknown27);

        writer.Write(Unknown28);
    }
}
