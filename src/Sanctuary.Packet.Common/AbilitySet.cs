using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class AbilitySet : ISerializableType
{
    private const int MaxAbilitySlots = 8;

    public Ability[] Abilities = new Ability[MaxAbilitySlots];

    public AbilitySet()
    {
        for (var i = 0; i < MaxAbilitySlots; i++)
            Abilities[i] = new Ability();
    }

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Abilities);
    }
}
