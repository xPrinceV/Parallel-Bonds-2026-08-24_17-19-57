using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Stage this bootstrap in the ISOLATED project's Editor folder only. Checks stay outside Assets.
// No -quit: real Play frames drive completion. External process deadline is 180 seconds.
[InitializeOnLoad]
public static class WorldMapChecksBatch
{
    const string Key = "WorldMapChecksBatch.Running";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static double deadline;
    static bool started, finished;
    static int passed, failed, unexpected, expected, aborts;
        static EditorWindow gameView;
    static string activeCall;
    static Assembly checks;
    public static string Suite => Arg("-mapSuite", "Checks");
    public static string Scene => Arg("-mapScene", "Main");
    static WorldMapChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    static string Arg(string name, string fallback) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i < 0 ? fallback : a[i + 1]; }
    public static void Run()
    {
        if (!Path.GetFullPath(Application.dataPath).Replace('\\', '/').Contains("/Library/DebugRunValidationProject/Assets"))
            throw new InvalidOperationException("Isolated validation project required; source editor forbidden.");
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/" + Scene + ".unity");
        if (!Application.isBatchMode)
        {
            gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.Show(); gameView.Focus();
        }
        Hook();
        EditorApplication.EnterPlaymode();
    }
    static void Hook()
    {
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Assert && type != LogType.Exception) return;
        bool injected = type == LogType.Error &&
            ((activeCall == "RunSwitchRollback" && message == "World switch failed; restoring the previous world.") ||
             (activeCall == "RunCleanupFailure" && message == "Fusion cleanup required the captured content root fallback; manager disabled."));
        if (injected) expected++; else { unexpected++; Debug.Log("MAP_BATCH UNEXPECTED " + message + "\n" + stack); }
    }
    static void Poll()
    {
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 140;
        if (finished) return;
        if (EditorApplication.timeSinceStartup > deadline) { Finish("140s internal timeout", 2); return; }
        if (started && !Application.isBatchMode)
                {
                    if (gameView == null) gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                    gameView.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
                }
                if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        try
        {
            Debug.Log("MAP_BATCH START " + Scene + "/" + Suite + " Unity=" + Application.unityVersion + " GPU=" + SystemInfo.graphicsDeviceName + "/" + SystemInfo.graphicsDeviceType);
            checks = Compile(new[] { "Tools/WorldMapChecks.cs", "Tools/WorldMapPresentationChecks.cs" }, "MapChecks");
            if (Suite == "Live" || Suite == "Finale")
            {
                var host = new GameObject("WorldMapChecksBatchHost").AddComponent<WorldMapChecksBatchHost>();
                Object.DontDestroyOnLoad(host.gameObject);
                host.StartCoroutine(Drive(Suite == "Live" ? Live() : ExistingFinale()));
                return;
            }
            if (Suite == "Checks")
            {
                Initial();
                Invoke("WorldMapChecks", "AssessMapClearPoints");
                Invoke("WorldMapChecks", "Run");
                Invoke("WorldMapPresentationChecks", "Run");
                Invoke("WorldMapChecks", "RunLifecycle");
            }
            else if (Suite == "Rollback") Invoke("WorldMapChecks", "RunSwitchRollback");
            else if (Suite == "CleanupFailure") Invoke("WorldMapChecks", "RunCleanupFailure");
            else if (Suite == "BossFailure") Invoke("WorldMapChecks", "RunBossSpawnFailure");
            else if (Suite == "Regression")
            {
                var files = BuffFiles();
                ExistingSync(files, "BuffRuntimeChecks", "Run");
                ExistingSync(new[] { "Tools/WeaponBuffChecks.cs" }, "WeaponBuffChecks", "Run");
            }
            Finish("completed");
        }
        catch (Exception e) { Finish(e.ToString(), 2); }
    }
    static string[] BuffFiles()
        {
            // Two legacy tests call an internal method directly. Bridge only accessibility,
            // against the actual loaded runtime type; do not duplicate production Buff types.
            var files = Directory.GetFiles("Tools/BuffRuntimeChecks", "*.cs").ToList();
            foreach (string name in new[] { "StackChecks", "InternalActivationChecks" })
            {
                string original = files.Single(p => Path.GetFileNameWithoutExtension(p) == name);
                string text = File.ReadAllText(original);
                string call = name == "StackChecks" ? "stack.ProcessEvent(hit, receiver, recipe)" : "legacyStack.ProcessEvent(hit, holder, recipe)";
                string target = name == "StackChecks" ? "stack" : "legacyStack";
                string receiver = name == "StackChecks" ? "receiver" : "holder";
                if (!text.Contains(call)) throw new InvalidOperationException("Legacy Buff accessibility bridge no longer matches " + original);
                text = text.Replace(call, "typeof(StackBuffInstance).GetMethod(\"ProcessEvent\", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(" + target + ", new object[] { hit, " + receiver + ", recipe, null })");
                string output = "Library/" + name + "_AccessibilityBridge.cs";
                File.WriteAllText(output, text); files[files.IndexOf(original)] = output;
            }
            files.Add("Tools/BuffRuntimeChecks.cs");
            Debug.Log("MAP_BATCH Buff tests: two internal ProcessEvent calls bridged by reflection; assertions and loaded production types unchanged.");
            return files.ToArray();
        }
        static Assembly Compile(string[] files, string name)
    {
        string output = Path.GetFullPath("Library/" + name + ".dll");
        var options = new CompilerParameters { GenerateInMemory = false, GenerateExecutable = false, OutputAssembly = output, CompilerOptions = "-nostdlib+ -define:UNITY_EDITOR" };
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) options.ReferencedAssemblies.Add(assembly.Location);
        CompilerResults result;
        using (var compiler = new CSharpCodeProvider()) result = compiler.CompileAssemblyFromFile(options, files);
        if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
        return Assembly.Load(File.ReadAllBytes(output));
    }
    static void ExistingSync(string[] files, string type, string method)
    {
        try
        {
            var assembly = Compile(files, type);
            string result = (string)assembly.GetType(type).GetMethod(method).Invoke(null, null);
            Debug.Log("MAP_BATCH EXISTING " + type + " " + result);
            var match = System.Text.RegularExpressions.Regex.Match(result, @"(\d+) (?:deterministic )?(?:checks )?passed", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) passed += int.Parse(match.Groups[1].Value); else Check(false, "Uncounted regression result: " + result);
        }
        catch (Exception e) { Check(false, type + " aborted: " + e); }
    }
    static string Invoke(string type, string method)
    {
        activeCall = method;
        try
        {
            var t = checks.GetType(type);
            string result = (string)t.GetMethod(method).Invoke(null, null);
            Debug.Log("MAP_BATCH SUITE " + result);
            if (method != "AssessInitialPosition")
            { passed += (int)t.GetProperty("Passed").GetValue(null); failed += (int)t.GetProperty("Failed").GetValue(null); }
            return result;
        }
        finally { activeCall = null; }
    }
    public static void Check(bool ok, string text)
    { if (ok) passed++; else failed++; Debug.Log("MAP_BATCH " + (ok ? "PASS " : "FAIL ") + text); }
    static void Require(bool ok, string text) { Check(ok, text); if (!ok) throw new InvalidOperationException(text); }
    static void Initial()
    {
        string result = Invoke("WorldMapChecks", "AssessInitialPosition");
        Check(result.Contains(" CLEAR"), "authored initial position clear (no relocation)");
        var manager = Object.FindFirstObjectByType<WorldManager>();
        var world = manager.CurrentWorld;
        Check(Clear(world.Map, world.Player.transform), "initial actual Physics2D geometry clearance");
    }
    static World[] Worlds(WorldManager manager) => (World[])Field(manager, "worlds");
    static object Field(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    static bool Exclusive(WorldManager m) => Worlds(m).Count(w => w.IsActive) == 1 && Worlds(m).Count(w => w.Map.IsActive) == 1 && m.CurrentWorld.IsActive && m.CurrentWorld.Map.IsActive && PlayerController.instance == m.CurrentWorld.Player;
    static bool Clear(WorldMap map, Transform body)
    {
        Physics2D.SyncTransforms();
        if (!WorldMap.TryGetFootprint(body, out Vector2 offset, out float radius) || !map.TryGetPlayableRect(out Rect rect)) return false;
        Vector2 center = (Vector2)body.position + offset;
        radius += 0.02f;
        if (center.x - radius < rect.xMin || center.x + radius > rect.xMax || center.y - radius < rect.yMin || center.y + radius > rect.yMax) return false;
        return !Physics2D.OverlapCircleAll(center, radius).Any(c => !c.isTrigger && c.transform.IsChildOf(map.transform));
    }
    static void Quiet()
    {
        // Controlled survival fixture; clocks, native physics and presentation remain live.
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemyController) b.enabled = false;
        foreach (var hp in Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None)) hp.maxHealth = hp.currentHealth = 1000000;
        // Keep real physics active but isolate position assertions from live input and enemy pushes.
        foreach (var player in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // PlayerController moves its Transform directly, independently of Rigidbody constraints.
            player.moveSpeed = 0f;
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            if (body != null) body.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        if (UIController.instance != null) UIController.instance.levelUpPanel.SetActive(false);
        Time.timeScale = 1; Application.runInBackground = true;
    }
    static IEnumerator Until(Func<bool> predicate, float seconds, string text)
    {
        double limit = EditorApplication.timeSinceStartup + seconds;
        while (!predicate()) { if (EditorApplication.timeSinceStartup > limit) throw new TimeoutException(text); yield return null; }
    }
    static IEnumerator Live()
    {
        Initial(); Invoke("WorldMapChecks", "AssessMapClearPoints");
        Quiet();
        var m = Object.FindFirstObjectByType<WorldManager>(); var worlds = Worlds(m);
        var flow = m.GetComponent<StateSwitchController>(); var run = Object.FindFirstObjectByType<RunStageController>();
        Require(flow.SwitchInterval == 30 && flow.AutomaticSwitchingEnabled, "authored natural 30s clock enabled; no timer injection");
        int events = 0, midpoint = 0, samples = 0, bad = 0;
        float start = Time.time, remaining = flow.RemainingTime, commitElapsed = 0;
        Action changed = () => { events++; commitElapsed = Time.time - start; Check(Exclusive(m), "WorldChanged observes exactly one content/map and correct alias"); };
        Action mid = () => { midpoint++; Check(flow.FlipProgress == 0.5f && Exclusive(m) && Clear(m.CurrentWorld.Map, m.CurrentWorld.Player.transform), "natural midpoint edge-on, exclusive, native-clear incoming hero"); };
        m.WorldChanged += changed; flow.TransitionMidpoint += mid;
        double limit = EditorApplication.timeSinceStartup + 35;
        while (events == 0 || flow.IsFlipping)
        {
            RequireTime(limit, "natural interval"); samples++; if (!Exclusive(m)) bad++;
            yield return null;
        }
        m.WorldChanged -= changed; flow.TransitionMidpoint -= mid;
        Check(events == 1 && midpoint == 1 && Math.Abs(commitElapsed - remaining) < 0.6f, "natural switch once after remaining=" + remaining + " elapsed=" + commitElapsed);
        Check(samples > 10 && bad == 0, "exclusive real-frame samples=" + samples + " bad=" + bad);
        flow.AutomaticSwitchingEnabled = false;
        World entry = m.CurrentWorld, other = worlds.Single(w => w != entry);
        var block = new GameObject("Full-search blocker (acceptance fixture)"); block.transform.SetParent(other.Map.transform, false);
        block.transform.position = entry.Player.transform.position; block.AddComponent<BoxCollider2D>().size = new Vector2(1000, 1000);
        int rejectedEvents = 0; Action rejection = () => rejectedEvents++; m.WorldChanged += rejection;
        Vector3 before = entry.Player.transform.position;
        Require(m.SwitchWorld(other.WorldId), "blocked switch request accepted through normal API");
        yield return Until(() => flow.IsFlipping, 8, "blocked flip starts");
        yield return Until(() => !flow.IsFlipping, 3, "blocked midpoint cancels");
        Check(rejectedEvents == 0 && m.CurrentWorld == entry && Exclusive(m) && entry.Player.transform.position == before,
            "blocked normal-API midpoint safely cancels without ownership or position mutation"
            + " events=" + rejectedEvents + " sameWorld=" + (m.CurrentWorld == entry)
            + " exclusive=" + Exclusive(m) + " before=" + before + " after=" + entry.Player.transform.position);
        m.WorldChanged -= rejection; block.SetActive(false); Object.Destroy(block);
        // Destroy is deferred; do not include the disposable blocker in restoration snapshots.
        yield return null;

        Require(entry.Map.TryGetPlayableRect(out Rect rect), "actual playable boundary available");
        WorldMap.TryGetFootprint(entry.Player, out Vector2 offset, out float radius);
        bool nearWall = false;
        for (float y = rect.center.y; y < rect.yMax - radius && !nearWall; y += 0.5f)
        {
            entry.Player.transform.position = new Vector3(rect.xMax - radius - offset.x - 0.1f, y, 0);
            nearWall = Clear(entry.Map, entry.Player.transform);
        }
        Require(nearWall, "controlled hero placement is native-clear near actual right wall");
        entry.Player.GetComponent<Rigidbody2D>().position = entry.Player.transform.position;
        Debug.Log("MAP_BATCH FIXTURE hero near right wall=" + entry.Player.transform.position + "; controlled combat/high HP; no elapsed-time or finale-state injection");
        var fusion = m.GetComponent<FusionTransitionController>();
        var flip = Object.FindFirstObjectByType<WorldFlipPresentation>();
        var colliders = other.Map.GetComponentsInChildren<Collider2D>(true).ToDictionary(c => c, c => c.enabled);
        var bodies = other.Map.GetComponentsInChildren<Rigidbody2D>(true).ToDictionary(b => b, b => b.simulated);
        var rendererFlags = worlds.SelectMany(w => w.Map.GetComponentsInChildren<Renderer>(true)).ToDictionary(r => r, r => r.forceRenderingOff);
        int faces = 0, restored = 0, renderBad = 0;
                string renderedFace = null;
                var captures = new HashSet<string>();
        Action<ScriptableRenderContext, Camera> begin = (context, camera) =>
        {
            if (camera != flip.WorldCamera || !fusion.IsFlipping) return;
            renderedFace = fusion.DisplayWorld == other ? "other-face" : "entry-face";
            if (fusion.DisplayWorld != other) return;
            faces++;
            bool active = other.Map.IsActive;
            // Composite enable flags may remain true; body simulation and native hits determine participation.
            var enabledColliders = colliders.Keys.Where(c => c.enabled
                && (c.attachedRigidbody == null || c.attachedRigidbody.simulated)).ToArray();
            var simulatedBodies = bodies.Keys.Where(b => b.simulated).ToArray();
            var nativeHits = Physics2D.OverlapCircleAll(entry.Player.transform.position, 100).Where(c => c.transform.IsChildOf(other.Map.transform)).ToArray();
            if (!active || enabledColliders.Length != 0 || simulatedBodies.Length != 0 || nativeHits.Length != 0)
            {
                if (renderBad == 0) Debug.Log("MAP_BATCH RENDER DETAIL active=" + active + " enabled=" + string.Join(",", enabledColliders.Select(c => c.name + "/" + c.GetType().Name))
                    + " simulated=" + simulatedBodies.Length + " nativeHits=" + nativeHits.Length);
                renderBad++;
            }
        };
        Action<ScriptableRenderContext, Camera> end = (context, camera) =>
        {
            if (camera != flip.WorldCamera || !fusion.IsPlaying) return;
            restored++;
            if (other.Map.IsActive || !entry.Map.IsActive || colliders.Any(p => p.Key.enabled != p.Value) || bodies.Any(p => p.Key.simulated != p.Value)
                || rendererFlags.Any(p => p.Key.forceRenderingOff != p.Value)) renderBad++;
        };
        RenderPipelineManager.beginCameraRendering += begin; RenderPipelineManager.endCameraRendering += end;
        int completeFrame = -1, spawnFrame = -1; EnemyController boss = null;
        fusion.Completed += () => completeFrame = Time.frameCount;
        Action state = () =>
        {
            if (!run.IsBossPhase || boss != null || run.RemainingBosses != 1) return;
            boss = ((IEnumerable)Field(run, "bosses")).Cast<EnemyController>().Single(); spawnFrame = Time.frameCount;
            Check(Clear(entry.Map, boss.transform), "actual spawned boss full footprint clears native terrain AND boundary at spawn callback");
            Debug.Log("MAP_BATCH boss=" + boss.transform.position + " hero=" + entry.Player.transform.position + " frame=" + spawnFrame);
        };
        run.StateChanged += state;
        try
        {
            Require(run.TryStartFinale(), "normal TryStartFinale accepted near right boundary");
            Check(m.IsFinalFusion && m.IsFused && worlds.All(w => w.IsActive) && entry.Map.IsActive && !other.Map.IsActive, "final fusion owns entry map only with both contents active");
            limit = EditorApplication.timeSinceStartup + 12;
                        while (!run.IsBossPhase && !run.IsDefeated)
                        {
                            RequireTime(limit, "normal finale boss placement");
                            if (!Application.isBatchMode)
                            {
                                yield return new WaitForEndOfFrame();
                                if (renderedFace != null && captures.Add(renderedFace)) Capture(renderedFace);
                            }
                            yield return null;
                        }
                        if (!Application.isBatchMode) { yield return new WaitForEndOfFrame(); Capture("boss"); }
            Check(run.IsBossPhase && !run.IsDefeated && boss != null && run.RemainingBosses == 1 && spawnFrame > completeFrame && completeFrame >= 0,
                "single boss spawned after real fusion Completed on a later frame");
            Check(faces > 0 && restored > 0 && renderBad == 0, "actual URP callbacks: other-face=" + faces + " restored=" + restored + " bad=" + renderBad);
            if (boss != null) { boss.TakeDamage(boss.health + 1); yield return Until(() => run.IsCompleted || run.IsDefeated, 3, "boss death completion"); }
            Check(run.IsCompleted && !run.IsDefeated && Time.timeScale == 0, "boss TakeDamage/Died completes finale through normal API");
        }
        finally { RenderPipelineManager.beginCameraRendering -= begin; RenderPipelineManager.endCameraRendering -= end; run.StateChanged -= state; }
        m.enabled = false;
        Check(!m.IsFused && entry.Map.IsActive && !other.Map.IsActive && !other.IsActive, "manager disable restores entry-only map/content");
        var old = m; run.RestartRun();
        yield return Until(() => old == null && Object.FindFirstObjectByType<WorldManager>() != null, 10, "RestartRun scene replacement");
        yield return null; yield return null;
        m = Object.FindFirstObjectByType<WorldManager>(); run = Object.FindFirstObjectByType<RunStageController>();
        Check(m.IsInitialized && Exclusive(m) && !m.IsFused && run.IsRunning && !run.IsFinaleStarted && run.RemainingBosses == 0 && Time.timeScale == 1,
            "normal RestartRun loads fresh initialized exclusive run with no stale boss/fusion");
        Initial();
    }
    static void Capture(string label)
        {
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../MapMergeCompatibility/" + Scene + "-" + label + ".png"));
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("MAP_BATCH CAPTURE " + path + " " + image.width + "x" + image.height);
            }
            finally { Object.Destroy(image); }
        }
        static void RequireTime(double deadline, string label) { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(label); }
    static IEnumerator ExistingFinale()
    {
        var assembly = Compile(new[] { "Tools/RunFinaleChecks.cs" }, "RunFinaleChecks");
        var type = assembly.GetType("RunFinaleChecks");
        yield return (IEnumerator)type.GetMethod("Run").Invoke(null, null);
        passed += (int)type.GetProperty("Passed").GetValue(null); failed += (int)type.GetProperty("Failed").GetValue(null);
    }
    static IEnumerator Drive(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        while (stack.Count > 0)
        {
            object current = null; Exception error = null;
            try
            {
                if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                current = stack.Peek().Current;
            }
            catch (Exception e) { error = e; }
            if (error != null)
            {
                while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { Debug.LogException(e); } }
                Finish(error.ToString(), 2); yield break;
            }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Finish("completed");
    }
    static void Finish(string message, int code = 0)
    {
        if (finished) return; finished = true;
        if (code != 0) aborts++;
        Debug.Log("MAP_BATCH RESULT " + Scene + "/" + Suite + " passed=" + passed + " failed=" + failed + " expectedErrors=" + expected + " unexpectedErrors=" + unexpected + " aborts=" + aborts + " " + message);
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        EditorApplication.Exit(code != 0 ? code : failed + unexpected > 0 ? 1 : 0);
    }
}
public sealed class WorldMapChecksBatchHost : MonoBehaviour { }
