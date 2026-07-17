using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Removes a buff/debuff tag by its InstanceId. Wire (verified live, 8 bytes):
// [op 38][sub 17][i32 InstanceId].
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
