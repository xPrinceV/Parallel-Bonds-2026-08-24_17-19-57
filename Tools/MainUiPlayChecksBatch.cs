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

[InitializeOnLoad]
public static class MainUiPlayChecksBatch
{
    const string Key = "MainUiPlayChecksBatch";
    static bool started, finished;
    static EditorWindow gameView;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static MainUiPlayChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    public static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("Required " + key);
        return args[i + 1];
    }
    public static void Run()
    {
        string project = Path.GetFullPath(Arg("-uiValidationProject")).TrimEnd('/', '\\');
        string source = Path.GetFullPath(Arg("-uiSource")).TrimEnd('/', '\\');
        if (project == source || Path.GetFileName(project) != "DebugRunValidationProject"
            || !string.Equals(Path.GetFullPath(Application.dataPath), Path.Combine(project, "Assets"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only explicitly selected isolated validation project allowed");
        SessionState.SetBool(Key, true);
        foreach (string field in new[] { ".errors", ".search", ".warnings" }) SessionState.SetInt(Key + field, 0);
        SessionState.SetString(Key + ".deadline", Math.Min(EditorApplication.timeSinceStartup + 145, 165).ToString(CultureInfo.InvariantCulture));
        Hook();
        try
        {
            Preflight();
            SessionState.SetInt(Key + ".passed", MainUiPlayChecks.Passed); SessionState.SetInt(Key + ".failed", MainUiPlayChecks.Failed);
            File.WriteAllLines(Path.Combine(Arg("-uiOutput"), "preflight-evidence.txt"), MainUiPlayChecks.Evidence);
            EditorSceneManager.OpenScene("Assets/Scenes/Main Menu.unity"); Focus(); EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static void Hook()
    { EditorApplication.update -= Poll; EditorApplication.update += Poll; Application.logMessageReceived -= Log; Application.logMessageReceived += Log; }
    static void Focus()
    {
        if (gameView == null) gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.Show(); gameView.Focus();
    }
    static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void Preflight()
    {
        var scenes = new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Main.unity", "Assets/Scenes/DebugRun.unity" };
        var rows = new List<string> { "path\tsource_sha256\tisolated_sha256" };
        foreach (var path in AssetDatabase.GetDependencies(scenes, true).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p))
        {
            foreach (var file in new[] { path, path + ".meta" }.Where(File.Exists))
            {
                string original = Path.Combine(Arg("-uiSource"), file);
                if (!File.Exists(original) || Hash(original) != Hash(file)) throw new InvalidOperationException("Imported dependency hash mismatch: " + file);
                rows.Add(file + "\t" + Hash(original) + "\t" + Hash(file));
            }
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) Missing(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }
        File.WriteAllLines(Path.Combine(Arg("-uiOutput"), "native-dependency-hashes.tsv"), rows);
        MainUiPlayChecks.Note("PREFLIGHT native identical dependency files=" + (rows.Count - 1));
        foreach (var path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects()) Missing(root);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            MainUiPlayChecks.Check(all.OfType<AudioService>().Count() == 1 && all.OfType<GameAudioEvents>().Count() == 1, scene.name + " imported singleton audio/hook");
            if (scene.name != "Main Menu")
            {
                var manager = all.OfType<WorldManager>().Single(); var worlds = MainUiPlayChecks.Get<World[]>(manager, "worlds");
                MainUiPlayChecks.Require(worlds.Length == 2 && worlds.All(w => w != null && w.IsConfigured && w.Player != null && w.Map != null)
                    && worlds.Select(w => w.WorldId).Distinct().Count() == 2, scene.name + " imported two configured worlds");
                var run = all.OfType<RunStageController>().Single();
                var timer = all.OfType<StateSwitchController>().Single(t => t.isActiveAndEnabled && MainUiPlayChecks.Get<WorldManager>(t, "worldManager") == manager);
                MainUiPlayChecks.Check(timer.SwitchInterval == 30 && MainUiPlayChecks.Get<float>(timer, "warningDuration") == 5
                    && MainUiPlayChecks.Get<float>(timer, "flipDuration") == .8f && run.FinaleStartTime == 480, scene.name + " native imported 30/5/.8/480");
                var over = all.OfType<GameOverManager>().Single(); MainUiPlayChecks.ResultButtons(over);
                MainUiPlayChecks.Check(MainUiPlayChecks.Get<RunStageController>(over, "runController") == run && !over.gameOverUI.activeSelf
                    && !MainUiPlayChecks.Get<GameObject>(over, "victoryUI").activeSelf && World.GetFor(over) == null, scene.name + " shared UI hook and initially hidden results");
            }
            MainUiPlayChecks.Require(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == path), scene.name + " enabled build scene");
            MainUiPlayChecks.Note("PREFLIGHT imported " + scene.name + "; missing scripts/bindings=0; MonoBehaviours=" + all.Length);
        }
    }
    static void Missing(GameObject root)
    {
        if (root == null) throw new InvalidOperationException("Failed imported prefab");
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0) throw new InvalidOperationException("Missing script: " + transform.name);
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var property = new SerializedObject(component).GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                    throw new InvalidOperationException("Missing serialized binding: " + component.name + "/" + property.propertyPath);
        }
    }
    static void Poll()
    {
        if (finished) return;
        if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + ".deadline", "0"), CultureInfo.InvariantCulture)) { Finish(2, "internal native deadline"); return; }
        if (gameView != null) gameView.Repaint();
        if (started) EditorApplication.QueuePlayerLoopUpdate();
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        MainUiPlayChecks.Passed = SessionState.GetInt(Key + ".passed", 0); MainUiPlayChecks.Failed = SessionState.GetInt(Key + ".failed", 0);
        try
        {
            Focus(); var host = new GameObject("Main UI isolated validation host"); Object.DontDestroyOnLoad(host);
            stack.Push(MainUiPlayChecks.Run()); host.AddComponent<MainUiPlayChecksBatchHost>().StartCoroutine(Drive());
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
        Finish(0, "Menu-to-Victory-to-restart-to-defeat-to-menu-to-Main completed; DebugRun static native only; no Hollow/weapon fixtures");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Warning) { SessionState.SetInt(Key + ".warnings", SessionState.GetInt(Key + ".warnings", 0) + 1); return; }
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        bool search = type == LogType.Exception && message.StartsWith("ArgumentOutOfRangeException: Index was out of range.")
            && trace.Contains("UnityEditor.Search.SearchDatabase") && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")
            && !trace.Contains("Assets/") && !trace.Contains("Assets\\");
        string counter = Key + (search ? ".search" : ".errors"); SessionState.SetInt(counter, SessionState.GetInt(counter, 0) + 1);
        Debug.Log("UI_NATIVE " + (search ? "KNOWN_SEARCH " : "UNEXPECTED_ERROR ") + message);
    }
    [Serializable] sealed class Summary
    {
        public int passed, failed, unexpectedErrors, knownSearchErrors, warnings, code;
        public string message, currentScenario;
        public string[] completedScenarios, notCompletedScenarios, evidence;
        public MainUiPlayChecks.ScenarioResult[] scenarios;
    }
    static void Finish(int code, string message)
    {
        if (finished) return; finished = true;
        while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { code = 2; message += "\n" + e; } }
        if (code != 0) MainUiPlayChecks.Check(false, "aborted " + MainUiPlayChecks.CurrentScenario + ": " + message);
        var summary = new Summary { passed = MainUiPlayChecks.Passed, failed = MainUiPlayChecks.Failed,
            unexpectedErrors = SessionState.GetInt(Key + ".errors", 0), knownSearchErrors = SessionState.GetInt(Key + ".search", 0), warnings = SessionState.GetInt(Key + ".warnings", 0),
            message = message, currentScenario = MainUiPlayChecks.CurrentScenario, completedScenarios = MainUiPlayChecks.CompletedScenarios.ToArray(),
            notCompletedScenarios = MainUiPlayChecks.Scenarios.Except(MainUiPlayChecks.CompletedScenarios).ToArray(), evidence = MainUiPlayChecks.Evidence.ToArray(), scenarios = MainUiPlayChecks.Results.ToArray() };
        if (code == 0 && summary.failed + summary.unexpectedErrors > 0) code = 1;
        if (code == 0 && summary.knownSearchErrors > 0) code = 3;
        summary.code = code;
        File.WriteAllText(Path.Combine(Arg("-uiOutput"), "summary.json"), JsonUtility.ToJson(summary, true));
        Debug.Log("UI_NATIVE RESULT passed=" + summary.passed + " failed=" + summary.failed + " unexpected=" + summary.unexpectedErrors + " knownSearch=" + summary.knownSearchErrors + " code=" + code);
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        EditorApplication.Exit(code);
    }
}
public sealed class MainUiPlayChecksBatchHost : MonoBehaviour { }
