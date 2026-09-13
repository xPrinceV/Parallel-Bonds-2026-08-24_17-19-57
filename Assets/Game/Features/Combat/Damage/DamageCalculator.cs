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
        // cached arrays and lists need no boxed interface enumerator on the attack path
        if (modifiers is IReadOnlyList<StatModifier> indexed)
        {
            for (int i = 0; i < indexed.Count; i++)
                Accumulate(indexed[i], ref totalPrefix, ref combinedMultiplier, ref totalPostfix);
        }
        else
        {
            foreach (StatModifier modifier in modifiers)
                Accumulate(modifier, ref totalPrefix, ref combinedMultiplier, ref totalPostfix);
        }

        // apply the requested formula without clamping or target defense calculation
        float finalDamage = (totalPrefix + weaponDamage) * combinedMultiplier + totalPostfix;
        if (!IsFinite(finalDamage))
            throw new OverflowException("Calculated damage exceeds the supported numeric range.");

        return finalDamage;
    }

    private static void Accumulate(StatModifier modifier, ref float prefix, ref float multiplier, ref float postfix)
    {
        // player stat ids must never be interpreted as weapon damage ids
        if (modifier.Target != ModifierTarget.Weapon || modifier.Stat != WeaponStatId.Damage)
            return;
        if (!IsFinite(modifier.Value))
            throw new ArgumentException("Damage modifier values must be finite.", "modifiers");

        switch (modifier.Type)
        {
            case ModifierType.Prefix:
                prefix += modifier.Value;
                break;
            case ModifierType.Multiplier:
                multiplier *= modifier.Value;
                break;
            case ModifierType.Postfix:
                postfix += modifier.Value;
                break;
            default:
                throw new ArgumentException("Unknown damage modifier type.", "modifiers");
        }
        if (!IsFinite(prefix) || !IsFinite(multiplier) || !IsFinite(postfix))
            throw new OverflowException("Damage modifiers exceed the supported numeric range.");
    }

    private static bool IsFinite(float value)
    {
        return !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
