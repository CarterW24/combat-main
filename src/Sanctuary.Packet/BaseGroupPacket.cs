using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// PARTY (op40 = the client's GROUP system). Sub-opcodes are SHORTS (the op40 dispatch in
// PacketReaderExtensions reads reader.Read<short>()). Ghidra-confirmed classes (2026-07-11), all
// proper SOE packet classes with named vtables:
//   C2S (server reads): 1 GroupInvite, 4 GroupAccept, 3 GroupLeave, 6 GroupKick
//   S2C (server writes): 1 GroupInvite (notify invitee), 2 GroupInviteReply (status enum),
//     5 GroupAcceptReply, 7 GroupKickReply, 8 GroupUpdate (roster), 9 GroupMemberUpdate,
//     11 RenamePlayer, 13 AnnounceEncounterReply
// Same header shape as every other Base*Packet: [short OpCode][short SubOpCode].
public class BaseGroupPacket
{
    public const short OpCode = 40;

    private short SubOpCode;

    public BaseGroupPacket(short subOpCode)
    {
        SubOpCode = subOpCode;
    }

    public virtual void Write(PacketWriter writer)
    {
        writer.Write(OpCode);
        writer.Write(SubOpCode);
    }

    public bool TryRead(ref PacketReader reader)
    {
        if (!reader.TryRead(out short opCode) && opCode != OpCode)
            return false;

        if (!reader.TryRead(out short subOpCode) && subOpCode != SubOpCode)
            return false;

        return true;
    }
}
