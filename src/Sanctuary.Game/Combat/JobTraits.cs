using System.Collections.Generic;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

public static class JobTraits
{
    public readonly record struct Trait(int NameId, int DescId, int IconId, int Level);

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
