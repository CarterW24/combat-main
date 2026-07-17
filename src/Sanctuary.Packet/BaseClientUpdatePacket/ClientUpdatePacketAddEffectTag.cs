using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// Adds a buff/debuff tag to the local player's buff bar. Wire (verified live, 97 bytes):
// [op 38][sub 16][i32 Id][i32 blobLen = 85][EffectTag]. Id duplicates Tag.InstanceId.
public class ClientUpdatePacketAddEffectTag : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 16;

    public EffectTag Tag = new();

    public ClientUpdatePacketAddEffectTag() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var blob = new PacketWriter();
        Tag.Serialize(blob);

        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Tag.InstanceId);
        writer.Write(blob.Buffer.Length);
        writer.Write(blob.Buffer);

        return writer.Buffer;
    }
}
