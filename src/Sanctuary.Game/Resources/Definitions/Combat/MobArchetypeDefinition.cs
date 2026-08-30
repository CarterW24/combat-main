namespace Sanctuary.Game.Resources.Definitions.Combat;

public sealed class MobArchetypeDefinition
{
    public int Id { get; set; }

    public string Comment { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int NameId { get; set; }

    public int ModelId { get; set; }
    public string? TextureAlias { get; set; }
    public float Scale { get; set; } = 1f;

    public int MaxHealth { get; set; }
    public int AttackDamage { get; set; }
    public int AttackIntervalMs { get; set; } = 2000;

    public float AggroRange { get; set; } = 15f;
    public float LeashRange { get; set; } = 40f;
    public float AttackRange { get; set; } = 5f;
    public float Speed { get; set; } = 6f;

    public int AttackAnimationId { get; set; }
    public int AttackHitEffectId { get; set; }

    public bool ShowHealthBar { get; set; }
    public bool IsBoss { get; set; }

    public int RespawnSeconds { get; set; } = 30;
}
