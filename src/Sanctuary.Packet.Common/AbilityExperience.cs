using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

// One entry in a profile's ability list — an ability DEFINITION + its progression. The AbilitiesScreen
// (AbilitiesScreen.gfx) builds the panel from this list: the "Traits" section shows the entries with
// IsActivateable == false (passives), each LOCKED until the player reaches
// RequiredLevel; activatable entries are the usable abilities.
// ★ Wire format REVERSE-ENGINEERED 2026-07-14 (Ghidra, FreeRealms.exe). The client value-object getter
// FUN_00c8efa0 + its field-name enum FUN_00c8ee10 expose the record fields (property index -> data):
//   0 Id  1 IsActivateable(bool)  2 Name  3 Description  4 Icon  5 ProfileId(context, not on the wire)
//   6 Experience  7 Rank  8 RankExperience  9 RankMaxExperience  10 RequiredLevel
// which map field-for-field onto this struct's serialize order. The old "Unknown" fields were:
//   Unknown = Present/Id (0 terminates the list), Unknown2 = IsActivateable, Unknown6 = Experience,
//   Unknown10 = RequiredLevel. Serialization ORDER and byte layout are UNCHANGED — only names + docs.
public class AbilityExperience : ISerializableType
{
    // Non-zero = a real entry; 0 = end-of-list terminator (see the loops in ClientPcProfile /
    // ClientUpdatePacketAddProfileAbilitySetApl). Doubles as the ability Id. (Was Unknown.)
    public int Present;

    // true = an activatable ability; FALSE = a PASSIVE — i.e. a TRAIT, shown in the panel's Traits
    // section and gated by RequiredLevel. (Was Unknown2.)
    public bool IsActivateable;

    public int NameId;
    public int DescriptionId;

    public int IconId;

    // Ability experience (progress toward the ability's own rank). (Was Unknown6.)
    public int Experience;

    // Ability rank / level. The active job's entry drives the on-screen job XP bar.
    public int Level;

    // XP into the current rank (bar fill). Client field "RankExperience".
    public int Progress;

    // XP needed for the current rank (bar denominator). Client field "RankMaxExperience".
    public int TotalForLevel;

    // Player job level at which this unlocks. The Traits section shows a lock + "Unlocked at level N"
    // until the active profile's rank reaches it. 0 = always available. (Was Unknown10.)
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
