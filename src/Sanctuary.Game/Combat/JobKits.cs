using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Combat;

public static class JobKits
{
    private static readonly Dictionary<int, IJobKit> ByProfileId = new IJobKit[]
    {
        new NinjaJobKit(),
        new ArcherJobKit(),
        new WizardJobKit(),
        new BrawlerJobKit(),
        new WarriorJobKit(),
    }.ToDictionary(kit => kit.ProfileId);

    public static IEnumerable<IJobKit> All => ByProfileId.Values;

    public static IJobKit? For(int profileId) => ByProfileId.GetValueOrDefault(profileId);

    public static IJobKit? Active(Player player) => For(player.ActiveProfileId);

    public static bool Has(int profileId) => ByProfileId.ContainsKey(profileId);

    public static void ConfigureAbilitiesScreen(IJobKit kit, ClientPcProfile profile, int equippedWeaponDefId)
    {
        var traits = kit.BuildTraitEntries(profile.Rank);
        if (traits is null)
            return;

        profile.AbilityExperiences = traits;

        var (basicName, _, basicIcon) = kit.SlotNameIcon(equippedWeaponDefId, 0);
        var (specialName, _, specialIcon) = kit.SlotNameIcon(equippedWeaponDefId, 1);

        if (basicName == 0)
            return;

        for (var i = 0; i < profile.AbilitySet.Abilities.Length; i++)
            profile.AbilitySet.Abilities[i] = Ability.Empty;

        profile.AbilitySet.Abilities[0] = new Ability { Type = 3, IconId = basicIcon, NameId = basicName, AbilityDefinitionId = kit.SlotAbilityDefIds[0] };
        profile.AbilitySet.Abilities[1] = new Ability { Type = 3, IconId = specialIcon, NameId = specialName, AbilityDefinitionId = kit.SlotAbilityDefIds[1] };
    }
}
