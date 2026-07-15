using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Combat;

// COMBAT: BRAWLER weapons, same weapon-driven pattern as the ninja Shadow Blades. Only ONE brawler
// ability is CONFIRMED in the team's OSFR Combat Spreadsheet so far - Enrage: anim 1001035, cast FX
// 16145 PFX_brawler_enrage_yellow_cast, persist FX 16147 (the lingering rage aura) - granted by the
// "of Rage" weapon (Brawler's Atlas Hammer of Rage 75058, matching the ninja "of X" naming). The rest
// of the brawler sheet is PENDING/UNKNOWN; add weapons here as rows get confirmed.
public static class BrawlerWeaponAbilities
{
    public const int BrawlerProfileId = 43;

    private const int MeleeHitFx = 7;        // PFX_Hit_Flash - generic until a brawler hit FX is confirmed
    private const int MeleeAnimation = 1021; // com_1hs_attack_01 - provisional swing (no confirmed hammer clip yet)
    private const int MeleeIcon = 14407;     // provisional; the hammer item art would need its image set cracked

    public static readonly IReadOnlyDictionary<int, NinjaWeapon> ByWeaponDefId = new Dictionary<int, NinjaWeapon>
    {
        // 75058 - Brawler's Atlas Hammer of Rage: Enrage (sheet CONFIRMED - anim 1001035, cast 16145,
        // persist 16147). A pure self dmg-buff: x2 ability damage for 15s (multiplier/duration provisional),
        // rage aura 16147 loops on the body for the duration.
        [75058] = new(
            new("Hammer Swing", MeleeIcon, 2609, MeleeAnimation, MeleeHitFx),
            new("Enrage", 11633, 0, 1001035, 0, 16145,
                BuffMultiplierPct: 200, BuffDurationMs: 15000, PersistEffectId: 16147)),
    };

    public static readonly int[] AllWeaponDefIds = ByWeaponDefId.Keys.ToArray();

    public static NinjaWeapon? GetEquippedWeapon(Player player)
    {
        var defId = player.GetEquippedWeaponDefinitionId();
        return defId != 0 && ByWeaponDefId.TryGetValue(defId, out var weapon) ? weapon : null;
    }
}
