using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class PacketPetInfo : ISerializableType
{
    public int Id;

    // Server side
    public int Definition;

    public int NameId;

    public int ImageSetId; // Server-side only - not serialized, client derives icon from NameId

    public int TintId;
    public string TintAlias = null!; // Server-side only - not serialized, client has no field for it

    public string TextureAlias = string.Empty;

    public ulong Guid;

    public bool MembersOnly;

    public bool IsNameable; // Server-side only - not serialized
    public string Name = string.Empty;
    public bool IsUpgradable; // Server-side only - not serialized, client struct has no matching field
    public bool IsUpgraded; // Server-side only - not serialized, client struct has no matching field

    // Matches the client's ClientPetData::sub_912CF0 deserializer field-for-field (reverse
    // engineered from FreeRealms_2014-03-13.exe). The client's PetInfoList reader also consumes
    // one leading int32 (used as a hash key) before constructing each ClientPetData entry, so
    // that value is written here too, ahead of the entry's own fields.
    public void Serialize(PacketWriter writer)
    {
        writer.Write(Id); // hash key (outer PetInfoList reader)

        writer.Write(Id); // ClientPetData::m_nId
        writer.Write(false); // m_bUnknown2
        writer.Write(0); // m_nUnknown3
        writer.Write(Name); // m_strName
        writer.Write(NameId); // m_nNameId
        writer.Write(1.0f); // Hunger
        writer.Write(1.0f); // Hygiene
        writer.Write(1.0f); // Play
        writer.Write(1.0f); // Mood
        writer.Write(false); // m_bUnknown8

        writer.Write(0); // m_PetTricks HashList count (sub_8FCDB0) - none known yet
        writer.Write(0); // nested list count (sub_8DB130, offset +0xBC) - none known yet
        writer.Write(0); // nested list count (offset +0xE4) - none known yet

        writer.Write(TintId); // unknown int, offset +0x100
        writer.Write(TextureAlias); // m_strTextureAlias, offset +0x108
        writer.Write(0); // unknown int, offset +0xFC
        writer.Write(false); // unknown bool, offset +0x104
        writer.Write(0); // unknown int, offset +0x118
        writer.Write(false); // unknown bool, offset +0x11C

        for (var i = 0; i < 4; i++)
            writer.Write(0); // fixed int[4], offset +0x88

        for (var i = 0; i < 8; i++)
            writer.Write(0); // fixed int[8], offset +0x98
    }
}
