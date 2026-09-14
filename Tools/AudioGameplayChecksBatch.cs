using System;

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Isolated Editor helper only. Never saves a scene or modifies production assets.
[InitializeOnLoad]
public static class AudioGameplayChecksBatch
{
    const string Key = "AudioGameplayChecksBatch";
    static bool started, finishing;
    static Type checks;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static AudioGameplayChecksBatch()
    {
        if (SessionState.GetBool(Key, false)) { EditorApplication.update += Poll; Application.logMessageReceived += Log; }
    }
    static string Arg(string name, string fallback = "")
    { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
    public static void Run()
    {
        SessionState.SetBool(Key, true);
        SessionState.SetInt(Key + ".Errors", 0); SessionState.SetInt(Key + ".SearchErrors", 0);
        SessionState.SetString(Key + ".Deadline", (EditorApplication.timeSinceStartup + 145).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        try
        {
            Preflight();
            string suite = Arg("-audioSuite", "Menu");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + (suite == "Menu" || suite == "Main" ? "Main Menu" : suite) + ".unity");
            if (suite == "DebugRun" && Arg("-audioEntry") == "Echo")
            {
                var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldManager>(true)).Single();

                var serialized = new SerializedObject(manager);
                serialized.FindProperty("initialWorldId").enumValueIndex = (int)WorldId.Echo;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("AUDIO_GAMEPLAY FIXTURE Echo initialWorldId changed ONLY in unsaved isolated scene memory before Awake/Start; disk scene hash preserved");
            }
            EditorApplication.update -= Poll; EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(2, error.ToString()); }
    }
    static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void Preflight()
    {
        var scenes = Arg("-audioMode", "Full") == "RestartOnly"
            ? new[] { "Assets/Scenes/DebugRun.unity" }
            : new[] { "Assets/Scenes/Main Menu.unity", "Assets/Scenes/Main.unity", "Assets/Scenes/DebugRun.unity" };
        const string prefabPath = "Assets/Game/Features/Audio/GameAudio.prefab";
        var paths = scenes.Concat(new[] { prefabPath }).ToArray();
        string root = Arg("-audioSource");
        var rows = new List<string> { "path\tsource_sha256\tisolated_sha256" };
        foreach (string path in AssetDatabase.GetDependencies(paths, true).Where(p => p.StartsWith("Assets/")).OrderBy(p => p))
        {
            foreach (string file in new[] { path, path + ".meta" })
            {
                if (!File.Exists(file)) continue;
                string source = Path.Combine(root, file);
                if (!File.Exists(source) || Hash(source) != Hash(file)) throw new InvalidOperationException("Native AssetDatabase dependency differs from source: " + file);
                rows.Add(file + "\t" + Hash(source) + "\t" + Hash(file));
            }
        }
        File.WriteAllLines(Path.Combine(Arg("-audioOutput"), "native-dependency-hashes.tsv"), rows);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null || prefab.GetComponent<AudioService>() == null || prefab.GetComponent<GameAudioEvents>() == null
            || !prefab.GetComponent<GameAudioEvents>().enabled || prefab.transform.parent != null)
            throw new InvalidOperationException("GameAudio prefab must contain enabled root AudioService and GameAudioEvents");
        foreach (string path in scenes)
        {
            var scene = EditorSceneManager.OpenScene(path);
            var services = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AudioService>(true)).ToArray();
            if (services.Length != 1 || services[0].transform.parent != null || !services[0].enabled
                || services[0].GetComponent<GameAudioEvents>() == null || !services[0].GetComponent<GameAudioEvents>().enabled
                || PrefabUtility.GetCorrespondingObjectFromSource(services[0].gameObject) != prefab)
                throw new InvalidOperationException("Scene's real root prefab wiring invalid: " + path);
            Debug.Log("AUDIO_GAMEPLAY PREFLIGHT exact GameAudio prefab instance with enabled GameAudioEvents/AudioService: " + path);
        }
        Debug.Log("AUDIO_GAMEPLAY PREFLIGHT native recursive asset dependency files hash-identical=" + (rows.Count - 1));
        var signatures = new List<string>();
        foreach (Type type in new[] { typeof(AudioService), typeof(GameAudioEvents), typeof(PlayerHealth), typeof(EnemyController), typeof(DaggerProjectile), typeof(WorldManager), typeof(RunStageController) })
        {
            signatures.Add(type.FullName + " assembly=" + type.Assembly.FullName + " MVID=" + type.Module.ModuleVersionId);
            signatures.AddRange(type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(m => "  " + m.MemberType + " " + m));
        }
        File.WriteAllLines(Path.Combine(Arg("-audioOutput"), "runtime-signatures.txt"), signatures);
    }
    static void Poll()
    {
        if (finishing) return;
        double deadline = double.Parse(SessionState.GetString(Key + ".Deadline", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "145s internal deadline"); return; }
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        try
        {
            // Compile unchanged tool copies with Unity's normal Roslyn pipeline, not the
            // legacy CodeDom compiler (which crashes on iterator out-var in catalog checks).
            checks = typeof(AudioGameplayChecks);
            var host = new GameObject("AudioGameplay isolated persistent host");
            UnityEngine.Object.DontDestroyOnLoad(host);
            stack.Push((IEnumerator)checks.GetMethod("Run").Invoke(null, null));
            host.AddComponent<AudioGameplayChecksBatchHost>().StartCoroutine(Drive());
        }
        catch (Exception error) { Finish(2, error.ToString()); }
    }
    static IEnumerator Drive()
    {
        while (stack.Count > 0)
        {
            object current;
            try
            {
                var step = stack.Peek();
                if (!step.MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                current = step.Current;
                if (current is IEnumerator child) { stack.Push(child); continue; }
            }
            catch (Exception error) { Finish(2, error.ToString()); yield break; }
            yield return current;
        }
        Finish((int)checks.GetProperty("Failed").GetValue(null) == 0 ? 0 : 1, "completed");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        bool search = type == LogType.Exception && message.StartsWith("ArgumentOutOfRangeException: Index was out of range.")
            && trace.Contains("UnityEditor.Search.SearchDatabase+<EnumerateAll>d__80.MoveNext ()")
            && trace.Contains("UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()")
            && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup ()")
            && !trace.Contains("Assets/") && !trace.Contains("Assets\\");
        string counter = search ? ".SearchErrors" : ".Errors";
        SessionState.SetInt(Key + counter, SessionState.GetInt(Key + counter, 0) + 1);
        Debug.Log("AUDIO_GAMEPLAY " + (search ? "KNOWN_EDITOR_ERROR (counted, not clean acceptance) " : "UNEXPECTED_ERROR ") + message + "\n" + trace);
    }
    [Serializable] class Summary
    {
        public string suite, entry, mode, message;
        public int passed, failed, unexpectedErrors, knownSearchErrors, processExitCode;
        public string[] evidence;
    }
    static void Finish(int code, string message)
    {
        if (finishing) return; finishing = true;
        while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch (Exception e) { message += "\nDispose: " + e; code = 2; } }
        if (checks != null) checks.GetMethod("Cleanup").Invoke(null, null);
        var summary = new Summary { suite = Arg("-audioSuite"), entry = Arg("-audioEntry"), mode = Arg("-audioMode", "Full"), message = message,
            passed = checks == null ? 0 : (int)checks.GetProperty("Passed").GetValue(null),
            failed = checks == null ? 0 : (int)checks.GetProperty("Failed").GetValue(null),
            unexpectedErrors = SessionState.GetInt(Key + ".Errors", 0), knownSearchErrors = SessionState.GetInt(Key + ".SearchErrors", 0),
            evidence = checks == null ? new string[0] : ((List<string>)checks.GetField("Evidence").GetValue(null)).ToArray() };
        if (code == 0 && summary.unexpectedErrors > 0) code = 1;
        // Known Search failure is NOT waived: separate exit 3 distinguishes environment-dirty from gameplay failures.
        if (code == 0 && summary.knownSearchErrors > 0) code = 3;
        summary.processExitCode = code;
        File.WriteAllText(Path.Combine(Arg("-audioOutput"), "summary.json"), JsonUtility.ToJson(summary, true));
        Debug.Log("AUDIO_GAMEPLAY RESULT " + summary.suite + "/" + summary.mode + ": " + summary.passed + " passed / " + summary.failed + " failed; unexpected="
            + summary.unexpectedErrors + "; knownSearch=" + summary.knownSearchErrors + "; code=" + code + "; " + message);
        Application.logMessageReceived -= Log; EditorApplication.update -= Poll;
        SessionState.SetBool(Key, false);
        EditorApplication.Exit(code);
    }
}
public sealed class AudioGameplayChecksBatchHost : MonoBehaviour { }
