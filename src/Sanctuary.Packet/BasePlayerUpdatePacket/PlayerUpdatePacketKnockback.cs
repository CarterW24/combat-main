using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// op35 sub4 "Knockback" — hurl an actor along a direction (client-side arc). GROUND TRUTH (04-01
// capture idx 37143, the Frostfang win moment): the coin-pile object (loot_coins_01) pops out of the
// defeated Alpha with one of these:
//   2300 0400 [guid] [00000000] [pos 4f (117.94, -0.62, 180.05, 1.0)]
//   [dir 4f (0.970, 0, -0.243, 0)] [f 0.0712]
public class PlayerUpdatePacketKnockback : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public ulong Guid;

    public int Unknown;

    public Vector4 Position;

    // Unit XZ direction vector (same convention as op125 rotation).
    public Vector4 Direction;

    public float Magnitude;

    public PlayerUpdatePacketKnockback() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Unknown);
        writer.Write(Position);
        writer.Write(Direction);
        writer.Write(Magnitude);

        return writer.Buffer;
    }
}
