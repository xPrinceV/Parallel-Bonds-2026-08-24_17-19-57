using System;
using UnityEngine;

// abstract definition a buff atom 
// this definition only declares that an atom will held a stat modifier to the game
// it is not tied to any specific buff type or source
// the structure is flexible and can be used to define any type of buff atom
[Serializable]
public abstract class BuffAtom
{
    public abstract bool isValid { get; }

    // describe modifiers only; the calculator controls their evaluation order
    public abstract StatModifier[] CreateModifiers();

    protected static bool IsFinite(float value)
    {
        return !float.IsInfinity(value) && !float.IsNaN(value);
    }
}

// identify numeric effects without coupling activation to concrete atom types
[Serializable]
public abstract class StatBuffAtom : BuffAtom
{
}

// detailed definition of each buff atom type
// when creating a buff, use the atoms that declared in this file
// never create a buff directly without using the buff atom types declared in this file
// 

[Serializable]
public class DamageBuffAtom : StatBuffAtom
{
    [SerializeField]
    private float damageMultiplier = 1f;
    [SerializeField]
    private float damagePrefix = 0f;
    [SerializeField]
    private float damagePostfix = 0f;

    public override bool isValid =>
        IsFinite(damageMultiplier) && IsFinite(damagePrefix) && IsFinite(damagePostfix);

    public override StatModifier[] CreateModifiers()
    {
        if (!isValid)
            throw new InvalidOperationException("Damage atom parameters must be finite.");

        // keep each stage separate instead of calculating or changing weapon damage
        return new[]
        {
            new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, damagePrefix),
            new StatModifier(WeaponStatId.Damage, ModifierType.Multiplier, damageMultiplier),
            new StatModifier(WeaponStatId.Damage, ModifierType.Postfix, damagePostfix)
        };
    }
}

[Serializable]
public class ProjectileBuffAtom : StatBuffAtom
{
    // add extra projectiles before count multipliers; zero leaves the base count unchanged
    [SerializeField]
    private int projectileCount = 0;
    // speed and travel range are multipliers, not absolute values
    [SerializeField]
    private float projectileSpeed = 1f;
    [SerializeField]
    private float projectileRange = 1f;
    
    // integer counts are always finite; final count limits belong to the stat calculator
    public override bool isValid => IsFinite(projectileSpeed) && IsFinite(projectileRange);
    
    public override StatModifier[] CreateModifiers()
    {
        if (!isValid)
            throw new InvalidOperationException("Projectile atom parameters must be finite.");
        
        return new[]
        {
            new StatModifier(WeaponStatId.ProjectileCount, ModifierType.Prefix, projectileCount),
            new StatModifier(WeaponStatId.ProjectileSpeed, ModifierType.Multiplier, projectileSpeed),
            new StatModifier(WeaponStatId.ProjectileRange, ModifierType.Multiplier, projectileRange)
        };
    }
}

[Serializable]
public class HealthBuffAtom : StatBuffAtom
{
    // modify maximum health; healing and current health adjustment belong to the health system
    [SerializeField]
    private float healthIncrease = 0f;
    
    public override bool isValid => IsFinite(healthIncrease);

    public override StatModifier[] CreateModifiers()
    {
        if (!isValid)
            throw new InvalidOperationException("Health atom parameters must be finite.");
        
        return new[]
        {
            new StatModifier(PlayerStatId.Health, ModifierType.Prefix, healthIncrease)
        };
    }
}

[Serializable]
public class ArmorBuffAtom : StatBuffAtom
{
    // add armor without deciding how armor reduces incoming damage
    [SerializeField]
    private float armorIncrease = 0f;
    
    public override bool isValid => IsFinite(armorIncrease);

    public override StatModifier[] CreateModifiers()
    {
        if (!isValid)
            throw new InvalidOperationException("Armor atom parameters must be finite.");
        
        return new[]
        {
            new StatModifier(PlayerStatId.Armor, ModifierType.Prefix, armorIncrease)
        };
    }
}

[Serializable]
public class StatsBuffAtom : StatBuffAtom
{
    // use this template for a single weapon stat modification
    [SerializeField]
    private WeaponStatId stat = WeaponStatId.Damage;
    [SerializeField]
    private ModifierType modifierType = ModifierType.Prefix;
    [SerializeField]
    private float value = 0f;

    // reject undefined ids and invalid values before exporting the modifier
    public override bool isValid =>
        Enum.IsDefined(typeof(WeaponStatId), stat)
        && Enum.IsDefined(typeof(ModifierType), modifierType)
        && IsFinite(value);

    public override StatModifier[] CreateModifiers()
    {
        if (!isValid)
            throw new InvalidOperationException("Stat atom requires valid ids and a finite value.");

        // multiplier values use 1 as neutral; prefix and postfix values use 0
        return new[]
        {
            new StatModifier(stat, modifierType, value)
        };
    }
}
