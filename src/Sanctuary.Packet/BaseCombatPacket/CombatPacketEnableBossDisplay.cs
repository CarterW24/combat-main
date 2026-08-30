using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketEnableBossDisplay : BaseCombatPacket, ISerializablePacket
{
    public new const short OpCode = 9;

    public ulong Guid;
    public bool Enable = true;

    public CombatPacketEnableBossDisplay() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Enable);

        return writer.Buffer;
    }
}
