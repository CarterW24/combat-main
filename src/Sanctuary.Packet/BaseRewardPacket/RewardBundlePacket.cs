using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Server -> client reward-earned celebration (opcode 50 / sub-opcode 1). The client's reward
/// handler (deserializer FUN_00b8a300 -> FUN_00b89190 header + FUN_008e7930 RewardBundleBase,
/// then FUN_00b8a5f0 -> FUN_00b89ca0) shows the coins/stars/experience fly-in with sound. Sent
/// when a quest reward is granted so completion has the proper flourish.
///
/// Wire: short OpCode(50) + byte SubOpCode(1) + RewardBundleBase = variable.
/// In the bundle, +0x50 = coins and +0x48 = job/profile experience (shown as XP).
/// </summary>
public class RewardBundlePacket : ISerializablePacket
{
    public const short OpCode = 50;
    public const byte SubOpCode = 1;

    public int RewardCoins;
    public int RewardExperience;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);    // short
        writer.Write(SubOpCode); // byte

        // RewardBundleBase - the coins/XP fly-in. Item celebration is handled separately by
        // RewardNonBundledItemPacket (50/2), so no item entries here.
        RewardBundleSerializer.Write(writer, RewardCoins, RewardExperience);

        return writer.Buffer;
    }
}
