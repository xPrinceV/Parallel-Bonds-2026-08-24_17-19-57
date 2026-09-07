using System;
using System.Collections.Generic;

namespace ParallelBonds.BuffChecks
{
    internal static class InternalActivationChecks
    {
        public static void Run(BuffCheckContext context)
        {
            var holder = context.CreateObject("owner").AddComponent<BuffController>();
            var other = context.CreateObject("other owner").AddComponent<BuffController>();
            var target = context.CreateObject("target");
            var baseline = Damage(context, 1.2f);
            var bonus = Damage(context, 1.1f);
            var projectile = new ProjectileBuffAtom();
            context.Set(projectile, "projectileCount", 1);
            var effects = new List<BuffAtom> { bonus, projectile };
            var action = new ActivateBuffEffects();
            context.Set(action, "effects", effects);
            var trigger = Trigger(context, action);
            var recipe = context.Recipe(baseline, trigger);
            context.Check(recipe.isValid && holder.TryAddBuff(recipe) && other.TryAddBuff(recipe),
                "same recipe can be granted to two holders");
            var instance = holder.FindBuff(recipe);
            var second = other.FindBuff(recipe);
            var stack = instance.StackInstances[0];
            context.Check(Near(holder.CalculateWeaponDamage(20f), 24f) && holder.CalculateProjectileCount(1) == 1,
                "baseline is 1.2 with no extra projectile");
            holder.ReportHit(target, 0f);
            holder.ReportHit(target, float.PositiveInfinity);
            holder.ReportHit(holder.Owner, 10f);
            context.Check(stack.CurrentStacks == 0, "invalid hits do not advance internal activation");
            Hit(holder, target, 4);
            context.Check(stack.CurrentStacks == 4 && !stack.HasTriggered
                && Near(holder.CalculateWeaponDamage(20f), 24f) && holder.CalculateProjectileCount(1) == 1,
                "four hits preserve baseline");
            Hit(holder, target, 1);
            context.Check(stack.HasTriggered && stack.CurrentStacks == 5
                && Near(holder.CalculateWeaponDamage(20f), 26f) && holder.CalculateProjectileCount(1) == 2,
                "fifth hit activates 1.3 and one extra projectile");
            int damageMultipliers = 0;
            float resolvedMultiplier = 0f;
            foreach (StatModifier modifier in instance.Modifiers)
                if (modifier.Target == ModifierTarget.Weapon && modifier.Stat == WeaponStatId.Damage
                    && modifier.Type == ModifierType.Multiplier)
                {
                    damageMultipliers++;
                    resolvedMultiplier = modifier.Value;
                }
            context.Check(damageMultipliers == 1 && Near(resolvedMultiplier, 1.3f),
                "instance exports one resolved damage multiplier");
            Hit(holder, target, 10);
            context.Check(stack.CurrentStacks == 5 && Near(holder.CalculateWeaponDamage(20f), 26f)
                && holder.CalculateProjectileCount(1) == 2, "trigger once never repeats");
            context.Check(!action.TryActivate(new BuffActivationContext(holder, recipe, instance))
                && holder.CalculateProjectileCount(1) == 2, "instance rejects duplicate internal activation");
            context.Check(second.StackInstances[0].CurrentStacks == 0 && !second.StackInstances[0].HasTriggered
                && Near(other.CalculateWeaponDamage(20f), 24f) && other.CalculateProjectileCount(1) == 1,
                "shared recipe does not share runtime progress or contributions");
            context.Check(recipe.AtomCount == 2 && ReferenceEquals(recipe.GetAtom(0), baseline)
                && ReferenceEquals(recipe.GetAtom(1), trigger) && ReferenceEquals(trigger.Activation, action)
                && trigger.RequiredStackCount == 5 && effects.Count == 2
                && ReferenceEquals(effects[0], bonus) && ReferenceEquals(effects[1], projectile)
                && Near(baseline.CreateModifiers()[1].Value, 1.2f)
                && Near(bonus.CreateModifiers()[1].Value, 1.1f) && projectile.CreateModifiers()[0].Value == 1f,
                "activation leaves recipe references and actual multiplier fields unchanged");
            context.Check(holder.TryAddBuff(context.DamageReward())
                && Near(holder.CalculateWeaponDamage(20f), 39f), "independent 1.5 buff still multiplies to 39");
            holder.Tick(3f);
            context.Check(holder.TryAddBuff(recipe) && ReferenceEquals(instance, holder.FindBuff(recipe))
                && instance.RemainingDuration == 10f && stack.HasTriggered
                && Near(holder.CalculateWeaponDamage(20f), 39f) && holder.CalculateProjectileCount(1) == 2,
                "refresh preserves activated effects without duplication");
            context.Check(holder.RemoveBuff(recipe) && !instance.IsActive && instance.Modifiers.Count == 0
                && Near(holder.CalculateWeaponDamage(20f), 30f) && holder.CalculateProjectileCount(1) == 1,
                "removal ends baseline and internal contributions together");
            context.Check(!action.TryActivate(new BuffActivationContext(holder, recipe, instance)),
                "removed source cannot reactivate");
            holder.TryAddBuff(recipe);
            var replacement = holder.FindBuff(recipe);
            context.Check(!ReferenceEquals(instance, replacement) && !replacement.StackInstances[0].HasTriggered
                && Near(holder.CalculateWeaponDamage(20f), 36f) && holder.CalculateProjectileCount(1) == 1,
                "regrant after removal starts with fresh internal state");
            Hit(other, target, 5);
            context.Check(Near(other.CalculateWeaponDamage(20f), 26f) && other.CalculateProjectileCount(1) == 2,
                "second instance can activate the same action independently");
            other.Tick(10f);
            context.Check(!second.IsActive && second.Modifiers.Count == 0 && other.FindBuff(recipe) == null
                && other.CalculateWeaponDamage(20f) == 20f && other.CalculateProjectileCount(1) == 1,
                "expiry ends all internal contributions");

            CheckValidationAndRetry(context, holder, target);
            CheckAggregation(context);
            CheckStatAtomContract(context);
            CheckConstructionFailure(context);
        }

        private static void CheckValidationAndRetry(BuffCheckContext context, BuffController holder,
            UnityEngine.GameObject target)
        {
            var action = new ActivateBuffEffects();
            context.Check(!action.isValid, "empty effects rejected");
            context.Set(action, "effects", null);
            context.Check(!action.isValid, "null effects rejected");
            context.Set(action, "effects", new List<BuffAtom> { null });
            context.Check(!action.isValid, "null effect rejected");
            var nested = Trigger(context, action);
            context.Set(action, "effects", new List<BuffAtom> { nested });
            context.Check(!action.isValid, "nested cyclic stack rejected without recursive validation");
            var bonus = Damage(context, 1.1f);
            context.Set(action, "effects", new List<BuffAtom> { bonus });
            var trigger = Trigger(context, action);
            var recipe = context.Recipe(trigger);
            var instance = new BuffInstance(recipe, holder);
            var hit = new StackEventContext(holder.Owner, target, 10f);
            var legacyStack = new StackBuffInstance(trigger);
            for (int i = 0; i < 6; i++) legacyStack.ProcessEvent(hit, holder, recipe);
            context.Check(legacyStack.CurrentStacks == 6 && !legacyStack.HasTriggered
                && !action.TryActivate(new BuffActivationContext(holder, recipe)),
                "legacy context without source retains failed activation stacks");
            for (int i = 0; i < 4; i++) instance.ProcessEvent(hit);
            context.Set(bonus, "damageMultiplier", float.NaN);
            instance.ProcessEvent(hit);
            context.Check(!action.isValid && instance.StackInstances[0].CurrentStacks == 4
                && !instance.StackInstances[0].HasTriggered && instance.Modifiers.Count == 0,
                "invalid internal action retains existing stacks and publishes nothing");
            context.Set(bonus, "damageMultiplier", 1.1f);
            instance.ProcessEvent(hit);
            context.Check(instance.StackInstances[0].HasTriggered && instance.Modifiers.Count == 3,
                "corrected internal action can activate from retained stacks");

            var huge = Damage(context, float.MaxValue);
            var projectile = new ProjectileBuffAtom();
            context.Set(projectile, "projectileCount", 1);
            var overflowAction = new ActivateBuffEffects();
            context.Set(overflowAction, "effects", new List<BuffAtom> { projectile, huge });
            var overflowRecipe = context.Recipe(Damage(context, float.MaxValue), Trigger(context, overflowAction));
            var overflow = new BuffInstance(overflowRecipe, holder);
            for (int i = 0; i < 6; i++) overflow.ProcessEvent(hit);
            context.Check(overflow.StackInstances[0].CurrentStacks == 6 && !overflow.StackInstances[0].HasTriggered
                && overflow.Modifiers.Count == 3, "failed aggregate retains all stacks and rolls back every effect");
            context.Set(huge, "damageMultiplier", 1f);
            overflow.ProcessEvent(hit);
            context.Check(overflow.StackInstances[0].HasTriggered && overflow.Modifiers.Count == 8,
                "failed activation is not marked as deduplicated and can retry");
            var wrongReceiver = new RecordingBuffReceiver { Owner = holder.Owner };
            context.Check(!action.TryActivate(new BuffActivationContext(wrongReceiver, recipe, new BuffInstance(recipe, holder)))
                && !action.TryActivate(new BuffActivationContext(holder, overflowRecipe, new BuffInstance(recipe, holder))),
                "mismatched receiver or recipe cannot activate a source instance");
        }

        private static void CheckAggregation(BuffCheckContext context)
        {
            var holder = context.CreateObject("aggregation").AddComponent<BuffController>();
            var first = Damage(context, 1.2f);
            context.Set(first, "damagePrefix", 5f);
            context.Set(first, "damagePostfix", 4f);
            var second = Damage(context, 1.1f);
            context.Set(second, "damagePrefix", 3f);
            context.Set(second, "damagePostfix", 2f);
            var stats = new StatsBuffAtom();
            context.Set(stats, "modifierType", ModifierType.Multiplier);
            context.Set(stats, "value", 0.9f);
            holder.TryAddBuff(context.Recipe(first, second, stats));
            context.Check(Near(holder.CalculateWeaponDamage(20f), 39.6f),
                "all weapon damage multipliers including Stats atoms add bonuses; prefixes and postfixes still sum");
            var speedA = new ProjectileBuffAtom();
            var speedB = new ProjectileBuffAtom();
            context.Set(speedA, "projectileSpeed", 1.2f);
            context.Set(speedB, "projectileSpeed", 1.1f);
            var recipe = context.Recipe(speedA, speedB);
            holder.TryAddBuff(recipe);
            int count = 0;
            float product = 1f;
            foreach (StatModifier modifier in holder.FindBuff(recipe).Modifiers)
                if (modifier.Stat == WeaponStatId.ProjectileSpeed && modifier.Type == ModifierType.Multiplier)
                {
                    count++;
                    product *= modifier.Value;
                }
            context.Check(count == 2 && Near(product, 1.32f), "non-damage multipliers remain separate and multiplicative");
        }

        private static void CheckStatAtomContract(BuffCheckContext context)
        {
            var action = new ActivateBuffEffects();
            context.Set(action, "effects", new List<BuffAtom>
            {
                new DamageBuffAtom(), new ProjectileBuffAtom(), new HealthBuffAtom(),
                new ArmorBuffAtom(), new StatsBuffAtom()
            });
            context.Check(action.isValid, "all existing numeric atoms satisfy the stat atom contract");

            var custom = new CustomStatAtom();
            context.Set(action, "effects", new List<BuffAtom> { custom, new DerivedDamageAtom() });
            context.Check(action.isValid, "new stat atoms and subclasses are accepted without a type whitelist");
            var holder = context.CreateObject("custom stat owner").AddComponent<BuffController>();
            var target = context.CreateObject("custom stat target");
            var recipe = context.Recipe(Damage(context, 1.2f), Trigger(context, action));
            holder.TryAddBuff(recipe);
            Hit(holder, target, 5);
            context.Check(holder.FindBuff(recipe).StackInstances[0].HasTriggered
                && Near(holder.CalculateWeaponDamage(20f), 24f) && holder.CalculateProjectileCount(1) == 3,
                "custom numeric effects activate through the source instance");

            custom.Value = float.NaN;
            context.Check(!action.isValid, "custom stat atoms still require valid numeric configuration");
            context.Set(action, "effects", new List<BuffAtom> { new NonStatAtom() });
            context.Check(!action.isValid, "plain BuffAtom effects are rejected even when valid");
        }

        private static void CheckConstructionFailure(BuffCheckContext context)
        {
            var holder = context.CreateObject("construction failure owner").AddComponent<BuffController>();
            var target = context.CreateObject("construction failure target");
            var action = new ActivateBuffEffects();
            context.Set(action, "effects", new List<BuffAtom> { Damage(context, 1.1f) });
            var existingRecipe = context.Recipe(Damage(context, 1.2f), Trigger(context, action));
            holder.TryAddBuff(existingRecipe);
            var existing = holder.FindBuff(existingRecipe);
            Hit(holder, target, 4);
            holder.Tick(3f);
            int modifierCount = holder.GetModifiers().Length;

            var overflowRecipe = context.Recipe(Damage(context, float.MaxValue), Damage(context, float.MaxValue));
            context.Check(overflowRecipe.isValid && !holder.TryAddBuff(overflowRecipe)
                && holder.FindBuff(overflowRecipe) == null,
                "finite atom values with overflowing combined damage reject grant without publishing");
            context.Check(ReferenceEquals(existing, holder.FindBuff(existingRecipe)) && existing.IsActive
                && existing.RemainingDuration == 7f && existing.StackInstances[0].CurrentStacks == 4
                && !existing.StackInstances[0].HasTriggered && holder.GetModifiers().Length == modifierCount
                && Near(holder.CalculateWeaponDamage(20f), 24f) && holder.CalculateProjectileCount(1) == 1,
                "failed construction leaves existing lifetime stacks and contributions unchanged");

            var brokenRecipe = context.Recipe(Damage(context, 1.5f), new ThrowingStatAtom());
            bool propagated = false;
            try
            {
                holder.TryAddBuff(brokenRecipe);
            }
            catch (InvalidOperationException)
            {
                propagated = true;
            }
            context.Check(propagated && holder.FindBuff(brokenRecipe) == null
                && holder.GetModifiers().Length == modifierCount && Near(holder.CalculateWeaponDamage(20f), 24f),
                "unexpected construction exceptions propagate without publishing partial effects");

            var normalRecipe = context.DamageReward();
            bool granted = holder.TryAddBuff(normalRecipe);
            Hit(holder, target, 1);
            context.Check(granted && holder.FindBuff(normalRecipe) != null
                && ReferenceEquals(existing, holder.FindBuff(existingRecipe))
                && existing.StackInstances[0].HasTriggered && Near(holder.CalculateWeaponDamage(20f), 39f),
                "normal grants and existing stack activation remain usable after construction failures");
        }

        [Serializable]
        private sealed class ThrowingStatAtom : StatBuffAtom
        {
            public override bool isValid => true;

            public override StatModifier[] CreateModifiers()
            {
                throw new InvalidOperationException("Simulated unexpected atom failure.");
            }
        }

        [Serializable]
        private sealed class CustomStatAtom : StatBuffAtom
        {
            public float Value = 2f;

            public override bool isValid => IsFinite(Value);

            public override StatModifier[] CreateModifiers()
            {
                if (!isValid)
                    throw new InvalidOperationException("Custom stat value must be finite.");
                return new[] { new StatModifier(WeaponStatId.ProjectileCount, ModifierType.Prefix, Value) };
            }
        }

        [Serializable]
        private sealed class DerivedDamageAtom : DamageBuffAtom
        {
        }

        [Serializable]
        private sealed class NonStatAtom : BuffAtom
        {
            public override bool isValid => true;
            public override StatModifier[] CreateModifiers() => Array.Empty<StatModifier>();
        }

        private static DamageBuffAtom Damage(BuffCheckContext context, float multiplier)
        {
            var atom = new DamageBuffAtom();
            context.Set(atom, "damageMultiplier", multiplier);
            return atom;
        }

        private static StackToActivationBuffAtom Trigger(BuffCheckContext context, ActivateBuffEffects action)
        {
            var atom = new StackToActivationBuffAtom();
            context.Set(atom, "stackCount", 5);
            context.Set(atom, "consumeMode", StackConsumeMode.TriggerOnce);
            context.Set(atom, "activation", action);
            return atom;
        }

        private static void Hit(BuffController holder, UnityEngine.GameObject target, int count)
        {
            for (int i = 0; i < count; i++) holder.ReportHit(target, 10f);
        }

        private static bool Near(float actual, float expected)
        {
            return Math.Abs(actual - expected) < 0.0001f;
        }
    }
}
