using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Signals that an entity has been destroyed/killed (OpCode 35, SubOpCode 56).
public class PlayerUpdatePacketDestroyed : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 56;

    public ulong Guid;

    // The entity that dealt the killing blow.
    public ulong KillerGuid;

    // Unknown purpose — set to 0.
    public int Unknown;

    public PlayerUpdatePacketDestroyed() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(KillerGuid);
        writer.Write(Unknown);

        return writer.Buffer;
    }
}
