using System.Collections.Generic;

namespace Sanctuary.Game.Resources.Definitions.Combat;

public sealed class AbilityProjectileDefinition
{
    public string ModelFileName { get; set; } = "arrow_projectile_magical.adr";
    public float Speed { get; set; } = 20f;
    public int TrailEffectId { get; set; }
    public float MuzzleHeight { get; set; } = 1.6f;
}

public sealed class AbilityHealDefinition
{
    public int Amount { get; set; }
    public int PercentOfDamage { get; set; }
    public float Radius { get; set; } = 10f;
    public string Scope { get; set; } = "Party";
}

public sealed class AbilityEnergyStealDefinition
{
    public int Amount { get; set; }
}

public sealed class AbilityDotDefinition
{
    public int TickDamage { get; set; }
    public int TickMs { get; set; }
    public int DurationMs { get; set; }
}

public sealed class AbilitySummonDefinition
{
    public int ModelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public int LifetimeMs { get; set; }
    public int SpawnEffectId { get; set; }
    public float MoveSpeed { get; set; } = 6.25f;
    public float AttackRange { get; set; } = 2.5f;
    public int WieldType { get; set; }
    public int AttackAnimationId { get; set; }
    public int AttackDamage { get; set; }
    public int AttackCooldownMs { get; set; } = 1400;
    public int HitEffectId { get; set; }
    public int RunAnimationId { get; set; }
}

public sealed class AbilityDefinition
{
    public int Id { get; set; }

    public string Comment { get; set; } = string.Empty;

    public string EffectType { get; set; } = "SweepDamage";

    public int Damage { get; set; }
    public Dictionary<int, int>? DamageByLevel { get; set; }
    public int HitCount { get; set; }
    public float AoeRadius { get; set; }
    public int EnergyCost { get; set; }

    public int AnimationId { get; set; }
    public int HitEffectId { get; set; }
    public int CastEffectId { get; set; }
    public int CastEffectStopMs { get; set; }
    public int CasterEndEffectId { get; set; }
    public int EnemyExtraEffectId { get; set; }

    public int WeaponEffectId { get; set; }
    public int WeaponEffectDurationMs { get; set; } = 10000;

    public int TargetAnimationId { get; set; }
    public int TargetEffectDurationMs { get; set; }
    public int ContactEffectId { get; set; }

    public int NameId { get; set; }
    public int DescriptionId { get; set; }
    public int IconId { get; set; }

    public AbilityProjectileDefinition? Projectile { get; set; }
    public AbilityHealDefinition? Heal { get; set; }
    public AbilityEnergyStealDefinition? EnergySteal { get; set; }
    public AbilityDotDefinition? Dot { get; set; }
    public AbilitySummonDefinition? Summon { get; set; }
}
