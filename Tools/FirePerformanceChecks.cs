using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Compile outside Assets against current Assembly-CSharp + Unity assemblies, like WeaponBuffChecks.
// Invoke Run on Unity's main thread in Play Mode; no frames are yielded or assets saved.
// Disable Deep Profiling for allocation checks. Callbacks are manual, not live physics delivery.
public static class FirePerformanceChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<Object> owned = new List<Object>();
    private static bool running;
    private static int checks;

    public static string Run()
    {
        if (!Application.isPlaying || running)
            throw new InvalidOperationException("Requires Unity Play Mode on the main thread, with no concurrent run.");
        running = true;
        checks = 0;
        DamageNumberController numbers = DamageNumberController.instance;
        try
        {
            // Keep presentation out of synchronous damage/allocation checks; restore before returning.
            DamageNumberController.instance = null;
            World world = MakeWorld("World");
            Contacts(world);
            InvalidContacts(world);
            OwnershipAndSleep(world);
            ReportsAndCallbacks(world);
            string allocations = Allocations(world);
            return checks + " checks passed; " + allocations +
                ". Manual callbacks only; real physics, frame scheduling, XP and presentation not tested.";
        }
        finally
        {
            try
            {
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                owned.Clear();
                Physics2D.SyncTransforms();
            }
            finally
            {
                DamageNumberController.instance = numbers;
                running = false;
            }
        }
    }

    private static void Contacts(World world)
    {
        var fire = new FireCalls(world);
        EnemyController enemy = Enemy(world);
        Collider2D first = Hitbox(enemy), second = Hitbox(enemy);
        Equal(fire.Fire.tickRate, 0.5f, "default tick rate unchanged");
        fire.Fire.SetDamage(20f);
        fire.Enter(first);
        Equal(enemy.health, 990f, "first contact immediately deals half weapon damage");
        fire.Enter(first);
        fire.Enter(second);
        Equal(enemy.health, 990f, "duplicate entry and second collider do not repeat entry damage");
        ContactsEqual(fire, enemy, 2, "both colliders share one target");
        fire.Tick();
        Equal(enemy.health, 980f, "one tick per enemy, not per collider");
        Equal(fire.Fire.tickCounter, fire.Fire.tickRate, "tick resets to configured rate");
        fire.Exit(first);
        ContactsEqual(fire, enemy, 1, "partial exit retains contact");
        fire.Tick();
        Equal(enemy.health, 970f, "partial exit still ticks once");
        fire.Exit(second);
        Empty(fire, "last exit clears contact");
        fire.Enter(second);
        Equal(enemy.health, 960f, "real reentry applies immediate damage again");
    }

    private static void InvalidContacts(World world)
    {
        var fire = new FireCalls(world);
        EnemyController enemy = Enemy(world);
        Collider2D first = Hitbox(enemy), second = Hitbox(enemy);
        fire.Enter(first); fire.Enter(second);
        first.enabled = false;
        fire.Idle();
        ContactsEqual(fire, enemy, 1, "disabled collider removed on non-tick frame");
        second.enabled = false;
        fire.Idle();
        Empty(fire, "last disabled collider removed before next tick");
        Equal(enemy.health, 990f, "non-tick cleanup deals no damage");
        second.enabled = true;
        fire.Enter(second);
        Equal(enemy.health, 980f, "reentry after disabled contact cleanup is immediate");
        second.gameObject.SetActive(false);
        fire.Tick();
        Empty(fire, "inactive hitbox removed without exit");
        Equal(enemy.health, 980f, "inactive hitbox receives no scheduled damage");

        Collider2D destroyed = Hitbox(enemy);
        fire.Enter(destroyed);
        Object.DestroyImmediate(destroyed);
        fire.Idle();
        Empty(fire, "destroyed collider removed using its stored key");
        Collider2D live = Hitbox(enemy);
        fire.Enter(live);
        enemy.gameObject.SetActive(false);
        fire.Idle();
        Empty(fire, "inactive enemy removed on non-tick frame");
        enemy.gameObject.SetActive(true);
        fire.Enter(live);
        enemy.health = 0f;
        fire.Idle();
        Empty(fire, "dead enemy removed on non-tick frame");
        fire.Enter(live);
        Empty(fire, "dead enemy rejected on entry");

        EnemyController doomed = Enemy(world);
        fire.Enter(Hitbox(doomed));
        Object.DestroyImmediate(doomed.gameObject);
        fire.Idle();
        Empty(fire, "destroyed enemy removed from both list and dictionary");

        var disabledFire = new FireCalls(world);
        EnemyController untouched = Enemy(world);
        disabledFire.Fire.enabled = false;
        disabledFire.Enter(Hitbox(untouched));
        Equal(untouched.health, 1000f, "disabled fire rejects entry");
        Empty(disabledFire, "disabled fire stores no contact");
    }

    private static void OwnershipAndSleep(World world)
    {
        var fire = new FireCalls(world);
        EnemyController enemy = Enemy(world), other = Enemy(world);
        Collider2D collider = Hitbox(enemy);
        collider.tag = "Untagged";
        fire.Enter(collider);
        Empty(fire, "untagged entry rejected");
        collider.tag = "Enemy";
        fire.Enter(collider);
        collider.tag = "Untagged";
        fire.Tick();
        Equal(enemy.health, 980f, "tag change alone preserves existing contact semantics");
        fire.Exit(collider);
        Empty(fire, "stored collider exits even after tag change");
        collider.tag = "Enemy";
        fire.Enter(collider);
        collider.transform.SetParent(other.transform, false);
        fire.Idle();
        Empty(fire, "parent change invalidates old owner before next tick");
        fire.Enter(collider);
        Equal(other.health, 990f, "reparented collider enters new owner immediately");
        collider.transform.SetParent(enemy.transform, false);
        fire.Exit(collider);
        Empty(fire, "exit removes stored collider after parent change");

        World foreign = MakeWorld("ForeignWorld");
        EnemyController foreignEnemy = Enemy(foreign);
        fire.Enter(Hitbox(foreignEnemy));
        Empty(fire, "active foreign-world target rejected");
        Equal(foreignEnemy.health, 1000f, "foreign target undamaged");
        fire.Enter(collider);
        enemy.transform.SetParent(foreign.ContentRoot, false);
        fire.Idle();
        Empty(fire, "retained enemy world ownership rechecked each frame");

        var sleepingFire = new FireCalls(world);
        EnemyController sleeper = Enemy(world);
        Collider2D first = Hitbox(sleeper), second = Hitbox(sleeper);
        sleepingFire.Enter(first); sleepingFire.Enter(second);
        sleepingFire.Fire.tickCounter = 0.25f;
        float duration = sleepingFire.Fire.durationCounter;
        HashSet<Collider2D> contact = sleepingFire.Colliders[sleeper];
        Check(world.SetWorldActive(false), "world sleeps");
        sleepingFire.Exit(first); sleepingFire.Exit(second);
        Check(!sleepingFire.Fire.isActiveAndEnabled, "sleep disables automatic fire updates");
        ContactsEqual(sleepingFire, sleeper, 2, "sleep exits preserve all contacts");
        Check(world.SetWorldActive(true), "world wakes");
        sleepingFire.Enter(first); sleepingFire.Enter(second);
        Check(ReferenceEquals(contact, sleepingFire.Colliders[sleeper]), "wake retains contact set identity");
        Equal(sleeper.health, 990f, "wake callbacks grant no extra entry damage");
        Equal(sleepingFire.Fire.tickCounter, 0.25f, "sleep/wake does not reset tick countdown");
        Equal(sleepingFire.Fire.durationCounter, duration, "sleep/wake does not reset duration");
        sleepingFire.Tick();
        Equal(sleeper.health, 980f, "wake resumes damage once per target");
    }

    private static void ReportsAndCallbacks(World world)
    {
        var recorder = new HitRecorder();
        BuffController source = Make("Source", world.ContentRoot).AddComponent<BuffController>();
        BuffDefinition recipe = ScriptableObject.CreateInstance<BuffDefinition>();
        owned.Add(recipe);
        var atom = new StackToActivationBuffAtom();
        Set(atom, "condition", recorder);
        Set(atom, "activation", new NoActivation());
        Set(recipe, "isPermanent", true);
        Set(recipe, "atoms", new List<BuffAtom> { atom });
        Check(source.TryAddBuff(recipe), "report observer granted");

        var fire = new FireCalls(world);
        fire.Fire.SetBuffSource(source);
        EnemyController enemy = Enemy(world);
        Collider2D first = Hitbox(enemy), second = Hitbox(enemy);
        fire.Enter(first); fire.Enter(second); fire.Tick();
        Check(recorder.Hits.Count == 2, "exactly one report for entry and one for multicollider tick");
        StackEventContext hit = recorder.Hits[0];
        Check(hit.Source == source.gameObject && hit.Target == enemy.gameObject, "report preserves source and target");
        Equal(hit.DamageDealt, 10f, "report uses actual entry damage");
        fire.Fire.damage = 0f;
        fire.Tick();
        Check(recorder.Hits.Count == 2, "zero actual damage produces no report");
        fire.Fire.damage = 50f;
        enemy.health = 3f;
        fire.Tick();
        Check(recorder.Hits.Count == 3, "lethal tick reports before deferred destruction");
        Equal(recorder.Hits[2].DamageDealt, 3f, "overkill report capped at remaining health");
        Empty(fire, "lethal target cleaned in same update");

        var entryFire = new FireCalls(world);
        entryFire.Fire.SetBuffSource(source);
        EnemyController entryTarget = Enemy(world);
        recorder.Callback = delegate(StackEventContext context) { entryTarget.TakeDamage(entryTarget.health); };
        entryFire.Enter(Hitbox(entryTarget));
        Empty(entryFire, "target killed during entry report cleaned before callback returns");
        recorder.Callback = null;

        var tickFire = new FireCalls(world);
        tickFire.Fire.SetBuffSource(source);
        EnemyController pending = Enemy(world), current = Enemy(world), processed = Enemy(world);
        tickFire.Enter(Hitbox(pending)); tickFire.Enter(Hitbox(current)); tickFire.Enter(Hitbox(processed));
        int reportsBefore = recorder.Hits.Count;
        recorder.Callback = delegate(StackEventContext context)
        {
            if (context.Target != current.gameObject) return;
            current.TakeDamage(current.health);
            pending.TakeDamage(pending.health);
            processed.TakeDamage(processed.health);
        };
        tickFire.Tick();
        recorder.Callback = null;
        Check(recorder.Hits.Count == reportsBefore + 2, "callback-killed pending target is skipped, without duplicate hits");
        Empty(tickFire, "current and already-processed callback victims cleaned in same update");

        var invalidatedFire = new FireCalls(world);
        invalidatedFire.Fire.SetBuffSource(source);
        EnemyController invalidated = Enemy(world);
        Collider2D invalidatedCollider = Hitbox(invalidated);
        invalidatedFire.Enter(invalidatedCollider);
        recorder.Callback = delegate(StackEventContext context) { invalidatedCollider.enabled = false; };
        invalidatedFire.Tick();
        recorder.Callback = null;
        Empty(invalidatedFire, "collider disabled during report cleaned before update returns");
    }

    private static string Allocations(World world)
    {
        MethodInfo counterMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
        if (counterMethod == null)
            throw new NotSupportedException("Runtime needs GC.GetAllocatedBytesForCurrentThread; allocation checks were not run.");
        var allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), counterMethod);
        allocated();
        long before = allocated();
        var probe = new byte[1024];
        long probeBytes = allocated() - before;
        GC.KeepAlive(probe);
        Check(probeBytes >= 1024, "allocation counter detects positive control");

        var fire = new FireCalls(world);
        fire.Fire.damage = 0f;
        const int targetCount = 16;
        var extras = new Collider2D[targetCount * 4];
        for (int i = 0; i < targetCount; i++)
        {
            EnemyController enemy = Enemy(world);
            fire.Enter(Hitbox(enemy));
            for (int j = 0; j < 4; j++)
            {
                Collider2D extra = Hitbox(enemy);
                extras[i * 4 + j] = extra;
                fire.Enter(extra);
            }
        }
        Action idle = fire.Idle;
        Action tick = fire.Tick;
        Action pruneAndReenter = delegate
        {
            for (int i = 0; i < extras.Length; i++) extras[i].enabled = false;
            fire.Idle();
            for (int i = 0; i < extras.Length; i++)
            {
                extras[i].enabled = true;
                fire.Enter(extras[i]);
            }
        };
        long idleBytes = Measure(idle, allocated, 4096);
        long tickBytes = Measure(tick, allocated, 1024);
        long churnBytes = Measure(pruneAndReenter, allocated, 512);
        Check(fire.Fire.burningList.Count == targetCount && fire.Colliders.Count == targetCount,
            "warm loops retain all distinct targets");
        foreach (var pair in fire.Colliders)
        {
            Check(pair.Value.Count == 5, "scratch removal/reentry preserves each target's contacts");
            Equal(pair.Key.health, 1000f, "zero-damage tick loop leaves target alive");
        }
        Check(idleBytes == 0, "warm non-tick updates allocate zero bytes: " + idleBytes);
        Check(tickBytes == 0, "warm zero-damage ticks without buff/presentation allocate zero bytes: " + tickBytes);
        Check(churnBytes == 0, "warm invalid-collider scratch reuse allocates zero bytes: " + churnBytes);
        return "warm allocations idle=" + idleBytes + ", tick=" + tickBytes + ", invalidation/reentry=" + churnBytes + " bytes";
    }

    private static long Measure(Action loop, Func<long> allocated, int iterations)
    {
        // Warm native bindings, delegates, HashSets and scratch capacity outside the measured region.
        for (int i = 0; i < 64; i++) loop();
        long before = allocated();
        for (int i = 0; i < iterations; i++) loop();
        return allocated() - before;
    }

    private sealed class FireCalls
    {
        public readonly LanternFire Fire;
        public readonly Action<Collider2D> Enter;
        public readonly Action<Collider2D> Exit;
        private readonly Action update;
        public readonly Dictionary<EnemyController, HashSet<Collider2D>> Colliders;

        public FireCalls(World world)
        {
            Fire = Make("Fire", world.ContentRoot).AddComponent<LanternFire>();
            Fire.damage = 10f;
            Fire.durationCounter = float.MaxValue;
            Enter = (Action<Collider2D>)Bind(typeof(Action<Collider2D>), Fire, "OnTriggerEnter2D");
            Exit = (Action<Collider2D>)Bind(typeof(Action<Collider2D>), Fire, "OnTriggerExit2D");
            update = (Action)Bind(typeof(Action), Fire, "Update");
            Colliders = (Dictionary<EnemyController, HashSet<Collider2D>>)Get(Fire, "burningColliders");
        }

        public void Idle() { Fire.tickCounter = float.MaxValue; update(); }
        public void Tick() { Fire.tickCounter = 0f; update(); }
    }

    private sealed class HitRecorder : StackCondition
    {
        public readonly List<StackEventContext> Hits = new List<StackEventContext>();
        public Action<StackEventContext> Callback;
        public override bool isValid { get { return true; } }
        public override bool Matches(StackEventContext context, GameObject owner)
        {
            Hits.Add(context);
            if (Callback != null) Callback(context);
            return false;
        }
    }

    private sealed class NoActivation : StackActivation
    {
        public override bool isValid { get { return true; } }
        public override bool TryActivate(BuffActivationContext context) { return false; }
    }

    private static World MakeWorld(string name)
    {
        World world = Make(name, null).AddComponent<World>();
        Set(world, "contentRoot", Make("Content", world.transform));
        Set(world, "worldId", WorldId.Material);
        Check(world.IsConfigured && world.Player == null, "isolated world configured without XP recipient");
        return world;
    }

    private static EnemyController Enemy(World world)
    {
        var enemy = Make("Enemy", world.ContentRoot).AddComponent<EnemyController>();
        enemy.enabled = false;
        enemy.health = 1000f;
        return enemy;
    }

    private static Collider2D Hitbox(EnemyController enemy)
    {
        GameObject obj = Make("Hitbox", enemy.transform);
        obj.tag = "Enemy";
        return obj.AddComponent<BoxCollider2D>();
    }

    private static GameObject Make(string name, Transform parent)
    {
        var obj = new GameObject("FirePerformanceChecks_" + name);
        owned.Add(obj);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = parent == null ? new Vector3(10000f, 10000f, 0f) : Vector3.zero;
        return obj;
    }

    private static Delegate Bind(Type type, object target, string method)
    {
        return Delegate.CreateDelegate(type, target, target.GetType().GetMethod(method, Flags));
    }

    private static object Get(object target, string name) { return target.GetType().GetField(name, Flags).GetValue(target); }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, Flags).SetValue(target, value); }

    private static void ContactsEqual(FireCalls fire, EnemyController enemy, int count, string label)
    {
        HashSet<Collider2D> colliders;
        Check(fire.Fire.burningList.Count == 1 && fire.Fire.burningList[0] == enemy && fire.Colliders.Count == 1
            && fire.Colliders.TryGetValue(enemy, out colliders) && colliders.Count == count, label);
    }

    private static void Empty(FireCalls fire, string label)
    {
        Check(fire.Fire.burningList.Count == 0 && fire.Colliders.Count == 0, label);
    }

    private static void Equal(float actual, float expected, string label)
    {
        Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < 0.001f,
            label + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FirePerformanceChecks failed after " + checks + " checks: " + label);
        checks++;
    }
}
