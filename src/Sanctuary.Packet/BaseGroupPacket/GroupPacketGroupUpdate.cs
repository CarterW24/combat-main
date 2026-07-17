using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public sealed class GroupPacketGroupUpdate : BaseGroupPacket, ISerializablePacket
{
    public new const short OpCode = 8;

    public sealed class Member
    {
        public ulong Guid;
        public NameData Name = new();
        public int ProfileId;
        public int ProfileRank;
        public int WorldId;
        public int Unknown3;    // int3 (+0x9c)
        public int Unknown4;    // int4 (+0xa0)
        public int Unknown5;    // int5 (+0xa4)
        public int Unknown6;    // int6 (+0xa8)
        public bool Online = true;
        public bool Unknown7;      // bool1 (+0xc5)
        public int Unknown8;    // int (+0xc8)
    }

    public ulong LeaderGuid;

    public List<Member> Members = [];

    public GroupPacketGroupUpdate() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Members.Count);
        foreach (var m in Members)
        {
            writer.Write(m.Guid);
            m.Name.Serialize(writer);
            writer.Write(m.ProfileId);
            writer.Write(m.ProfileRank);
            writer.Write(m.WorldId);
            writer.Write(m.Unknown3);    // int3
            writer.Write(m.Unknown4);    // int4
            writer.Write(m.Unknown5);    // int5
            writer.Write(m.Unknown6);    // int6
            writer.Write(m.Online);
            writer.Write(m.Unknown7);    // bool1
            writer.Write(m.Unknown8);    // int
        }

        return writer.Buffer;
    }
}
