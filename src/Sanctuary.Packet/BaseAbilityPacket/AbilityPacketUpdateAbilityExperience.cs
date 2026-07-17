using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

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
