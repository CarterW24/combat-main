using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class AbilityPacketJobLevelUp : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 15;

    public int ProfileId;
    public int NewLevel;
    public int ProfileIconId;
    public int ProfileNameId;

    public AbilityPacketJobLevelUp() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        writer.Write(ProfileId);
        writer.Write(NewLevel);
        writer.Write(ProfileIconId);
        writer.Write(ProfileNameId);

        return writer.Buffer;
    }
}
