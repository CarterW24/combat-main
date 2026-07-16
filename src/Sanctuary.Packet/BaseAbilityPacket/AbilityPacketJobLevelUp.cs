using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Triggers the full-screen job level-up celebration UI (levelup_<job>.gfx) on the client
// (OpCode 36 / SubOpCode 15). Client-side this is FUN_009392c0 case 0xf, which deserializes the payload
// and fires the "JobLevelUp" UI event. Exact field order is best-effort (the client reads via the
// generic SoE deserializer); tune against in-game behavior.
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
