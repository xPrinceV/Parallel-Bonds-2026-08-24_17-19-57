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
public static class HealthUiPlayChecksBatch
{
    const string Key = "HealthUiPlayChecksBatch";
    static bool started, finished;
    static EditorWindow gameView;
    static readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
    static HealthUiPlayChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    public static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Required " + key);
        return args[index + 1];
    }
    public static void Run()
    {
        string project = Path.GetFullPath(Arg("-healthValidationProject")).TrimEnd('/', '\\');
        string source = Path.GetFullPath(Arg("-healthSource")).TrimEnd('/', '\\');
        if (!string.Equals(project, Path.Combine(source, "Library", "DebugRunValidationProject"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(Application.dataPath), Path.Combine(project, "Assets"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only exact warmed isolated project allowed; never source editor");
        string sceneName = Arg("-healthScene");
        if (sceneName != "Main" && sceneName != "DebugRun") throw new ArgumentException("Only Main/DebugRun");
        SessionState.SetBool(Key, true);
        foreach (string field in new[] { ".errors", ".search", ".warnings" }) SessionState.SetInt(Key + field, 0);
        SessionState.SetString(Key + ".deadline", (EditorApplication.timeSinceStartup + 175).ToString(CultureInfo.InvariantCulture));
        Hook();
        try
        {
            string path = "Assets/Scenes/" + sceneName + ".unity";
            var rows = new List<string> { "path\tsource_sha256\tisolated_sha256" };
            foreach (string dependency in AssetDatabase.GetDependencies(path, true).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p))
                foreach (string file in new[] { dependency, dependency + ".meta" }.Where(File.Exists))
                {
                    string original = Path.Combine(source, file);
                    if (!File.Exists(original) || Hash(original) != Hash(file)) throw new InvalidOperationException("Native dependency mismatch: " + file);
                    rows.Add(file + "\t" + Hash(original) + "\t" + Hash(file));
                }
            File.WriteAllLines(Path.Combine(Arg("-healthOutput"), "native-dependency-hashes.tsv"), rows);
            HealthUiPlayChecks.Note("Native AssetDatabase dependency files=" + (rows.Count - 1));
            var scene = EditorSceneManager.OpenScene(path);
            HealthUiPlayChecks.Preflight(scene);
            File.WriteAllLines(Path.Combine(Arg("-healthOutput"), "preflight-evidence.txt"), HealthUiPlayChecks.Evidence);

            SessionState.SetInt(Key + ".passed", HealthUiPlayChecks.Passed);
            SessionState.SetString(Key + ".completed", string.Join("\n", HealthUiPlayChecks.Completed));
            Focus(); EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
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
        { Finish(2, "internal native deadline"); return; }
        if (gameView != null) gameView.Repaint();
        if (started) EditorApplication.QueuePlayerLoopUpdate();
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        HealthUiPlayChecks.Passed = SessionState.GetInt(Key + ".passed", 0);
        HealthUiPlayChecks.Completed.AddRange(SessionState.GetString(Key + ".completed", "").Split('\n'));
        try
        {
            Focus(); var host = new GameObject("Health UI isolated validation host"); Object.DontDestroyOnLoad(host);
            Stack.Push(HealthUiPlayChecks.Run()); host.AddComponent<HealthUiPlayChecksHost>().StartCoroutine(Drive());
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive()
    {
        while (Stack.Count > 0)
        {
            object current;
            try
            {
                if (!Stack.Peek().MoveNext()) { (Stack.Pop() as IDisposable)?.Dispose(); continue; }
                current = Stack.Peek().Current;
                if (current is IEnumerator nested) { Stack.Push(nested); continue; }
            }
            catch (Exception e) { Finish(2, e.ToString()); yield break; }
            yield return current;
        }
        Finish(0, Arg("-healthScene") + " health-only native scenarios completed");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Warning) { SessionState.SetInt(Key + ".warnings", SessionState.GetInt(Key + ".warnings", 0) + 1); return; }
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        bool search = trace.Contains("UnityEditor.Search.SearchDatabase") && !trace.Contains("Assets/") && !trace.Contains("Assets\\");
        string field = Key + (search ? ".search" : ".errors"); SessionState.SetInt(field, SessionState.GetInt(field, 0) + 1);
    }
    [Serializable] sealed class Summary
    {
        public int code, passed, failed, unexpectedErrors, knownSearchErrors, warnings;
        public string scene, message;
        public string[] completedScenarios, evidence;
    }
    static void Finish(int code, string message)
    {
        if (finished) return; finished = true;
        while (Stack.Count > 0)
            try { (Stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { code = 2; message += "\n" + e; }
        var summary = new Summary { passed = HealthUiPlayChecks.Passed, failed = HealthUiPlayChecks.Failed,
            unexpectedErrors = SessionState.GetInt(Key + ".errors", 0), knownSearchErrors = SessionState.GetInt(Key + ".search", 0),
            warnings = SessionState.GetInt(Key + ".warnings", 0), scene = Arg("-healthScene"), message = message,
            completedScenarios = HealthUiPlayChecks.Completed.ToArray(), evidence = HealthUiPlayChecks.Evidence.ToArray() };
        if (code == 0 && summary.failed + summary.unexpectedErrors > 0) code = 1;
        if (code == 0 && summary.knownSearchErrors > 0) code = 3;
        summary.code = code;
        File.WriteAllText(Path.Combine(Arg("-healthOutput"), "summary.json"), JsonUtility.ToJson(summary, true));
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        Debug.Log("HEALTH_UI RESULT code=" + code + " passed=" + summary.passed + " failed=" + summary.failed);
        EditorApplication.Exit(code);
    }
}
public sealed class HealthUiPlayChecksHost : MonoBehaviour { }
