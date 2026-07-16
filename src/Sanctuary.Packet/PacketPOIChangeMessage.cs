using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PacketPOIChangeMessage : ISerializablePacket
{
    public const short OpCode = 104;

    // Unused
    private int NameId = default;

    public int ZoneId;

    // Unused
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