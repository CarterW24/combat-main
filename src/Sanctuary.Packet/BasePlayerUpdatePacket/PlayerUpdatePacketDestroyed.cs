using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PlayerUpdatePacketDestroyed : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 56;

    public ulong Guid;

    public ulong KillerGuid;

    // Unknown purpose — set to 0.
    public int Unknown;

    public PlayerUpdatePacketDestroyed() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(KillerGuid);
        writer.Write(Unknown);

        return writer.Buffer;
    }
}
