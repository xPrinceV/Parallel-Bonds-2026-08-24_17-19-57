using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ParallelBonds.BuffChecks;
using UnityEngine;

// Compile in the same assembly as the buff sources and Tools/BuffRuntimeChecks*.cs.
// Run() includes the existing runtime suite; Run(false) runs only these checks.
// Standalone stub instructions/output live in Library/BuffPerformanceValidation.
// Allocation results describe the executing managed runtime, not Unity Play Mode.
public static class BuffPerformanceChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Iterations = 10000;
    private static float damageSink;
    private static int countSink;

    public static string Run(bool includeRuntimeChecks = true)
    {
        string existing = includeRuntimeChecks ? BuffRuntimeChecks.Run() + "; " : "";
        int checks = Group("cache", Cache)
            + Group("activation", Activation)
            + Group("lifecycle", Lifecycle)
            + Group("dispatch", Dispatch)
            + Group("exception cleanup", Exceptions)
            + Group("calculator paths", CalculatorPaths)
            + Group("consume modes", ConsumeModes)
            + Group("warm allocations", Allocations);
        return existing + checks + " buff performance checks passed";
    }

    private static int Group(string name, Action<BuffCheckContext> run)
    {
        using (var c = new BuffCheckContext("performance " + name))
        {
            run(c);
            return c.CheckCount;
        }
    }

    private static T Field<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, PrivateInstance);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        return (T)field.GetValue(target);
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
    private static StatModifier[] Snapshot(BuffController holder) { return Field<StatModifier[]>(holder, "cachedModifiers"); }
    private static bool Dirty(BuffController holder) { return Field<bool>(holder, "modifiersDirty"); }

    private static DamageBuffAtom Damage(BuffCheckContext c, float multiplier)
    {
        var atom = new DamageBuffAtom();
        c.Set(atom, "damageMultiplier", multiplier);
        return atom;
    }

    private static ActivateBuffEffects Effects(BuffCheckContext c, params BuffAtom[] atoms)
    {
        var action = new ActivateBuffEffects();
        c.Set(action, "effects", new List<BuffAtom>(atoms));
        return action;
    }

    private static StackToActivationBuffAtom Trigger(BuffCheckContext c, StackActivation action)
    {
        var atom = new StackToActivationBuffAtom();
        c.Set(atom, "activation", action);
        return atom;
    }

    private static void Cache(BuffCheckContext c)
    {
        var holder = c.CreateObject("owner").AddComponent<BuffController>();
        c.Check(Dirty(holder), "initial cache is dirty");
        c.Check(holder.CalculateWeaponDamage(20f) == 20f && holder.CalculateProjectileCount(2) == 2
            && !Dirty(holder) && Snapshot(holder).Length == 0, "first empty queries build clean cache");
        var damage = Damage(c, 1.5f);
        c.Set(damage, "damagePrefix", 2f);
        c.Set(damage, "damagePostfix", 3f);
        var projectile = new ProjectileBuffAtom();
        c.Set(projectile, "projectileCount", 2);
        var recipe = c.Recipe(damage, projectile);
        c.Check(holder.TryAddBuff(recipe) && Dirty(holder), "adding instance invalidates even a warm empty cache");
        c.Check(holder.CalculateWeaponDamage(20f) == 36f && holder.CalculateProjectileCount(2) == 4,
            "initial populated cache serves both calculations");
        var snapshot = Snapshot(holder);
        for (int i = 0; i < 32; i++)
        {
            c.Check(holder.CalculateWeaponDamage(20f) == 36f && holder.CalculateProjectileCount(2) == 4
                && ReferenceEquals(snapshot, Snapshot(holder)) && !Dirty(holder), "continuous query reuses snapshot " + i);
        }
        var first = holder.GetModifiers();
        var second = holder.GetModifiers();
        c.Check(first.Length == snapshot.Length && !ReferenceEquals(first, second)
            && !ReferenceEquals(first, snapshot), "GetModifiers returns caller-owned nonempty clones");
        for (int i = 0; i < first.Length; i++)
            first[i] = new StatModifier(WeaponStatId.Damage, ModifierType.Multiplier, 99f);
        c.Check(holder.CalculateWeaponDamage(20f) == 36f && holder.CalculateProjectileCount(2) == 4
            && second[0].Value == 2f && holder.GetModifiers()[0].Value == 2f,
            "mutating caller array cannot poison damage, count, or later exports");
        holder.Tick(1f);
        c.Check(holder.TryAddBuff(recipe) && !Dirty(holder) && ReferenceEquals(snapshot, Snapshot(holder)),
            "duration tick and refresh do not rebuild unchanged modifiers");
        var instance = holder.FindBuff(recipe);
        c.Check(Subscribed(instance, holder), "new instance subscribes to controller invalidation");
        c.Check(holder.RemoveBuff(recipe) && Dirty(holder) && !Subscribed(instance, holder)
            && holder.CalculateWeaponDamage(20f) == 20f && holder.CalculateProjectileCount(2) == 2,
            "legacy removal invalidates and detaches");
    }

    private static void Activation(BuffCheckContext c)
    {
        var holder = c.CreateObject("owner").AddComponent<BuffController>();
        var target = c.CreateObject("target");
        var extra = new ProjectileBuffAtom();
        c.Set(extra, "projectileCount", 1);
        var action = Effects(c, Damage(c, 1.1f), extra);
        var recipe = c.Recipe(Damage(c, 1.2f), Trigger(c, action));
        c.Check(holder.TryAddBuff(recipe), "activation recipe accepted");
        var instance = holder.FindBuff(recipe);
        int notifications = 0;
        Action observer = () => notifications++;
        instance.ModifiersChanged += observer;
        c.Check(Near(holder.CalculateWeaponDamage(100f), 120f) && holder.CalculateProjectileCount(1) == 1,
            "cache is warm before internal activation");
        var before = Snapshot(holder);
        holder.ReportHit(target, 1f);
        c.Check(notifications == 1 && Dirty(holder), "successful activation notifies and invalidates");
        c.Check(Near(holder.CalculateWeaponDamage(100f), 130f) && holder.CalculateProjectileCount(1) == 2
            && !ReferenceEquals(before, Snapshot(holder)), "same buff adds 20% + 10% = 30%, count cache also refreshes");
        var activated = Snapshot(holder);
        holder.ReportHit(target, 1f);
        c.Check(notifications == 1 && !Dirty(holder) && ReferenceEquals(activated, Snapshot(holder)),
            "deduplicated activation does not notify or invalidate");
        c.Check(!action.TryActivate(new BuffActivationContext(new RecordingBuffReceiver(), recipe, instance))
            && notifications == 1, "wrong receiver cannot publish changes");
        var cross = c.Recipe(Damage(c, 1.5f));
        c.Check(holder.TryAddBuff(cross) && Near(holder.CalculateWeaponDamage(100f), 195f),
            "separate buff multiplies resolved 1.3 by 1.5");
        var exported = holder.GetModifiers();
        instance.Remove();
        c.Check(notifications == 2 && Dirty(holder) && holder.CalculateWeaponDamage(100f) == 150f
            && holder.CalculateProjectileCount(1) == 1 && exported.Length > holder.GetModifiers().Length,
            "external Remove invalidates immediately before controller Tick; old export stays intact");
        instance.Remove();
        c.Check(notifications == 2 && !Dirty(holder), "second Remove is silent");
        holder.Tick(0f);
        c.Check(!Subscribed(instance, holder), "Tick detaches externally removed instance");
        instance.ModifiersChanged -= observer;

        var hugeHolder = c.CreateObject("overflow owner").AddComponent<BuffController>();
        var bonus = Damage(c, float.MaxValue);
        var overflow = Effects(c, bonus);
        var hugeRecipe = c.Recipe(Damage(c, float.MaxValue), Trigger(c, overflow));
        c.Check(hugeHolder.TryAddBuff(hugeRecipe) && hugeHolder.CalculateWeaponDamage(1f) == float.MaxValue,
            "overflow fixture warms initial finite snapshot");
        var hugeInstance = hugeHolder.FindBuff(hugeRecipe);
        var unchanged = Snapshot(hugeHolder);
        int changes = 0;
        Action hugeObserver = () => changes++;
        hugeInstance.ModifiersChanged += hugeObserver;
        hugeHolder.ReportHit(target, 1f);
        c.Check(changes == 0 && !Dirty(hugeHolder) && ReferenceEquals(unchanged, Snapshot(hugeHolder))
            && !hugeInstance.StackInstances[0].HasTriggered, "failed aggregate publishes neither event nor cache change");
        c.Set(bonus, "damageMultiplier", 1f);
        hugeHolder.ReportHit(target, 1f);
        c.Check(changes == 1 && Dirty(hugeHolder) && hugeInstance.StackInstances[0].HasTriggered,
            "failed activation remains retryable");
        hugeInstance.ModifiersChanged -= hugeObserver;
    }

    private static bool Subscribed(BuffInstance instance, BuffController holder)
    {
        var listeners = Field<Action>(instance, "ModifiersChanged");
        if (listeners != null)
            foreach (Delegate listener in listeners.GetInvocationList())
                if (ReferenceEquals(listener.Target, holder)) return true;
        return false;
    }

    private static void Lifecycle(BuffCheckContext c)
    {
        var holder = c.CreateObject("owner").AddComponent<BuffController>();
        var recipe = c.DamageReward();
        c.Check(holder.TryAddBuff(recipe), "timed legacy buff accepted");
        var expired = holder.FindBuff(recipe);
        c.Check(holder.CalculateWeaponDamage(100f) == 150f, "warm before expiry");
        holder.Tick(recipe.Duration);
        c.Check(!expired.IsActive && !Subscribed(expired, holder) && Dirty(holder)
            && holder.CalculateWeaponDamage(100f) == 100f, "expiry invalidates and detaches");
        var first = holder.GrantBuff(recipe, new object());
        var second = holder.GrantBuff(recipe, new object());
        c.Check(first != null && second != null && holder.CalculateWeaponDamage(100f) == 225f, "owned cache warmed");
        c.Check(holder.RevokeBuff(first) && !Subscribed(first.Instance, holder) && Dirty(holder)
            && holder.CalculateWeaponDamage(100f) == 150f, "owned revoke invalidates only its contribution and detaches");
        holder.Tick(recipe.Duration);
        c.Check(!second.IsActive && !Subscribed(second.Instance, holder) && !holder.RevokeBuff(second)
            && holder.CalculateWeaponDamage(100f) == 100f, "owned expiry drops grant bookkeeping");

        c.Check(holder.TryAddBuff(recipe), "legacy sleep fixture accepted");
        var legacy = holder.FindBuff(recipe);
        var owned = holder.GrantBuff(recipe, new object());
        c.Check(owned != null && holder.CalculateWeaponDamage(100f) == 225f, "sleep fixture warmed");
        var snapshot = Snapshot(holder);
        holder.Tick(2f);
        float remaining = legacy.RemainingDuration;
        holder.SetWorldSuspended(true);
        holder.enabled = false;
        c.Invoke(holder, "OnDisable"); // Same explicit Edit Mode convention as BuffRuntimeChecks.
        holder.Tick(100f);
        c.Check(legacy.IsActive && owned.IsActive && legacy.RemainingDuration == remaining
            && Subscribed(legacy, holder) && holder.CalculateWeaponDamage(100f) == 100f
            && holder.GetModifiers().Length == 0, "sleep-on-disable retains buffs but hides effects and pauses lifetime");
        holder.enabled = true;
        holder.SetWorldSuspended(false);
        c.Check(holder.CalculateWeaponDamage(100f) == 225f && ReferenceEquals(snapshot, Snapshot(holder)),
            "wake restores unchanged cached modifiers");

        holder.SetWorldSuspended(true);
        holder.enabled = false;
        c.Invoke(holder, "OnDisable");
        owned.Instance.Remove();
        c.Check(Dirty(holder) && holder.CalculateWeaponDamage(100f) == 100f && Dirty(holder),
            "external removal during sleep stays dirty through disabled queries");
        holder.enabled = true;
        holder.SetWorldSuspended(false);
        c.Check(holder.CalculateWeaponDamage(100f) == 150f, "wake rebuilds if effects changed during sleep");
        holder.Tick(0f);
        c.Check(!Subscribed(owned.Instance, holder), "wake cleanup detaches removed owned instance");
        var replacement = holder.GrantBuff(recipe, new object());
        c.Check(replacement != null && holder.CalculateWeaponDamage(100f) == 225f, "normal disable fixture warmed");
        holder.enabled = false;
        c.Invoke(holder, "OnDisable");
        c.Check(!legacy.IsActive && !replacement.IsActive && !Subscribed(legacy, holder)
            && !Subscribed(replacement.Instance, holder) && !holder.RevokeBuff(replacement)
            && Snapshot(holder).Length == 0, "normal disable clears owned grants, legacy effects and subscriptions");
        holder.enabled = true;
        c.Check(holder.CalculateWeaponDamage(100f) == 100f && holder.CalculateProjectileCount(2) == 2,
            "normal reenable cannot resurrect cache");
        var destroyed = holder.GrantBuff(recipe, new object());
        c.Check(destroyed != null && holder.CalculateWeaponDamage(100f) == 150f, "destroy fixture warmed");
        holder.SetWorldSuspended(true);
        c.Invoke(holder, "OnDestroy");
        c.Check(!destroyed.IsActive && !Subscribed(destroyed.Instance, holder)
            && !holder.RevokeBuff(destroyed) && holder.CalculateWeaponDamage(100f) == 100f,
            "destroy clears even while world-suspended");
    }

    private static void CleanDispatch(BuffCheckContext c, BuffController holder, List<BuffInstance> snapshot, string label)
    {
        c.Check(ReferenceEquals(snapshot, Field<List<BuffInstance>>(holder, "eventSnapshot"))
            && snapshot.Count == 0 && !Field<bool>(holder, "isDispatching"), label);
    }

    private static void Dispatch(BuffCheckContext c)
    {
        var holder = c.CreateObject("owner").AddComponent<BuffController>();
        var target = c.CreateObject("target");
        var order = new List<string>();
        var later = c.Recipe(Trigger(c, new CallbackActivation(() => order.Add("B"))));
        var survivor = c.Recipe(Trigger(c, new CallbackActivation(() => order.Add("C"))));
        var added = c.Recipe(Trigger(c, new CallbackActivation(() => order.Add("D"))));
        BuffDefinition first = null;
        first = c.Recipe(Trigger(c, new CallbackActivation(() =>
        {
            order.Add("A");
            c.Check(holder.RemoveBuff(first) && holder.RemoveBuff(later), "callback removes self and later snapshot member");
            c.Check(holder.TryAddBuff(added), "callback adds new buff");
            holder.ReportHit(target, 1f);
            holder.ReportEvent(new StackEventContext(holder.Owner, target, 1f));
        })));
        c.Check(holder.TryAddBuff(first) && holder.TryAddBuff(later) && holder.TryAddBuff(survivor), "dispatch fixture accepted");
        var removed = holder.FindBuff(later);
        var snapshot = Field<List<BuffInstance>>(holder, "eventSnapshot");
        holder.ReportHit(target, 1f);
        c.Check(string.Join("", order) == "AC" && removed.StackInstances[0].CurrentStacks == 0
            && holder.FindBuff(added).StackInstances[0].CurrentStacks == 0,
            "stable order skips removed member, excludes new buff, and suppresses both reentry routes");
        CleanDispatch(c, holder, snapshot, "normal dispatch clears and retains reusable list");
        order.Clear();
        holder.ReportHit(target, 1f);
        c.Check(string.Join("", order) == "CD", "next event includes new buff once in insertion order");
        CleanDispatch(c, holder, snapshot, "next dispatch remains reusable");

        var clearHolder = c.CreateObject("clear owner").AddComponent<BuffController>();
        int oldCalls = 0, newCalls = 0;
        var fresh = c.Recipe(Trigger(c, new CallbackActivation(() => newCalls++)));
        var clearer = c.Recipe(Trigger(c, new CallbackActivation(() =>
        {
            clearHolder.enabled = false;
            c.Invoke(clearHolder, "OnDisable");
            clearHolder.enabled = true;
            c.Check(clearHolder.TryAddBuff(fresh), "callback can grant after clear and reenable");
            clearHolder.ReportHit(target, 1f);
        })));
        c.Check(clearHolder.TryAddBuff(clearer)
            && clearHolder.TryAddBuff(c.Recipe(Trigger(c, new CallbackActivation(() => oldCalls++)))), "clear dispatch fixture accepted");
        clearHolder.ReportHit(target, 1f);
        c.Check(oldCalls == 0 && newCalls == 0, "clear mid-event stops old buffs without admitting replacements or reentry");
        clearHolder.ReportHit(target, 1f);
        c.Check(newCalls == 1, "dispatch survives mid-event lifecycle clear");
    }

    private static void Exceptions(BuffCheckContext c)
    {
        foreach (bool fromCondition in new[] { false, true })
        {
            var holder = c.CreateObject("exception owner").AddComponent<BuffController>();
            var target = c.CreateObject("target");
            int firstCalls = 0, tailCalls = 0, addedCalls = 0;
            var sentinel = new InvalidOperationException("intentional callback failure");
            var added = c.Recipe(Trigger(c, new CallbackActivation(() => addedCalls++)));
            Action callback = () =>
            {
                firstCalls++;
                holder.ReportHit(target, 1f);
                if (firstCalls == 1)
                {
                    c.Check(holder.TryAddBuff(added), "throwing callback adds buff before failure");
                    throw sentinel;
                }
            };
            var trigger = Trigger(c, new CallbackActivation(fromCondition ? (Action)(() => { }) : callback));
            if (fromCondition) c.Set(trigger, "condition", new CallbackCondition(callback));
            var recipe = c.Recipe(trigger);
            c.Check(holder.TryAddBuff(recipe)
                && holder.TryAddBuff(c.Recipe(Trigger(c, new CallbackActivation(() => tailCalls++)))), "exception fixture accepted");
            var snapshot = Field<List<BuffInstance>>(holder, "eventSnapshot");
            Exception observed = null;
            try { holder.ReportHit(target, 1f); }
            catch (InvalidOperationException error) { observed = error; }
            c.Check(ReferenceEquals(observed, sentinel) && firstCalls == 1 && tailCalls == 0 && addedCalls == 0,
                "original callback exception propagates and aborts current dispatch");
            CleanDispatch(c, holder, snapshot, "finally clears references and dispatch guard after callback exception");
            holder.ReportHit(target, 1f);
            c.Check(firstCalls == 2 && tailCalls == 1 && addedCalls == 1
                && holder.FindBuff(recipe).StackInstances[0].HasTriggered,
                "condition/activation stack guard and controller guard recover on next event");
            CleanDispatch(c, holder, snapshot, "post-exception success leaves reusable snapshot empty");
        }
    }

    private static void CalculatorPaths(BuffCheckContext c)
    {
        var values = new[]
        {
            new StatModifier(WeaponStatId.Damage, ModifierType.Postfix, 4f),
            new StatModifier(WeaponStatId.Damage, ModifierType.Multiplier, 1.5f),
            new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, 5f),
            new StatModifier(PlayerStatId.Health, ModifierType.Prefix, float.NaN),
            new StatModifier(WeaponStatId.ProjectileCount, ModifierType.Prefix, float.NaN),
            new StatModifier(WeaponStatId.Damage, ModifierType.Multiplier, 1.2f),
            new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, 3f)
        };
        var indexed = new IndexOnlyList(values);
        var fallback = new EnumerableOnly(values);
        c.Check(Near(DamageCalculator.CalculateDamage(20f, indexed), 54.4f) && indexed.Reads == values.Length,
            "IReadOnlyList is indexed without ever requesting its throwing enumerator");
        c.Check(Near(DamageCalculator.CalculateDamage(20f, fallback), 54.4f) && fallback.Enumerations == 1,
            "IEnumerable-only fallback preserves stages, cross-buff multipliers and target filtering");
        foreach (bool enumerable in new[] { false, true })
        {
            c.Check(Throws<ArgumentNullException>(() => DamageCalculator.CalculateDamage(1f, null)), "null input rejected");
            c.Check(Throws<ArgumentOutOfRangeException>(() => DamageCalculator.CalculateDamage(float.NaN, Path(values, enumerable))), "NaN base rejected");
            c.Check(Throws<ArgumentOutOfRangeException>(() => DamageCalculator.CalculateDamage(float.PositiveInfinity, Path(values, enumerable))), "infinite base rejected");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                c.Check(Throws<ArgumentException>(() => DamageCalculator.CalculateDamage(1f, Path(new[] {
                    new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, invalid) }, enumerable))), "invalid matching value rejected");
            c.Check(Throws<ArgumentException>(() => DamageCalculator.CalculateDamage(1f, Path(new[] {
                new StatModifier(WeaponStatId.Damage, (ModifierType)99, 1f) }, enumerable))), "unknown stage rejected");
            c.Check(Throws<OverflowException>(() => DamageCalculator.CalculateDamage(1f, Path(new[] {
                new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, float.MaxValue),
                new StatModifier(WeaponStatId.Damage, ModifierType.Prefix, float.MaxValue) }, enumerable))), "accumulation overflow rejected");
            c.Check(Throws<OverflowException>(() => DamageCalculator.CalculateDamage(float.MaxValue, Path(new[] {
                new StatModifier(WeaponStatId.Damage, ModifierType.Multiplier, 2f) }, enumerable))), "final overflow rejected");
            c.Check(DamageCalculator.CalculateDamage(-10f, Path(Array.Empty<StatModifier>(), enumerable)) == -10f,
                "negative result is not clamped");
        }
    }

    private static IEnumerable<StatModifier> Path(StatModifier[] values, bool enumerable)
    {
        return enumerable ? (IEnumerable<StatModifier>)new EnumerableOnly(values) : new IndexOnlyList(values);
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private static void ConsumeModes(BuffCheckContext c)
    {
        var trigger = Trigger(c, new CallbackActivation(() => { }));
        foreach (StackConsumeMode mode in new[] { StackConsumeMode.ConsumeRequiredStacks,
            StackConsumeMode.ConsumeAllStacks, StackConsumeMode.TriggerOnce })
        {
            c.Set(trigger, "consumeMode", mode);
            c.Check(trigger.isValid, "legal consume mode accepted: " + mode);
        }
        foreach (int value in new[] { -1, 3, int.MaxValue })
        {
            c.Set(trigger, "consumeMode", (StackConsumeMode)value);
            c.Check(!trigger.isValid, "undefined consume mode rejected: " + value);
        }
    }

    private static void Allocations(BuffCheckContext c)
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
        if (method == null) throw new NotSupportedException("Per-thread allocation counter unavailable; benchmark not validated.");
        var allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        var holder = c.CreateObject("owner").AddComponent<BuffController>();
        var target = c.CreateObject("target");
        var projectile = new ProjectileBuffAtom();
        c.Set(projectile, "projectileCount", 2);
        c.Check(holder.TryAddBuff(c.Recipe(Damage(c, 1.5f), projectile)), "allocation fixture has nonempty modifiers");
        Zero(c, allocated, "CalculateWeaponDamage", () => damageSink = holder.CalculateWeaponDamage(20f));
        Zero(c, allocated, "CalculateProjectileCount", () => countSink = holder.CalculateProjectileCount(1));
        c.Check(damageSink == 30f && countSink == 3, "measured calls produce expected results");
        Zero(c, allocated, "ReportHit/stat-only", () => holder.ReportHit(target, 1f));
        var hit = new StackEventContext(holder.Owner, target, 1f);
        Zero(c, allocated, "ReportEvent/stat-only", () => holder.ReportEvent(hit));
        var values = holder.GetModifiers(); // Clone is deliberately outside every measurement window.
        Zero(c, allocated, "DamageCalculator/array", () => damageSink = DamageCalculator.CalculateDamage(20f, values));
        var list = new List<StatModifier>(values);
        Zero(c, allocated, "DamageCalculator/list", () => damageSink = DamageCalculator.CalculateDamage(20f, list));

        // Include active stack validation in both zero-allocation checks, while keeping
        // the threshold above all warmup/measured hits to exclude activation work.
        var trigger = Trigger(c, new CallbackActivation(() => { }));
        c.Set(trigger, "stackCount", int.MaxValue);
        var recipe = c.Recipe(trigger);
        c.Check(holder.TryAddBuff(recipe), "active stack allocation fixture accepted");
        var instance = holder.FindBuff(recipe);
        Zero(c, allocated, "ProcessEvent/active-stack", () => instance.ProcessEvent(hit));
        Zero(c, allocated, "ReportHit/active-stack", () => holder.ReportHit(target, 1f));
    }

    private static void Zero(BuffCheckContext c, Func<long> allocated, string label, Action call)
    {
        long bytes = Measure(allocated, call);
        Console.WriteLine("BUFF_ALLOC " + label + "=" + bytes + "B / " + Iterations + " calls");
        c.Check(bytes == 0, label + " allocates zero bytes after warmup");
    }

    private static long Measure(Func<long> allocated, Action call)
    {
        for (int i = 0; i < Iterations; i++) call();
        allocated();
        long before = allocated();
        for (int i = 0; i < Iterations; i++) call();
        return allocated() - before;
    }

    private sealed class CallbackActivation : StackActivation
    {
        private readonly Action callback;
        public CallbackActivation(Action callback) { this.callback = callback; }
        public override bool isValid => true;
        public override bool TryActivate(BuffActivationContext context) { callback(); return true; }
    }

    private sealed class CallbackCondition : StackCondition
    {
        private readonly Action callback;
        public CallbackCondition(Action callback) { this.callback = callback; }
        public override bool isValid => true;
        public override bool Matches(StackEventContext context, GameObject owner) { callback(); return true; }
    }

    private sealed class IndexOnlyList : IReadOnlyList<StatModifier>
    {
        private readonly StatModifier[] values;
        public int Reads;
        public IndexOnlyList(StatModifier[] values) { this.values = values; }
        public int Count => values.Length;
        public StatModifier this[int index] { get { Reads++; return values[index]; } }
        public IEnumerator<StatModifier> GetEnumerator() { throw new InvalidOperationException("Indexed path must not enumerate"); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    private sealed class EnumerableOnly : IEnumerable<StatModifier>
    {
        private readonly StatModifier[] values;
        public int Enumerations;
        public EnumerableOnly(StatModifier[] values) { this.values = values; }
        public IEnumerator<StatModifier> GetEnumerator()
        {
            Enumerations++;
            for (int i = 0; i < values.Length; i++) yield return values[i];
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}
