using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// PARTY op40/sub3 GroupLeave (S2C) — tells a client it is NO LONGER in a group. The client's group
// processor (Ghidra FUN_0093daf0 case 3) FREES the entire group state and closes the group/combat-group
// window. Send this to whoever left / was kicked, and to every member on a disband. An empty sub-8
// GroupUpdate does NOT close the window — this is the packet that does.
// Body (reader FUN_008fb5c0, 2026-07-11): header [40][3] | ulong Guid | NameData  (the departed player,
// used for the client's "X left the group" line; the teardown happens regardless of these values).
public sealed class GroupPacketGroupLeave : BaseGroupPacket, ISerializablePacket
{
    public new const short OpCode = 3;

    // The player who left / was removed (drives the client's leave message).
    public ulong Guid;

    public NameData Name = new();

    public GroupPacketGroupLeave() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);          // [short 40][short 3]

        writer.Write(Guid);
        Name.Serialize(writer);

        return writer.Buffer;
    }
}
