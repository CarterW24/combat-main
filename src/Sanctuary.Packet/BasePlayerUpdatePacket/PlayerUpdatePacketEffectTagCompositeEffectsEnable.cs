using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PlayerUpdatePacketEffectTagCompositeEffectsEnable : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 43;

    public bool Enable;

    public PlayerUpdatePacketEffectTagCompositeEffectsEnable() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Enable);

        return writer.Buffer;
    }
}
