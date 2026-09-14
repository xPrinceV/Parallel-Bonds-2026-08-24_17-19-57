using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// In-memory isolated checks. Pointer events below are not OS mouse/keyboard input.
public static class DeveloperDebugGuiChecks
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static DeveloperDebugGui gui;
    static WorldManager manager;
    static T[] All<T>() where T : Object { return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Debug.Log("DEVELOPER_GUI " + (ok ? "PASS " : "FAIL ") + SceneManager.GetActiveScene().name + " " + text); }
    static Button Button(string name) { return gui.GetComponentsInChildren<Button>(true).Single(b => b.name == name); }
    static List<RaycastResult> Hits(Button button, out PointerEventData pointer)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = Field<Canvas>(gui, "canvas");
        pointer = new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center)),
            button = PointerEventData.InputButton.Left
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        return hits;
    }
    static void Click(string name, bool interactable = true)
    {
        var button = Button(name);
        PointerEventData pointer;
        var hits = Hits(button, out pointer);
        bool reachable = pointer.position.x >= 0 && pointer.position.y >= 0 && pointer.position.x < Screen.width && pointer.position.y < Screen.height
            && hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button;
        Check(reachable && button.IsInteractable() == interactable, "pointer " + name + " interactable=" + button.IsInteractable() + " at=" + pointer.position + " top=" + (hits.Count == 0 ? "none" : hits[0].gameObject.name));
        if (!reachable) throw new Exception("Required GUI pointer target blocked: " + name);
        // Dispatch to the actual top-hit button; never invoke onClick or a private GUI callback.
        var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
    }
    static void Hidden(string context)
    {
        Check(!gui.IsOpen && gui.gameObject.activeInHierarchy && gui.enabled
            && !Field<Canvas>(gui, "canvas").enabled && !Field<GraphicRaycaster>(gui, "raycaster").enabled, context + " hidden canvas/raycaster, active listener root");
        Check(EventSystem.current.currentSelectedGameObject == null || !EventSystem.current.currentSelectedGameObject.transform.IsChildOf(gui.transform), context + " no GUI selection");
        foreach (var button in gui.GetComponentsInChildren<Button>(true))
        {
            PointerEventData pointer;
            Check(!Hits(button, out pointer).Any(h => h.gameObject.transform.IsChildOf(gui.transform)), context + " no GUI raycast at " + button.name);
        }
    }
    static void Bind()
    {
        gui = All<DeveloperDebugGui>().Single(); manager = All<WorldManager>().Single();
        Check(All<EventSystem>().Count(e => e.isActiveAndEnabled) == 1 && EventSystem.current.currentInputModule != null, "single active EventSystem/input module");
        Check(gui.IsAvailable && Field<WorldManager>(gui, "worldManager") == manager && Field<KeyCode>(gui, "toggleKey") == KeyCode.BackQuote, "editor availability, manager binding, serialized BackQuote");
        Hidden("startup/reload");
    }
    static void Cycle(string state)
    {
        float scale = Time.timeScale;
        gui.SetOpen(true);
        Check(gui.IsOpen && Field<Canvas>(gui, "canvas").enabled && Field<GraphicRaycaster>(gui, "raycaster").enabled && Time.timeScale == scale, state + " SetOpen preserves exact timeScale=" + scale);
        EventSystem.current.SetSelectedGameObject(Button("Close").gameObject);
        gui.Toggle(); Hidden(state + " Toggle close");
        Check(Time.timeScale == scale, state + " Toggle close preserves exact timeScale");
        gui.Toggle();
        Check(gui.IsOpen && gui.gameObject.activeInHierarchy && Time.timeScale == scale, state + " Toggle open preserves exact timeScale");
        gui.SetOpen(false); gui.SetOpen(false); gui.SetOpen(true); gui.SetOpen(true);
        Check(gui.IsOpen && Time.timeScale == scale, state + " repeated SetOpen preserves exact timeScale");
    }
    static void Status()
    {
        var text = Field<TMP_Text>(gui, "worldStatus");
        Check(text.isActiveAndEnabled && text.canvas.enabled && text.text.Contains("World: " + manager.CurrentWorldId)
            && text.text.Contains("Fusion: " + (manager.IsFused ? "On" : "Off")) && text.text.Contains("Shared HP:"), "visible canvas and live status: " + text.text.Replace("\n", " | "));
    }
    static void CompileGuards()
    {
        const string path = "Assets/Game/Presentation/UI/DeveloperDebugGui.cs";
        string source = File.ReadAllText(path);
        Check(source.Contains("Input.GetKeyDown(toggleKey)") && source.Contains("Input.GetKeyDown(KeyCode.Escape)")
            && source.Contains("KeyCode.BackQuote") && !source.Contains("KeyCode.Tilde"), "source shortcut mapping: BackQuote is bare/Shift tilde key; Escape close (not OS injected)");
        foreach (string define in new[] { "", "UNITY_EDITOR", "DEVELOPMENT_BUILD" })
        {
            var options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false,
                CompilerOptions = "-nostdlib+ -optimize+" + (define == "" ? "" : " -define:" + define) };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)) options.ReferencedAssemblies.Add(assembly.Location);
            CompilerResults result;
            using (var compiler = new CSharpCodeProvider()) result = compiler.CompileAssemblyFromFile(options, path);
            if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            var getter = result.CompiledAssembly.GetType("DeveloperDebugGui").GetProperty("IsAvailable").GetGetMethod();
            byte[] il = getter.GetMethodBody().GetILAsByteArray();
            Check(il.SequenceEqual(new byte[] { (byte)(define == "" ? 0x16 : 0x17), 0x2a }), "exact unchanged GUI source compiles availability=" + (define != "") + " defines=" + (define == "" ? "none (release)" : define));
        }
    }
    public static IEnumerator Run()
    {
        Passed = Failed = 0;
        CompileGuards();
        foreach (string scene in new[] { "DebugRun", "Main" })
        {
            if (SceneManager.GetActiveScene().name != scene)
            {
                Time.timeScale = 1;
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
                yield return null; yield return null; yield return null;
            }
            Bind();
            bool debug = scene == "DebugRun";
            Check(All<RunStageController>().Length == (debug ? 1 : 0) && All<RunStagePanel>().Length == (debug ? 1 : 0), "scene stage controller/panel scope");
            if (debug) Check(All<RunStagePanel>().Single().GetComponentInParent<Canvas>() == Field<Canvas>(gui, "canvas"), "stage UI shares developer canvas");
            Cycle("playing");
            Time.timeScale = .375f; Cycle("fractional play");
            Time.timeScale = 0; Cycle("paused");
            yield return null; yield return null;
            var world = manager.CurrentWorldId;
            Click("Fusion", false); Click(world == WorldId.Material ? "Echo" : "Material", false);
            Check(!manager.IsFused && manager.CurrentWorldId == world && Time.timeScale == 0, "paused pointer actions rejected without mutation");
            Time.timeScale = 1;
            // Re-enable the real component and root repeatedly to expose accidental OnEnable subscriptions.
            for (int i = 0; i < 5; i++)
            {
                gui.SetOpen(false); gui.SetOpen(true); gui.enabled = false;
                Check(!gui.IsOpen && !Field<Canvas>(gui, "canvas").enabled, "component disable closes");
                gui.enabled = true; gui.gameObject.SetActive(false); gui.gameObject.SetActive(true);
                Hidden("reenable " + i); gui.SetOpen(true);
            }
            foreach (var button in gui.GetComponentsInChildren<Button>(true))
            {
                var prepare = typeof(UnityEngine.Events.UnityEventBase).GetMethod("PrepareInvoke", BindingFlags.Instance | BindingFlags.NonPublic);
                var listeners = (IList)prepare.Invoke(button.onClick, null);
                Check(listeners.Count == 1, "exactly one effective callback after repeated open/enable: " + button.name + " count=" + listeners.Count);
            }
            yield return null; yield return null;
            Click(manager.CurrentWorldId == WorldId.Material ? "Echo" : "Material");
            yield return new WaitForSecondsRealtime(.6f);
            Check(manager.CurrentWorldId != world, "world pointer selects other world after repeated enables"); Status();
            Click(world == WorldId.Material ? "Material" : "Echo");
            yield return new WaitForSecondsRealtime(.6f);
            Check(manager.CurrentWorldId == world, "world pointer selects original world");
            Click("Fusion");
            Check(manager.IsFused, "single Fusion pointer enters (duplicate handlers would toggle twice)");
            yield return null; yield return null; Status();
            Click("Fusion"); Check(!manager.IsFused, "single Fusion pointer exits");
            yield return null; yield return null;
            float closeScale = Time.timeScale;
            Click("Close"); Hidden("Close pointer"); Check(Time.timeScale == closeScale, "Close pointer preserves exact timeScale");
            gui.Toggle(); yield return null; yield return null;
            if (!debug) continue;
            var run = All<RunStageController>().Single();
            int changes = 0; Action changed = () => changes++; run.StateChanged += changed;
            Click("Next"); Check(run.CurrentStageIndex == 1 && changes == 1, "Next pointer advances exactly once after repeated enables");
            yield return null; yield return null;
            Click("Wave 3"); Check(run.CurrentStageIndex == 2 && changes == 2, "Wave 3 pointer uses stage API once");
            yield return null; yield return null;
            Click("Wave 1"); Check(run.CurrentStageIndex == 0 && changes == 3, "Wave 1 pointer uses stage API once");
            yield return null; yield return null;
            Click("Wave 2"); Check(run.CurrentStageIndex == 1 && changes == 4, "Wave 2 pointer uses stage API once");
            yield return null; yield return null;
            Click("Boss"); Check(run.CurrentStageIndex == 3 && changes == 5, "Boss pointer uses stage API once");
            run.StateChanged -= changed;
            yield return null; yield return null;
            Click("Fusion"); yield return null; yield return null;
            foreach (var titan in All<TitanEnemyController>()) titan.TakeDamage(titan.health + 1);
            yield return null; yield return null;
            Check(run.IsCompleted && Time.timeScale == 0, "real boss deaths create completed fixture");
            Cycle("complete"); yield return null; yield return null;
            Click("Next", false); Check(run.IsCompleted && run.CurrentStageIndex == 3 && Time.timeScale == 0, "complete rejects stage pointer");
            int oldId = run.GetInstanceID(); Click("Restart");
            yield return null; yield return null; yield return null;
            Bind(); run = All<RunStageController>().Single();
            Check(run.GetInstanceID() != oldId && run.IsRunning && run.CurrentStageIndex == 0 && Time.timeScale == 1, "complete Restart pointer reloads fresh hidden GUI");
            gui.SetOpen(true); yield return null; yield return null;
            PlayerHealth.instance.DamageHandler(PlayerHealth.instance.currentHealth + 1);
            yield return null; yield return null;
            Check(run.IsDefeated && Time.timeScale == 0, "real lethal damage creates defeated fixture");
            Cycle("dead"); yield return null; yield return null;
            Click("Fusion", false); Click("Next", false);
            Check(!manager.IsFused && run.IsDefeated && Time.timeScale == 0, "dead pointer actions rejected");
            oldId = run.GetInstanceID(); Click("Restart");
            yield return null; yield return null; yield return null;
            Bind(); run = All<RunStageController>().Single();
            Check(run.GetInstanceID() != oldId && run.IsRunning && !run.IsDefeated && Time.timeScale == 1, "dead Restart pointer reloads fresh hidden GUI");
        }
        Debug.Log("DEVELOPER_GUI COVERAGE synthetic raycast-coordinate pointer events; no OS shortcut injection, no screenshot/appearance assertion; release is source/define compilation, not a player build");
    }
}
