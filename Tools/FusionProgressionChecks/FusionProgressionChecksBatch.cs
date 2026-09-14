using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FusionProgressionChecksBatch
{
    const string Key = "FusionProgressionChecksBatch";
    static bool started, finished;
    static EditorWindow gameView;
    static FusionProgressionChecksHost host;
    static FusionProgressionChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    public static string Arg(string key)
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Required " + key);
        return args[index + 1];
    }
    public static void Run()
    {
        string project = Path.GetFullPath(Arg("-fusionProject")).TrimEnd('/', '\\');
        string source = Path.GetFullPath(Arg("-fusionSource")).TrimEnd('/', '\\');
        if (!string.Equals(project, Path.Combine(source, "Library", "DebugRunValidationProject"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(Application.dataPath), Path.Combine(project, "Assets"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Exact isolated project required; never source editor");
        string scene = Arg("-fusionScene");
        if (scene != "Main" && scene != "DebugRun") throw new ArgumentException("Only Main/DebugRun");
        SessionState.SetBool(Key, true);
        foreach (string field in new[] { ".errors", ".search", ".warnings" }) SessionState.SetInt(Key + field, 0);
        SessionState.SetString(Key + ".deadline", (EditorApplication.timeSinceStartup + 210).ToString(CultureInfo.InvariantCulture));
        Hook();
        try
        {
            string path = "Assets/Scenes/" + scene + ".unity";
            var rows = new System.Collections.Generic.List<string> { "path\tsource_sha256\tisolated_sha256" };
            foreach (string dependency in AssetDatabase.GetDependencies(path, true).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p))
                foreach (string file in new[] { dependency, dependency + ".meta" }.Where(File.Exists))
                {
                    string original = Path.Combine(source, file);
                    if (!File.Exists(original) || Hash(original) != Hash(file)) throw new InvalidOperationException("Native dependency mismatch: " + file);
                    rows.Add(file + "\t" + Hash(original) + "\t" + Hash(file));
                }
            File.WriteAllLines(Path.Combine(Arg("-fusionOutput"), "native-dependency-hashes.tsv"), rows);
            EditorSceneManager.OpenScene(path); // Isolated copy only; never save any scene or asset.
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
        { Finish(2, "internal native deadline (210 seconds after executeMethod; external launch bound 270 seconds)"); return; }
        if (gameView != null) gameView.Repaint();
        if (started) EditorApplication.QueuePlayerLoopUpdate();
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        try
        {
            Focus(); var go = new GameObject("Fusion progression isolated acceptance host"); UnityEngine.Object.DontDestroyOnLoad(go);
            host = go.AddComponent<FusionProgressionChecksHost>(); host.StartCoroutine(Drive(FusionProgressionChecks.Run()));
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive(IEnumerator sequence)
    {
        var stack = new System.Collections.Generic.Stack<IEnumerator>(); stack.Push(sequence);
        while (stack.Count > 0 && !finished)
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
        Finish(0, "Native scenario driver completed");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type == LogType.Warning) { SessionState.SetInt(Key + ".warnings", SessionState.GetInt(Key + ".warnings", 0) + 1); return; }
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        bool search = trace.Contains("UnityEditor.Search.SearchDatabase");
        string field = Key + (search ? ".search" : ".errors"); SessionState.SetInt(field, SessionState.GetInt(field, 0) + 1);
        File.AppendAllText(Path.Combine(Arg("-fusionOutput"), "native-errors.log"), type + ": " + message + "\n" + trace + "\n");
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
        var summary = new Summary { passed = FusionProgressionChecks.Passed, failed = FusionProgressionChecks.Failed,
            unexpectedErrors = SessionState.GetInt(Key + ".errors", 0), knownSearchErrors = SessionState.GetInt(Key + ".search", 0),
            warnings = SessionState.GetInt(Key + ".warnings", 0), scene = Arg("-fusionScene"), message = message,
            completedScenarios = FusionProgressionChecks.Completed.ToArray(), evidence = FusionProgressionChecks.Evidence.ToArray() };
        if (code == 0 && (summary.failed + summary.unexpectedErrors > 0 || summary.completedScenarios.Length != 3)) code = 1;
        if (code == 0 && summary.knownSearchErrors > 0) code = 3;
        summary.code = code;
        File.WriteAllText(Path.Combine(Arg("-fusionOutput"), "summary.json"), JsonUtility.ToJson(summary, true));
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        Debug.Log("FUSION_PROGRESSION RESULT code=" + code + " passed=" + summary.passed + " failed=" + summary.failed);
        EditorApplication.Exit(code);
    }
}
// Run stages (-80) may restart ordinary waves; stop them before spawner Updates without invalidating enabled-component checks.
[DefaultExecutionOrder(-70)]
public sealed class FusionProgressionChecksHost : MonoBehaviour
{
    void Update() { FusionProgressionChecks.SuppressOrdinarySpawns(); }
}
