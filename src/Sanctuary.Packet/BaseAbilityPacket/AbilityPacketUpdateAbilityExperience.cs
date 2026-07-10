using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

/// <summary>
/// Updates a job's (ability set's) experience on the client (OpCode 36 / SubOpCode 8). This is what
/// drives the on-screen job XP bar and the level-up celebration - the client reads the AbilityExperience
/// (Level/Progress/TotalForLevel) and renders the bar (Progress*100/TotalForLevel) + fires JobLevelUp
/// when Level increases. Handled client-side by FUN_009392c0 case 8 (deserializer FUN_008f8b40).
/// </summary>
public class AbilityPacketUpdateAbilityExperience : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 8;

    public AbilityExperience Experience = new();

    public AbilityPacketUpdateAbilityExperience() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        Experience.Serialize(writer);

        return writer.Buffer;
    }
}
