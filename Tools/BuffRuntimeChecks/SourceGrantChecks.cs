using System.Collections.Generic;
using UnityEngine;

namespace ParallelBonds.BuffChecks
{
    internal static class SourceGrantChecks
    {
        public static void Run(BuffCheckContext context)
        {
            context.Check(typeof(BuffHandle).IsPublic && typeof(BuffHandle).IsSealed
                && typeof(BuffHandle).IsClass && !typeof(UnityEngine.Object).IsAssignableFrom(typeof(BuffHandle)),
                "handle is a public sealed managed class");
            CheckIdentityAndRevocation(context);
            CheckInvalidGrants(context);
            CheckRefreshAndExpiry(context);
            CheckLegacyCoexistence(context);
            CheckDisable(context);
            CheckStacks(context);
        }

        private static void CheckIdentityAndRevocation(BuffCheckContext context)
        {
            var holder = context.CreateObject("identity owner").AddComponent<BuffController>();
            var other = context.CreateObject("foreign owner").AddComponent<BuffController>();
            var recipe = context.DamageReward();
            var source = new EqualSource();
            var secondSource = new EqualSource();
            context.Check(!ReferenceEquals(source, secondSource) && source.Equals(secondSource),
                "distinct sources deliberately compare equal");
            BuffHandle first = holder.GrantBuff(recipe, source);
            BuffHandle second = holder.GrantBuff(recipe, secondSource);
            context.Check(first != null && first.IsActive && ReferenceEquals(first.Definition, recipe)
                && ReferenceEquals(first.Source, source), "handle exposes its definition and exact source");
            context.Check(second != null && second.IsActive && !ReferenceEquals(first, second)
                && ReferenceEquals(second.Source, secondSource), "equal sources have independent handles");
            context.Check(holder.CalculateWeaponDamage(100f) == 225f,
                "same definition from distinct sources multiplies independently");
            context.Check(ReferenceEquals(first, holder.GrantBuff(recipe, source))
                && ReferenceEquals(second, holder.GrantBuff(recipe, secondSource))
                && holder.CalculateWeaponDamage(100f) == 225f, "repeated grants do not duplicate either source");

            var differentRecipe = context.DamageReward();
            var different = holder.GrantBuff(differentRecipe, source);
            context.Check(different != null && different.IsActive && !ReferenceEquals(first, different)
                && ReferenceEquals(different.Definition, differentRecipe) && ReferenceEquals(different.Source, source)
                && holder.CalculateWeaponDamage(100f) == 337.5f, "same source can own different definitions");
            var foreign = other.GrantBuff(recipe, source);
            context.Check(foreign != null && foreign.IsActive && !ReferenceEquals(first, foreign)
                && other.CalculateWeaponDamage(100f) == 150f, "same source and definition are controller local");
            context.Check(!holder.RevokeBuff(null) && !holder.RevokeBuff(foreign) && !other.RevokeBuff(first)
                && first.IsActive && foreign.IsActive && holder.CalculateWeaponDamage(100f) == 337.5f
                && other.CalculateWeaponDamage(100f) == 150f, "null and foreign revokes change neither controller");
            context.Check(holder.RevokeBuff(first) && !first.IsActive && second.IsActive && different.IsActive
                && foreign.IsActive && holder.CalculateWeaponDamage(100f) == 225f,
                "revoke removes only the selected grant");
            context.Check(!holder.RevokeBuff(first) && holder.CalculateWeaponDamage(100f) == 225f,
                "repeated revoke rejects stale handle");
            var replacement = holder.GrantBuff(recipe, source);
            context.Check(replacement != null && replacement.IsActive && !ReferenceEquals(first, replacement)
                && !holder.RevokeBuff(first) && replacement.IsActive
                && holder.CalculateWeaponDamage(100f) == 337.5f, "stale handle cannot revoke replacement");
        }

        private static void CheckInvalidGrants(BuffCheckContext context)
        {
            var holder = context.CreateObject("validation owner").AddComponent<BuffController>();
            var recipe = context.DamageReward();
            var source = new object();
            var invalid = context.Recipe();
            context.Check(!invalid.isValid && holder.GrantBuff(invalid, source) == null,
                "invalid definition rejected");
            context.Check(holder.GrantBuff(null, source) == null && holder.GrantBuff(recipe, null) == null,
                "null definition and source rejected");
            var destroyedDefinition = context.DamageReward();
            UnityEngine.Object.DestroyImmediate(destroyedDefinition);
            context.Check(holder.GrantBuff(destroyedDefinition, source) == null, "destroyed definition rejected");
            var unitySource = context.CreateObject("unity source");
            object boxedSource = unitySource;
            var live = holder.GrantBuff(recipe, boxedSource);
            context.Check(live != null && live.IsActive && ReferenceEquals(live.Source, boxedSource),
                "live unity source accepted through object reference");
            context.Check(holder.RevokeBuff(live), "live unity source grant can be revoked");
            UnityEngine.Object.DestroyImmediate(unitySource);
            context.Check(!ReferenceEquals(boxedSource, null) && holder.GrantBuff(recipe, boxedSource) == null,
                "destroyed unity source rejected through object reference");
            context.Check(holder.GetModifiers().Length == 0 && holder.CalculateWeaponDamage(100f) == 100f,
                "rejected grants publish no effects");
        }

        private static void CheckRefreshAndExpiry(BuffCheckContext context)
        {
            var holder = context.CreateObject("expiry owner").AddComponent<BuffController>();
            var recipe = context.DamageReward();
            context.Set(recipe, "duration", 10f);
            var source = new object();
            var first = holder.GrantBuff(recipe, source);
            var second = holder.GrantBuff(recipe, new object());
            context.Check(first != null && second != null, "timed grants accepted");
            holder.Tick(6f);
            context.Check(ReferenceEquals(first, holder.GrantBuff(recipe, source)), "refresh returns original handle");
            holder.Tick(4f);
            context.Check(first.IsActive && !second.IsActive && holder.CalculateWeaponDamage(100f) == 150f,
                "refresh extends only its own source lifetime");
            holder.Tick(5f);
            context.Check(first.IsActive, "refreshed grant survives until full duration");
            holder.Tick(1f);
            context.Check(!first.IsActive && !holder.RevokeBuff(first) && !holder.RevokeBuff(second)
                && holder.GetModifiers().Length == 0 && holder.CalculateWeaponDamage(100f) == 100f,
                "tick expiry invalidates handles and removes effects");
            var replacement = holder.GrantBuff(recipe, source);
            context.Check(replacement != null && replacement.IsActive && !ReferenceEquals(first, replacement)
                && !holder.RevokeBuff(first) && replacement.IsActive, "expired source regrant gets fresh handle");
            holder.Tick(9f);
            context.Check(replacement.IsActive, "replacement starts with full lifetime");
            holder.Tick(1f);
            context.Check(!replacement.IsActive, "replacement expires on its own deadline");
        }

        private static void CheckLegacyCoexistence(BuffCheckContext context)
        {
            var holder = context.CreateObject("legacy owner").AddComponent<BuffController>();
            var recipe = context.DamageReward();
            context.Set(recipe, "duration", 10f);
            var source = new object();
            var owned = holder.GrantBuff(recipe, source);
            context.Check(owned != null && holder.FindBuff(recipe) == null && !holder.RemoveBuff(recipe)
                && owned.IsActive, "legacy lookup and removal ignore source-only grant");
            holder.Tick(4f);
            context.Check(holder.TryAddBuff(recipe), "legacy grant coexists with owned grant");
            var legacy = holder.FindBuff(recipe);
            context.Check(legacy != null && holder.CalculateWeaponDamage(100f) == 225f,
                "legacy grant creates a separate multiplying instance");
            holder.Tick(3f);
            context.Check(holder.TryAddBuff(recipe) && ReferenceEquals(legacy, holder.FindBuff(recipe))
                && legacy.RemainingDuration == 10f && holder.CalculateWeaponDamage(100f) == 225f,
                "legacy refresh preserves legacy identity without duplication");
            holder.Tick(3f);
            context.Check(!owned.IsActive && legacy.IsActive && legacy.RemainingDuration == 7f
                && holder.CalculateWeaponDamage(100f) == 150f, "legacy add and refresh never extend owned lifetime");
            var renewed = holder.GrantBuff(recipe, source);
            context.Check(renewed != null && renewed.IsActive && legacy.RemainingDuration == 7f
                && ReferenceEquals(legacy, holder.FindBuff(recipe)), "source grant does not refresh existing legacy buff");
            holder.Tick(2f);
            context.Check(ReferenceEquals(renewed, holder.GrantBuff(recipe, source))
                && legacy.RemainingDuration == 5f, "source refresh does not refresh legacy lifetime");
            context.Check(holder.RemoveBuff(recipe) && !legacy.IsActive && renewed.IsActive
                && holder.FindBuff(recipe) == null && !holder.RemoveBuff(recipe)
                && holder.CalculateWeaponDamage(100f) == 150f, "legacy removal leaves owned grant untouched");
            context.Check(holder.TryAddBuff(recipe), "legacy buff can be readded beside owned grant");
            var replacementLegacy = holder.FindBuff(recipe);
            context.Check(replacementLegacy != null && !ReferenceEquals(legacy, replacementLegacy)
                && holder.RevokeBuff(renewed) && replacementLegacy.IsActive
                && ReferenceEquals(replacementLegacy, holder.FindBuff(recipe))
                && holder.CalculateWeaponDamage(100f) == 150f, "source revoke leaves legacy instance untouched");
        }

        private static void CheckDisable(BuffCheckContext context)
        {
            var holder = context.CreateObject("disable owner").AddComponent<BuffController>();
            var recipe = context.DamageReward();
            var source = new object();
            var first = holder.GrantBuff(recipe, source);
            var second = holder.GrantBuff(recipe, new object());
            context.Check(first != null && second != null && holder.TryAddBuff(recipe),
                "disable fixture contains owned and legacy grants");
            var legacy = holder.FindBuff(recipe);
            holder.enabled = false;
            // edit mode needs explicit lifecycle dispatch
            context.Invoke(holder, "OnDisable");
            context.Check(!first.IsActive && !second.IsActive && legacy != null && !legacy.IsActive
                && holder.FindBuff(recipe) == null && holder.GetModifiers().Length == 0,
                "disable invalidates every grant and legacy instance");
            context.Check(holder.GrantBuff(recipe, source) == null
                && holder.GrantBuff(recipe, new object()) == null && !holder.TryAddBuff(recipe)
                && !holder.RevokeBuff(first) && !holder.RevokeBuff(second),
                "disabled controller rejects grants and stale revokes");
            holder.enabled = true;
            context.Check(!first.IsActive && !second.IsActive && holder.CalculateWeaponDamage(100f) == 100f,
                "reenable does not resurrect old grants");
            var replacement = holder.GrantBuff(recipe, source);
            context.Check(replacement != null && replacement.IsActive && !ReferenceEquals(first, replacement)
                && !holder.RevokeBuff(first) && replacement.IsActive
                && holder.CalculateWeaponDamage(100f) == 150f, "reenabled controller accepts fresh source grant");
        }

        private static void CheckStacks(BuffCheckContext context)
        {
            var holder = context.CreateObject("stack owner").AddComponent<BuffController>();
            var target = context.CreateObject("stack target");
            var damage = new DamageBuffAtom();
            context.Set(damage, "damageMultiplier", 1.5f);
            var activation = new ActivateBuffEffects();
            context.Set(activation, "effects", new List<BuffAtom> { damage });
            var trigger = context.Trigger(context.DamageReward(), StackConsumeMode.TriggerOnce);
            context.Set(trigger, "activation", activation);
            var recipe = context.Recipe(trigger);
            var source = new object();
            var secondSource = new object();
            var first = holder.GrantBuff(recipe, source);
            context.Check(recipe.isValid && first != null, "source stack recipe accepted");
            Hit(holder, target, 3);
            holder.Tick(6f);
            context.Check(ReferenceEquals(first, holder.GrantBuff(recipe, source)),
                "stack refresh returns same handle");
            var second = holder.GrantBuff(recipe, secondSource);
            context.Check(second != null && !ReferenceEquals(first, second), "second source gets fresh stack grant");
            holder.Tick(4f);
            Hit(holder, target, 1);
            context.Check(first.IsActive && second.IsActive && holder.CalculateWeaponDamage(100f) == 100f,
                "refreshed stack survives original deadline and neither grant triggers early");
            Hit(holder, target, 1);
            context.Check(holder.CalculateWeaponDamage(100f) == 150f,
                "refresh preserves three stacks and only older source reaches five hits");
            context.Check(ReferenceEquals(first, holder.GrantBuff(recipe, source))
                && holder.CalculateWeaponDamage(100f) == 150f,
                "refresh preserves activated effects without duplication");
            Hit(holder, target, 2);
            context.Check(holder.CalculateWeaponDamage(100f) == 150f,
                "younger source remains below its own threshold");
            Hit(holder, target, 1);
            context.Check(holder.CalculateWeaponDamage(100f) == 225f,
                "younger source activates independently on its fifth hit");
            Hit(holder, target, 5);
            context.Check(holder.CalculateWeaponDamage(100f) == 225f,
                "refresh preserves trigger-once state for both grants");
            context.Check(holder.RevokeBuff(first) && !first.IsActive && second.IsActive
                && holder.CalculateWeaponDamage(100f) == 150f, "revoke removes only its internal activation");
            var replacement = holder.GrantBuff(recipe, source);
            context.Check(replacement != null && replacement.IsActive && !ReferenceEquals(first, replacement)
                && holder.CalculateWeaponDamage(100f) == 150f, "regrant starts without previous activated effects");
            Hit(holder, target, 4);
            context.Check(holder.CalculateWeaponDamage(100f) == 150f, "regrant starts with zero stack progress");
            Hit(holder, target, 1);
            context.Check(holder.CalculateWeaponDamage(100f) == 225f, "replacement can activate independently");
            holder.Tick(10f);
            context.Check(!replacement.IsActive && !second.IsActive && holder.CalculateWeaponDamage(100f) == 100f,
                "expiry removes owned stack activation effects");
        }

        private static void Hit(BuffController holder, GameObject target, int count)
        {
            for (int i = 0; i < count; i++) holder.ReportHit(target, 10f);
        }

        private sealed class EqualSource
        {
            public override bool Equals(object obj)
            {
                return obj is EqualSource;
            }

            public override int GetHashCode()
            {
                return 1;
            }
        }
    }
}
