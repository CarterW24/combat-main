using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// PARTY op40/sub8 GroupUpdate (S2C) — the ROSTER push that drives the group/combat-group window.
// ★ MEMBER RECORD FORMAT DECODED via Ghidra (2026-07-11, reader FUN_008fcb20): per member =
//   ulong Guid(8) | NameData | int32×7 | bool | bool | int32
// (object offsets +0x90..+0xa8 for the 7 ints, +0xc4/+0xc5 the two bools, +0xc8 the last int). Exact
// semantics of the 7 ints/2 bools aren't all named, but the client resolves job/level/portrait from
// its OWN cached character (by guid) — these fields just need to be the RIGHT SIZE so the member list
// stays aligned for 2+ members. Best-known mapping: int0=ProfileId(job), int1=ProfileRank(level),
// int2=WorldId; bool0=Online. The rest 0 until named.
public sealed class GroupPacketGroupUpdate : BaseGroupPacket, ISerializablePacket
{
    public new const short OpCode = 8;

    public sealed class Member
    {
        public ulong Guid;
        public NameData Name = new();
        public int ProfileId;   // int0 (+0x90) — job
        public int ProfileRank; // int1 (+0x94) — level
        public int WorldId;     // int2 (+0x98)
        public int Unknown3;    // int3 (+0x9c)
        public int Unknown4;    // int4 (+0xa0)
        public int Unknown5;    // int5 (+0xa4)
        public int Unknown6;    // int6 (+0xa8)
        public bool Online = true; // bool0 (+0xc4)
        public bool Unknown7;      // bool1 (+0xc5)
        public int Unknown8;    // int (+0xc8)
    }

    /// <summary>The party leader's guid (the group id).</summary>
    public ulong LeaderGuid;

    public List<Member> Members = [];

    public GroupPacketGroupUpdate() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);              // [short 40][short 8]

        // ★ NO LeaderGuid field here. The client body reader (FUN_0092aed0) reads ONLY the [40][8]
        // header (FUN_008d8e70) then the member-list count (FUN_009140f0). A LeaderGuid before the
        // count was misread AS the count (e.g. 385) -> the roster tried to parse 385 members and every
        // member guid/name came out garbage, so headshots never resolved. Format = [40][8][count][members].
        writer.Write(Members.Count);
        foreach (var m in Members)
        {
            writer.Write(m.Guid);
            m.Name.Serialize(writer);
            writer.Write(m.ProfileId);   // int0
            writer.Write(m.ProfileRank); // int1
            writer.Write(m.WorldId);     // int2
            writer.Write(m.Unknown3);    // int3
            writer.Write(m.Unknown4);    // int4
            writer.Write(m.Unknown5);    // int5
            writer.Write(m.Unknown6);    // int6
            writer.Write(m.Online);      // bool0
            writer.Write(m.Unknown7);    // bool1
            writer.Write(m.Unknown8);    // int
        }

        return writer.Buffer;
    }
}
