using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BaseCombatPacket (op 32) sub-opcode 5 = "AttackAttackerMissed" — the "Miss" twin of AttackTargetDodged.
// Same wire shape (reader sub_A2A7A0): [op16][sub16] ulong Attacker ulong Target (20 bytes). The handler
// (sub_A2B360) renders the floating "Miss" text over the target.
public class CombatPacketAttackAttackerMissed : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 5;

    public ulong AttackerGuid;
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
