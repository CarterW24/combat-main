using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// op35 sub9 "UpdateMana" — per-entity vitals push. GROUND TRUTH (04-01 capture): the live server sends
// this to EVERY freshly spawned encounter NPC (wolves, powerups, the exit door) right after its AddNpc:
//   2300 0900 [guid] [64000000 = 100] [20030000 = 800] [20030000 = 800]
// Also used for player mana updates from the leveling system (set CurrentMana/MaxMana explicitly).
public class PlayerUpdatePacketUpdateMana : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 9;

    public ulong Guid;

    public int CurrentMana = 100;
    public int MaxMana = 800;
    public int Unknown3 = 800;

    public PlayerUpdatePacketUpdateMana() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(CurrentMana);
        writer.Write(MaxMana);
        writer.Write(Unknown3);

        return writer.Buffer;
    }
}
