using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Sent server→client to confirm damage dealt to a target (OpCode 32, SubOpCode 4).
public class CombatPacketAttackTargetDamage : BaseCombatPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    // GUID of the attacker.
    public ulong AttackerGuid;

    // GUID of the target being damaged.
    public ulong TargetGuid;

    // Damage amount dealt.
    public int Damage;

    public CombatPacketAttackTargetDamage() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(AttackerGuid);
        writer.Write(TargetGuid);
        writer.Write(Damage);

        return writer.Buffer;
    }
}
