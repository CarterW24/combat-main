using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// NPC ANIMATION CONTROL (pcap-verified 2026-07-02): op35 sub8 = SetAnimation. The LIVE 2014 SOE server
// streams these alongside op125 position updates (240 in logs/2014-03-25.pcap) — THIS is how real NPCs
// play locomotion/action clips while the movement manager slides their transform (without it: the
// "wolves glide without running" bug). Wire format from live samples (all 21 bytes):
//   [int16 35][int16 8][ulong Guid][int32 AnimationId][int32 Unknown=0][byte PlayType=2]
// (client class PlayerUpdatePacketSetAnimation, ctor 0x8B3EE0, dispatched HandlePlayerUpdatePacket case 8;
// live AnimationIds are full animation-group ids, e.g. 0xCF4/0xC1D per-model clips.)
public class PlayerUpdatePacketSetAnimation : ISerializablePacket
{
    public const short OpCode = 35;
    public const short SubOpCode = 8;

    public ulong Guid;
    public int AnimationId;
    public int Unknown;
    public byte PlayType = 2; // constant 2 across all live samples

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(Guid);
        writer.Write(AnimationId);
        writer.Write(Unknown);
        writer.Write(PlayType);

        return writer.Buffer;
    }
}
