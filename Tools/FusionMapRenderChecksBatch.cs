using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.IO;
using System.Linq;
using Microsoft.CSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stage in isolated Assets/Game/Editor only. Checks compile from Tools and run on real Play frames.
[InitializeOnLoad]
public static class FusionMapRenderChecksBatch
{
    const string Key = "FusionMapRenderChecksBatch.Running";
    static double deadline;
    static bool started, finished;
    static int unexpected;
    static EditorWindow gameView;
    static Type checks;
    static FusionMapRenderChecksBatch() { if (SessionState.GetBool(Key, false)) Hook(); }
    static string Arg(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
    public static void Run()
    {
        if (!Path.GetFullPath(Application.dataPath).Replace('\\', '/').EndsWith("/Library/DebugRunValidationProject/Assets", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source Unity forbidden: existing isolated project required.");
        if (Application.isBatchMode) throw new InvalidOperationException("Native Game View / WaitForEndOfFrame required, not batchmode.");
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/" + Arg("-renderScene", "Main") + ".unity");
        var manager = UnityEngine.Object.FindFirstObjectByType<WorldManager>();
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("initialWorldId").intValue = (int)(WorldId)Enum.Parse(typeof(WorldId), Arg("-renderEntry", "Material"));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // In-memory only, BEFORE Awake; never save the fixture scene or mutate source assets.
        foreach (var player in UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            player.transform.position = Vector3.zero;
            player.moveSpeed = 0;
            var health = player.GetComponent<PlayerHealth>();
            health.maxHealth = health.currentHealth = 1000000;
        }
        Application.runInBackground = true;
        ShowGameView(); Hook();
        EditorApplication.EnterPlaymode();
    }
    static void ShowGameView()
    {
        if (gameView == null) gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.Show(); gameView.Focus(); gameView.Repaint();
    }
    static void Hook()
    {
        EditorApplication.update -= Poll; EditorApplication.update += Poll;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        unexpected++;
        Debug.Log("FUSION_MAP_RENDER UNEXPECTED " + message + "\n" + stack);
    }
    static void Poll()
    {
        if (finished) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 135;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "135s internal deadline"); return; }
        ShowGameView(); EditorApplication.QueuePlayerLoopUpdate();
        if (started || !Application.isPlaying || Time.frameCount < 2) return;
        started = true;
        try
        {
            Debug.Log("FUSION_MAP_RENDER START GPU=" + SystemInfo.graphicsDeviceName + "/" + SystemInfo.graphicsDeviceType);
            string output = Path.GetFullPath("Library/FusionMapRenderChecks.dll");
            var options = new CompilerParameters { GenerateInMemory = false, GenerateExecutable = false, OutputAssembly = output, CompilerOptions = "-nostdlib+ -define:UNITY_EDITOR" };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) options.ReferencedAssemblies.Add(assembly.Location);
            CompilerResults result;
            using (var compiler = new CSharpCodeProvider()) result = compiler.CompileAssemblyFromFile(options, "Tools/FusionMapRenderChecks.cs");
            if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            checks = System.Reflection.Assembly.Load(File.ReadAllBytes(output)).GetType("FusionMapRenderChecks");
            checks.GetProperty("OutputDirectory").SetValue(null, Arg("-renderOutput", "Library/FusionMapRenderValidation"));
            checks.GetProperty("Termination").SetValue(null, Arg("-renderTermination", "Complete"));
            checks.GetProperty("Position").SetValue(null, Arg("-renderPosition", "Center"));
            checks.GetProperty("VisualNoClear").SetValue(null, bool.Parse(Arg("-renderVisualNoClear", "false")));
            checks.GetProperty("CameraGeometry").SetValue(null, bool.Parse(Arg("-renderCameraGeometry", "false")));
            var routine = (IEnumerator)checks.GetMethod("Run").Invoke(null, null);
            var host = new GameObject("FusionMapRenderChecksHost (isolated)").AddComponent<FusionMapRenderChecksHost>();
            host.StartCoroutine(Drive(routine));
        }
        catch (Exception e) { Finish(2, e.ToString()); }
    }
    static IEnumerator Drive(IEnumerator routine)
    {
        while (true)
        {
            object next;
            try { if (!routine.MoveNext()) break; next = routine.Current; }
            catch (Exception e) { (routine as IDisposable)?.Dispose(); Finish(2, e.ToString()); yield break; }
            yield return next;
        }
        int passed = (int)checks.GetProperty("Passed").GetValue(null), failed = (int)checks.GetProperty("Failed").GetValue(null);
        Finish(failed == 0 ? 0 : 1, passed + " passed / " + failed + " failed");
    }
    static void Finish(int code, string message)
    {
        if (finished) return;
        finished = true;
        if (unexpected > 0 && code == 0) code = 1;
        Debug.Log("FUSION_MAP_RENDER RESULT " + message + "; unexpected=" + unexpected + "; exit=" + code);
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Poll; Application.logMessageReceived -= Log;
        EditorApplication.Exit(code);
    }
}
public sealed class FusionMapRenderChecksHost : MonoBehaviour { }
