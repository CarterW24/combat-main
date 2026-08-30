using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PlayerUpdatePacketPlayCompositeEffect : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 16;

    public ulong Guid;
    public ulong SourceGuid;

    public int CompositeEffectId;

    public int DelayMs;

    public int LifetimeMs;

    public Vector4 Position;

    public bool Clear;

    public PlayerUpdatePacketPlayCompositeEffect() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        writer.Write(Guid);
        writer.Write(SourceGuid);

        writer.Write(CompositeEffectId);

        writer.Write(DelayMs);

        writer.Write(LifetimeMs);

        writer.Write(Position);

        writer.Write(Clear);

        return writer.Buffer;
    }
}
