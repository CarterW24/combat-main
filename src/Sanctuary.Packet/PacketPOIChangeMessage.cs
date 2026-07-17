using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PacketPOIChangeMessage : ISerializablePacket
{
    public const short OpCode = 104;

    private int NameId = default;

    public int ZoneId;

    private int AreaId = default;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);

        writer.Write(NameId);

        writer.Write(ZoneId);

        writer.Write(AreaId);

        return writer.Buffer;
    }
}
