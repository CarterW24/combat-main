using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Server -> client quest turn-in / completion screen (case 13: FUN_00c7cd40 -> FUN_00c7bbd0 ->
/// FUN_00c7b990). Drives the client's "Quest Complete" end screen (QuestHandler:ShowEndScreen) and
/// the completion camera close-up on the turn-in NPC (processor FUN_00a95420 focuses the "HEAD"
/// bone, same path the offer uses). After the 6-byte header the deserializer reads, in order:
///   8 bytes  -> obj+0x10/+0x14  (NPC guid - the camera focus target)
///   int      -> obj+0x18        (QuestId - the client echoes THIS back in QuestEndReplyPacket, so
///                                it must match the accepted quest's id or the objective/journal
///                                never clears)
///   int      -> obj+0x1c        (title text id)
///   int      -> obj+0x20        (description text id)
///   RewardBundleBase (FUN_008e7930, 69 bytes)
///   float    -> obj+0xe0        (completion %, NaN-checked)
/// Total 99 bytes = 6 header + 93 payload.
/// </summary>
public class QuestEndPacket : BaseQuestPacket, ISerializablePacket
{
    public const int SubOpCode = 13;

    public ulong NpcGuid;        // obj+0x10/+0x14 - turn-in NPC, drives the camera close-up
    public int QuestId;          // obj+0x18 - echoed in QuestEndReply; must match the active quest
    public int TitleId;          // obj+0x1c
    public int DescriptionId;    // obj+0x20
    public float Percent = 1f;   // obj+0xe0

    public int RewardCoins;      // RewardBundleBase +0x50
    public int RewardStars;      // RewardBundleBase +0x48

    public QuestEndPacket() : base(SubOpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // short OpCode(49) + int SubOpCode(13) = 6-byte header

        writer.Write(NpcGuid);
        writer.Write(QuestId);
        writer.Write(TitleId);
        writer.Write(DescriptionId);

        // RewardBundleBase (FUN_008e7930 read order) - +0x50 = coins, +0x48 = stars; rest 0.
        writer.Write(false); // +0x74 bool
        writer.Write(RewardCoins); // +0x50 int (coins)
        writer.Write(RewardStars); // +0x48 int (stars)
        writer.Write(0); // +0x4C int
        writer.Write(0); // +0x54 int
        writer.Write(0); // +0x6C int
        writer.Write(0); // +0x70 int
        writer.Write(0f); // +0x78 float
        writer.Write(0); // +0x5C int
        writer.Write(0); // +0x60 int
        writer.Write(0); // guid pair 1, low
        writer.Write(0); // guid pair 1, high
        writer.Write(0); // guid pair 2, low
        writer.Write(0); // guid pair 2, high
        writer.Write(0); // +0x64 int
        writer.Write(0); // +0x68 int
        writer.Write(0); // discarded temp int
        writer.Write(0); // +0x58 int

        writer.Write(Percent);

        return writer.Buffer;
    }
}
