using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Field layout verified LIVE via debugger (IDA local Windows debugger, breakpoint on entry,
/// single-stepped through the client's actual deserializer): ClientPcData::sub_A107F0 case 1
/// -> sub_C7BB60 -> sub_C7B7A0, and RewardBundleBase::sub_8E7930 / FUN_008c9d20,
/// FreeRealms_2014-03-13.exe. This supersedes an earlier layout traced against sub_C7B990,
/// which turned out to not be the function actually invoked at runtime - that mismatch is why
/// a "byte-perfect" (against the wrong function) packet never produced any client reaction.
/// Six int32 fields, a bool, a nested RewardBundleBase (18 fixed-size fields, no lists), then
/// two more int32 fields, one more int32, and two more bools. 108 bytes of payload.
/// Field semantics beyond QuestId are positionally confirmed but not semantically named yet.
/// </summary>
public class QuestInfoPacket : BaseQuestPacket, ISerializablePacket
{
    public const int SubOpCode = 1;

    public int QuestId;
    public int TitleId;
    public int DescriptionId;
    public int HelperTextId;
    public int IconId;
    public int Unknown6;
    public bool Unknown7;

    /// <summary>
    /// Read as one 8-byte value in a single bounds check right after RewardBundleBase - the same
    /// pattern used for 64-bit guids elsewhere in this protocol. Almost certainly the quest
    /// giver NPC's guid, used by the client to know whose portrait/model to show in the offer
    /// popup (previously sent as 0, which made the client fall back to showing the player).
    /// </summary>
    public ulong NpcGuid;

    public int Unknown10;
    public bool Unknown11;
    public bool Unknown12;

    /// <summary>RewardBundleBase +0x50 - coins shown in the offer's reward preview.</summary>
    public int RewardCoins;
    /// <summary>RewardBundleBase +0x48 - stars shown in the offer's reward preview.</summary>
    public int RewardStars;

    public QuestInfoPacket() : base(SubOpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(QuestId);
        writer.Write(TitleId);
        writer.Write(DescriptionId);
        writer.Write(HelperTextId);
        writer.Write(IconId);
        writer.Write(Unknown6);
        writer.Write(Unknown7);

        // RewardBundleBase - +0x50 = coins, +0x48 = stars (confirmed live); rest 0.
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

        writer.Write(NpcGuid);
        writer.Write(Unknown10);
        writer.Write(Unknown11);
        writer.Write(Unknown12);

        return writer.Buffer;
    }
}
