using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// op50 (RewardBase family) sub 1 — "RewardBundlePacket": the standalone REWARD GRANT banner the live
// server sent right after the loot wheel stopped (04-01 idx 38142 + 38146). Wire = [short 50][byte 1]
// + one RewardBundleBase. Two live shapes:
//   * CONTENTS grant (38142): 1 ITEM entry (3x Flabbergast Sphere 3015) with the bundle's ItemGuid-tail
//     flag set (tail = the player's inventory item row id).
//   * PRIZE banner (38146): 0 entries, U13/U14 = the won prize's icon/name (Mystery Pack 973/6666),
//     trailing bundle int 957 — the "you won X" display for the wheel result itself.
public class RewardBundlePacket : ISerializablePacket
{
    public const short OpCode = 50;
    public const byte SubOpCode = 1;

    public List<RewardEntry> Entries = [];

    public int Coins;
    public int Xp;

    /// <summary>Banner icon/name (-1 = defer to entry[0] — the client's U13/U14 fallback).</summary>
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
