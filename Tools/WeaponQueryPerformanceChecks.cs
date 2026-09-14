using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

// Compile outside Assets against current Assembly-CSharp and Unity assemblies.
// Run synchronously on Unity's main thread in Play Mode; no frames or asset writes.
public static class WeaponQueryPerformanceChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(20000f, 20000f, 0f);
    const float Radius = 20f;
    static bool running;

    public static string Run()
    {
        if (!Application.isPlaying || running)
            throw new InvalidOperationException("Requires Play Mode on the main thread, without reentry.");
        Physics2D.SyncTransforms();
        if (Physics2D.OverlapCircleAll(Origin, Radius, Physics2D.AllLayers).Length != 0)
            throw new InvalidOperationException("Remote test area is occupied.");
        bool triggers = Physics2D.queriesHitTriggers;
        var random = Random.state;
        running = true;
        var fixture = new Fixture();
        try
        {
            fixture.Run();
            return fixture.Checks + " checks passed. " + fixture.Measurements
                + " Synchronous queries/manual contact callbacks only; no real impact, frame timing or sleep-duration test.";
        }
        finally
        {
            try { fixture.Dispose(); }
            finally
            {
                Physics2D.queriesHitTriggers = triggers;
                Random.state = random;
                running = false;
            }
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly List<GameObject> owned = new List<GameObject>();
        readonly List<Collider2D> results = new List<Collider2D>(1);
        readonly List<Collider2D> queryScratch = new List<Collider2D>();
        readonly List<EnemyController> crowd = new List<EnemyController>();
        public int Checks;
        public readonly StringBuilder Measurements = new StringBuilder();
        Transform content;
        PistolController pistol;
        DaggerController dagger;
        LightningController lightning;
        DaggerProjectile projectile;
        Func<EnemyController> pistolQuery, daggerQuery, lightningQuery;
        Func<EnemyController, EnemyController> bounceQuery;
        HashSet<EnemyController> hits;
        Dictionary<Collider2D, EnemyController> contacts;

        public void Run()
        {
            content = MakeWorld("Local");
            pistol = Make("Pistol", content, Vector3.zero).AddComponent<PistolController>();
            dagger = Make("Dagger", content, Vector3.zero).AddComponent<DaggerController>();
            lightning = Make("Lightning", content, Vector3.zero).AddComponent<LightningController>();
            projectile = Make("Projectile", content, Vector3.zero).AddComponent<DaggerProjectile>();
            foreach (Weapon weapon in new Weapon[] { pistol, dagger, lightning })
            {
                weapon.enabled = false;
                weapon.stats = new WeaponStats();
                Set(weapon, "attackRange", Radius);
            }
            projectile.range = Radius;
            var ownerScratch = new HashSet<object>();
            foreach (object owner in new object[] { pistol, dagger, lightning, projectile })
                Check(ownerScratch.Add(Get(owner, "queryScratch")), "each query owner has independent scratch");
            pistolQuery = Bind<Func<EnemyController>>(pistol, "FindClosestEnemy");
            daggerQuery = Bind<Func<EnemyController>>(dagger, "FindClosestEnemy");
            lightningQuery = Bind<Func<EnemyController>>(lightning, "FindRandomEnemy");
            bounceQuery = Bind<Func<EnemyController, EnemyController>>(projectile, "FindClosestEnemy");
            hits = (HashSet<EnemyController>)Get(projectile, "hitList");
            contacts = (Dictionary<Collider2D, EnemyController>)Get(projectile, "contacts");
            Physics2D.queriesHitTriggers = true;

            for (int i = 0; i < 160; i++)
                Make("NonEnemy", content, new Vector3(12f, i * .01f, (i % 9) - 4)).AddComponent<CircleCollider2D>();
            var far = Enemy("BeyondInitialCapacity", content, new Vector3(8f, 0f, 10f));
            CompareResults(161);
            Check(pistolQuery() == far && daggerQuery() == far && bounceQuery(null) == far
                && lightningQuery() == far, "enemy behind 160 non-enemy colliders is not truncated");

            var high = Enemy("HighZ", content, new Vector3(3f, 0f, 4f));
            var low = Enemy("LowZ", content, new Vector3(-3f, 0f, -4f));
            CompareResults(163);
            CompareTargets();
            Check(pistolQuery() == low && daggerQuery() == low && bounceQuery(null) == low,
                "equal 3D distance prefers lower collider Z");
            high.transform.position = Origin + Vector3.right * 5f;
            low.transform.position = Origin - Vector3.right * 5f;
            for (int i = 0; i < 8; i++)
            {
                high.GetComponent<Collider2D>().enabled = false;
                high.GetComponent<Collider2D>().enabled = true;
                CompareResults(163);
                CompareTargets();
            }

            var parentOnly = Enemy("ParentOnly", content, Vector3.right, false);
            var child = Make("ChildHitbox", parentOnly.transform, Vector3.right).AddComponent<BoxCollider2D>();
            child.gameObject.tag = "Enemy";
            var foreign = Enemy("Foreign", MakeWorld("ForeignWorld"), Vector3.zero);
            var dead = Enemy("Dead", content, Vector3.zero); dead.health = 0f;
            var inactive = Enemy("Inactive", content, Vector3.zero); inactive.gameObject.SetActive(false);
            var ignored = Enemy("IgnoreRaycast", content, Vector3.zero); ignored.gameObject.layer = 2;
            var trigger = Enemy("Trigger", content, Vector3.up * .5f);
            trigger.GetComponent<Collider2D>().isTrigger = true;
            foreach (bool includeTriggers in new[] { true, false, true })
            {
                Physics2D.queriesHitTriggers = includeTriggers;
                CompareResults(-1);
                CompareTargets();
                Check(!results.Contains(ignored.GetComponent<Collider2D>()), "default mask excludes Ignore Raycast");
                Check(results.Contains(trigger.GetComponent<Collider2D>()) == includeTriggers,
                    "query follows current queriesHitTriggers");
                Check(pistolQuery() == (includeTriggers ? trigger : LegacyNearest(pistol, false, null, false)),
                    "pistol retains direct-component filtering");
                if (!includeTriggers)
                    Check(daggerQuery() == parentOnly && bounceQuery(null) == parentOnly && pistolQuery() != parentOnly,
                        "only daggers resolve parent-only enemy");
            }
            Check(foreign.health == 1000f && dead.health == 0f && inactive.health == 1000f,
                "target queries never damage invalid enemies");

            ContactChecks(parentOnly, child, low);
            for (int i = 0; i < 140; i++)
            {
                var enemy = Enemy("Crowd", content, new Vector3(10f, i * .02f, i % 5));
                crowd.Add(enemy);
                if (i == 0)
                    for (int c = 0; c < 7; c++) enemy.gameObject.AddComponent<BoxCollider2D>();
            }
            CompareResults(-1);
            Check(results.Count > 300, "dense fixture has more than 300 colliders");
            CompareTargets();
            Check(LegacyCandidates().Count > 100, "more than 100 independent lightning candidates");
            RandomChecks();
            Measure();
            StrikeTimingChecks(far.GetComponent<Collider2D>());

            content.gameObject.SetActive(false);
            Physics2D.SyncTransforms();
            WeaponQuery.OverlapCircle(Origin, Radius, results, queryScratch);
            Check(results.Count == 1, "shrinking query removes stale local hits, leaving foreign collider");
            Check(pistolQuery() == null && daggerQuery() == null && bounceQuery(null) == null && lightningQuery() == null,
                "empty valid candidate set clears reused lists and hashset");
            content.gameObject.SetActive(true);
            CompareResults(-1);
            CompareTargets();
            RandomChecks();
            SortingChecks();
        }

        void SortingChecks()
        {
            const int count = 1025;
            Vector3 offset = Vector3.right * 100f;
            Vector3 origin = Origin + offset;
            Physics2D.SyncTransforms();
            Check(Physics2D.OverlapCircleAll(origin, Radius, Physics2D.AllLayers).Length == 0,
                "large sorting fixture area is empty");
            var candidates = new List<Collider2D>(count);
            var scratch = new List<Collider2D>();
            var sort = (Action<List<Collider2D>, List<Collider2D>>)Delegate.CreateDelegate(
                typeof(Action<List<Collider2D>, List<Collider2D>>),
                typeof(WeaponQuery).GetMethod("SortByZ", BindingFlags.Static | BindingFlags.NonPublic));
            for (int i = 0; i < count; i++)
                candidates.Add(Make("SortHit", content, offset + Vector3.forward * (count - 1 - i)).AddComponent<CircleCollider2D>());

            // Native queries may already be sorted, so exercise reverse input directly too.
            CheckStableSort(candidates, scratch, sort, "1025 reverse unique depths");
            int capacity = scratch.Capacity;
            Check(capacity >= count && scratch.Count == 0, "merge scratch grows fully and releases hit references");
            Physics2D.SyncTransforms();
            var legacy = Physics2D.OverlapCircleAll(origin, Radius);
            WeaponQuery.OverlapCircle(origin, Radius, results, queryScratch);
            Check(legacy.Length == count && results.Count == count, "large different-Z query has no lost hits");
            for (int i = 0; i < count; i++)
                Check(results[i] == legacy[i], "large different-Z query matches legacy order at " + i);

            var bytes = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),
                typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes));
            long allocated = Allocated(delegate { candidates.Reverse(); sort(candidates, scratch); }, bytes);
            Check(allocated == 0 && scratch.Capacity == capacity,
                "warm reverse merge reuses scratch without allocations; actual=" + allocated);
            Measurements.Append("ReverseSort bytes/128 warm calls=").Append(allocated).Append("; ");

            for (int i = 0; i < count; i++)
                candidates[i].transform.position = origin + Vector3.forward * ((count - 1 - i) / 3);
            CheckStableSort(candidates, scratch, sort, "reverse depth groups preserve equal-Z order");
            Random.InitState(41893);
            for (int i = count - 1; i > 0; i--)
            {
                int other = Random.Range(0, i + 1);
                Collider2D saved = candidates[i];
                candidates[i] = candidates[other];
                candidates[other] = saved;
            }
            CheckStableSort(candidates, scratch, sort, "shuffled depth groups preserve equal-Z order");
            Check(scratch.Capacity == capacity && scratch.Count == 0, "mixed-depth sort retains scratch capacity only");

            var unusedScratch = new List<Collider2D>();
            CheckStableSort(candidates, unusedScratch, sort, "already sorted different depths");
            Check(unusedScratch.Capacity == 0, "ordered scan does not allocate scratch");
            foreach (var collider in candidates) collider.transform.position = origin;
            CheckStableSort(candidates, unusedScratch, sort, "1025 flat hits retain input order");
            allocated = Allocated(delegate { sort(candidates, unusedScratch); }, bytes);
            Check(allocated == 0 && unusedScratch.Capacity == 0, "flat fast path has no scratch or warm allocations");
            Measurements.Append("FlatSort bytes/128 warm calls=").Append(allocated).Append("; ");
            CheckStableSort(new List<Collider2D>(), unusedScratch, sort, "empty input");
            CheckStableSort(new List<Collider2D> { candidates[0] }, unusedScratch, sort, "single hit");
        }

        void CheckStableSort(List<Collider2D> input, List<Collider2D> scratch,
            Action<List<Collider2D>, List<Collider2D>> sort, string label)
        {
            var order = new Dictionary<Collider2D, int>();
            for (int i = 0; i < input.Count; i++) order.Add(input[i], i);
            var expected = new List<Collider2D>(input);
            expected.Sort(delegate(Collider2D a, Collider2D b)
            {
                int depth = a.transform.position.z.CompareTo(b.transform.position.z);
                return depth != 0 ? depth : order[a].CompareTo(order[b]);
            });
            sort(input, scratch);
            Check(input.Count == expected.Count, label + " count");
            for (int i = 0; i < input.Count; i++) Check(input[i] == expected[i], label + " at " + i);
        }

        void StrikeTimingChecks(Collider2D sentinel)
        {
            var update = Bind<Action>(lightning, "Update");
            var buffer = (List<Collider2D>)Get(lightning, "enemiesInRange");
            lightning.transform.position = Origin + Vector3.right * 1000f;
            Set(lightning, "attackCounter", float.MaxValue);
            Set(lightning, "strikes", 2f);
            Set(lightning, "strikeCounter", float.MaxValue);
            Set(lightning, "strikeInterval", 1f);
            int count = buffer.Count;
            Check(count > 0, "strike timing starts with previous query results");
            update();
            Check(buffer.Count == count, "no overlap query between strikes");
            for (int i = 0; i < 2; i++)
            {
                buffer.Add(sentinel);
                Set(lightning, "strikeCounter", 0f);
                update();
                Check(buffer.Count == 0 && (float)Get(lightning, "strikes") == 1f - i,
                    "each due strike queries again, even without a target");
            }
            buffer.Add(sentinel);
            update();
            Check(buffer.Count == 1, "no overlap query after burst completion");
            lightning.transform.position = Origin;
        }

        void ContactChecks(EnemyController enemy, Collider2D first, EnemyController other)
        {
            var second = Make("SecondChild", enemy.transform, Vector3.right).AddComponent<CircleCollider2D>();
            second.gameObject.tag = "Enemy";
            var enter = Bind<Action<Collider2D>>(projectile, "OnTriggerEnter2D");
            var exit = Bind<Action<Collider2D>>(projectile, "OnTriggerExit2D");
            Set(projectile, "lastHit", enemy);
            enter(first); enter(first); enter(second);
            Check(contacts.Count == 2, "duplicate enter does not duplicate collider contact");
            CompareTargets();
            exit(first);
            Check(contacts.ContainsValue(enemy), "partial exit still blocks enemy");
            CompareTargets();
            hits.Add(other);
            Check(bounceQuery(enemy) == LegacyNearest(projectile, true, enemy, true), "unhit target priority matches legacy");
            exit(second); exit(second);
            Check(contacts.Count == 0, "all exits release enemy; duplicate exit is harmless");
            Check(bounceQuery(other) == LegacyNearest(projectile, true, other, true), "revisit after all exits matches legacy");
            foreach (var collider in Physics2D.OverlapCircleAll(Origin, Radius))
            {
                var candidate = collider.GetComponentInParent<EnemyController>();
                if (candidate != null) hits.Add(candidate);
            }
            Check(bounceQuery(enemy) == LegacyNearest(projectile, true, enemy, true), "all-hit fallback matches legacy");
            hits.Clear();
            Set(projectile, "lastHit", null);
        }

        void CompareResults(int expectedCount)
        {
            Physics2D.SyncTransforms();
            Collider2D[] old = Physics2D.OverlapCircleAll(Origin, Radius);
            WeaponQuery.OverlapCircle(Origin, Radius, results, queryScratch);
            Check(results.Count == old.Length, "complete collider count matches OverlapCircleAll");
            if (expectedCount >= 0) Check(results.Count == expectedCount, "fixture collider count");
            for (int i = 0; i < old.Length; i++)
                Check(results[i] == old[i], "collider order including equal Z matches legacy at " + i);
        }

        void CompareTargets()
        {
            Check(pistolQuery() == LegacyNearest(pistol, false, null, false), "pistol target matches legacy");
            Check(daggerQuery() == LegacyNearest(dagger, true, null, false), "dagger target matches legacy");
            Check(bounceQuery(null) == LegacyNearest(projectile, true, null, true), "bounce target matches legacy");
        }

        void RandomChecks()
        {
            var expected = LegacyCandidates();
            Random.InitState(71319);
            for (int i = 0; i < 256; i++)
            {
                var state = Random.state;
                var target = expected[Random.Range(0, expected.Count)];
                Random.state = state;
                Check(lightningQuery() == target, "one uniform draw per independent enemy, legacy seeded target");
            }
            var actual = (List<EnemyController>)Get(lightning, "availableEnemies");
            Check(actual.Count == expected.Count && new HashSet<EnemyController>(actual).Count == actual.Count,
                "multi-collider enemy has one candidate slot");
            var seen = Get(lightning, "seenEnemies");
            crowd[0].gameObject.SetActive(false);
            Physics2D.SyncTransforms();
            lightningQuery();
            Check(ReferenceEquals(actual, Get(lightning, "availableEnemies")) && ReferenceEquals(seen, Get(lightning, "seenEnemies")),
                "lightning reuses candidate containers");
            Check(!actual.Contains(crowd[0]) && actual.Count == expected.Count - 1, "next selection re-queries changed candidates");
            crowd[0].gameObject.SetActive(true);
            Physics2D.SyncTransforms();
        }

        void Measure()
        {
            MethodInfo counter = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            if (counter == null) throw new NotSupportedException("Per-thread allocation counter is required.");
            var bytes = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), counter);
            Action query = delegate { WeaponQuery.OverlapCircle(Origin, Radius, results, queryScratch); };
            long warm = Allocated(query, bytes);
            Check(warm == 0, "warmed reusable overlap allocates zero managed bytes; actual=" + warm);
            Report("Overlap", warm, Allocated(delegate { Physics2D.OverlapCircleAll(Origin, Radius); }, bytes));
            MeasureTarget("Pistol", delegate { pistolQuery(); }, delegate { LegacyNearest(pistol, false, null, false); }, bytes);
            MeasureTarget("Dagger", delegate { daggerQuery(); }, delegate { LegacyNearest(dagger, true, null, false); }, bytes);
            MeasureTarget("Bounce", delegate { bounceQuery(null); }, delegate { LegacyNearest(projectile, true, null, true); }, bytes);
            foreach (var collider in results)
            {
                var enemy = collider.GetComponentInParent<EnemyController>();
                if (enemy != null) hits.Add(enemy);
            }
            MeasureTarget("BounceFallback", delegate { bounceQuery(null); }, delegate { LegacyNearest(projectile, true, null, true); }, bytes);
            hits.Clear();
            MeasureTarget("Lightning", delegate { lightningQuery(); }, delegate
            {
                var candidates = LegacyCandidates();
                if (candidates.Count > 0) _ = candidates[Random.Range(0, candidates.Count)];
            }, bytes);
        }

        void MeasureTarget(string label, Action current, Action legacy, Func<long> bytes)
        {
            long warm = Allocated(current, bytes), old = Allocated(legacy, bytes);
            Report(label, warm, old);
            Check(warm < old, label + " warm allocations lower than legacy; current=" + warm + " legacy=" + old);
        }

        static long Allocated(Action action, Func<long> bytes)
        {
            for (int i = 0; i < 32; i++) action();
            bytes();
            long start = bytes();
            for (int i = 0; i < 128; i++) action();
            return bytes() - start;
        }

        void Report(string label, long current, long legacy)
        {
            Measurements.Append(label).Append(" bytes/128 warm calls=").Append(current)
                .Append(" (legacy=").Append(legacy).Append("); ");
        }

        EnemyController LegacyNearest(Component source, bool parents, EnemyController ignore, bool bounce)
        {
            var colliders = Physics2D.OverlapCircleAll(source.transform.position, Radius);
            for (int pass = 0; pass < (bounce ? 2 : 1); pass++)
            {
                EnemyController nearest = null;
                float closest = Mathf.Infinity;
                foreach (var collider in colliders)
                {
                    var enemy = parents ? collider.GetComponentInParent<EnemyController>() : collider.GetComponent<EnemyController>();
                    if (!Valid(source, enemy) || enemy == ignore || (bounce && (contacts.ContainsValue(enemy) || (pass == 0 && hits.Contains(enemy))))) continue;
                    float distance = Vector3.Distance(source.transform.position, enemy.transform.position);
                    if (distance < closest) { closest = distance; nearest = enemy; }
                }
                if (nearest != null) return nearest;
            }
            return null;
        }

        List<EnemyController> LegacyCandidates()
        {
            var candidates = new List<EnemyController>();
            foreach (var collider in Physics2D.OverlapCircleAll(lightning.transform.position, Radius))
            {
                var enemy = collider.GetComponent<EnemyController>();
                if (Valid(lightning, enemy) && !candidates.Contains(enemy)) candidates.Add(enemy);
            }
            return candidates;
        }

        static bool Valid(Component source, EnemyController enemy)
        {
            return enemy != null && enemy.gameObject.activeInHierarchy && enemy.health > 0f && World.CanInteract(source, enemy);
        }

        Transform MakeWorld(string name)
        {
            var world = Make(name, null, Vector3.zero).AddComponent<World>();
            var root = Make("Content", world.transform, Vector3.zero);
            Set(world, "contentRoot", root);
            return root.transform;
        }

        EnemyController Enemy(string name, Transform parent, Vector3 offset, bool collider = true)
        {
            var go = Make(name, parent, offset);
            go.tag = "Enemy";
            if (collider) go.AddComponent<CircleCollider2D>();
            var enemy = go.AddComponent<EnemyController>();
            enemy.enabled = false;
            enemy.health = 1000f;
            return enemy;
        }

        GameObject Make(string name, Transform parent, Vector3 offset)
        {
            var go = new GameObject("WeaponQueryChecks_" + name);
            owned.Add(go);
            go.transform.SetParent(parent, false);
            go.transform.position = Origin + offset;
            return go;
        }

        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("WeaponQueryPerformanceChecks after " + Checks + " checks: " + label);
            Checks++;
        }

        public void Dispose()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            Physics2D.SyncTransforms();
        }
    }

    static object Get(object value, string name) { return value.GetType().GetField(name, Flags).GetValue(value); }
    static void Set(object value, string name, object field) { value.GetType().GetField(name, Flags).SetValue(value, field); }
    static T Bind<T>(object target, string name) where T : class
    {
        return Delegate.CreateDelegate(typeof(T), target, target.GetType().GetMethod(name, Flags)) as T;
    }
}
