using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class MiniGameInfo : ISerializableType
{
    public int NameId;
    public int IconId;
    public int DescriptionId;
    public int Difficulty;
    public int ProfileType;

    public RewardBundleBase RewardBundleBase = new();
    public RewardBundleBase RewardBundleBase_Member = new();
    public RewardBundleBase RewardBundleBase_Preview = new();

    public int Type;

    public bool MembersOnly;

    public int Unknown14;

    public int PreselectedGameId;

    public int Unknown20;

    public bool ShowStarCounter;
    public bool ShowStatusIcon;
    public bool ShowActionBar;

    public bool Unknown11;

    public bool ShowEndDialog;

    public bool Unknown15;
    public bool Unknown16;
    public bool Unknown17;
    public bool Unknown18;
    public bool Unknown19;

    public string? Unknown13;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(NameId);
        writer.Write(IconId);
        writer.Write(DescriptionId);
        writer.Write(Difficulty);
        writer.Write(ProfileType);
        writer.Write(Type);

        writer.Write(MembersOnly);

        RewardBundleBase.Serialize(writer);
        RewardBundleBase_Member.Serialize(writer);
        RewardBundleBase_Preview.Serialize(writer);

        writer.Write(0);

        writer.Write(ShowStarCounter);
        writer.Write(ShowStatusIcon);
        writer.Write(ShowActionBar);

        writer.Write(Unknown11);

        writer.Write(ShowEndDialog);

        writer.Write(Unknown13);

        writer.Write(Unknown14);

        writer.Write(Unknown15);

        writer.Write(PreselectedGameId);

        writer.Write(Unknown16);
        writer.Write(Unknown17);
        writer.Write(Unknown18);
        writer.Write(Unknown19);

        writer.Write(Unknown20);
    }
}
