using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Server -> client reward-earned celebration (opcode 50 / sub-opcode 1). The client's reward
/// handler (deserializer FUN_00b8a300 -> FUN_00b89190 header + FUN_008e7930 RewardBundleBase,
/// then FUN_00b8a5f0 -> FUN_00b89ca0) shows the coins/stars/experience fly-in with sound. Sent
/// when a quest reward is granted so completion has the proper flourish.
///
/// Wire: short OpCode(50) + byte SubOpCode(1) + RewardBundleBase (69 bytes) = 72 bytes.
/// In the bundle, +0x50 = coins and +0x48 = stars (confirmed live via the offer preview).
/// </summary>
public class RewardBundlePacket : ISerializablePacket
{
    public const short OpCode = 50;
    public const byte SubOpCode = 1;

    public int RewardCoins;
    public int RewardStars;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);    // short
        writer.Write(SubOpCode); // byte

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

        return writer.Buffer;
    }
}
