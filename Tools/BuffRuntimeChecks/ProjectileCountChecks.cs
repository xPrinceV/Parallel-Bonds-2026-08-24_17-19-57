using System;

namespace ParallelBonds.BuffChecks
{
    internal static class ProjectileCountChecks
    {
        public static void Run(BuffCheckContext context)
        {
            var holder = context.CreateObject("owner").AddComponent<BuffController>();
            context.Check(holder.CalculateProjectileCount(3) == 3, "empty holder preserves base count");
            context.Check(holder.CalculateProjectileCount(int.MaxValue) == int.MaxValue,
                "maximum base count is preserved without float rounding");
            context.Check(Throws<ArgumentOutOfRangeException>(() => holder.CalculateProjectileCount(-1)),
                "negative base count rejected");
            var recipe = context.Recipe(
                Stat(context, ModifierType.Postfix, 0.9f),
                Stat(context, ModifierType.Multiplier, 1.5f),
                Stat(context, ModifierType.Prefix, 1f));
            holder.TryAddBuff(recipe);
            context.Check(holder.CalculateProjectileCount(2) == 5, "prefix then multiplier then postfix followed by floor");
            var more = context.Recipe(Stat(context, ModifierType.Multiplier, 2f));
            holder.TryAddBuff(more);
            context.Check(holder.CalculateProjectileCount(2) == 9, "independent count multipliers multiply");
            holder.RemoveBuff(more);
            holder.RemoveBuff(recipe);
            var sameRecipe = context.Recipe(Stat(context, ModifierType.Multiplier, 1.5f),
                Stat(context, ModifierType.Multiplier, 2f));
            holder.TryAddBuff(sameRecipe);
            context.Check(holder.CalculateProjectileCount(2) == 6, "same-buff count multipliers still multiply");
            holder.RemoveBuff(sameRecipe);
            var reduction = context.Recipe(Stat(context, ModifierType.Postfix, -100f));
            holder.TryAddBuff(reduction);
            context.Check(holder.CalculateProjectileCount(1) == 0, "negative final count clamps to zero");
            holder.RemoveBuff(reduction);
            var fractional = context.Recipe(Stat(context, ModifierType.Postfix, 0.75f));
            holder.TryAddBuff(fractional);
            context.Check(holder.CalculateProjectileCount(int.MaxValue) == int.MaxValue,
                "floor occurs before integer range validation");
            holder.RemoveBuff(fractional);
            var overflow = context.Recipe(Stat(context, ModifierType.Prefix, 1f));
            holder.TryAddBuff(overflow);
            context.Check(Throws<OverflowException>(() => holder.CalculateProjectileCount(int.MaxValue)),
                "integer overflow rejected rather than saturated");
            holder.RemoveBuff(overflow);
            var unrelated = context.Recipe(new RawModifierAtom(
                new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, float.NaN),
                new StatModifier(PlayerStatId.Armor, ModifierType.Multiplier, float.PositiveInfinity),
                new StatModifier(WeaponStatId.ProjectileSpeed, ModifierType.Multiplier, float.NaN)));
            holder.TryAddBuff(unrelated);
            context.Check(holder.CalculateProjectileCount(2) == 2, "unrelated weapon and player stats are ignored");
            holder.RemoveBuff(unrelated);
            CheckInvalidValue(context, holder, float.NaN, "NaN count modifier rejected");
            CheckInvalidValue(context, holder, float.PositiveInfinity, "positive infinite count modifier rejected");
            CheckInvalidValue(context, holder, float.NegativeInfinity, "negative infinite count modifier rejected");
            var hugeModifiers = new StatModifier[10];
            for (int i = 0; i < hugeModifiers.Length; i++)
                hugeModifiers[i] = new StatModifier(WeaponStatId.ProjectileCount, ModifierType.Multiplier, float.MaxValue);
            var nonfiniteResult = context.Recipe(new RawModifierAtom(hugeModifiers));
            holder.TryAddBuff(nonfiniteResult);
            context.Check(Throws<OverflowException>(() => holder.CalculateProjectileCount(0)),
                "nonfinite intermediate product rejected even with zero base");
            holder.RemoveBuff(nonfiniteResult);
            var invalidStage = context.Recipe(new RawModifierAtom(
                new StatModifier(WeaponStatId.ProjectileCount, (ModifierType)99, 1f)));
            holder.TryAddBuff(invalidStage);
            context.Check(Throws<ArgumentOutOfRangeException>(() => holder.CalculateProjectileCount(1)),
                "undefined count stage rejected");
            holder.RemoveBuff(invalidStage);
            holder.TryAddBuff(context.Recipe(Stat(context, ModifierType.Prefix, 2f)));
            holder.enabled = false;
            context.Invoke(holder, "OnDisable");
            context.Check(holder.CalculateProjectileCount(2) == 2, "disabled holder contributes no projectile modifiers");
            context.Check(Throws<ArgumentOutOfRangeException>(() => holder.CalculateProjectileCount(-1)),
                "disabled holder still rejects negative base count");
        }

        private static StatsBuffAtom Stat(BuffCheckContext context, ModifierType type, float value)
        {
            var atom = new StatsBuffAtom();
            context.Set(atom, "stat", WeaponStatId.ProjectileCount);
            context.Set(atom, "modifierType", type);
            context.Set(atom, "value", value);
            return atom;
        }

        private static void CheckInvalidValue(BuffCheckContext context, BuffController holder, float value, string name)
        {
            // A test-only atom bypasses recipe validation to exercise the consumer's defensive checks.
            var recipe = context.Recipe(new RawModifierAtom(
                new StatModifier(WeaponStatId.ProjectileCount, ModifierType.Prefix, value)));
            holder.TryAddBuff(recipe);
            context.Check(Throws<ArgumentOutOfRangeException>(() => holder.CalculateProjectileCount(1)), name);
            holder.RemoveBuff(recipe);
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return true;
            }
            return false;
        }

        private sealed class RawModifierAtom : BuffAtom
        {
            private readonly StatModifier[] modifiers;

            public RawModifierAtom(params StatModifier[] modifiers)
            {
                this.modifiers = modifiers;
            }

            public override bool isValid => true;
            public override StatModifier[] CreateModifiers() => modifiers;
        }
    }
}
