using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PacketGeneratePortraitRequest : BaseFotomatPacket, ISerializablePacket
{
    public new const short OpCode = 1;

    public ulong Guid;

    public string? Provider;

    public PacketGeneratePortraitRequest() : base(OpCode)
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
