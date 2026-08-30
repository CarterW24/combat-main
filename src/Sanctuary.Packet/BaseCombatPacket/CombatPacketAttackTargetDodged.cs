using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketAttackTargetDodged : BaseCombatPacket, ISerializablePacket
{
    public new const short OpCode = 6;

    public ulong AttackerGuid;

    public ulong TargetGuid;

    public CombatPacketAttackTargetDodged() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(AttackerGuid);
        writer.Write(TargetGuid);

        return writer.Buffer;
    }
}
