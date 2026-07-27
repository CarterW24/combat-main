using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class EncounterParticipantMessagePacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 120;

    public int MessageStringId;

    public ulong PlayerGuid;

    public string FallbackName = string.Empty;

    public EncounterParticipantMessagePacket(int encounterId = 0, int instanceId = 0) : base(OpCode)
    {
        Unknown = encounterId;
        Unknown2 = instanceId;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(MessageStringId);
        writer.Write(PlayerGuid);
        writer.Write(FallbackName);

        return writer.Buffer;
    }
}
