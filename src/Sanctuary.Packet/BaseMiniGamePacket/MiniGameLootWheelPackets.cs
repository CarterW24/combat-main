using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class MiniGameLootWheelSetItemToLandOnPacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 45;

    public List<RewardEntry> Entries = [];

    public int Coins;

    // Live wheel bundles carry 957 in the trailing bundle int (same value as the details preview).
    public int Unknown15 = 957;

    public MiniGameLootWheelSetItemToLandOnPacket() : base(OpCode, -1, -1, -1)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        var iconOverride = Entries.Count > 0 ? Entries[0].IconId : -1;
        var nameOverride = Entries.Count > 0 ? Entries[0].NameId : -1;
        RewardBundle.Write(writer, Entries, Coins, 0, iconOverride, nameOverride, Unknown15);

        return writer.Buffer;
    }
}

public sealed class MiniGameScoreRow
{
    public string Name = "";
    public int NameId = -1;
    public int Order;
    public int Value = -1;
    public int Max = -1;
    public int Points;
}

public class MiniGameGameEndScorePacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 47;

    public List<MiniGameScoreRow> Rows = [];

    public MiniGameGameEndScorePacket() : base(OpCode, -1, -1, -1)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Rows.Count);
        foreach (var row in Rows)
        {
            writer.Write(row.Name);
            writer.Write(row.NameId);
            writer.Write(row.Order);
            writer.Write(row.Value);
            writer.Write(row.Max);
            writer.Write(row.Points);
        }

        return writer.Buffer;
    }
}
