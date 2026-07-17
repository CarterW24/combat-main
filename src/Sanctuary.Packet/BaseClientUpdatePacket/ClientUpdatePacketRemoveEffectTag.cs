using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ClientUpdatePacketRemoveEffectTag : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 17;

    public int InstanceId;

    public ClientUpdatePacketRemoveEffectTag() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(InstanceId);

        return writer.Buffer;
    }
}
