using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class AbilityExperience : ISerializableType
{
    public int Present;

    public bool IsActivateable;

    public int NameId;
    public int DescriptionId;

    public int IconId;

    public int Experience;

    public int Level;

    public int Progress;

    public int TotalForLevel;

    public int RequiredLevel;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Present);

        if (Present == 0)
            return;

        writer.Write(IsActivateable);

        writer.Write(NameId);
        writer.Write(DescriptionId);

        writer.Write(IconId);

        writer.Write(Experience);

        writer.Write(Level);
        writer.Write(Progress);
        writer.Write(TotalForLevel);
        writer.Write(RequiredLevel);
    }
}
