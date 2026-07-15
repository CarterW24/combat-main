using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BaseCombatPacket (op 32) sub-opcode 6 = "AttackTargetDodged" — tells the client a target evaded a swing.
//
// WIRE FORMAT PROVEN 2026-07-15 from the client reader (sub_A2A820) + handler (sub_A2B580):
//
//   wire: [op16][sub16] ulong Attacker ulong Target   (20 bytes total)
//
//   Unlike AttackProcessed (op32/7), the attacker guid is written ONCE here (not duplicated).
//   The handler looks up both entities; on the TARGET it enqueues the floating "Dodge" hit-type
//   text (client CID for "Dodge"), and on the ATTACKER it plays the swing/contact so the enemy
//   still looks like it took a shot. No damage/health fields — a dodge deals nothing.
public class CombatPacketAttackTargetDodged : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 6;

    /// <summary>Who swung and got evaded.</summary>
    public ulong AttackerGuid;

    /// <summary>Who dodged — the client renders the "Dodge" text over this entity.</summary>
    public ulong TargetGuid;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(AttackerGuid);
        writer.Write(TargetGuid);

        return writer.Buffer;
    }
}
