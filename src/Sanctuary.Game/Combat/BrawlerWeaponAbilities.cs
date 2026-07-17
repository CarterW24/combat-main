using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Combat;

public static class BrawlerWeaponAbilities
{
    public const int BrawlerProfileId = 43;

    private const int MeleeHitFx = 7;
    private const int MeleeAnimation = 1021;
    private const int MeleeIcon = 14407;

    public static readonly IReadOnlyDictionary<int, NinjaWeapon> ByWeaponDefId = new Dictionary<int, NinjaWeapon>
    {
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
