using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class ClientActivityLaunchMember : ISerializableType
{
    public int Id;

    public ulong Guid;

    public string? Name;

    public byte InviteStatus;

    public bool IsFoundingMember;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Id);

        writer.Write(Guid);

        writer.Write(Name);

        writer.Write(InviteStatus);
        writer.Write(IsFoundingMember);
    }
}
