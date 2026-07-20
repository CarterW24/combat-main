using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

public static class JobWeaponAbilities
{
    public static bool HasKit(Player player) => JobKits.Has(player.ActiveProfileId);

    public static AbilityPacketSetDefinition? BuildToolbar(Player player, IResourceManager resources) =>
        JobKits.Active(player)?.BuildToolbar(player, resources);

    public static WeaponAbility ResolveAbility(Player player, int slot) =>
        JobKits.Active(player)?.ResolveAbility(player, slot) ?? NinjaWeaponAbilities.ResolveAbility(player, slot);

    public static (int NameId, int DescId, int IconId)? ResolveAbilityDefinition(Player player, int abilityDefId) =>
        JobKits.Active(player)?.ResolveDefinition(player, abilityDefId);

    public static float AutoTargetReach(Player player) => JobKits.Active(player)?.AutoTargetReach ?? 7f;

    public static bool SendToolbarWithFxPreload(Player player, IResourceManager resources)
    {
        var toolbar = BuildToolbar(player, resources);
        if (toolbar is null)
            return false;

        PreloadAbilityDefinitions(player);
        player.SendTunneled(toolbar);
        PreloadAbilityEffects(player);
        return true;
    }

    public static void PreloadAbilityDefinitions(Player player)
    {
        var kit = JobKits.Active(player);
        if (kit is null)
            return;

        foreach (var defId in kit.SlotAbilityDefIds)
        {
            var def = kit.ResolveDefinition(player, defId);
            if (def is null)
                continue;

            player.SendTunneled(new AbilityPacketAbilityDefinition
            {
                AbilityId = defId,
                NameId = def.Value.NameId,
                DescriptionId = def.Value.DescId,
                IconId = def.Value.IconId,
            });
        }
    }

    public static void PreloadAbilityEffects(Player player)
    {
        var ids = new HashSet<int>();
        for (var slot = 0; slot <= 1; slot++)
        {
            var ability = ResolveAbility(player, slot);
            ids.Add(ability.EffectId);
            if (ability.CastEffectStopMs == 0)
                ids.Add(ability.CastEffectId);
            ids.Add(ability.CasterEndEffectId);
            ids.Add(ability.EnemyExtraEffectId);
            ids.Add(ability.SwordEffectId);
        }

        var warmPos = new Vector4(player.Position.X, player.Position.Y - 400f, player.Position.Z, 1f);

        foreach (var id in ids)
        {
            if (id <= 0)
                continue;

            player.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = 0,
                CompositeEffectId = id,
                Position = warmPos,
            });
        }
    }

    public static AbilityPacketSetDefinition BuildToolbar(Player player, IResourceManager resources,
        Sanctuary.Packet.Common.Ability? heldPowerup)
    {
        var def = BuildToolbar(player, resources)
                  ?? new AbilityPacketSetDefinition { ProfileId = player.ActiveProfileId };

        // The held powerup rides the toolbar PINNED at slot index 2 (the "3" key).
        if (heldPowerup is not null)
            def.AbilitySet.Abilities[2] = heldPowerup;

        return def;
    }

    public static Sanctuary.Packet.Common.Ability MakePowerupSlot(int iconId, int nameId) => new()
    {
        Type = 3,
        Unknown2 = NinjaWeaponAbilities.MeleeAbilityDefId,
        ManaCost = 0,
        IconId = iconId,
        NameId = nameId,
        Unknown7 = 4,
        Unknown9 = 1,
        AbilityDefinitionId = NinjaWeaponAbilities.MeleeAbilityDefId,
        ForceDismount = true,
    };
}
