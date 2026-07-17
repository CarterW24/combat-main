using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class ClientUpdatePacketAddProfileAbilitySetApl : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 15;

    public List<AbilityExperience> AbilityExperiences = new();

    public ClientUpdatePacketAddProfileAbilitySetApl() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        foreach (var abilityExperience in AbilityExperiences)
        {
            abilityExperience.Serialize(writer);

            if (abilityExperience.Present == 0)
                return writer.Buffer;
        }

        new AbilityExperience { Present = 0 }.Serialize(writer);

        return writer.Buffer;
    }
}
