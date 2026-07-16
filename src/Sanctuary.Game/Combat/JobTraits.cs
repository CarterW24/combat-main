using System.Collections.Generic;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

// Shared builder for a job's passive-trait list (the AbilitiesScreen Traits section). A kit just declares its
// four traits and calls Build.
public static class JobTraits
{
    // One trait row: the client name/desc/icon ids + the job level it unlocks at.
    public readonly record struct Trait(int NameId, int DescId, int IconId, int Level);

    // Passive AbilityExperience entries for a rank, ending with the Present=0 terminator.
    // Present must be distinct + non-zero (we use NameId) or the client crashes on connect. The padlock is off
    // when the entry's Level (rank) is > 0 — so 1 once the job level reaches the trait's unlock, else 0 (locked);
    // RequiredLevel is just the "Unlocked at level N" caption.
    public static List<AbilityExperience> Build(IReadOnlyList<Trait> traits, int rank)
    {
        var list = new List<AbilityExperience>(traits.Count + 1);
        foreach (var t in traits)
            list.Add(new AbilityExperience
            {
                Present = t.NameId,
                IsActivateable = false,
                NameId = t.NameId,
                DescriptionId = t.DescId,
                IconId = t.IconId,
                Level = rank >= t.Level ? 1 : 0,
                RequiredLevel = t.Level,
            });
        list.Add(new AbilityExperience { Present = 0 });
        return list;
    }
}
