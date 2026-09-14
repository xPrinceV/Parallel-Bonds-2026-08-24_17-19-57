using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Same native, domain-reload-safe host as RiftLordChecksBatch; never saves a scene/asset.
[InitializeOnLoad]
public static class FiringFlipChecksBatch
{
    const string Key = "FiringFlipChecksBatch";
    static bool started, finished;
    static EditorWindow gameView;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static FiringFlipChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    public static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
        if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("Required " + key);
        return args[i + 1];
    }
    public static void Run()
    {
        string project = Path.GetFullPath(Arg("-firingValidationProject")).TrimEnd('/', '\\');
        string source = Path.GetFullPath(Arg("-firingSource")).TrimEnd('/', '\\');
        if (!string.Equals(project, Path.Combine(source, "Library", "DebugRunValidationProject"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(Application.dataPath), Path.Combine(project, "Assets"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only source/Library/DebugRunValidationProject allowed");
        SessionState.SetBool(Key, true);
        foreach (string field in new[] { ".errors", ".search", ".warnings" }) SessionState.SetInt(Key + field, 0);
        SessionState.SetString(Key + ".deadline", (EditorApplication.timeSinceStartup + 210).ToString(CultureInfo.InvariantCulture));
        Hook();
        try
        {
            FiringFlipChecks.Defaults();
            foreach (var path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/DebugRun.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                            throw new InvalidOperationException("Missing scene script: " + t.name);
                FiringFlipChecks.SceneBinding(scene);
            }
            SessionState.SetInt(Key + ".passed", FiringFlipChecks.Passed); SessionState.SetInt(Key + ".failed", FiringFlipChecks.Failed);
            File.WriteAllLines(Path.Combine(Arg("-firingOutput"), "preflight-evidence.txt"), FiringFlipChecks.Evidence);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); Focus(); EditorApplication.EnterPlaymode();
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
    static void Poll()
    {
        if (finished) return;
        if (EditorApplication.timeSinceStartup > double.Parse(SessionState.GetString(Key + ".deadline", "0"), CultureInfo.InvariantCulture))
        { Finish(2, "internal native deadline (no external process termination)"); return; }
        if (gameView != null) gameView.Repaint();
        if (started) EditorApplication.QueuePlayerLoopUpdate();
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        FiringFlipChecks.Passed = SessionState.GetInt(Key + ".passed", 0); FiringFlipChecks.Failed = SessionState.GetInt(Key + ".failed", 0);
        FiringFlipChecks.Evidence.AddRange(File.ReadAllLines(Path.Combine(Arg("-firingOutput"), "preflight-evidence.txt")));
        try
        {
            Focus(); var host = new GameObject("Firing flip isolated check host"); Object.DontDestroyOnLoad(host);
            stack.Push(FiringFlipChecks.Run()); host.AddComponent<FiringFlipChecksBatchHost>().StartCoroutine(Drive());
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
        Finish(0, "Focused real-frame firing flip regression; cloned Play-only fixtures, no synthetic weapon Update calls");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Warning) { SessionState.SetInt(Key + ".warnings", SessionState.GetInt(Key + ".warnings", 0) + 1); return; }
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        bool search = type == LogType.Exception && message.StartsWith("ArgumentOutOfRangeException: Index was out of range.")
            && trace.Contains("UnityEditor.Search.SearchDatabase") && !trace.Contains("Assets/") && !trace.Contains("Assets\\");
        string counter = Key + (search ? ".search" : ".errors"); SessionState.SetInt(counter, SessionState.GetInt(counter, 0) + 1);
        Debug.Log("FIRING_NATIVE " + (search ? "KNOWN_SEARCH" : "UNEXPECTED_ERROR") + " (details in Unity.log)");
    }
    [Serializable] sealed class Summary
    {
        public int passed, failed, unexpectedErrors, knownSearchErrors, warnings, code;
        public string message, currentScenario;
        public string[] completedScenes, evidence;
    }
    static void Finish(int code, string message)
    {
        if (finished) return; finished = true;
        while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { code = 2; message += "\n" + e; } }
        if (code != 0) FiringFlipChecks.Check(false, "aborted " + FiringFlipChecks.Scenario + ": " + message);
        var summary = new Summary { passed = FiringFlipChecks.Passed, failed = FiringFlipChecks.Failed,
            unexpectedErrors = SessionState.GetInt(Key + ".errors", 0), knownSearchErrors = SessionState.GetInt(Key + ".search", 0), warnings = SessionState.GetInt(Key + ".warnings", 0),
            message = message, currentScenario = FiringFlipChecks.Scenario, completedScenes = FiringFlipChecks.Completed.ToArray(), evidence = FiringFlipChecks.Evidence.ToArray() };
        if (code == 0 && (summary.failed + summary.unexpectedErrors > 0 || summary.completedScenes.Length != 2)) code = 1;
        if (code == 0 && summary.knownSearchErrors > 0) code = 3;
        summary.code = code;
        File.WriteAllText(Path.Combine(Arg("-firingOutput"), "summary.json"), JsonUtility.ToJson(summary, true));
        Debug.Log("FIRING_NATIVE RESULT passed=" + summary.passed + " failed=" + summary.failed + " unexpected=" + summary.unexpectedErrors + " knownSearch=" + summary.knownSearchErrors + " code=" + code);
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        EditorApplication.Exit(code);
    }
}
public sealed class FiringFlipChecksBatchHost : MonoBehaviour { }
