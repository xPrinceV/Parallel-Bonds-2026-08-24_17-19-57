using System;
using System.CodeDom.Compiler;
using System.Collections;

using System.Linq;

using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stage this file temporarily under Assets/Game/Editor with a unique filename, then run:
// Unity -batchmode -projectPath <repo> -executeMethod BowDaggerBatchProbe_20260908.Run -logFile <log>
// Do not pass -quit: this enters real Play Mode and exits after frames (90s internal deadline).
// Bound the process externally to 120s; remove the staged helper AND its .meta in a finally block.
// Requires no other Unity process and a disposable Play session; never saves scenes or settings.
[InitializeOnLoad]
public static class BowDaggerBatchProbe_20260908
{
    const string Key = "BowDaggerBatchProbe_20260908";
    static double deadline;
    static bool started;
    static BowDaggerBatchProbe_20260908()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Poll;
    }
    public static void Run()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        EditorApplication.EnterPlaymode();
    }
    static void Poll()
    {
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 90;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "90s probe timeout"); return; }
        if (started || !Application.isPlaying || Time.frameCount < 5) return;
        started = true;
        try
        {
            var files = new[] { "Tools/BowDaggerIntegrationChecks.cs", "Tools/WeaponBuffChecks.cs", "Tools/DualWorldChecks.cs" };
            var options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false, CompilerOptions = "-nostdlib+" };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) options.ReferencedAssemblies.Add(assembly.Location);
            CompilerResults result;
            using (var compiler = new CSharpCodeProvider()) result = compiler.CompileAssemblyFromFile(options, files);
            if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            var compiled = result.CompiledAssembly;
            int suiteFailures = 0;
            foreach (var name in new[] { "WeaponBuffChecks", "DualWorldChecks" })
            {
                try { Debug.Log("EXISTING " + name + ": " + compiled.GetType(name).GetMethod("Run").Invoke(null, null)); }
                catch (Exception e) { suiteFailures++; Debug.LogError("EXISTING " + name + " FAILED: " + e); }
            }
            var type = compiled.GetType("BowDaggerIntegrationChecks");
            var routine = (IEnumerator)type.GetMethod("Run").Invoke(null, null);
            var host = new GameObject("BowDaggerBatchProbe_20260908").AddComponent<BowDaggerBatchProbeHost_20260908>();
            host.StartCoroutine(Drive(routine, type, suiteFailures));
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive(IEnumerator routine, Type type, int suiteFailures)
    {
        while (true)
        {
            object current;
            try { if (!routine.MoveNext()) break; current = routine.Current; }
            catch (Exception e) { Finish(2, e.ToString()); yield break; }
            yield return current;
        }
        int passed = (int)type.GetProperty("Passed").GetValue(null);
        int failed = (int)type.GetProperty("Failed").GetValue(null);
        Finish(failed + suiteFailures == 0 ? 0 : 1, "RESULT new=" + passed + " passed/" + failed + " failed; existing suite failures=" + suiteFailures);
    }
    static void Finish(int code, string message)
    {
        Debug.Log("BOW_DAGGER_BATCH " + message);
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll;
        EditorApplication.Exit(code);
    }
}
public class BowDaggerBatchProbeHost_20260908 : MonoBehaviour { }
