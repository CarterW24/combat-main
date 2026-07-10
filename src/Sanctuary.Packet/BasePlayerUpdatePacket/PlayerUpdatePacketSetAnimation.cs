using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// op35 sub8 "SetAnimation" — play an animation on an entity. Wire format (21 bytes, pcap-verified
// 2026-07-02 against logs/2014-03-25.pcap AND RE'd from the client dispatcher FUN_0092f460 case 8):
//   [int16 35][int16 8][ulong Guid][int32 AnimationId][int32 Unknown=0][byte PlayType]
// The LIVE 2014 SOE server streams these alongside op125 position updates — THIS is how real NPCs
// play locomotion/action clips while the movement manager slides their transform (without it: the
// "wolves glide without running" bug). Live AnimationIds are full animation-group ids (e.g. 0xCF4).
// PlayType (client +0x20): live samples are constant 2 = play now; bit0 set (1) = set the entity's
// BASE/IDLE animation instead (stored at entity+0x51c) — used by the boombox continuous dances.
public class PlayerUpdatePacketSetAnimation : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 8;

    public ulong Guid;
    public int AnimationId;
    public int Unknown;
    public byte PlayType = 2; // constant 2 across all live samples; 1 = set as base/idle anim

    public PlayerUpdatePacketSetAnimation() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 35][sub 8]

        writer.Write(Guid);
        writer.Write(AnimationId);
        writer.Write(Unknown);
        writer.Write(PlayType);

        return writer.Buffer;
    }
}
