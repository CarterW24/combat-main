using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketAttackAttackerMissed : BaseCombatPacket, ISerializablePacket
{
    public new const short OpCode = 5;

    public ulong AttackerGuid;

    public ulong TargetGuid;

    public CombatPacketAttackAttackerMissed() : base(OpCode)
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
