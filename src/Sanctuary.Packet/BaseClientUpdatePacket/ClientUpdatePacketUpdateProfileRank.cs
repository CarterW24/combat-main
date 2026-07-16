using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Notifies the client of a profile rank/level change (OpCode 38, SubOpCode 18).
// Triggers the level-up display in the client.
public class ClientUpdatePacketUpdateProfileRank : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 18;

    // Profile/Job ID that leveled up.
    public int ProfileId;

    // New rank/level of the profile.
    public int NewRank;

    // Profile icon to display in UI.
    public int ProfileIconId;

    // Profile name string ID to display in level-up notification.
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
