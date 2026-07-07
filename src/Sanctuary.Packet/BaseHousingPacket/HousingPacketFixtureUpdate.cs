using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class HousingPacketFixtureUpdate : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 40;

    public ulong FixtureGuid;
    public Vector4 Position;
    public Quaternion Rotation;
    public float Scale;

    public HousingPacketFixtureUpdate() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(FixtureGuid);
        writer.Write(Position);
        writer.Write(Rotation);
        writer.Write(Scale);

        return writer.Buffer;
    }
}
