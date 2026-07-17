using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PacketSnapshotRequest : BaseFotomatPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public ulong Guid;

    public string? Provider;

    public PacketSnapshotRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        Write(writer);
        writer.Write(Guid);
        writer.Write(Provider);
        return writer.Buffer;
    }
}
