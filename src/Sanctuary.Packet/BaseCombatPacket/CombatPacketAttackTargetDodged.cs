using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketAttackTargetDodged : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 6;

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
