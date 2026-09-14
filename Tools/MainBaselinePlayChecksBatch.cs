using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Same warm-project/real-coroutine pattern as WorldMapChecksBatch; no dynamic compiler or saves.
[InitializeOnLoad]
public static class MainBaselinePlayChecksBatch
{
    const string Key = "MainBaselinePlayChecksBatch";
    static bool started, finished;
    static EditorWindow gameView;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static MainBaselinePlayChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    public static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("Required " + key);
        return args[i + 1];
    }
    public static void Run()
    {
        string actual = Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\');
        string expected = Path.GetFullPath(Path.Combine(Arg("-baselineSource"), "Library/DebugRunValidationProject/Assets"));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only isolated validation project allowed");
        SessionState.SetBool(Key, true);
        SessionState.SetInt(Key + ".errors", 0); SessionState.SetInt(Key + ".search", 0);
        // Include import/startup time in the budget, leaving time to flush evidence before the external kill.
        SessionState.SetString(Key + ".deadline", Math.Min(EditorApplication.timeSinceStartup + 145, 165).ToString(CultureInfo.InvariantCulture));
        Hook();
        try
        {
            Preflight();
            SessionState.SetInt(Key + ".preflightPassed", MainBaselinePlayChecks.Passed);
            SessionState.SetInt(Key + ".preflightFailed", MainBaselinePlayChecks.Failed);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Focus(); EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static void Hook()
    {
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
    }
    static void Focus()
    {
        if (gameView == null) gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.Show(); gameView.Focus();
    }
    static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var s = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    static void Preflight()
    {
        var scenePaths = new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/DebugRun.unity" };
        var dependencies = AssetDatabase.GetDependencies(scenePaths.Concat(new[] { "Assets/Prefabs/Hollow.prefab" }).ToArray(), true);
        var rows = new List<string> { "path\tsource_sha256\tisolated_sha256" };
        foreach (var path in dependencies.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p))
        {
            foreach (var file in new[] { path, path + ".meta" }.Where(File.Exists))
            {
                string source = Path.Combine(Arg("-baselineSource"), file);
                if (!File.Exists(source) || Hash(source) != Hash(file)) throw new InvalidOperationException("Native dependency hash mismatch: " + file);
                rows.Add(file + "\t" + Hash(source) + "\t" + Hash(file));
            }
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                MainBaselinePlayChecks.Require(prefab != null, "import prefab " + path);
                Missing(prefab);
            }
        }
        File.WriteAllLines(Path.Combine(Arg("-baselineOutput"), "native-dependency-hashes.tsv"), rows);
        MainBaselinePlayChecks.Note("PREFLIGHT native dependency hashes identical=" + (rows.Count - 1));
        foreach (string path in scenePaths)
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects()) Missing(root);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            var manager = all.OfType<WorldManager>().Single();
            var worlds = MainBaselinePlayChecks.Get<World[]>(manager, "worlds");
            MainBaselinePlayChecks.Require(worlds.Length == 2 && worlds.All(w => w != null && w.IsConfigured && w.Player != null && w.Map != null)
                && worlds.Select(w => w.WorldId).Distinct().Count() == 2, scene.name + " actual imported two-world config");
            foreach (var world in worlds)
            {
                foreach (Type type in new[] { typeof(PistolController), typeof(LanternController), typeof(LightningController), typeof(BowController), typeof(DaggerController), typeof(ScytheController) })
                {
                    var weapons = world.ContentRoot.GetComponentsInChildren(type, true);
                    MainBaselinePlayChecks.Require(weapons.Length == 1, scene.name + "/" + world.WorldId + " actual " + type.Name + " binding");
                }
                var scythe = world.ContentRoot.GetComponentInChildren<ScytheController>(true);
                var pistol = world.ContentRoot.GetComponentInChildren<PistolController>(true);
                MainBaselinePlayChecks.Check(MainBaselinePlayChecks.Get<float>(scythe, "attackDamage") == 16 && MainBaselinePlayChecks.Get<float>(scythe, "attackSpeed") == .25f
                    && MainBaselinePlayChecks.Get<float>(pistol, "attackSpeed") == 1.5f && MainBaselinePlayChecks.Get<float>(pistol, "attackDamage") == 8f
                    && MainBaselinePlayChecks.Get<float>(pistol, "attackRange") == 7.5f,
                    scene.name + "/" + world.WorldId + " imported Scythe damage16/speed.25; Pistol speed1.5/damage8/range7.5");
            }
            var run = all.OfType<RunStageController>().Single();
            var timer = all.OfType<StateSwitchController>().Single(t => t.isActiveAndEnabled
                && MainBaselinePlayChecks.Get<WorldManager>(t, "worldManager") == manager);
            MainBaselinePlayChecks.Check(timer.SwitchInterval == 30 && MainBaselinePlayChecks.Get<float>(timer, "warningDuration") == 5
                && MainBaselinePlayChecks.Get<float>(timer, "flipDuration") == .8f && run.FinaleStartTime == 480,
                scene.name + " imported 30/5/.8/480");
            MainBaselinePlayChecks.Require(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == path), scene.name + " registered for production restart");
            MainBaselinePlayChecks.Note("PREFLIGHT " + scene.name + " loaded natively; missing scripts=0; components=" + all.Length);
        }
    }
    static void Missing(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                throw new InvalidOperationException("Missing script: " + root.name + "/" + transform.name);
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var serialized = new SerializedObject(component); var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                    throw new InvalidOperationException("Missing serialized binding: " + component.name + "/" + property.propertyPath);
        }
    }
    public static double RemainingSeconds => double.Parse(SessionState.GetString(Key + ".deadline", "0"), CultureInfo.InvariantCulture) - EditorApplication.timeSinceStartup;
    static void Poll()
    {
        if (finished) return;
        if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + ".deadline", "0"), CultureInfo.InvariantCulture))
        { Finish(2, "internal native budget exceeded"); return; }
        if (gameView != null) gameView.Repaint();
        if (started) EditorApplication.QueuePlayerLoopUpdate();
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        MainBaselinePlayChecks.Passed = SessionState.GetInt(Key + ".preflightPassed", 0);
        MainBaselinePlayChecks.Failed = SessionState.GetInt(Key + ".preflightFailed", 0);
        try
        {
            Focus();
            var host = new GameObject("MainBaseline persistent validation host"); Object.DontDestroyOnLoad(host);
            stack.Push(MainBaselinePlayChecks.Run()); host.AddComponent<MainBaselinePlayChecksBatchHost>().StartCoroutine(Drive());
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive()
    {
        while (stack.Count > 0)
        {
            object current;
            try
            {
                if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                current = stack.Peek().Current;
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
            }
            catch (Exception e) { Finish(2, e.ToString()); yield break; }
            yield return current;
        }
        Finish(0, "completed; DebugRun native static load, Main live flow; no long victory suite");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        bool search = type == LogType.Exception && message.StartsWith("ArgumentOutOfRangeException: Index was out of range.")
            && trace.Contains("UnityEditor.Search.SearchDatabase") && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")
            && !trace.Contains("Assets/") && !trace.Contains("Assets\\");
        string counter = Key + (search ? ".search" : ".errors");
        SessionState.SetInt(counter, SessionState.GetInt(counter, 0) + 1);
        Debug.Log("BASELINE " + (search ? "KNOWN_SEARCH " : "UNEXPECTED_ERROR ") + message);
    }
    [Serializable] class Summary
    {
        public int passed, failed, unexpectedErrors, knownSearchErrors, code;
        public string message;
        public string[] evidence;
        public MainBaselinePlayChecks.CaseResult[] cases;
    }
    static void Finish(int code, string message)
    {
        if (finished) return; finished = true;
        while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { code = 2; message += "\n" + e; } }
        var summary = new Summary { passed = MainBaselinePlayChecks.Passed, failed = MainBaselinePlayChecks.Failed,
            unexpectedErrors = SessionState.GetInt(Key + ".errors", 0), knownSearchErrors = SessionState.GetInt(Key + ".search", 0),
            message = message, evidence = MainBaselinePlayChecks.Evidence.ToArray(), cases = MainBaselinePlayChecks.Cases.ToArray() };
        if (code == 0 && summary.failed + summary.unexpectedErrors > 0) code = 1;
        if (code == 0 && summary.knownSearchErrors > 0) code = 3;
        summary.code = code;
        File.WriteAllText(Path.Combine(Arg("-baselineOutput"), "summary.json"), JsonUtility.ToJson(summary, true));
        Debug.Log("BASELINE RESULT passed=" + summary.passed + " failed=" + summary.failed + " unexpected=" + summary.unexpectedErrors
            + " knownSearch=" + summary.knownSearchErrors + " code=" + code + " " + message);
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        EditorApplication.Exit(code);
    }
}
public sealed class MainBaselinePlayChecksBatchHost : MonoBehaviour { }
