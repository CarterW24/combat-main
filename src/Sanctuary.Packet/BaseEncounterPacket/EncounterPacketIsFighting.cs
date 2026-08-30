using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class EncounterPacketIsFighting : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 133;

    public bool InWorldCombat;

    public EncounterPacketIsFighting() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(InWorldCombat);

        return writer.Buffer;
    }
}
