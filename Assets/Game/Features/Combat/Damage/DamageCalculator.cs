using System;
using System.Collections.Generic;

public class DamageCalculator
{
    // calculate attack damage without changing weapon stats or buff definitions
    public static float CalculateDamage(float weaponDamage, IEnumerable<StatModifier> modifiers)
    {
        if (!IsFinite(weaponDamage))
            throw new ArgumentOutOfRangeException(nameof(weaponDamage), "Weapon damage must be finite.");
        if (modifiers == null)
            throw new ArgumentNullException(nameof(modifiers));

        float totalPrefix = 0f;
        float combinedMultiplier = 1f;
        float totalPostfix = 0f;

        // group modifiers by stage so postfix never receives an attack multiplier
        foreach (StatModifier modifier in modifiers)
        {
            // player stat ids must never be interpreted as weapon damage ids
            if (modifier.Target != ModifierTarget.Weapon || modifier.Stat != WeaponStatId.Damage)
                continue;
            if (!IsFinite(modifier.Value))
                throw new ArgumentException("Damage modifier values must be finite.", nameof(modifiers));

            switch (modifier.Type)
            {
                case ModifierType.Prefix:
                    totalPrefix += modifier.Value;
                    break;
                case ModifierType.Multiplier:
                    combinedMultiplier *= modifier.Value;
                    break;
                case ModifierType.Postfix:
                    totalPostfix += modifier.Value;
                    break;
                default:
                    throw new ArgumentException("Unknown damage modifier type.", nameof(modifiers));
            }

            if (!IsFinite(totalPrefix) || !IsFinite(combinedMultiplier) || !IsFinite(totalPostfix))
                throw new OverflowException("Damage modifiers exceed the supported numeric range.");
        }

        // apply the requested formula without clamping or target defense calculation
        float finalDamage = (totalPrefix + weaponDamage) * combinedMultiplier + totalPostfix;
        if (!IsFinite(finalDamage))
            throw new OverflowException("Calculated damage exceeds the supported numeric range.");

        return finalDamage;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
