using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP: BasePlayerUpdatePacket (op 35) sub-opcode 35 = "HitPointModification" — the floating
// combat damage/heal number shown over an entity.
//
// WIRE FORMAT CONFIRMED from IDA (client UnserializePacket sub_8D6C50): 30 bytes total —
//   ulong Guid   (m_llGuid)   target
//   ulong Guid2  (m_llGuid2)  source / attacker
//   bool  Unknown  (m_bUnknown)
//   int   Unknown2 (m_nUnknown2)   <-- damage amount (best hypothesis)
//   int   Unknown3 (m_nUnknown3)
//   int   Unknown4 (m_nUnknown4)
//   bool  Unknown5 (m_bUnknown5)
// A short packet trips m_bReachedEnd and the client rejects it (the previous bug). The semantics of the
// bools + which int holds the amount are still being pinned down live; Unknown2 = amount is the first try.
public class PlayerUpdatePacketHitPointModification : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 35;

    public ulong Guid;
    public ulong Guid2;

    public bool Unknown;

    public int Unknown2;
    public int Unknown3;
    public int Unknown4;

    public bool Unknown5;

    public PlayerUpdatePacketHitPointModification() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 35][sub 35]

        writer.Write(Guid);
        writer.Write(Guid2);

        writer.Write(Unknown);

        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);

        writer.Write(Unknown5);

        return writer.Buffer;
    }
}
