using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Only staged in the isolated Editor assembly. Reflection sets bounded test preconditions,
// never calls Start/Update/Fire. Native Unity owns all firing, timing, physics and commits.
public static class FiringFlipChecks
{
    public static int Passed, Failed;
    public static string Scenario = "preflight";
    public static readonly List<string> Evidence = new List<string>(), Completed = new List<string>();
    public static readonly List<Emission> Emissions = new List<Emission>();
    public sealed class Emission
    {
        public string key; public int frame; public float time, progress;
        public bool flipping, fusion; public FiringFlipEmissionProbe probe;
    }
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type[] Types = { typeof(PistolController), typeof(LanternController), typeof(BowController), typeof(DaggerController), typeof(SniperController), typeof(LightningController) };
    static readonly string[] PrefabFields = { "bullet", "lantern", "arrow", "dagger", "bullet", "lightningPrefab" };
    public static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, Flags).GetValue(o);
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Flags).SetValue(o, value);
    static void Optional(object o, string field, object value) { o.GetType().GetField(field, Flags)?.SetValue(o, value); }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .00001f;
    public static void Note(string s) { Evidence.Add(Scenario + " " + s); Debug.Log("FIRING_NATIVE " + s); }
    public static void Check(bool ok, string s) { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + s); }
    static void Require(bool ok, string s) { Check(ok, s); if (!ok) throw new InvalidOperationException(s); }
    static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    static IEnumerator Until(Func<bool> ready, float seconds, string label)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
    public static void Defaults()
    {
        var go = new GameObject("inactive defaults probe"); go.SetActive(false);
        try
        {
            var flow = go.AddComponent<StateSwitchController>();
            Check(flow.SwitchInterval == 30 && Get<float>(flow, "warningDuration") == 5 && Near(Get<float>(flow, "flipDuration"), .8f), "component defaults 30/5/.8");
            Check(((DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(WorldManager), typeof(DefaultExecutionOrder))).order == -100
                && ((DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(StateSwitchController), typeof(DefaultExecutionOrder))).order == -50, "declared order manager -100 then flow -50");
            int order = MonoImporter.GetExecutionOrder(MonoScript.FromMonoBehaviour(flow));
                        Check(order == 0 || order == -50, "no conflicting flow importer override (0 delegates to attribute); native boundary test is authoritative; importer=" + order);
        }
        finally { Object.DestroyImmediate(go); }
    }
    public static void SceneBinding(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var flow = roots.SelectMany(r => r.GetComponentsInChildren<StateSwitchController>(true)).Single(f => f.isActiveAndEnabled);
        var run = roots.SelectMany(r => r.GetComponentsInChildren<RunStageController>(true)).Single(r => r.isActiveAndEnabled);
        var manager = Get<WorldManager>(flow, "worldManager");
        var players = Get<World[]>(manager, "worlds").Select(w => w.Player).ToArray();
        Check(flow.SwitchInterval == 30 && Get<float>(flow, "warningDuration") == 5 && Near(Get<float>(flow, "flipDuration"), .8f)
            && run.FinaleStartTime == 480, scene.name + " authored interval30 warning5 flip.8 finale480");
        Check(Get<float>(run, "bossHealth") == 600 && Get<EnemyController>(run, "bossPrefab").health == 600, scene.name + " preexisting boss600 preserved");
        foreach (Type type in Types)
        {
            var weapons = players.SelectMany(p => p.GetComponentsInChildren(type, true)).Cast<Weapon>().ToArray();
            if (type == typeof(SniperController) && weapons.Length == 0)
                        {
                            Require(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PhysicsBullet.prefab").GetComponent<PhysicsBullet>() != null,
                                scene.name + " deferred Sniper: imported PhysicsBullet available for Play-only Pistol-config adaptation");
                            Note("FIXTURE Sniper absent from authored loadouts (not merely inactive); no source loadout will be changed.");
                            continue;
                        }
                        Require(weapons.Length == 2, scene.name + " two authored " + type.Name + " configurations");
            Check(weapons.All(w => MonoImporter.GetExecutionOrder(MonoScript.FromMonoBehaviour(w)) == 0), type.Name + " effective execution order0 after flow");
            if (type == typeof(BowController) || type == typeof(DaggerController) || type == typeof(SniperController))
                Check(weapons.All(w => w.GetComponentInParent<PlayerController>(true).unassignedWeapons.Contains(w)), scene.name + " authored unassigned " + type.Name);
        }
    }
    public static IEnumerator Run()
    {
        Require(Application.isPlaying && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "native rendered Play Mode");
        Application.runInBackground = true;
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            Scenario = scene;
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null; yield return null;
            using (var f = new Fixture())
            {
                yield return f.Setup();
                yield return f.Flip(true);
                yield return f.Flip(false);
                yield return f.Releases();
                yield return f.Fusion();
                f.OriginalsUnchanged();
                Note("EMISSIONS total=" + Emissions.Count + " normalFlip=" + Emissions.Count(e => e.flipping && !e.key.StartsWith("standalone") && !e.key.Contains("Scythe")));
            }
            Completed.Add(scene);
        }
    }
    sealed class Fixture : IDisposable
    {
        readonly List<GameObject> owned = new List<GameObject>();
        readonly List<Tuple<Weapon, string>> originals = new List<Tuple<Weapon, string>>();
        readonly List<GameObject> incomingSentinels = new List<GameObject>();
        readonly Dictionary<Behaviour, bool> quiet = new Dictionary<Behaviour, bool>();
        readonly Dictionary<World, Weapon[]> weapons = new Dictionary<World, Weapon[]>();
        readonly Dictionary<World, EnemyController> enemies = new Dictionary<World, EnemyController>();
        WorldManager manager; StateSwitchController flow; World[] worlds; Transform templates;
        Weapon[] standalone; ScytheController scythe; ShieldController shield; LanternFire fire;
        int commits, midpoints; float midpointProgress; bool outgoingCleanAtEvent;
        List<GameObject> oldShots; World outgoing;
        float savedScale;
        GameObject Make(string name, Transform parent, bool active = true)
        {
            var go = new GameObject("FiringFixture " + name); go.SetActive(active); go.transform.SetParent(parent, false); owned.Add(go); return go;
        }
        string Key(Weapon w) => w.GetComponent<FiringFlipStartProbe>().key;
        int Count(Weapon w) => Emissions.Count(e => e.key == Key(w));
        EnemyController Enemy(Transform parent, Vector3 position)
        {
            var go = Make("high HP stationary target", parent); go.transform.position = position; go.tag = "Enemy";
            var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().radius = .4f;
            var e = go.AddComponent<EnemyController>(); e.RB = body; e.health = 1000000; e.attack = 0; e.moveSpeed = 0;
            return e;
        }
        Weapon Config(World world, Type type)
                {
                    var found = world.Player.GetComponentsInChildren(type, true).Cast<Weapon>().SingleOrDefault();
                    if (found != null) return found;
                    Require(type == typeof(SniperController) || type == typeof(ShieldController), "only documented deferred Sniper/Shield need adapted config");
                    var config = (Weapon)Make("deferred " + type.Name + " config", templates, false).AddComponent(type);
                    var pistol = world.Player.GetComponentInChildren<PistolController>(true);
                    config.stats = JsonUtility.FromJson<WeaponStats>(JsonUtility.ToJson(pistol.stats));
                    config.player = world.Player;
                    if (config is SniperController sniper)
                    {
                        sniper.attackSpeed = Get<float>(pistol, "attackSpeed"); sniper.attackDamage = Get<float>(pistol, "attackDamage");
                        sniper.attackRange = Get<float>(pistol, "attackRange"); sniper.projectileSpeed = Get<float>(pistol, "projectileSpeed");
                        sniper.bullet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PhysicsBullet.prefab");
                    }
                    else ((ShieldController)config).shieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Shield.prefab");
                    return config;
                }
                Weapon Clone(Weapon config, Transform parent, string key, string prefabField)
        {
            if (!originals.Any(o => o.Item1 == config)) originals.Add(Tuple.Create(config, EditorJsonUtility.ToJson(config)));
            var w = Object.Instantiate(config, templates); owned.Add(w.gameObject);
            w.name = "FiringFixture " + key; w.gameObject.SetActive(false);
            w.stats = new WeaponStats(); w.player = parent.GetComponent<PlayerController>();
            Optional(w, "buffHolder", w.player == null ? null : w.player.GetComponent<BuffController>());
            Optional(w, "attackSpeed", 2f); Optional(w, "attackDamage", .1f); Optional(w, "projectileSpeed", .1f);
            Optional(w, "attackRange", 30f); Optional(w, "duration", 20f);
            Optional(w, "amount", w is LightningController ? 4f : 0f);
            if (prefabField != null)
            {
                var sourcePrefab = Get<GameObject>(config, prefabField);
                Require(sourcePrefab != null && EditorUtility.IsPersistent(sourcePrefab), key + " source prefab reference imported");
                var prefab = Object.Instantiate(sourcePrefab, templates); prefab.name = "FiringFixture template " + key;
                prefab.SetActive(true); var probe = prefab.AddComponent<FiringFlipEmissionProbe>(); probe.key = key;
                Set(w, prefabField, prefab);
                                if (w is LanternController)
                                    foreach (string effect in new[] { "fire", "explosion" })
                                    {
                                        var lantern = prefab.GetComponent<LanternProjController>();
                                        var effectCopy = Object.Instantiate(Get<GameObject>(lantern, effect), templates);
                                        effectCopy.AddComponent<FiringFlipEmissionProbe>().key = "cleanup/" + key + "/" + effect;
                                        Set(lantern, effect, effectCopy);
                                    }
            }
            var start = w.gameObject.AddComponent<FiringFlipStartProbe>(); start.key = key;
            w.transform.SetParent(parent, false); w.transform.localPosition = Vector3.zero;
            w.enabled = true; w.gameObject.SetActive(true);
            return w;
        }
        public IEnumerator Setup()
        {
            savedScale = Time.timeScale; Time.timeScale = 1; Emissions.Clear();
            manager = All<WorldManager>().Single(m => m.isActiveAndEnabled);
            flow = All<StateSwitchController>().Single(f => f.isActiveAndEnabled);
            worlds = All<World>().Where(w => w.Manager == manager).ToArray();
            Require(manager.IsInitialized && worlds.Length == 2 && manager.CurrentWorldId == WorldId.Material, "initialized two-world scene");
            flow.AutomaticSwitchingEnabled = false;
            foreach (var b in All<MonoBehaviour>())
                if (b is EnemySpawner || b is RunStageController || b is EnemyController)
                { quiet[b] = b.enabled; b.enabled = false; }
            // Original weapons and inventory entries remain untouched and enabled as authored.
            // Count only cloned prefab births, not scene combat or objects observed after destruction.
            templates = Make("inactive templates", null, false).transform;
            manager.WorldChanged += Changed; flow.TransitionMidpoint += Midpoint;
            foreach (var world in worlds)
            {
                weapons[world] = Types.Select((t, i) => Clone(Config(world, t),
                    world.Player.transform, world.WorldId + "/" + t.Name, PrefabFields[i])).ToArray();
                enemies[world] = Enemy(world.ContentRoot, world.Player.transform.position + Vector3.right * 6);
            }
            var current = manager.CurrentWorld;
            // A separate unscoped hero proves the gate reads its OWN World, not a singleton manager.
            var hero = Make("worldless hero", null).AddComponent<PlayerController>(); hero.transform.position = new Vector3(20000, 20000, 0);
            hero.gameObject.AddComponent<BuffController>();
            standalone = Types.Select((t, i) => Clone(originals.First(o => o.Item1.GetType() == t).Item1, hero.transform, "standalone/" + t.Name, PrefabFields[i])).ToArray();
            Enemy(null, hero.transform.position + Vector3.right * 6); current.Player.BindAsCurrent();
            scythe = (ScytheController)Clone(current.Player.GetComponentInChildren<ScytheController>(true), current.Player.transform, "control/Scythe", "scythe");
            shield = (ShieldController)Clone(Config(current, typeof(ShieldController)), current.Player.transform, "control/Shield", null);
            shield.duration = 100; shield.orbitSpeed = 90;
            var firePrefab = Get<GameObject>(weapons[current][1], "lantern").GetComponent<LanternProjController>().fire;
            fire = Object.Instantiate(firePrefab, current.ContentRoot).GetComponent<LanternFire>(); owned.Add(fire.gameObject);
            fire.transform.position = enemies[current].transform.position; fire.damage = .1f; fire.duration = 100; fire.tickRate = .03f;
            Physics2D.SyncTransforms();
            yield return null; yield return null; yield return null;
            Require(weapons[current].All(w => w.GetComponent<FiringFlipStartProbe>().started)
                && weapons[Other()].All(w => !w.GetComponent<FiringFlipStartProbe>().started), "outgoing started, incoming six clones never Start before first midpoint");
            Require(fire.burningList.Contains(enemies[current]), "real physics established sustained flame contact");
            Note("FIXTURE cloned six configs/player + all six worldless controls; cloned emission prefabs preserve imported component wiring; clone-only speed=.1 damage=.1 attackSpeed=2 amount1 (Lightning4). Bow/Dagger originals remain unassigned; deferred Sniper adapts cloned Pistol scalars/stats with imported PhysicsBullet; deferred Shield uses cloned stats/imported Shield prefab; neither enters the source loadout. Stationary high-HP targets; spawner/run/preexisting enemy updates quieted; original weapons untouched. No production asset saves or synthetic Updates.");
        }
        World Other() => worlds.Single(w => w != manager.CurrentWorld);
        void Changed()
        {
            commits++;
            outgoingCleanAtEvent = oldShots == null || oldShots.All(g => g == null || !g.activeSelf);
        }
        void Midpoint() { midpoints++; midpointProgress = flow.FlipProgress; }
        sealed class Snapshot
        {
            public Weapon weapon; public float attack, strike, strikes;
            public Collider2D[] query;
            public Snapshot(Weapon w)
            {
                weapon = w; attack = Get<float>(w, "attackCounter");
                if (w is LightningController) { strike = Get<float>(w, "strikeCounter"); strikes = Get<float>(w, "strikes"); }
                query = w.GetType().GetField("enemiesInRange", Flags) == null ? null : Get<List<Collider2D>>(w, "enemiesInRange").ToArray();
            }
            public bool Frozen() => Near(attack, Get<float>(weapon, "attackCounter")) && (!(weapon is LightningController)
                || (Near(strike, Get<float>(weapon, "strikeCounter")) && strikes == Get<float>(weapon, "strikes")))
                && (query == null || query.SequenceEqual(Get<List<Collider2D>>(weapon, "enemiesInRange")));
        }
        public IEnumerator Flip(bool first)
        {
            Scenario = SceneManager.GetActiveScene().name + (first ? "/firstStart-due" : "/reverse-positive-cooldown");
            outgoing = manager.CurrentWorld; var incoming = Other();
            int[] beforeWarning = weapons[outgoing].Select(Count).ToArray();
            Require(flow.RequestSwitch(incoming.WorldId), "real warning request accepted");
            foreach (var w in weapons[outgoing]) Set(w, "attackCounter", 0f);
            yield return null; yield return null;
            for (int i = 0; i < Types.Length; i++) Check(Count(weapons[outgoing][i]) > beforeWarning[i], Types[i].Name + " emits during warning");
            Check(!manager.IsWorldTransitioning && flow.WarningProgress > 0 && flow.RemainingTime > .4f, "warning is NOT firing suspension");
            var lightning = weapons[outgoing].OfType<LightningController>().Single();
            Require(Get<float>(lightning, "strikes") > 0, "warning naturally queues Lightning remainder");
            foreach (var w in weapons.Values.SelectMany(v => v)) Set(w, "attackCounter", first ? 0f : .35f);
            Set(lightning, "attackCounter", .45f); Set(lightning, "strikeCounter", .08f);
            // Empty the already allocated query buffers. Any target query during the flip
            // repopulates them with the live target; unchanged empty buffers prove the early return.
            foreach (var w in weapons.Values.SelectMany(v => v))
                if (w.GetType().GetField("enemiesInRange", Flags) != null) Get<List<Collider2D>>(w, "enemiesInRange").Clear();
            var snapshots = weapons.Values.SelectMany(v => v).Select(w => new Snapshot(w)).ToArray();
            var frozen = snapshots.ToDictionary(s => s, s => true);
            oldShots = Emissions.Where(e => e.key.StartsWith(outgoing.WorldId + "/") && e.probe != null
                && e.probe.GetComponent<IWorldProjectile>() != null).Select(e => e.probe.gameObject).Distinct().ToList();
            Require(oldShots.Count >= 5, "old real outgoing projectile fixture exists before narrowing");
            var bullet = oldShots.Select(g => g.GetComponent<BulletController>()).First(b => b != null);
            Vector3 bulletPosition = bullet.transform.position;
            incomingSentinels.Clear();
                        for (int i = 0; i < 5; i++)
                        {
                            var config = originals.First(o => o.Item1.GetType() == Types[i]).Item1;
                            var sentinel = Object.Instantiate(Get<GameObject>(config, PrefabFields[i]), templates);
                            sentinel.SetActive(false); sentinel.transform.SetParent(incoming.ContentRoot, false);
                            owned.Add(sentinel); incomingSentinels.Add(sentinel);
                        }
                        int before = Emissions.Count, c = commits, m = midpoints;
            float fireLife = fire.durationCounter; float enemyClock = 100;
                        float burnedHealth = enemies[outgoing].health;
                        if (first) fire.tickCounter = 0;
            if (first) { enemies[outgoing].hitCounter = enemyClock; Set(scythe, "attackCounter", 0f); }
            float shieldClock = Get<float>(shield, "timer"); int scytheBefore = Count(scythe);
            int[] standaloneBefore = standalone.Select(Count).ToArray();
            foreach (var w in standalone) Set(w, "attackCounter", 0f);
            // Place just at the half-duration boundary after this frame's Updates. The NEXT
            // native flow Update must publish narrowing before ANY order-0 weapon Update.
            Set(flow, "timerCounter", .4f);
            yield return null;
            Require(flow.IsFlipping && flow.FlipProgress < .5f && commits == c, "first native narrowing frame observed, no early commit");
            Check(!Emissions.Skip(before).Any(e => e.flipping && weapons.Values.SelectMany(v => v).Any(w => Key(w) == e.key)), "zero new six-controller emissions on narrowing START frame");
            Check(bullet != null && bullet.transform.position != bulletPosition, "existing in-flight bullet keeps moving while firing suspended");
            if (first)
            {
                Check(fire.durationCounter < fireLife && fire.burningList.Contains(enemies[outgoing]) && enemies[outgoing].health < burnedHealth,
                                    "sustained flame stays active, advances and damages during narrowing");
                Check(Get<float>(shield, "timer") < shieldClock && shield.shieldPivot.activeInHierarchy && Count(scythe) > scytheBefore,
                    "shield timer/orbit remains active; scythe still emits during narrowing");
                Check(enemies[outgoing].hitCounter < enemyClock && enemies[outgoing].enabled, "enemy native Update still advances during narrowing");
            }
            int narrowFrames = 0, unfoldFrames = 0; bool precommitAlive = true, incomingStarted = false, cleanupChecked = false;
            float deadline = Time.realtimeSinceStartup + 8;
            while (flow.IsFlipping)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("flip completion");
                foreach (var s in snapshots) frozen[s] &= s.Frozen();
                if (flow.FlipProgress < .5f) { narrowFrames++; precommitAlive &= oldShots.All(g => g != null); }
                else
                {
                    unfoldFrames++; incomingStarted |= weapons[incoming].All(w => w.GetComponent<FiringFlipStartProbe>().started);
                    if (!cleanupChecked && unfoldFrames > 1)
                    {
                        Check(oldShots.All(g => g == null) && outgoingCleanAtEvent, "single midpoint deactivates then destroys OLD outgoing projectiles");
                        Check(incomingSentinels.All(g => g != null && !g.activeSelf), "midpoint preserves incoming inactive real projectiles without Start");
                                                Check(!Emissions.Skip(before).Any(e => e.key.StartsWith("cleanup/" + outgoing.WorldId + "/")), "outgoing midcommit cleanup creates no lantern fire/explosion");
                                                cleanupChecked = true;
                    }
                }
                yield return null;
            }
            Require(narrowFrames > 0 && unfoldFrames > 0 && cleanupChecked, "both narrowing and unfolding sampled: " + narrowFrames + "/" + unfoldFrames + " frames");
            Check(precommitAlive && commits == c + 1 && midpoints == m + 1 && Near(midpointProgress, .5f)
                && manager.CurrentWorld == incoming, "old shots retained before exactly ONE midpoint at .5");
            Check(incomingStarted, "incoming first/native Start occurs inside unfolding");
            foreach (var s in snapshots)
            {
                var births = Emissions.Skip(before).Where(e => e.key == Key(s.weapon) && e.flipping).ToArray();
                Check(births.Length == 0 && frozen[s] && s.weapon.enabled && s.weapon.gameObject.activeSelf,
                    Key(s.weapon) + " zero NEW emissions, frozen attack/queued strike/query state; component never disabled");
            }
            for (int i = 0; i < Types.Length; i++) Check(Count(standalone[i]) > standaloneBefore[i] && !World.GetFor(standalone[i]), Types[i].Name + " worldless fires despite another manager flipping");
            if (first)
            {
                foreach (var w in weapons[incoming]) Check(Count(w) > 0, Key(w) + " firstStart due attack releases on fully-unfolded completion frame");
                Check(fire != null && shield.shieldPivot == null && !Get<bool>(shield, "isShieldActive"),
                                    "legacy midpoint retains sustained flame but clears IWorldProjectile shield and releases its owner cycle");
            }
            else
            {
                foreach (var w in weapons[incoming])
                    Check(Get<float>(w, "attackCounter") > 0 && Near(Get<float>(w, "attackCounter"), .35f - Time.deltaTime), Key(w) + " completion subtracts only current delta, no accumulated cooldown");
                int[] releaseBefore = weapons[incoming].Select(Count).ToArray(); float releasedAt = Time.time;
                yield return Until(() => weapons[incoming].Select((w, i) => Count(w) > releaseBefore[i]).All(x => x), 3, "natural cooldown resume");
                foreach (var w in weapons[incoming])
                {
                    var next = Emissions.Skip(before).First(e => e.key == Key(w));
                    float expected = w is LightningController ? snapshots.Single(s => s.weapon == w).strike : .35f;
                    Check(next.time - releasedAt >= expected - .08f && next.time - releasedAt < expected + .15f,
                        Key(w) + " resumes naturally after preserved " + (w is LightningController ? "queued strike" : "cooldown") + " expected=" + expected + " elapsed=" + (next.time - releasedAt).ToString("F4"));
                }
            }
            // Give the outgoing queued burst its original owner back on the reverse pass.
            if (!first) Check(Get<float>(weapons[incoming].OfType<LightningController>().Single(), "strikes") > 0, "Lightning resumes queued/burst processing rather than a latched lock");
            yield return null;
        }
        public IEnumerator Releases()
        {
            Scenario = SceneManager.GetActiveScene().name + "/release-paths";
            foreach (string release in new[] { "pause", "cancel", "disable" })
            {
                Require(flow.RequestNextWorldSwitch(), release + " request"); Set(flow, "timerCounter", .4f);
                yield return null; Require(flow.IsFlipping, release + " starts narrowing");
                var current = weapons[manager.CurrentWorld];
                foreach (var w in current) Set(w, "attackCounter", .15f);
                if (release == "pause")
                {
                    var snapshots = current.Select(w => new Snapshot(w)).ToArray(); float progress = flow.FlipProgress;
                    Time.timeScale = 0; yield return null; yield return null; yield return null;
                    Check(flow.IsFlipping && flow.FlipProgress == progress && snapshots.All(s => s.Frozen()), "pause freezes flip and weapons, retains correct lock");
                    Time.timeScale = 1;
                    yield return Until(() => !flow.IsFlipping, 4, "pause resumes flip");
                    current = weapons[manager.CurrentWorld];
                }
                else if (release == "cancel") flow.CancelTransition();
                else flow.enabled = false;
                Check(!manager.IsWorldTransitioning, release + " releases manager gate without latched state");
                int[] before = current.Select(Count).ToArray();
                yield return Until(() => current.Select((w, i) => Count(w) > before[i]).All(x => x), 3, release + " firing resumes");
                Check(current.All(w => w.enabled), release + " all six resume naturally with components enabled");
                if (!flow.enabled) flow.enabled = true;
                flow.CancelTransition(); yield return null;
            }
        }
        public IEnumerator Fusion()
        {
            Scenario = SceneManager.GetActiveScene().name + "/fusion-control";
            Require(flow.RequestNextWorldSwitch(), "normal flip requested before official finale cancellation");
            Set(flow, "timerCounter", .4f); yield return null;
            Require(flow.IsFlipping, "official finale begins from an active normal flip");
            var run = All<RunStageController>().Single(); run.enabled = true;
            // ValidateConfiguration requires enabled spawners. No frame runs between
            // restoring them and the official API, which stops them via ClearEncounter.
            foreach (var spawner in Get<EnemySpawner[]>(run, "spawners")) spawner.enabled = true;
            int before = Emissions.Count;
            Require(manager.TryEnterFusion() && manager.IsFusionTransitioning && !manager.IsWorldTransitioning, "official finale entrance uses separate fusion transition, not normal flip gate");
            // Official finale clears the encounter. Supply fresh targets AFTER that cleanup,
            // without replacing the entrance or allowing target absence to fake a firing lock.
            foreach (var world in worlds) enemies[world] = Enemy(world.ContentRoot, manager.FusionPlayer.transform.position + Vector3.right * 6);
            Physics2D.SyncTransforms();
            foreach (var w in weapons.Values.SelectMany(v => v)) Set(w, "attackCounter", 0f);
            yield return null; yield return null;
            foreach (var w in weapons.Values.SelectMany(v => v))
                Check(Emissions.Skip(before).Any(e => e.key == Key(w) && e.fusion && !e.flipping), Key(w) + " firing remains enabled during fusion entrance");
            yield return Until(() => !manager.IsFusionTransitioning, 6, "fusion entrance completion");
            Check(!manager.IsWorldTransitioning && manager.IsFinalFusion && run.FinaleStartTime == 480 && Get<float>(run, "bossHealth") == 600,
                "fusion completes with normal gate released; authored finale480/boss600 unchanged");
        }
        public void OriginalsUnchanged()
        {
            // Runtime-private fields are not serialized; original owner resolution may occur
            // on first activation, so verify prefab/config and inventory rather than whole JSON.
            foreach (var row in originals)
            {
                // Clone operations must never replace the original's authored prefab reference.
                int i = Array.IndexOf(Types, row.Item1.GetType());
                Check(i < 0 || EditorUtility.IsPersistent(Get<GameObject>(row.Item1, PrefabFields[i])), row.Item1.GetType().Name + " original prefab reference still persistent");
            }
            Check(worlds.All(w => w.Player.unassignedWeapons.Any(x => x is BowController)
                && w.Player.unassignedWeapons.Any(x => x is DaggerController)
                && !w.Player.assignedWeapons.Concat(w.Player.unassignedWeapons).Any(x => x is SniperController)), "original Bow/Dagger remain unassigned; deferred Sniper never inserted into inventory");
        }
        public void Dispose()
        {
            Time.timeScale = savedScale;
            if (manager != null) manager.WorldChanged -= Changed;
            if (flow != null) flow.TransitionMidpoint -= Midpoint;
            foreach (var go in owned) if (go != null) Object.Destroy(go);
            foreach (var row in quiet) if (row.Key != null) row.Key.enabled = row.Value;
        }
    }
}
public sealed class FiringFlipEmissionProbe : MonoBehaviour
{
    public string key;
    void Awake()
    {
        var world = World.GetFor(this); var manager = world == null ? null : world.Manager;
        var flow = manager == null ? null : manager.GetComponent<StateSwitchController>();
        FiringFlipChecks.Emissions.Add(new FiringFlipChecks.Emission { key = key, frame = Time.frameCount, time = Time.time,
            flipping = manager != null && manager.IsWorldTransitioning, fusion = manager != null && manager.IsFusionTransitioning,
            progress = flow == null ? 0 : flow.FlipProgress, probe = this });
    }
}
public sealed class FiringFlipStartProbe : MonoBehaviour
{
    public string key;
    [NonSerialized] public bool started;
    void Start() { started = true; }
}
