using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Linq;
using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stage only in the warmed isolated project's Editor folder. No -quit; exits after real Play frames.
[InitializeOnLoad]
public static class DebugRunChecksBatch
{
    const string Key = "DebugRunChecksBatch.Running";
    static bool started;
    static double deadline;
    static DebugRunChecksBatch()
    {
        if (SessionState.GetBool(Key, false)) { EditorApplication.update += Poll; Application.logMessageReceived += Log; }
    }
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-debugRunSuite");
        string suite = index >= 0 && index + 1 < args.Length ? args[index + 1] : "DebugRun";
        if (suite != "DebugRun" && suite != "Gui" && suite != "Visual") throw new ArgumentException("Unknown DebugRun suite: " + suite);
        if (suite == "Visual")
        {
            // Report the existing editor validator independently; never change its rules or scene data.
            foreach (string scene in new[] { "Main", "DebugRun" })
            {
                var opened = EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity");
                foreach (var player in opened.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)))
                {
                    try
                    {
                        typeof(DualWorldSceneSetup).GetMethod("ValidateHero", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(null, new object[] { player, true, player.name == "EchoPlayer" });
                        Debug.Log("FUSION_VISUAL PREFLIGHT ValidateHero PASS " + scene + "/" + player.name);
                    }
                    catch (System.Reflection.TargetInvocationException e)
                    { Debug.Log("FUSION_VISUAL PREFLIGHT ValidateHero BLOCKED " + scene + "/" + player.name + ": " + e.InnerException); }
                }
            }
        }
        SessionState.SetString(Key + ".Suite", suite);
        SessionState.SetBool(Key, true);
        SessionState.SetInt(Key + ".Errors", 0);
        SessionState.SetInt(Key + ".SearchErrors", 0);
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        EditorSceneManager.OpenScene("Assets/Scenes/DebugRun.unity");
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        EditorApplication.EnterPlaymode();
    }
    static void Poll()
    {
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 130;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "130s internal timeout"); return; }
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        try
        {
            var options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false, CompilerOptions = "-nostdlib+" };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) options.ReferencedAssemblies.Add(assembly.Location);
            string suite = SessionState.GetString(Key + ".Suite", "DebugRun");
                        string className = suite == "Visual" ? "FusionVisualChecks" : suite == "Gui" ? "DeveloperDebugGuiChecks" : "DebugRunChecks";
            CompilerResults result;
            using (var compiler = new CSharpCodeProvider()) result = compiler.CompileAssemblyFromFile(options, "Tools/" + className + ".cs");
            if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            var type = result.CompiledAssembly.GetType(className);
            var host = new GameObject("DebugRunChecks persistent isolated host");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<DebugRunChecksBatchHost>().StartCoroutine(Drive((IEnumerator)type.GetMethod("Run").Invoke(null, null), type));
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive(IEnumerator routine, Type type)
    {
        while (true)
        {
            object current;
            try { if (!routine.MoveNext()) break; current = routine.Current; }
            catch (Exception e) { Finish(2, e.ToString()); yield break; }
            yield return current;
        }
        int passed = (int)type.GetProperty("Passed").GetValue(null), failed = (int)type.GetProperty("Failed").GetValue(null);
        Finish(failed == 0 ? 0 : 1, passed + " passed/" + failed + " failed");
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        // Only this known editor-startup failure is nonfatal. All other errors, including
        // scene-unload/fusion errors and unknown editor exceptions, still fail the batch.
        const string searchMessage = "ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.\nParameter name: index";
        bool knownSearch = !started && type == LogType.Exception
            && message.Replace("\r\n", "\n").Trim() == searchMessage
            && !string.IsNullOrEmpty(stack)
            && stack.Contains("UnityEditor.Search.SearchDatabase+<EnumerateAll>d__80.MoveNext ()")
            && stack.Contains("UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()")
            && stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup ()")
            && stack.Contains("UnityEditor.EditorApplication.Internal_CallDelayFunctions ()")
            && !stack.Contains("Assets/") && !stack.Contains("Assets\\");
        string counter = knownSearch ? ".SearchErrors" : ".Errors";
        SessionState.SetInt(Key + counter, SessionState.GetInt(Key + counter, 0) + 1);
        if (knownSearch)
            Debug.Log("DEBUG_RUN KNOWN_EDITOR_SEARCH_EXCEPTION counted separately; original exception remains in log");
        else
            Debug.Log("DEBUG_RUN UNEXPECTED_ERROR " + type + ": " + message + "\n" + stack);
    }
    static void Finish(int code, string message)
    {
        Application.logMessageReceived -= Log;
        int errors = SessionState.GetInt(Key + ".Errors", 0);
        Debug.Log("DEBUG_RUN RESULT " + SessionState.GetString(Key + ".Suite", "DebugRun") + " " + message + "; runtime/unexpected errors=" + errors
            + "; known editor Search exceptions=" + SessionState.GetInt(Key + ".SearchErrors", 0));
        SessionState.SetBool(Key, false); EditorApplication.update -= Poll;
        EditorApplication.Exit(code == 0 && errors > 0 ? 1 : code);
    }
}
public class DebugRunChecksBatchHost : MonoBehaviour { }
