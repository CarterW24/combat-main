using Sanctuary.Game.Entities;
using Sanctuary.Packet;

namespace Sanctuary.Game.Combat;

// COMBAT: the cross-job weapon-ability front door. FR abilities are granted by the EQUIPPED WEAPON, so
// resolution just checks each job's weapon table for the equipped item def id - the tables can't collide
// (disjoint item ids). The ability handler and every toolbar call site route through here so ninja,
// brawler and wizard all work without per-job wiring at the call sites.
public static class JobWeaponAbilities
{
    /// <summary>Slot 0 = the weapon's basic attack, slot 1 = its special. Bare hands = the ninja
    /// BareMelee strike (any job can punch).</summary>
    public static WeaponAbility ResolveAbility(Player player, int slot)
    {
        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return NinjaWeaponAbilities.BareMelee;

        return slot <= 0 ? weapon.Melee : weapon.Special;
    }

    public static NinjaWeapon? GetEquippedWeapon(Player player)
    {
        var defId = player.GetEquippedWeaponDefinitionId();
        if (defId == 0)
            return null;

        if (NinjaWeaponAbilities.ByWeaponDefId.TryGetValue(defId, out var w))
            return w;
        if (BrawlerWeaponAbilities.ByWeaponDefId.TryGetValue(defId, out w))
            return w;
        if (WizardWandAbilities.ByWeaponDefId.TryGetValue(defId, out w))
            return w;

        return null;
    }

    /// <summary>True for the profiles whose ability toolbar we populate from the equipped weapon.</summary>
    public static bool IsCombatProfile(int profileId) =>
        profileId is NinjaWeaponAbilities.NinjaProfileId
                  or BrawlerWeaponAbilities.BrawlerProfileId
                  or WizardWandAbilities.WizardProfileId;

    /// <summary>The 2-slot weapon toolbar for any combat profile. Ninja keeps its dedicated builder
    /// (icon-probe overrides etc.); brawler/wizard use the same proven slot-def ids.</summary>
    public static AbilityPacketSetDefinition BuildToolbar(Player player, IResourceManager resources)
    {
        if (player.ActiveProfileId == NinjaWeaponAbilities.NinjaProfileId)
            return NinjaWeaponAbilities.BuildToolbar(player, resources);

        var weapon = GetEquippedWeapon(player);

        if (weapon is null)
            return AbilityPacketSetDefinition.CreateEmpty(player.ActiveProfileId);

        var nameId = 0;
        if (resources.ClientItemDefinitions.TryGetValue(player.GetEquippedWeaponDefinitionId(), out var weaponDef))
            nameId = weaponDef.NameId;

        var def = new AbilityPacketSetDefinition { ProfileId = player.ActiveProfileId, SlotCount = 8 };

        def.Slots.Add(MakeSlot(NinjaWeaponAbilities.MeleeSlotDefId, weapon.Melee.IconImageId, nameId, manaCost: 0));
        def.Slots.Add(MakeSlot(NinjaWeaponAbilities.SpecialSlotDefId, weapon.Special.IconImageId, nameId,
            manaCost: NinjaWeaponAbilities.SpecialEnergyCost));

        return def;
    }

    private static AbilityPacketSetDefinition.Slot MakeSlot(int abilityDefId, int iconId, int nameId, int manaCost) => new()
    {
        Type = 3,
        Unknown2 = abilityDefId,
        ManaCost = manaCost,
        IconId = iconId,
        NameId = nameId,
        AbilityDefinitionId = abilityDefId,
    };

    /// <summary>Toolbar with a held powerup pinned at SLOT INDEX 2 — the "3" key. GROUND TRUTH (2026-07-12,
    /// 04-01 capture): the on-screen 1/2/3/4 keys are ability-bar (Id=1) slots 0-3 — every C2S StartAbility
    /// carries ActionBarData{Id=1, Slot=key-1}, and slots are POSITIONAL in this packet (no index field), so
    /// the powerup must sit at list index 2 behind Type-0 fillers if slots 0/1 are empty. op38/25 is the
    /// wrong bar entirely: all 84 capture samples target bar 2 = the consumable QUICK-ITEM bar (potions;
    /// in-encounter Frost Grenade / Frag Sphere rows).</summary>
    public static AbilityPacketSetDefinition BuildToolbar(Player player, IResourceManager resources,
        AbilityPacketSetDefinition.Slot? heldPowerup)
    {
        var def = BuildToolbar(player, resources);

        if (heldPowerup is not null)
        {
            while (def.Slots.Count < 2)
                def.Slots.Add(null); // Type-0 empty fillers keep the powerup at the "3" key
            if (def.Slots.Count == 2)
                def.Slots.Add(heldPowerup);
            else
                def.Slots[2] = heldPowerup;
        }

        return def;
    }

    /// <summary>A held-powerup toolbar slot (Flame Wave / Earth Shard / Super Shield). Reuses the
    /// proven-castable melee slot-def id so the client accepts the press (it sends only bar+slot index;
    /// the server routes ability-bar slot 2 to the arena's UseHeldPowerup).</summary>
    public static AbilityPacketSetDefinition.Slot MakePowerupSlot(int iconId, int nameId) =>
        MakeSlot(NinjaWeaponAbilities.MeleeSlotDefId, iconId, nameId, manaCost: 0);
}
