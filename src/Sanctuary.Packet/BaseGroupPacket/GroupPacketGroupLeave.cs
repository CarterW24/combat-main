using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public sealed class GroupPacketGroupLeave : BaseGroupPacket, ISerializablePacket
{
    public new const short OpCode = 3;

    public ulong Guid;

    public NameData Name = new();

    public GroupPacketGroupLeave() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        Name.Serialize(writer);

        return writer.Buffer;
    }
}
