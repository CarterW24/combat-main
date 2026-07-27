using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class EncounterInvitationRequestPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 102;

    public ulong InviterGuid;

    public int NameStringId;

    public int TimeoutSeconds;

    public EncounterInvitationRequestPacket(int encounterId = 0, int instanceId = 0) : base(OpCode)
    {
        Unknown = encounterId;
        Unknown2 = instanceId;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(InviterGuid);
        writer.Write(NameStringId);
        writer.Write(TimeoutSeconds);

        return writer.Buffer;
    }
}
