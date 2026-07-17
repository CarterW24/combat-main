using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class RewardBundlePacket : ISerializablePacket
{
    public const short OpCode = 50;
    public const byte SubOpCode = 1;

    public List<RewardEntry> Entries = [];

    public int Coins;
    public int Xp;

    public int IconId = -1;
    public int NameId = -1;

    public int Unknown15;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        RewardBundle.Write(writer, Entries, Coins, Xp, IconId, NameId, Unknown15);

        return writer.Buffer;
    }
}
