using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Staged in the disposable project's Editor assembly only. No synthetic Update/Fire calls.
public static class RiftLordChecks
{
    public static int Passed, Failed;
    public static readonly List<string> Evidence = new List<string>();
    public static readonly List<string> Completed = new List<string>();
    public static string Scenario = "preflight";
    const string BossPath = "Assets/Prefabs/RiftLordBoss.prefab";
    const string ProjectilePath = "Assets/Prefabs/RiftLordProjectile.prefab";
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static T Get<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)field.GetValue(target);
    }
    static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    public static void Note(string text) { Evidence.Add(Scenario + " " + text); Debug.Log("RIFT_NATIVE " + text); }
    public static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + text); }
    static void Require(bool ok, string text) { Check(ok, text); if (!ok) throw new InvalidOperationException(text); }
    static bool Near(float a, float b, float tolerance = .001f) => Mathf.Abs(a - b) <= tolerance;
    static IEnumerator Until(Func<bool> ready, float seconds, string label)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready()) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(label); yield return null; }
    }
    static IEnumerator Scaled(float seconds)
    {
        float end = Time.time + seconds;
        yield return Until(() => Time.time >= end, seconds * 5 + 5, "scaled wait " + seconds);
    }
    static RiftLordProjectile[] Shots(RiftLordBarrage b) => Get<List<RiftLordProjectile>>(b, "shots").Where(s => s != null && s.isActiveAndEnabled).ToArray();
    static Vector2 Heading(RiftLordProjectile s) => Get<Vector2>(s, "direction");
    static bool Gone(RiftLordProjectile s) => s == null || !s.gameObject.activeInHierarchy;
    static void Quiet(TitanEnemyController boss) { boss.moveSpeed = 0; boss.range = -1; boss.attack = 0; boss.RB.linearVelocity = Vector2.zero; }
    static IEnumerator Capture(string name)
    {
        float scale = Time.timeScale; Time.timeScale = 0;
        string path = Path.Combine(RiftLordChecksBatch.Arg("-riftOutput"), name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        float end = Time.realtimeSinceStartup + 4;
        while (!File.Exists(path) && Time.realtimeSinceStartup < end) yield return null;
        Note("CAPTURE " + name + " exists=" + File.Exists(path) + "; screen=" + Screen.width + "x" + Screen.height);
        Time.timeScale = scale;
    }
    public static void Prefabs()
    {
        var boss = AssetDatabase.LoadAssetAtPath<GameObject>(BossPath);
        var titan = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Titan.prefab");
        var projectile = AssetDatabase.LoadAssetAtPath<RiftLordProjectile>(ProjectilePath);
        Require(boss != null && titan != null && projectile != null, "native prefab imports");
        var b = boss.GetComponent<RiftLordBarrage>();
        Require(b != null && b.enabled && boss.GetComponent<TitanEnemyController>() != null, "RiftLordBarrage addon retains Titan controller");
        Check(boss.transform.localScale == Vector3.one * .5f && titan.transform.localScale == Vector3.one * .25f, "root scale Rift=.5 Titan=.25 on all axes");
        Check(PrefabUtility.GetCorrespondingObjectFromSource(boss) == titan, "Rift boss remains Titan variant");
        Check(boss.GetComponent<TitanEnemyController>().health == 600, "prefab health=600");
        Check(Get<RiftLordProjectile>(b, "projectilePrefab") == projectile && Get<SpriteRenderer>(b, "bodyRenderer") != null
            && Get<SpriteRenderer>(b, "bodyRenderer").sprite.name == "Rift Lord_0", "projectile path and Rift Lord body binding");
        Check(Get<float>(b, "ringInterval") == 6 && Near(Get<float>(b, "telegraphDuration"), .6f)
            && Get<int>(b, "ringDirections") == 12 && Get<int>(b, "ringGapDirections") == 2, "defaults ring 6s / warning .6s / 12 directions / adjacent gap 2");
        Check(Near(Get<float>(b, "fanInterval"), .8f) && Get<int>(b, "fanProjectileCount") == 5
            && Get<int>(b, "halfHealthRounds") == 3 && Get<float>(b, "fanSpread") == 60, "defaults 3 x 5-shot / 60 degree / .8s fans");
        Check(Get<float>(b, "projectileSpeed") == 3 && Get<float>(b, "projectileDamage") == 10 && Get<float>(b, "projectileLifetime") == 8,
            "defaults projectile speed3 damage10 lifetime8");
        Check(projectile.GetComponent<SpriteRenderer>().sprite != null && !projectile.GetComponent<CircleCollider2D>().enabled,
            "rendered query-only projectile prefab");
        bool bossFootprint = WorldMap.TryGetFootprint(boss.transform, out _, out float radius);
        bool titanFootprint = WorldMap.TryGetFootprint(titan.transform, out _, out float titanRadius);
        Require(bossFootprint && titanFootprint, "prefab physical footprints");
        Check(Near(radius, titanRadius * 2), "doubled physical footprint Rift=" + radius + " Titan=" + titanRadius);
        var paths = new[] { BossPath, BossPath + ".meta", ProjectilePath, ProjectilePath + ".meta" };
        Require(paths.All(File.Exists), "existing setup branch requires imported assets and metadata; never regenerate");
        var before = paths.Select(File.ReadAllBytes).ToArray();
        var ensure = typeof(DebugRunSceneSetup).GetMethod("EnsureBossPrefab", BindingFlags.Static | BindingFlags.NonPublic);
        Require(ensure != null, "verified existing-prefab editor setup method");
        Check((EnemyController)ensure.Invoke(null, null) == boss.GetComponent<EnemyController>(), "updated editor setup validates EXISTING boss and barrage references");
        Check(paths.Select((p, i) => File.ReadAllBytes(p).SequenceEqual(before[i])).All(v => v)
            && !EditorUtility.IsDirty(boss), "existing setup branch leaves prefab bytes/metadata unchanged and asset clean");
    }
    public static void SceneBinding(RunStageController run, string scene)
    {
        Check(Get<EnemyController>(run, "bossPrefab") == AssetDatabase.LoadAssetAtPath<GameObject>(BossPath).GetComponent<EnemyController>()
            && Get<float>(run, "bossHealth") == 600 && Get<float>(run, "bossDistance") == 6, scene + " authored finale boss binding / health600 / distance6");
    }
    static void SafeSpawns(World[] worlds)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPath);
        Require(WorldMap.TryGetFootprint(prefab.transform, out Vector2 offset, out float radius), "boss footprint for independent geometry checks");
        foreach (var world in worlds)
        {
            var player = world.Player; var map = world.Map; Vector3 saved = player.transform.position;
            bool contentActive = world.IsActive, mapActive = map.gameObject.activeSelf;
            bool playable = map.TryGetPlayableRect(out Rect rect);
            bool footprint = WorldMap.TryGetFootprint(player, out Vector2 po, out float pr);
            Require(playable && footprint, "playable rect/player footprint " + world.WorldId);
            Require(world.ContentRoot.lossyScale == Vector3.one && world.ContentRoot.rotation == Quaternion.identity, "unit spawn parent footprint basis " + world.WorldId);
            var points = new[] { rect.center, new Vector2(rect.xMin + pr + .05f, rect.center.y) - po,
                new Vector2(rect.xMax - pr - .05f, rect.center.y) - po, new Vector2(rect.center.x, rect.yMin + pr + .05f) - po,
                new Vector2(rect.center.x, rect.yMax - pr - .05f) - po };
            try
            {
                for (int i = 0; i < points.Length; i++)
                {
                    player.transform.position = new Vector3(points[i].x, points[i].y, saved.z); Physics2D.SyncTransforms();
                    bool found = map.TryFindSafeSpawnPosition(prefab.transform, world.ContentRoot, player, 6, out Vector3 spawn);
                    Check(found, "safe API " + world.WorldId + " sample=" + i + " player=" + points[i] + " spawn=" + spawn);
                    Check(world.IsActive == contentActive && map.gameObject.activeSelf == mapActive && player.transform.position == new Vector3(points[i].x, points[i].y, saved.z),
                        "probe restores map/content and does not move hero " + world.WorldId + "/" + i);
                    if (!found) continue;
                    Vector2 center = (Vector2)spawn + offset;
                    map.SetMapActive(true); Physics2D.SyncTransforms();
                    var hits = Physics2D.OverlapCircleAll(center, radius + .019f);
                    bool clear = !hits.Any(c => !c.isTrigger && (c.transform.IsChildOf(map.transform) || c.transform.IsChildOf(map.BoundaryRoot)));
                    Check(clear && center.x - radius >= rect.xMin && center.x + radius <= rect.xMax
                        && center.y - radius >= rect.yMin && center.y + radius <= rect.yMax
                        && Vector2.Distance(center, points[i] + po) >= radius + pr + .019f, "independent doubled-collider wall/map/hero clearance " + world.WorldId + "/" + i);
                    map.SetMapActive(mapActive);
                }
            }
            finally { player.transform.position = saved; map.SetMapActive(mapActive); Physics2D.SyncTransforms(); }
        }
    }
    static IEnumerator RingAndLifetime(RiftLordBarrage b, float spawnTime, string scene)
    {
        yield return Until(() => b.IsTelegraphing, 9, "natural ring warning");
        float warning = Time.time;
        Check(warning - spawnTime >= 5.3f && warning - spawnTime < 5.7f && b.ActiveProjectileCount == 0,
            "first warning near5.4s elapsed=" + (warning - spawnTime));
        var preview = Get<RiftLordProjectile>(b, "preview");
        var markers = preview.GetComponentsInChildren<SpriteRenderer>();
        Check(markers.Length == 36 && markers.Count(s => s.name == "Safe Gap") == 6 && preview.GetComponentsInChildren<Collider2D>().Length == 0,
            "12 warning directions; 2 green safe directions; collider-free preview");
        var hp = World.GetFor(b).InteractionPlayer.GetComponent<PlayerHealth>(); float health = hp.currentHealth;
        Vector3 previewPosition = preview.transform.position; preview.transform.position = hp.transform.position;
        yield return Scaled(.12f);
        Check(hp.currentHealth == health && b.ActiveProjectileCount == 0, "actual warning overlap harmless during scaled updates");
        preview.transform.position = previewPosition;
        float clock = Get<float>(b, "phaseClock"), lifetime = Get<float>(preview, "lifetimeRemaining");
        Time.timeScale = 0; yield return new WaitForSecondsRealtime(.3f);
        Check(b.IsTelegraphing && b.ActiveProjectileCount == 0 && Near(clock, Get<float>(b, "phaseClock"))
            && Near(lifetime, Get<float>(preview, "lifetimeRemaining")), "pause freezes warning and preview lifetime");
        Time.timeScale = 1;
        yield return Capture(scene + "-ring-warning");
        yield return Until(() => b.ActiveProjectileCount > 0, 3, "natural ring fire");
        float fire = Time.time; var shots = Shots(b);
        Check(fire - spawnTime >= 5.95f && fire - spawnTime < 6.3f && fire - warning >= .59f && fire - warning < .8f,
            "ring fires at6s after .6s warning; spawnToFire=" + (fire - spawnTime) + "; warningToFire=" + (fire - warning));
        Require(shots.Length == 10, "actual ring launches exactly10");
        var indices = shots.Select(s => (Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Heading(s).y, Heading(s).x) * Mathf.Rad2Deg, 360) / 30) % 12)).ToArray();
        var gaps = Enumerable.Range(0, 12).Except(indices).ToArray();
        Check(indices.Distinct().Count() == 10 && gaps.Length == 2 && ((gaps[1] - gaps[0] == 1) || (gaps[1] - gaps[0] == 11))
            && shots.All(s => Near(Vector2.Angle(Heading(s), Quaternion.Euler(0, 0, Mathf.Round(Mathf.Repeat(Mathf.Atan2(Heading(s).y, Heading(s).x) * Mathf.Rad2Deg, 360) / 30) * 30) * Vector2.right), 0, .1f)),
            "actual ring 30-degree lattice and adjacent wrap-aware missing pair=" + string.Join(",", gaps));
        var shot = shots.OrderByDescending(s => Vector2.Dot(Heading(s), Vector2.right)).First();
        Vector3 position = shot.transform.position; float remaining = Get<float>(shot, "lifetimeRemaining");
        Time.timeScale = 0; yield return new WaitForSecondsRealtime(.3f);
        Check(shot != null && shot.transform.position == position && Near(remaining, Get<float>(shot, "lifetimeRemaining")), "pause freezes moving projectile and lifetime");
        Time.timeScale = .5f; float t = Time.time, real = Time.realtimeSinceStartup;
        yield return Scaled(.4f);
        float dt = Time.time - t;
        Require(shot != null, "outward shot survives movement sample");
        Check(Vector3.Distance(shot.transform.position, position + (Vector3)Heading(shot) * 3 * dt) < .12f
            && Near(remaining - Get<float>(shot, "lifetimeRemaining"), dt, .05f) && Time.realtimeSinceStartup - real >= .7f,
            "native scaled movement speed3 and lifetime decrement; scaled=" + dt + " real=" + (Time.realtimeSinceStartup - real));
        Time.timeScale = 1;
        yield return Capture(scene + "-ring-fired");
        yield return Until(() => Shots(b).Any(s => !shots.Contains(s)), 9, "second natural ring");
        Check(Time.time - fire >= 5.95f && Time.time - fire < 6.35f, "repeat ring launch cadence6s elapsed=" + (Time.time - fire));
        yield return Until(() => shot == null, 4, "natural projectile expiry");
        Check(Time.time - fire >= 7.95f && Time.time - fire < 8.3f, "projectile expires at8 scaled seconds elapsed=" + (Time.time - fire));
        World.GetFor(b).ClearProjectiles(); yield return null;
        Check(b.ActiveProjectileCount == 0 && !b.IsTelegraphing, "existing World.ClearProjectiles removes real shots");
    }
    static IEnumerator Fans(RiftLordBarrage b, TitanEnemyController boss, string scene, bool damagedBeforeStart = false)
    {
        // Keep aimed volleys observable before collision; only the fixture hero is repositioned.
        var player = World.GetFor(b).InteractionPlayer;
        player.transform.position = boss.transform.position + Vector3.left * 20; Physics2D.SyncTransforms();
        if (!damagedBeforeStart)
        {
            boss.TakeDamage(299); yield return Scaled(.1f);
            Check(boss.health == 301 && !b.HasTriggeredHalfHealth, "spawn health600 threshold not triggered at301 after earlier rings");
            boss.TakeDamage(1);
        }
        float threshold = Time.time;
        yield return Until(() => b.IsTelegraphing && b.HasTriggeredHalfHealth, 2, "half health warning");
        Check(boss.health == 300 && Get<float>(b, "initialSpawnHealth") == 600, "exact50 percent uses assigned spawn health600");
        yield return Capture(scene + "-half-warning");
        var seen = new HashSet<RiftLordProjectile>(); var launches = new List<float>();
        float end = Time.realtimeSinceStartup + 5; Vector2 aim = Vector2.left;
        while (launches.Count < 3 && Time.realtimeSinceStartup < end)
        {
            if (b.IsTelegraphing)
            {
                aim = ((Vector2)(player.transform.position - Get<Vector3>(b, "origin"))).normalized;
                Check(Get<RiftLordProjectile>(b, "preview").GetComponentsInChildren<SpriteRenderer>().Length == 15, "fan warning has5 directions x3 markers round=" + launches.Count);
                yield return Until(() => !b.IsTelegraphing, 2, "fan warning completes");
            }
            var added = Shots(b).Where(s => !seen.Contains(s)).ToArray();
            if (added.Length > 0)
            {
                launches.Add(Time.time); foreach (var s in added) seen.Add(s);
                float[] angles = added.Select(s => Vector2.SignedAngle(aim, Heading(s))).OrderBy(a => a).ToArray();
                Check(added.Length == 5 && angles.Zip(new[] { -30f, -15f, 0f, 15f, 30f }, (a, expected) => Near(a, expected, .15f)).All(v => v),
                    "actual aimed5-shot fan /60degrees round=" + launches.Count + " angles=" + string.Join(",", angles));
                if (launches.Count < 3) player.transform.position += Vector3.up * 4;
            }
            yield return null;
        }
        Require(launches.Count == 3, "half health produces exactly3 observed launches");
        Check(launches[0] - threshold >= .59f && launches.Skip(1).Select((t, i) => t - launches[i]).All(d => d >= .79f && d < 1f),
            "fan launch intervals .8s (each re-aimed); times=" + string.Join(",", launches));
        boss.health = 600; yield return Scaled(.1f); boss.TakeDamage(450);
        yield return Scaled(2.5f);
        Check(b.HasTriggeredHalfHealth && Get<int>(b, "fanRoundsRemaining") == 0 && !b.IsTelegraphing
            && Shots(b).All(s => seen.Contains(s)), "heal/re-cross below50 cannot repeat half event or fourth fan");
        yield return Until(() => b.IsTelegraphing, 5, "natural ring resumes after half fans");
        float warning = Time.time - launches[2];
        var markers = Get<RiftLordProjectile>(b, "preview").GetComponentsInChildren<SpriteRenderer>();
        Check(warning >= 5.3f && warning < 5.7f && markers.Length == 36 && markers.Count(s => s.name == "Safe Gap") == 6,
            "post-half warning is ring, not repeated half fan; sinceLastFan=" + warning);
        yield return Until(() => Shots(b).Any(s => !seen.Contains(s)), 2, "post-half natural ring launch");
        Check(Shots(b).Count(s => !seen.Contains(s)) == 10 && Time.time - launches[2] >= 5.95f && Time.time - launches[2] < 6.35f
            && b.HasTriggeredHalfHealth && Get<int>(b, "fanRoundsRemaining") == 0,
            "rings resume naturally with10 shots after one half sequence; sinceLastFan=" + (Time.time - launches[2]));
        World.GetFor(b).ClearProjectiles(); yield return null;
    }
    static IEnumerator Collision(RiftLordBarrage b, TitanEnemyController boss)
    {
        b.enabled = false; b.enabled = true;
        yield return Until(() => b.ActiveProjectileCount == 10, 9, "collision test natural ring");
        var shots = Shots(b); var shot = shots[0]; foreach (var other in shots.Skip(1)) other.Despawn();
        var hp = World.GetFor(b).InteractionPlayer.GetComponent<PlayerHealth>();
        // Two overlapping real Player-layer colliders must still take just one hit.
        var extra = hp.gameObject.AddComponent<CircleCollider2D>(); extra.isTrigger = true; extra.radius = .15f;
        hp.transform.position = shot.transform.position + (Vector3)Heading(shot) * 1.5f; Physics2D.SyncTransforms();
        float before = hp.currentHealth; Vector3 start = shot.transform.position;
        yield return Until(() => Gone(shot), 2, "native swept collision");
        Check(hp.currentHealth == before - 10 && b.ActiveProjectileCount == 0, "real moving query collision damages exactly10 once with duplicate player colliders; start=" + start);
        yield return Scaled(.25f);
        Check(hp.currentHealth == before - 10, "despawn latch prevents repeated damage after collision");
        Object.Destroy(extra);
        hp.transform.position = boss.transform.position + Vector3.left * 20; Physics2D.SyncTransforms();
    }
    static IEnumerator OwnershipAndEnd(RiftLordBarrage b, TitanEnemyController boss, RunStageController run, string scene)
    {
        var world = World.GetFor(b); var otherWorld = All<World>().Single(w => w != world);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPath);
        var otherBoss = Object.Instantiate(prefab, boss.transform.position + Vector3.up * 12, Quaternion.identity, world.ContentRoot).GetComponent<TitanEnemyController>();
        var crossBoss = Object.Instantiate(prefab, boss.transform.position + Vector3.down * 12, Quaternion.identity, otherWorld.ContentRoot).GetComponent<TitanEnemyController>();
        Quiet(otherBoss); Quiet(crossBoss);
        var other = otherBoss.GetComponent<RiftLordBarrage>(); var cross = crossBoss.GetComponent<RiftLordBarrage>();
        b.enabled = false; b.enabled = true;
        yield return Until(() => b.IsTelegraphing && other.IsTelegraphing && cross.IsTelegraphing, 9, "three independent owner warnings");
        var warning = Get<RiftLordProjectile>(b, "preview");
        b.enabled = false;
        Check(Gone(warning) && other.IsTelegraphing && cross.IsTelegraphing, "disable clears OWN preview, not other owners/world");
        b.enabled = true;
        yield return Until(() => other.ActiveProjectileCount == 10 && cross.ActiveProjectileCount == 10, 3, "peer natural shots");
        var otherShots = Shots(other); var crossShots = Shots(cross);
        other.enabled = false;
        Check(otherShots.All(Gone) && crossShots.All(s => !Gone(s)), "disable clears OWN live shots, cross-world owner untouched");
        other.enabled = true;
        yield return Until(() => b.ActiveProjectileCount == 10, 9, "main owner shots for death");
        var ownShots = Shots(b);
        // Re-arm peer warning at half health while the real encounter boss has live shots.
        otherBoss.TakeDamage(300); yield return Until(() => other.IsTelegraphing, 2, "peer half warning");
        if (scene == "Main")
        {
            otherBoss.TakeDamage(1000);
            Check(!other.IsTelegraphing && other.ActiveProjectileCount == 0 && ownShots.All(s => !Gone(s)), "real Died event clears OWN warning without touching encounter boss shots");
            yield return Until(() => cross.IsTelegraphing, 7, "cross-world natural warning");
            var crossWarning = Get<RiftLordProjectile>(cross, "preview");
            world.ClearProjectiles();
            Check(ownShots.All(Gone) && cross.IsTelegraphing && !Gone(crossWarning), "World.ClearProjectiles respects world ownership and keeps other-world warning");
            otherWorld.ClearProjectiles();
            Check(!cross.IsTelegraphing && cross.ActiveProjectileCount == 0, "World.ClearProjectiles also clears render-only warnings");
            yield return Scaled(.8f);
            Check(cross.ActiveProjectileCount == 0, "cleared warning does not fire cancelled volley");
            b.enabled = false; b.enabled = true;
            yield return Until(() => b.ActiveProjectileCount == 10, 9, "victory death with owned live shots");
            ownShots = Shots(b); boss.TakeDamage(1000);
            Check(ownShots.All(Gone) && b.ActiveProjectileCount == 0, "actual finale boss Died synchronously clears OWN live projectiles");
            yield return Until(() => run.IsCompleted || run.IsDefeated, 3, "official victory");
            Check(run.IsCompleted && !run.IsDefeated && !run.IsRunning && run.RemainingBosses == 0 && Time.timeScale == 0, "official finale death resolves Victory once");
        }
        else
        {
            var warningAtEnd = Get<RiftLordProjectile>(other, "preview");
            var allShots = All<RiftLordProjectile>();
            var hp = world.InteractionPlayer.GetComponent<PlayerHealth>(); hp.DamageHandler(hp.currentHealth + 1);
            yield return Until(() => run.IsDefeated, 3, "official defeat");
            Check(!run.IsRunning && !run.IsCompleted && Time.timeScale == 0 && allShots.All(Gone) && Gone(warningAtEnd), "official player death/end clears all encounter owned shots and warnings");
        }
        yield return Capture(scene + "-result");
    }
    static void PrepareRun()
    {
        foreach (var weapon in All<Weapon>()) weapon.enabled = false;
        foreach (var spawner in All<EnemySpawner>()) spawner.StopSpawning(true);
        foreach (var world in All<World>()) { var hp = world.Player.GetComponent<PlayerHealth>(); hp.maxHealth = hp.currentHealth = 100000; }
    }
    static IEnumerator RestartAndSpawnHealth(RunStageController outgoing, string scene)
    {
        var handle = outgoing.gameObject.scene.handle;
        outgoing.RestartRun();
        yield return Until(() => SceneManager.GetActiveScene().name == scene && SceneManager.GetActiveScene().handle != handle, 8, "official result RestartRun");
        yield return null; yield return null;
        var run = All<RunStageController>().Single(); PrepareRun();
        Require(outgoing == null && run.IsRunning && !run.IsFinaleStarted && run.RemainingBosses == 0 && Time.timeScale == 1,
            "official result restart creates fresh running scene " + scene);
        TitanEnemyController boss = null; RiftLordBarrage b = null; int callbacks = 0;
        Action damageOnSpawn = () =>
        {
            if (!run.IsBossPhase || boss != null) return;
            boss = (TitanEnemyController)Get<HashSet<EnemyController>>(run, "bosses").Single();
            b = boss.GetComponent<RiftLordBarrage>(); callbacks++;
            Check(!Get<bool>(b, "started") && boss.health == 600 && Get<float>(b, "initialSpawnHealth") == 600,
                "official spawn StateChanged observes initialized600 BEFORE Start");
            boss.TakeDamage(300); Quiet(boss);
            Check(!Get<bool>(b, "started") && boss.health == 300 && Get<float>(b, "initialSpawnHealth") == 600 && !b.HasTriggeredHalfHealth,
                "synchronous StateChanged damage600->300 preserves baseline600 before first yield");
        };
        run.StateChanged += damageOnSpawn;
        try
        {
            Require(run.TryStartFinale(), "after-restart official finale accepts pre-Start damage observer");
            yield return Until(() => boss != null, 15, "official spawn callback");
        }
        finally { run.StateChanged -= damageOnSpawn; }
        Check(callbacks == 1, "exactly one newly spawned boss damaged by StateChanged observer");
        yield return Fans(b, boss, scene + "-restart", true);

        // Standalone prefab instance in the live world, deliberately outside the official boss registry.
        var world = World.GetFor(b);
        var independent = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BossPath),
            boss.transform.position + Vector3.up * 12, Quaternion.identity, world.ContentRoot).GetComponent<TitanEnemyController>();
        var addon = independent.GetComponent<RiftLordBarrage>(); Quiet(independent);
        Check(!Get<bool>(addon, "started") && Get<float>(addon, "initialSpawnHealth") == 600, "standalone Awake captures prefab600");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool rejected = false;
            try { addon.InitializeSpawnHealth(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && Get<float>(addon, "initialSpawnHealth") == 600, "invalid spawn baseline rejected without mutation: " + invalid);
        }
        independent.health = 1000; addon.InitializeSpawnHealth(1000); independent.TakeDamage(500);
        Check(!Get<bool>(addon, "started") && independent.health == 500 && Get<float>(addon, "initialSpawnHealth") == 1000
            && !addon.HasTriggeredHalfHealth && !Get<HashSet<EnemyController>>(run, "bosses").Contains(independent),
            "standalone override1000 then damage500 before yield; not prefab600 fallback or official registry");
        yield return Until(() => addon.HasTriggeredHalfHealth && addon.IsTelegraphing, 2, "standalone1000 immediate half warning");
        var firstWarning = Get<RiftLordProjectile>(addon, "preview");
        Check(Get<float>(addon, "initialSpawnHealth") == 1000 && independent.health == 500 && Get<int>(addon, "directionCount") == 5,
            "standalone threshold uses1000 after Start, not damaged500 or prefab600");
        bool lateRejected = false;
        try { addon.InitializeSpawnHealth(2000); } catch (InvalidOperationException) { lateRejected = true; }
        Check(lateRejected && Get<float>(addon, "initialSpawnHealth") == 1000, "spawn initializer rejects changes after Start");
        yield return Until(() => addon.ActiveProjectileCount == 5, 2, "standalone first real fan");
        yield return Until(() => addon.IsTelegraphing, 2, "standalone second warning with live shots");
        Check(Gone(firstWarning) && Get<int>(addon, "fanRoundsRemaining") == 2 && addon.ActiveProjectileCount == 5,
            "standalone half sequence advances once, not re-triggered every frame at500");
        var oldShots = All<RiftLordProjectile>(); var oldWarning = Get<RiftLordProjectile>(addon, "preview");
        Require(oldShots.Any(s => !Get<bool>(s, "renderOnly")) && oldWarning != null && b.HasTriggeredHalfHealth && addon.HasTriggeredHalfHealth,
            "restart fixture has real live shots, warning and spent official/standalone half latches");
        handle = run.gameObject.scene.handle;
        run.RestartRun();
        yield return Until(() => SceneManager.GetActiveScene().name == scene && SceneManager.GetActiveScene().handle != handle, 8, "official live-barrage RestartRun");
        yield return null; yield return null;
        Check(run == null && boss == null && b == null && independent == null && addon == null && Gone(oldWarning) && oldShots.All(Gone)
            && All<RiftLordProjectile>().Length == 0 && All<RiftLordBarrage>().Length == 0, "official live restart destroys all old owners/shots/warnings/half state");
        var fresh = All<RunStageController>().Single(); PrepareRun();
        Require(fresh.IsRunning && !fresh.IsFinaleStarted && !fresh.IsCompleted && !fresh.IsDefeated && fresh.RemainingBosses == 0 && Time.timeScale == 1,
            "live restart resets official run state");
        Require(fresh.TryStartFinale(), "fresh official finale after live-barrage restart");
        yield return Until(() => fresh.IsBossPhase, 15, "fresh600 official spawn");
        var freshBoss = (TitanEnemyController)Get<HashSet<EnemyController>>(fresh, "bosses").Single(); Quiet(freshBoss);
        var freshAddon = freshBoss.GetComponent<RiftLordBarrage>();
        yield return Scaled(.8f);
        Check(freshBoss.health == 600 && Get<float>(freshAddon, "initialSpawnHealth") == 600 && !freshAddon.HasTriggeredHalfHealth
            && !freshAddon.IsTelegraphing && freshAddon.ActiveProjectileCount == 0 && Get<int>(freshAddon, "fanRoundsRemaining") == 0,
            "new official600 boss retains no old half trigger, warning or shots after Start");
        freshBoss.TakeDamage(300);
        yield return Until(() => freshAddon.IsTelegraphing, 2, "fresh boss half trigger after restart");
        Check(freshAddon.HasTriggeredHalfHealth && Get<int>(freshAddon, "directionCount") == 5 && Get<int>(freshAddon, "fanRoundsRemaining") == 3,
            "fresh half latch can trigger anew after real restart");
        Note("RESTART_REGRESSION completed " + scene + "; officialRestarts=2; preStartCallback=1; standaloneOverride=1000");
    }
    public static IEnumerator Run()
    {
        Application.runInBackground = true;
        Require(Application.isPlaying && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "native graphical Play Mode");
        Note("FIXTURE only: weapons disabled; public StopSpawning; shared HP100000; Titan moveSpeed0/range-1/attack0; hero positioning; no barrage parameter/phase/clock writes, no synthetic collision/update/fire calls");
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            Scenario = scene;
            if (SceneManager.GetActiveScene().name != scene) { Time.timeScale = 1; yield return SceneManager.LoadSceneAsync(scene); }
            yield return null; yield return null;
            var run = All<RunStageController>().Single(); var manager = All<WorldManager>().Single(); var worlds = All<World>();
            Require(run.IsRunning && manager.IsInitialized && worlds.Length == 2, "actual scene running with2 worlds");
            SceneBinding(run, scene);
            PrepareRun();
            SafeSpawns(worlds);
            if (scene == "DebugRun")
            {
                var target = worlds.Single(w => w != manager.CurrentWorld);
                Require(manager.SwitchWorld(target.WorldId), "official alternate-world request");
                var flow = All<StateSwitchController>().Single(t => t.isActiveAndEnabled && Get<WorldManager>(t, "worldManager") == manager);
                float bound = Get<float>(flow, "warningDuration") + Get<float>(flow, "flipDuration") + 3;
                yield return Until(() => manager.CurrentWorld == target && !manager.IsWorldTransitioning, bound, "official alternate world commit");
            }
            Require(run.TryStartFinale(), "official TryStartFinale accepted without editing run clock");
            Check(manager.IsFinalFusion && manager.IsFusionTransitioning && run.RemainingBosses == 0, "actual fusion entrance defers boss spawn");
            yield return Until(() => run.IsBossPhase || run.IsDefeated, 15, "official fusion completes and safely spawns boss");
            var bosses = Get<HashSet<EnemyController>>(run, "bosses").ToArray();
            Require(run.IsBossPhase && bosses.Length == 1 && bosses[0] is TitanEnemyController, "one official finale Titan-derived boss");
            var boss = (TitanEnemyController)bosses[0]; var b = boss.GetComponent<RiftLordBarrage>();
            Require(b != null && b.enabled && boss.health == 600 && boss.transform.localScale == Vector3.one * .5f
                && World.GetFor(boss) == manager.CurrentWorld, "actual spawned Rift binding/health600/root.5/entry world=" + manager.CurrentWorldId);
            float spawnTime = Time.time; Quiet(boss);
            yield return RingAndLifetime(b, spawnTime, scene);
            yield return Fans(b, boss, scene);
            yield return Collision(b, boss);
            yield return OwnershipAndEnd(b, boss, run, scene);
            yield return RestartAndSpawnHealth(run, scene);
            run = All<RunStageController>().Single();
            Completed.Add(scene); Note("SCENARIO completed " + scene);
            // Respect production pre-unload cleanup; raw loading from paused Victory lets
            // the outgoing run's LateUpdate pause the incoming scene.
            run.ReturnToMainMenu();
            yield return Until(() => SceneManager.GetActiveScene().name == "Main Menu", 5, "official run exit");
            yield return null;
        }
    }
}
