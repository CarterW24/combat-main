using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class PacketPetInfo : ISerializableType
{
    public int Id;

    public int Definition;

    public int NameId;

    public int ImageSetId;

    public int TintId;
    public string TintAlias = null!;

    public string TextureAlias = string.Empty;

    public ulong Guid;

    public bool MembersOnly;

    public bool IsNameable;
    public string Name = string.Empty;
    public bool IsUpgradable;
    public bool IsUpgraded;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Id);

        writer.Write(Id);
        writer.Write(false);
        writer.Write(0);
        writer.Write(Name);
        writer.Write(NameId);
        writer.Write(1.0f);
        writer.Write(1.0f);
        writer.Write(1.0f);
        writer.Write(1.0f);
        writer.Write(false);

        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        writer.Write(TintId);
        writer.Write(TextureAlias);
        writer.Write(0);
        writer.Write(false);
        writer.Write(0);
        writer.Write(false);

        for (var i = 0; i < 4; i++)
            writer.Write(0);

        for (var i = 0; i < 8; i++)
            writer.Write(0);
    }
}
