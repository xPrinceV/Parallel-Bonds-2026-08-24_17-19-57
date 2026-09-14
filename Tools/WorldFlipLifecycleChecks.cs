using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Compile in memory and run on a DontDestroyOnLoad coroutine host in Play mode.
// Controlled disposable DebugRun fixture: no input, no asset writes, no editor scene loading.
// Real components and restart APIs, but no real impacts, prefab wiring or pool rentals.
public sealed class WorldFlipLifecycleChecks : IDisposable
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Prefix = "WorldFlipLifecycleChecks ";
    static readonly Type[] ProjectileTypes = { typeof(ArrowController), typeof(BulletController),
        typeof(DaggerProjectile), typeof(LanternProjController), typeof(HollowProjectile) };
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }

    readonly List<GameObject> created = new List<GameObject>();
    readonly Dictionary<Behaviour, bool> enabledStates = new Dictionary<Behaviour, bool>();
    readonly Dictionary<PlayerHealth, Vector2> healthStates = new Dictionary<PlayerHealth, Vector2>();
    readonly List<WorldManager> managers = new List<WorldManager>();
    readonly List<StateSwitchController> flows = new List<StateSwitchController>();
    readonly List<string> errors = new List<string>();
    readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
    float savedTimeScale;
    bool savedBackground, disposed, started;
    WorldManager manager;
    StateSwitchController flow;
    WorldFlipPresentation view;
    RunStageController run;
    Camera camera;
    Matrix4x4 projection;
    int commits, midpoints;
    float midpointProgress;

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("WorldFlipLifecycleChecks already running");
        Running = true;
        Passed = Failed = 0;
        var session = new WorldFlipLifecycleChecks();
        var stack = new Stack<IEnumerator>();
        stack.Push(session.Execute());
        try
        {
            // Flatten so coroutine hosts only need to forward null frame yields to Unity.
            while (stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                Exception error = null;
                try
                {
                    if (session.clock.Elapsed.TotalSeconds >= 19.5)
                        throw new TimeoutException("19.5s fixture deadline (no shortened production timers)");
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                }
                catch (Exception e) { error = e; }
                if (error != null) { Check(false, "aborted: " + error); break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                var nested = next as IEnumerator;
                if (nested != null) stack.Push(nested);
                else yield return next;
            }
        }
        finally
        {
            try
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            }
            finally
            {
                session.Dispose();
                Running = false;
                Debug.Log("WorldFlipLifecycleChecks RESULT: " + Passed + " Passed / " + Failed
                    + " Failed; elapsed=" + session.clock.Elapsed.TotalSeconds.ToString("F3")
                    + "s. Limits: controlled HP/disabled combat; no Start, real hit, prefab or pool-rental coverage; "
                    + "timeScale=0 restart, not upgrade-button coverage. Requires advancing Play frames and rendering; "
                    + "a blocked Unity frame cannot be preempted. Leaves the fresh DebugRun loaded; no EditorExit.");
            }
        }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "Play mode host required");
        savedTimeScale = Time.timeScale;
        savedBackground = Application.runInBackground;
        started = true;
        clock.Start();
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
        Time.timeScale = 1f;
        // Runtime loading preserves the build index required by the real RestartRun method.
        SceneManager.LoadScene("DebugRun");
        yield return ThreeFrames();
        BindFresh();
        projection = camera.projectionMatrix;
        CheckFresh("initial load");
        ControlCombat();

        var fire = NewObject("fire sentinel", null);
        fire.AddComponent<LanternFire>();
        var explosion = NewObject("explosion sentinel", null);
        var direct = new List<GameObject>();
        foreach (Type type in ProjectileTypes)
        {
            var go = NewProjectile(type, manager.CurrentWorld.ContentRoot);
            var lantern = go.GetComponent<LanternProjController>();
            if (lantern != null) { lantern.fire = fire; lantern.explosion = explosion; }
            direct.Add(go);
        }
        var fireIds = FireIds();
        var effectIds = EffectIds();
        foreach (GameObject go in direct)
        {
            // AddComponent happened while inactive. Enable and cancel in the same MoveNext,
            // before yielding: OnEnable runs, but Start/Update/physics cannot run between calls.
            go.SetActive(true);
            var projectile = (IWorldProjectile)go.GetComponent<MonoBehaviour>();
            projectile.Despawn();
            Check(!go.activeSelf, go.name + " first Despawn immediately inactive");
            projectile.Despawn();
            Check(!go.activeSelf, go.name + " second Despawn safe and still inactive");
        }
        Check(NoNewEffects(fireIds, effectIds), "direct cancellation adds no fire or explosion, same frame");
        yield return null;
        foreach (GameObject go in direct) Check(go == null, "real projectile destroyed after one frame");
        Check(NoNewEffects(fireIds, effectIds), "direct cancellation adds no fire or explosion after deferred destruction");

        World outgoingWorld = manager.CurrentWorld;
        World incomingWorld = OtherWorld();
        var outgoing = ProjectileTypes.Select(t => NewProjectile(t, outgoingWorld.ContentRoot)).ToArray();
        var incoming = ProjectileTypes.Select(t => NewProjectile(t, incomingWorld.ContentRoot)).ToArray();
        var retained = new List<GameObject> { NewObject("non-projectile probe", outgoingWorld.ContentRoot) };
        // Persistent attacks also remain inactive, so their own Awake/Start cannot require prefab wiring.
        foreach (Type type in new[] { typeof(LanternFire), typeof(TitanAttack), typeof(ScytheHitController) })
        {
            var go = NewObject("retained " + type.Name, outgoingWorld.ContentRoot);
            go.AddComponent(type);
            retained.Add(go);
        }
        Check(outgoing.All(g => World.GetFor(g.transform) == outgoingWorld)
            && incoming.All(g => World.GetFor(g.transform) == incomingWorld), "fixture ownership uses source Content roots");
        fireIds = FireIds(); effectIds = EffectIds();
        int before = commits, beforeMidpoint = midpoints;
        Require(flow.RequestSwitch(incomingWorld.WorldId), "RequestSwitch accepted (not direct commit)");
        Check(commits == before && outgoing.All(g => g != null), "request does not clear before midpoint");
        while (commits == before)
        {
            Require(outgoing.All(g => g != null), "outgoing projectiles survive until commit");
            yield return null;
        }
        Check(commits == before + 1 && midpoints == beforeMidpoint + 1 && Mathf.Abs(midpointProgress - .5f) < .0001f
            && manager.CurrentWorld == incomingWorld, "one real commit at p=.5 selects incoming world");
        yield return null;
        for (int i = 0; i < ProjectileTypes.Length; i++)
        {
            Check(outgoing[i] == null, ProjectileTypes[i].Name + " outgoing destroyed");
            Check(incoming[i] != null && !incoming[i].activeSelf && World.GetFor(incoming[i].transform) == incomingWorld,
                ProjectileTypes[i].Name + " incoming survives inactive without Start");
        }
        Check(retained.All(g => g != null && !g.activeSelf), "non-projectile probe, fire, Titan attack and scythe retained");
        Check(NoNewEffects(fireIds, effectIds), "midpoint cancellation adds no fire or explosion");
        while (flow.IsFlipping) yield return null;
        yield return null; // presentation LateUpdate restores the backbuffer before the next request
        Check(camera.targetTexture == null && MatrixNear(camera.projectionMatrix, projection), "completed flip restores camera");

        Require(flow.RequestSwitch(outgoingWorld.WorldId), "second request for narrowing restart");
        before = commits;
        while (!(flow.IsFlipping && flow.FlipProgress > .1f && flow.FlipProgress < .5f
            && view.HorizontalScale < .99f && camera.targetTexture != null))
        {
            Require(commits == before, "narrowing window must be observed before commit");
            yield return null;
        }
        Check(MatrixNear(camera.projectionMatrix, projection), "narrowing leaves projection unchanged");
        var oldResources = RuntimeResources();
        Require(oldResources.OfType<Canvas>().Any() && oldResources.OfType<RenderTexture>().Any(),
            "restart fixture has an actual runtime overlay and RT");
        Debug.Log("WorldFlipLifecycleChecks narrowing restart p=" + flow.FlipProgress.ToString("F4"));
        yield return RestartAndCheck("narrowing", oldResources);

        // Exercise the explicitly supported pause alternative, not a simulated upgrade selection.
        Time.timeScale = 0f;
        yield return null;
        float remaining = flow.RemainingTime;
        yield return null;
        Check(Time.timeScale == 0f && flow.RemainingTime == remaining, "timeScale=0 freezes the fresh run");
        yield return RestartAndCheck("timeScale=0", RuntimeResources());
        Check(errors.Count == 0, "no runtime errors/assertions: " + string.Join(" | ", errors.ToArray()));
    }

    IEnumerator RestartAndCheck(string label, Object[] oldResources)
    {
        var oldScene = run.gameObject.scene;
        int oldRunId = run.GetInstanceID(), oldManagerId = manager.GetInstanceID(), oldViewId = view.GetInstanceID();
        var oldRun = run;
        var oldCamera = camera;
        int before = commits;
        // No reflection invocation or editor loading: exercise the production public API.
        run.RestartRun();
        Check(Time.timeScale == 1f, label + " RestartRun immediately restores timeScale");
        yield return ThreeFrames();
        BindFresh();
        Check(run.GetInstanceID() != oldRunId && manager.GetInstanceID() != oldManagerId && view.GetInstanceID() != oldViewId
            && run.gameObject.scene.handle != oldScene.handle && oldRun == null && oldCamera == null,
            label + " replaces scene handle and run/manager/presentation/camera instances");
        Check(oldResources.All(o => o == null) && RuntimeResources().Length == 0,
            label + " no old HideAndDontSave overlay/RT/material remains (Resources scan)");
        CheckFresh(label);
        ControlCombat();
        WorldId freshWorld = manager.CurrentWorldId;
        double until = clock.Elapsed.TotalSeconds + 1.0;
        bool stable = true;
        while (clock.Elapsed.TotalSeconds < until)
        {
            yield return null;
            stable &= commits == before && manager.CurrentWorldId == freshWorld && !flow.IsFlipping
                && Time.timeScale == 1f && camera.targetTexture == null && MatrixNear(camera.projectionMatrix, projection);
        }
        Check(stable && RuntimeResources().Length == 0, label + " fresh state stable for 1s; no stale/second commit or capture");
    }

    void BindFresh()
    {
        Scene scene = SceneManager.GetActiveScene();
        Require(scene.name == "DebugRun" && scene.buildIndex >= 0, "runtime DebugRun is registered in Build Settings");
        manager = InScene<WorldManager>(scene).Single();
        // DebugRun retains disabled legacy clocks under both heroes.
        flow = InScene<StateSwitchController>(scene).Single(f => f.isActiveAndEnabled && Get(f, "worldManager") == manager);
        view = InScene<WorldFlipPresentation>(scene).Single(v => v.isActiveAndEnabled && Get(v, "stateSwitchController") == flow);
        run = InScene<RunStageController>(scene).Single();
        camera = (Camera)Get(view, "worldCamera");
        Require(camera != null && manager.IsInitialized && flow.isActiveAndEnabled && view.isActiveAndEnabled
            && run.isActiveAndEnabled && (bool)Get(flow, "initialized"), "three-frame scene initialization complete");
        manager.WorldChanged += Changed; managers.Add(manager);
        flow.TransitionMidpoint += Midpoint; flows.Add(flow);
    }

    void CheckFresh(string label)
    {
        Check((float)Get(flow, "timer") == 30f && (float)Get(flow, "warningDuration") == 5f
            && (float)Get(flow, "flipDuration") == .8f && flow.RemainingTime > 29f && flow.RemainingTime <= 30f,
            label + " untouched timer=30/warning=5/flip=.8, remaining=" + flow.RemainingTime.ToString("F4"));
        Check(run.IsRunning && run.CurrentStageIndex == 0 && !run.IsDefeated && !run.IsCompleted
            && !manager.IsFused && !flow.IsFlipping && flow.FlipProgress == 0f && flow.WarningProgress == 0f
            && Time.timeScale == 1f, label + " fresh stage and transition state");
        Check(Get(view, "stateSwitchController") == flow && camera.targetTexture == null
            && MatrixNear(camera.projectionMatrix, projection) && view.HorizontalScale == 1f && view.BlurPixels == 0f,
            label + " presentation observes new flow, backbuffer target null and projection unchanged");
        Check(UIController.instance == null || UIController.instance.levelUpPanel == null
            || !UIController.instance.levelUpPanel.activeSelf, label + " upgrade panel closed");
    }

    void ControlCombat()
    {
        foreach (var b in InScene<MonoBehaviour>(run.gameObject.scene))
            if (b is Weapon || b is EnemySpawner || b is EnemyController)
            {
                if (!enabledStates.ContainsKey(b)) enabledStates.Add(b, b.enabled);
                b.enabled = false;
                var body = b.GetComponent<Rigidbody2D>();
                if (body != null && b is EnemyController) body.linearVelocity = Vector2.zero;
            }
        foreach (var health in InScene<PlayerHealth>(run.gameObject.scene))
        {
            if (!healthStates.ContainsKey(health))
                healthStates.Add(health, new Vector2(health.currentHealth, health.maxHealth));
        }
        foreach (var health in InScene<PlayerHealth>(run.gameObject.scene))
            health.maxHealth = health.currentHealth = 1000000f;
    }

    GameObject NewObject(string label, Transform parent)
    {
        var go = new GameObject(Prefix + label);
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        created.Add(go);
        return go;
    }
    GameObject NewProjectile(Type type, Transform parent)
    {
        var go = NewObject(type.Name, parent);
        var component = go.AddComponent(type);
        Require(component is IWorldProjectile, type.Name + " implements IWorldProjectile");
        return go;
    }
    World OtherWorld() { return InScene<World>(run.gameObject.scene).Single(w => w.Manager == manager && w != manager.CurrentWorld); }
    void Changed() { commits++; }
    void Midpoint() { midpoints++; midpointProgress = flow.FlipProgress; }
    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }
    static IEnumerator ThreeFrames() { yield return null; yield return null; yield return null; }
    static T[] InScene<T>(Scene scene) where T : Component
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene == scene).ToArray();
    }
    static object Get(object value, string name) { return value.GetType().GetField(name, Fields).GetValue(value); }
    static HashSet<int> FireIds()
    {
        return new HashSet<int>(Resources.FindObjectsOfTypeAll<LanternFire>()
            .Where(f => f.gameObject.scene.IsValid()).Select(f => f.GetInstanceID()));
    }
    static HashSet<int> EffectIds()
    {
        return new HashSet<int>(Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g.scene.IsValid() && (g.name.StartsWith(Prefix + "fire sentinel", StringComparison.Ordinal)
                || g.name.StartsWith(Prefix + "explosion sentinel", StringComparison.Ordinal))).Select(g => g.GetInstanceID()));
    }
    static bool NoNewEffects(HashSet<int> fires, HashSet<int> effects)
    {
        return FireIds().IsSubsetOf(fires) && EffectIds().IsSubsetOf(effects);
    }
    static Object[] RuntimeResources()
    {
        return Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c.name == "World Flip Overlay (Runtime)").Cast<Object>()
            .Concat(Resources.FindObjectsOfTypeAll<RenderTexture>().Where(r => r.name == "World Flip Capture (Runtime)").Cast<Object>())
            .Concat(Resources.FindObjectsOfTypeAll<Material>().Where(m => m.name == "World Flip Material (Runtime)").Cast<Object>()).ToArray();
    }
    static bool MatrixNear(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
            if (float.IsNaN(a[i]) || float.IsInfinity(a[i]) || float.IsNaN(b[i]) || float.IsInfinity(b[i])
                || Mathf.Abs(a[i] - b[i]) > .0001f) return false;
        return true;
    }
    static void Require(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
    }
    static void Check(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("WorldFlipLifecycleChecks " + (ok ? "PASS " : "FAIL ") + label);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Application.logMessageReceived -= OnLog;
        foreach (var m in managers) if (!ReferenceEquals(m, null)) m.WorldChanged -= Changed;
        foreach (var f in flows) if (!ReferenceEquals(f, null)) f.TransitionMidpoint -= Midpoint;
        foreach (var go in created) if (go != null) Object.Destroy(go);
        foreach (var pair in healthStates)
            if (pair.Key != null && !pair.Key.IsDead)
            {
                pair.Key.maxHealth = pair.Value.y;
                pair.Key.currentHealth = pair.Value.x;
            }
        foreach (var pair in enabledStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (started) { Time.timeScale = savedTimeScale; Application.runInBackground = savedBackground; }
        clock.Stop();
    }
}
