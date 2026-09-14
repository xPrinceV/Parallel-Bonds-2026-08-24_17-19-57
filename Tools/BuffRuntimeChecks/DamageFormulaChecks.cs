using System;

namespace ParallelBonds.BuffChecks
{
    // test final buff contributions without depending on scene objects or atom implementation
    internal static class DamageFormulaChecks
    {
        public static int Run()
        {
            int checks = 0;
            Action<string, float, float, StatModifier[]> check = (name, expected, basis, modifiers) =>
            {
                float actual = DamageCalculator.CalculateDamage(basis, modifiers);
                // decimal multipliers cannot always be represented exactly by a float
                if (float.IsNaN(actual) || float.IsInfinity(actual) || Math.Abs(actual - expected) > 0.0001f)
                    throw new InvalidOperationException("Failed [Damage formula]: " + name
                        + "; expected " + expected + ", actual " + actual);
                checks++;
            };

            check("no modifiers preserve base damage", 20f, 20f, Array.Empty<StatModifier>());
            check("neutral modifiers preserve base damage", 20f, 20f, new[]
            {
                Damage(ModifierType.Prefix, 0f),
                Damage(ModifierType.Multiplier, 1f),
                Damage(ModifierType.Postfix, 0f)
            });
            check("prefix values add before multiplication", 56f, 20f, new[]
            {
                Damage(ModifierType.Prefix, 5f),
                Damage(ModifierType.Prefix, 3f),
                Damage(ModifierType.Multiplier, 2f)
            });
            check("postfix values add without multiplication", 47f, 20f, new[]
            {
                Damage(ModifierType.Postfix, 4f),
                Damage(ModifierType.Multiplier, 2f),
                Damage(ModifierType.Postfix, 3f)
            });

            var example = new[]
            {
                Damage(ModifierType.Prefix, 5f),
                Damage(ModifierType.Prefix, 3f),
                Damage(ModifierType.Multiplier, 1.2f),
                Damage(ModifierType.Multiplier, 1.5f),
                Damage(ModifierType.Postfix, 4f)
            };
            check("documented formula gives 54.4", 54.4f, 20f, example);
            Array.Reverse(example);
            check("input order does not change calculation stages", 54.4f, 20f, example);

            check("independent twenty percent buffs multiply", 28.8f, 20f, new[]
            {
                Damage(ModifierType.Multiplier, 1.2f),
                Damage(ModifierType.Multiplier, 1.2f)
            });
            check("twenty percent reduction uses 0.8", 16f, 20f, new[]
            {
                Damage(ModifierType.Multiplier, 0.8f)
            });
            check("zero multiplier does not remove postfix", 4f, 20f, new[]
            {
                Damage(ModifierType.Multiplier, 0f),
                Damage(ModifierType.Postfix, 4f)
            });

            // these inputs represent one buff's resolved output, not its internal atoms
            var buffOutput = new[] { Damage(ModifierType.Multiplier, 1.2f) };
            check("base buff contribution is 1.2", 24f, 20f, buffOutput);
            buffOutput[0] = Damage(ModifierType.Multiplier, 1.3f);
            check("upgraded buff replaces its contribution with 1.3", 26f, 20f, buffOutput);
            check("resolved buff multiplies with an independent buff", 39f, 20f, new[]
            {
                buffOutput[0],
                Damage(ModifierType.Multiplier, 1.5f)
            });
            check("other stats do not enter damage calculation", 26f, 20f, new[]
            {
                buffOutput[0],
                new StatModifier(WeaponStatId.ProjectileCount, ModifierType.Prefix, 1f),
                new StatModifier(PlayerStatId.Health, ModifierType.Prefix, 100f),
                new StatModifier(PlayerStatId.Armor, ModifierType.Multiplier, 2f)
            });

            return checks;
        }

        private static StatModifier Damage(ModifierType type, float value)
        {
            return new StatModifier(WeaponStatId.Damage, type, value);
        }
    }
}
