using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

/// <summary>
/// One entry in a profile's ability list — an ability DEFINITION + its progression. The AbilitiesScreen
/// (<c>AbilitiesScreen.gfx</c>) builds the panel from this list: the "Traits" section shows the entries with
/// <see cref="IsActivateable"/> == false (passives), each LOCKED until the player reaches
/// <see cref="RequiredLevel"/>; activatable entries are the usable abilities.
///
/// ★ Wire format REVERSE-ENGINEERED 2026-07-14 (Ghidra, FreeRealms.exe). The client value-object getter
/// FUN_00c8efa0 + its field-name enum FUN_00c8ee10 expose the record fields (property index -> data):
///   0 Id  1 IsActivateable(bool)  2 Name  3 Description  4 Icon  5 ProfileId(context, not on the wire)
///   6 Experience  7 Rank  8 RankExperience  9 RankMaxExperience  10 RequiredLevel
/// which map field-for-field onto this struct's serialize order. The old "Unknown" fields were:
///   Unknown = Present/Id (0 terminates the list), Unknown2 = IsActivateable, Unknown6 = Experience,
///   Unknown10 = RequiredLevel. Serialization ORDER and byte layout are UNCHANGED — only names + docs.
/// </summary>
public class AbilityExperience : ISerializableType
{
    /// <summary>Non-zero = a real entry; 0 = end-of-list terminator (see the loops in ClientPcProfile /
    /// ClientUpdatePacketAddProfileAbilitySetApl). Doubles as the ability Id. (Was <c>Unknown</c>.)</summary>
    public int Present;

    /// <summary>true = an activatable ability; FALSE = a PASSIVE — i.e. a TRAIT, shown in the panel's Traits
    /// section and gated by <see cref="RequiredLevel"/>. (Was <c>Unknown2</c>.)</summary>
    public bool IsActivateable;

    public int NameId;
    public int DescriptionId;

    public int IconId;

    /// <summary>Ability experience (progress toward the ability's own rank). (Was <c>Unknown6</c>.)</summary>
    public int Experience;

    /// <summary>Ability rank / level. The active job's entry drives the on-screen job XP bar.</summary>
    public int Level;

    /// <summary>XP into the current rank (bar fill). Client field "RankExperience".</summary>
    public int Progress;

    /// <summary>XP needed for the current rank (bar denominator). Client field "RankMaxExperience".</summary>
    public int TotalForLevel;

    /// <summary>Player job level at which this unlocks. The Traits section shows a lock + "Unlocked at level N"
    /// until the active profile's rank reaches it. 0 = always available. (Was <c>Unknown10</c>.)</summary>
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
