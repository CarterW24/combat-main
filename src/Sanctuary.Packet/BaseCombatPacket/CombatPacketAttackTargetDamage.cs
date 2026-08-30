using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketAttackTargetDamage : BaseCombatPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public ulong AttackerGuid;

    public ulong TargetGuid;

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
