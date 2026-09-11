using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Temporarily stage in Assets/Game/Editor. No -quit: the runner exits after real Play frames.
// -fusionSuite Fusion | Cleanup | Regression | Timing | Transition. External process bound: 120 seconds.
[InitializeOnLoad]
public static class FusionChecksBatch
{
    const string Key = "FusionChecksBatch.Running";
    static double deadline;
    static bool started;
    static string mode;
    static Type fusionType;
    static int expectedLogCount;
    static readonly List<string> unexpectedLogs = new List<string>();
    static FusionChecksBatch() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll; }
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-fusionSuite");
        SessionState.SetString(Key + ".Mode", index >= 0 ? args[index + 1] : "Fusion");
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        EditorApplication.EnterPlaymode();
    }
    static void Poll()
    {
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 90;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "90s internal timeout"); return; }
        if (started)
        {
            if (mode == "Timing")
            {
                string result = SessionState.GetString("DualWorldTimingChecks.Result", "running");
                if (result != "running") Finish(result.StartsWith("PASS") ? 0 : 1, "DualWorldTimingChecks " + result);
            }
            return;
        }
        if (!Application.isPlaying || Time.frameCount < 5) return;
        started = true; mode = SessionState.GetString(Key + ".Mode", "Fusion");
        try
        {
            var files = new[] { "Tools/FusionChecks.cs", "Tools/WeaponBuffChecks.cs", "Tools/DualWorldChecks.cs", "Tools/BowDaggerIntegrationChecks.cs", "Tools/DualWorldTimingChecks.cs", "Tools/WorldTransitionChecks.cs" };
            var options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false, CompilerOptions = "-nostdlib+" };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) options.ReferencedAssemblies.Add(assembly.Location);
            CompilerResults result;
            using (var compiler = new CSharpCodeProvider()) result = compiler.CompileAssemblyFromFile(options, files);
            if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            var compiled = result.CompiledAssembly;
            if (mode == "Timing") { compiled.GetType("DualWorldTimingChecks").GetMethod("Begin").Invoke(null, null); return; }
            int failures = 0;
            if (mode == "Regression")
                foreach (var name in new[] { "WeaponBuffChecks", "DualWorldChecks" })
                {
                    try { Debug.Log("FUSION_BATCH EXISTING " + name + ": " + compiled.GetType(name).GetMethod("Run").Invoke(null, null)); }
                    catch (Exception e) { failures++; Debug.LogError("FUSION_BATCH EXISTING " + name + " FAILED: " + e); }
                }
            var type = compiled.GetType(mode == "Transition" ? "WorldTransitionChecks" : mode == "Regression" ? "BowDaggerIntegrationChecks" : "FusionChecks");
            if (mode != "Regression")
            {
                fusionType = mode == "Transition" ? null : type;
                Application.logMessageReceived += ObserveLog;
            }
            var routine = (IEnumerator)type.GetMethod(mode == "Cleanup" ? "RunCleanup" : "Run").Invoke(null, null);
            new GameObject("FusionChecksBatchHost").AddComponent<FusionChecksBatchHost>().StartCoroutine(Drive(routine, type, failures));
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive(IEnumerator routine, Type type, int failures)
    {
        while (true)
        {
            object current;
            try { if (!routine.MoveNext()) break; current = routine.Current; }
            catch (Exception e) { (routine as IDisposable)?.Dispose(); Finish(2, e.ToString()); yield break; }
            yield return current;
        }
        int passed = (int)type.GetProperty("Passed").GetValue(null), failed = (int)type.GetProperty("Failed").GetValue(null);
        Finish(failed + failures == 0 ? 0 : 1, type.Name + " " + passed + " passed/" + failed + " failed; existing suite failures=" + failures);
    }
    static void ObserveLog(string condition, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (type == LogType.Error && fusionType != null && (bool)fusionType.GetMethod("ConsumeExpectedError").Invoke(null, new object[] { condition }))
            expectedLogCount++;
        else unexpectedLogs.Add(condition + "\n" + stack);
    }
    static void Finish(int code, string message)
    {
        Application.logMessageReceived -= ObserveLog;
        if (unexpectedLogs.Count > 0)
        {
            code = code == 0 ? 1 : code;
            Debug.Log("FUSION_BATCH UNEXPECTED RUNTIME ERRORS\n" + string.Join("\n", unexpectedLogs));
        }
        if (mode == "Transition" && (FusionChecksBatchHost.InitialSamples < 4 || FusionChecksBatchHost.InitialFailures > 0))
        { code = 1; Debug.Log("FUSION_BATCH initial frame failures=" + FusionChecksBatchHost.InitialFailures + "; samples=" + FusionChecksBatchHost.InitialSamples); }
        Debug.Log("FUSION_BATCH RESULT " + mode + " " + message + "; expected errors=" + expectedLogCount + "; unexpected errors=" + unexpectedLogs.Count);
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll; EditorApplication.Exit(code);
    }
}
[DefaultExecutionOrder(10000)]
public class FusionChecksBatchHost : MonoBehaviour
{
    public static int InitialSamples, InitialFailures;
    bool observeInitial;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ObserveStartup()
    {
        InitialSamples = InitialFailures = 0;
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-fusionSuite");
        if (index < 0 || args[index + 1] != "Transition") return;
        new GameObject("WorldTransitionInitialFrameObserver_Temporary").AddComponent<FusionChecksBatchHost>().observeInitial = true;
    }
    void LateUpdate()
    {
        if (!observeInitial || InitialSamples >= 4) return;
        var filter = UnityEngine.Object.FindFirstObjectByType<WorldFilter>();
        var manager = UnityEngine.Object.FindFirstObjectByType<WorldManager>();
        var image = filter == null ? null : (UnityEngine.UI.Image)typeof(WorldFilter).GetField("overlay",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(filter);
        bool ok = manager != null && manager.IsInitialized && filter != null && image != null
            && !filter.IsTransitioning && image.color == manager.CurrentWorld.AmbientColor;
        InitialSamples++;
        if (!ok) InitialFailures++;
        Debug.Log("FUSION_BATCH initial LateUpdate frame=" + Time.frameCount + " no-flash=" + ok);
    }
}
