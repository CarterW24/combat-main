using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PlayerUpdatePacketUpdateDisposition : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 28;

    public ulong Guid;
    public int Disposition;

    public PlayerUpdatePacketUpdateDisposition() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Disposition);

        return writer.Buffer;
    }
}
