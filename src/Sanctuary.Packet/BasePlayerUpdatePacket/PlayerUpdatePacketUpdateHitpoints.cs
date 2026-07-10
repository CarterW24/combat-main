using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP: BasePlayerUpdatePacket (op 35) sub-opcode 5 = "UpdateHitpoints". Per-entity hitpoints
// update (carries a Guid), unlike ClientUpdatePacketHitpoints (op 38/sub 1) which is self-only. Drives
// an NPC's nameplate health bar.
//
// WIRE FORMAT CONFIRMED from IDA (client UnserializePacket sub_8D6F10): reads an 8-byte Guid (two
// dwords) then THREE int32s = 20 bytes total. A short packet trips m_bReachedEnd and the client
// REJECTS the whole packet (this was the original bug — only 2 ints were sent). The meaning of the
// three ints is still provisional; current/max first is the working hypothesis, Unknown=0.
public class PlayerUpdatePacketUpdateHitpoints : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 5;

    public ulong Guid;

    public int Hitpoints;
    public int MaxHitpoints;
    public int Unknown;

    public PlayerUpdatePacketUpdateHitpoints() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 35][sub 5]

        writer.Write(Guid);

        writer.Write(Hitpoints);
        writer.Write(MaxHitpoints);
        writer.Write(Unknown);

        return writer.Buffer;
    }
}
