using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class EncounterHideRespawnWindowPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 126;

    public EncounterHideRespawnWindowPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        return writer.Buffer;
    }
}
