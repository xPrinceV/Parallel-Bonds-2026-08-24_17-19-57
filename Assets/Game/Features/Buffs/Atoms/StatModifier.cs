// declare the stat currently supported by damage atoms
public enum WeaponStatId
{
    Damage = 0,
    ProjectileCount = 1,
    ProjectileSpeed = 2,
    ProjectileRange = 3
}

// keep player stat ids separate from weapon stat ids
public enum PlayerStatId
{
    // health refers to maximum health, not immediate healing
    Health = 0,
    Armor = 1
}

// identify which stat namespace the modifier belongs to
public enum ModifierTarget
{
    Weapon = 0,
    Player = 1
}

// declare the calculation stage rather than the order of atoms in a buff
public enum ModifierType
{
    Prefix = 0,
    Multiplier = 1,
    Postfix = 2
}

// store modifier data without applying it to a weapon or shared config
public readonly struct StatModifier
{
    // read Stat for weapon modifiers and PlayerStat for player modifiers
    // Target must be checked because both enums can contain the same numeric id
    public ModifierTarget Target { get; }
    public WeaponStatId Stat { get; }
    public PlayerStatId PlayerStat { get; }
    public ModifierType Type { get; }
    public float Value { get; }

    public StatModifier(WeaponStatId stat, ModifierType type, float value)
    {
        Target = ModifierTarget.Weapon;
        Stat = stat;
        PlayerStat = default(PlayerStatId);
        Type = type;
        Value = value;
    }

    // preserve the existing weapon constructor while accepting player stat atoms
    public StatModifier(PlayerStatId stat, ModifierType type, float value)
    {
        Target = ModifierTarget.Player;
        Stat = default(WeaponStatId);
        PlayerStat = stat;
        Type = type;
        Value = value;
    }
}
