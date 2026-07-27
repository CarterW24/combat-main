using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public sealed class GroupPacketAnnounceEncounterReply : BaseGroupPacket, ISerializablePacket
{
    public new const short OpCode = 13;

    public ulong MemberGuid;

    public bool Accepted;

    public GroupPacketAnnounceEncounterReply() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(MemberGuid);
        writer.Write(Accepted);

        return writer.Buffer;
    }
}
