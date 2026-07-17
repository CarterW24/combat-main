using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ClientUpdatePacketUpdateProfileRank : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 18;

    public int ProfileId;

    public int NewRank;

    public int ProfileIconId;

    public int ProfileNameId;

    public ClientUpdatePacketUpdateProfileRank() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ProfileId);
        writer.Write(NewRank);
        writer.Write(ProfileIconId);
        writer.Write(ProfileNameId);

        return writer.Buffer;
    }
}
