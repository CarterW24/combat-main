using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class HousingPacketFixturePlacementDenied : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 53;

    public int ItemDefinitionId;

    public HousingPacketFixturePlacementDenied() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ItemDefinitionId);

        return writer.Buffer;
    }
}
