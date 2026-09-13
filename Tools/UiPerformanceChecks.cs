using System;
using System.Reflection;

#if !UI_PERFORMANCE_FIXTURE
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;

// Standalone source checks, not a Unity player/Play Mode benchmark.
// PowerShell from the repo root (requires dotnet and Unity's Roslyn compiler):
// Add-Type -Path Tools/UiPerformanceChecks.cs -ReferencedAssemblies System.dll,System.Core.dll,System.Xml.dll
// [UiPerformanceChecks]::Run((Get-Location).Path, '<Unity Editor>/Data/DotNetSdkRoslyn/csc.dll')
// Compiler outputs use OS temp files, loaded into memory and deleted, never Assets.
public static class UiPerformanceChecks
{
    public static int Passed { get; private set; }

    public static void Run(string root, string compilerPath)
    {
        Passed = 0;
        string[] paths = {
            "Tools/UiPerformanceChecks.cs",
            "Assets/Game/Presentation/UI/RunStagePanel.cs",
            "Assets/Game/Presentation/UI/DeveloperDebugGui.cs",
            "Assets/Game/Presentation/UI/UIController.cs",
            "Assets/Game/Features/GameFlow/LevelManager.cs"
        };
        foreach (string define in new[] { "", "UNITY_EDITOR", "DEVELOPMENT_BUILD" })
        {
            var assembly = Compile(compilerPath, paths.Select(p => Path.GetFullPath(Path.Combine(root, p))),
                new[] { typeof(object).Assembly.Location, typeof(Uri).Assembly.Location, typeof(Enumerable).Assembly.Location },
                "UI_PERFORMANCE_FIXTURE" + (define == "" ? "" : "," + define));
            Console.WriteLine("UI_PERFORMANCE CONFIG " + (define == "" ? "release" : define));
            try
            {
                Passed += (int)assembly.GetType("UiPerformanceFixture")
                    .GetMethod("Run").Invoke(null, null);
            }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        Console.WriteLine("UI_PERFORMANCE RESULT " + Passed + " passed; exact UI sources, stubbed Unity/gameplay; no scenes changed");
    }

    // Separate from the fixture: compile current gameplay sources with the project's
    // real Unity references. Do not trust a potentially stale Assembly-CSharp.dll.
    public static void CompileUnity(string root, string compilerPath)
    {
        var project = new XmlDocument();
        project.Load(Path.Combine(root, "Assembly-CSharp.csproj"));
        var references = project.SelectNodes("//*[local-name()='Reference']/*[local-name()='HintPath']")
            .Cast<XmlNode>().Select(node => Path.GetFullPath(Path.Combine(root, node.InnerText))).ToArray();
        var paths = project.SelectNodes("//*[local-name()='Compile']")
            .Cast<XmlNode>().Select(node => Path.GetFullPath(Path.Combine(root, node.Attributes["Include"].Value)))
            .Where(File.Exists)
            .Concat(Directory.GetFiles(Path.Combine(root, "Assets/Game"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/Editor/")))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string define in new[] { "UI_PERFORMANCE_COMPILE", "UNITY_EDITOR", "DEVELOPMENT_BUILD" })
        {
            Compile(compilerPath, paths, references, define);
            Console.WriteLine("UI_PERFORMANCE UNITY_COMPILE PASS " + define);
        }
    }

    public static Assembly Compile(string compilerPath, IEnumerable<string> sources, IEnumerable<string> references, string defines)
    {
        string output = Path.Combine(Path.GetTempPath(), "UiPerformance-" + Guid.NewGuid().ToString("N") + ".dll");
        string response = output + ".rsp";
        try
        {
            File.WriteAllLines(response, new[] { "/nologo", "/target:library", "/optimize+", "/nostdlib+",
                "/out:" + Quote(output), "/define:" + defines }
                .Concat(references.Distinct().Select(r => "/reference:" + Quote(Path.GetFullPath(r))))
                .Concat(sources.Select(s => Quote(Path.GetFullPath(s)))));
            var start = new ProcessStartInfo("dotnet", Quote(compilerPath) + " /noconfig @" + Quote(response)) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill(); process.WaitForExit();
                    throw new TimeoutException("UI source compilation exceeded 30 seconds");
                }
                Console.Write(stdout.Result);
                Console.Write(stderr.Result);
                if (process.ExitCode != 0) throw new Exception("UI source compiler exit=" + process.ExitCode);
            }
            return Assembly.Load(File.ReadAllBytes(output));
        }
        finally { File.Delete(response); File.Delete(output); }
    }

    static string Quote(string value) { return "\"" + value + "\""; }
}
#else
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class UiPerformanceFixture
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>),
        typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static));

    static void Set(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }
    static Action Method(object target, string name) { return (Action)Delegate.CreateDelegate(typeof(Action), target, target.GetType().GetMethod(name, Private)); }
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("UI_PERFORMANCE FAIL " + message);
        passed++;
        Console.WriteLine("UI_PERFORMANCE PASS " + message);
    }
    static void NoWork(Action update, string context)
    {
        for (int i = 0; i < 32; i++) update(); // warm JIT and caches before measurement
        int texts = TMP_Text.Writes, buttons = Button.Writes;
        long before = Allocated();
        for (int i = 0; i < 1000; i++) update();
        long bytes = Allocated() - before;
        Check(bytes == 0 && texts == TMP_Text.Writes && buttons == Button.Writes,
            context + ": 1000 calls, bytes=" + bytes + ", text setters=" + (TMP_Text.Writes - texts)
            + ", button setters=" + (Button.Writes - buttons));
    }
    static Button NewButton()
    {
        var button = new Button();
        button.Components[typeof(TMP_Text)] = new TMP_Text();
        return button;
    }
    static TMP_Text Label(Button button) { return button.GetComponentInChildren<TMP_Text>(true); }

    public static int Run()
    {
        passed = 0;
        Timer();
        Panels();
        return passed;
    }

    static void Timer()
    {
        var ui = new UIController { timeText = new TMP_Text() };
        Method(ui, "Awake")();
        ui.UpdateTimer(0);
        Check(ui.timeText.text == "0:00", "first zero second initializes text");
        NoWork(() => ui.UpdateTimer(.999f), "unchanged displayed timer second");
        float[] times = { 1f, 1.999f, 59.999f, 60f, 60.001f, 119.999f, 120f, 479.999f, 480f, 3600f, 0f };
        string[] expected = { "0:01", "0:01", "0:59", "1:00", "1:00", "1:59", "2:00", "7:59", "8:00", "60:00", "0:00" };
        for (int i = 0; i < times.Length; i++)
        {
            ui.UpdateTimer(times[i]);
            Check(ui.timeText.text == expected[i], "timer boundary " + times[i] + " -> " + expected[i]);
        }
        var first = ui.timeText;
        ui.timeText = new TMP_Text();
        ui.UpdateTimer(.1f);
        Check(ui.timeText.text == "0:00", "same-second new text binding initialized");
        ui.timeText = null;
        ui.UpdateTimer(.1f);
        ui.timeText = first;
        int writes = TMP_Text.Writes;
        ui.UpdateTimer(.1f);
        Check(TMP_Text.Writes == writes + 1, "null binding invalidates timer cache");
        var nextSceneUi = new UIController { timeText = new TMP_Text() };
        Method(nextSceneUi, "Awake")();
        nextSceneUi.UpdateTimer(.1f);
        Check(nextSceneUi.timeText.text == "0:00" && UIController.instance == nextSceneUi, "new scene controller has independent cache");

        var level = new LevelManager();
        var run = new RunStageController { ElapsedTime = 59.9f };
        level.ConfigureStages(run);
        Action update = Method(level, "Update");
        update();
        run.ElapsedTime = 59.99f;
        writes = TMP_Text.Writes;
        update();
        Check(level.timer == run.ElapsedTime && TMP_Text.Writes == writes, "LevelManager still copies full-precision shared time each frame");
        run.ElapsedTime = 60;
        update();
        Check(nextSceneUi.timeText.text == "1:00", "shared time crosses minute without cached clock");
        Time.timeScale = 0;
        NoWork(update, "paused shared timer");
        Time.timeScale = 1;
        level.ConfigureStages(null);
        Method(level, "Start")();
        Time.deltaTime = .25f;
        float before = level.timer;
        update();
        Check(level.timer == before + .25f, "fallback gameplay clock still advances by exact delta");
        nextSceneUi.levelUpPanel = new GameObject();
        before = level.timer;
        update();
        Check(level.timer == before, "fallback upgrade pause unchanged");
        UIController.instance = null;
    }

    static void Panels()
    {
        var canvas = new Canvas();
        var gui = new DeveloperDebugGui();
        var panel = new RunStagePanel();
        var run = new RunStageController { IsRunning = true, CurrentStageIndex = 0, UsesSharedWaves = true,
            FinaleStartTime = 480, RemainingUntilFinale = 479.9f };
        var health = new PlayerHealth { currentHealth = 100, maxHealth = 100 };
        PlayerHealth.instance = health;
        var player = new Component();
        player.Components[typeof(PlayerHealth)] = health;
        var world = new World { Player = player };
        var flow = new StateSwitchController { AutomaticSwitchingEnabled = true, SwitchInterval = 30 };
        var manager = new WorldManager { IsInitialized = true, CurrentWorld = world, SwitchFlow = flow };
        var ui = new UIController();
        UIController.instance = ui;
        var status = new TMP_Text();
        var worldStatus = new TMP_Text();
        var finale = NewButton();
        var restart = NewButton();
        var manual = NewButton();
        var automatic = NewButton();
        gui.Components[typeof(RunStagePanel[])] = new[] { panel };
        panel.Components[typeof(Canvas)] = canvas;
        panel.Components[typeof(DeveloperDebugGui)] = gui;
        Set(panel, "runController", run); Set(panel, "uiController", ui); Set(panel, "statusText", status);
        Set(panel, "stageButtons", new[] { NewButton(), NewButton(), NewButton(), finale }); Set(panel, "restartButton", restart);
        Set(gui, "worldManager", manager); Set(gui, "canvas", canvas); Set(gui, "raycaster", new GraphicRaycaster());
        Set(gui, "worldStatus", worldStatus); Set(gui, "materialButton", manual); Set(gui, "echoButton", automatic); Set(gui, "closeButton", NewButton());
        Method(panel, "Awake")(); Method(gui, "Awake")();
        Action panelUpdate = Method(panel, "Update"), guiUpdate = Method(gui, "Update");
        Action tick = () => { panelUpdate(); guiUpdate(); };
        NoWork(() => { run.RemainingUntilFinale -= .01f; health.currentHealth -= .01f; tick(); }, "closed Canvas with changing run/HP");
        Check(status.text == null && worldStatus.text == null, "hidden startup never assigns authored text");
        gui.SetOpen(true);
        if (!gui.IsAvailable)
        {
            canvas.enabled = true; // release must stay inert even if another caller enables Canvas
            NoWork(() => { tick(); panel.RefreshDisplay(true); }, "ordinary release including forced refresh");
            Check(!gui.IsOpen && status.text == null && worldStatus.text == null, "release cannot open or format debug UI");
            return;
        }

        Check(gui.IsOpen && status.text.Contains("Fusion in: " + Mathf.CeilToInt(run.RemainingUntilFinale) + "s")
            && worldStatus.text.Contains("Shared HP:") && Label(finale).text == "480s Finale", "SetOpen refreshes both panels synchronously");
        Check(Label(automatic).text == "Auto switch: On" && Label(manual).text == "30s Switch", "original Auto/manual labels retained");
        NoWork(tick, "open unchanged labels/status/guards");
        NoWork(() => gui.SetOpen(true), "repeated SetOpen(true)");
        run.RemainingUntilFinale = 10.9f; tick();
        NoWork(() => { run.RemainingUntilFinale = 10.1f; tick(); }, "countdown changes within same ceiling second");
        run.RemainingUntilFinale = 10f; tick();
        Check(status.text.Contains("Fusion in: 10s"), "countdown exact ceiling boundary refreshes");
        run.RemainingUntilFinale = .001f; tick();
        Check(status.text.Contains("Fusion in: 1s"), "countdown just above zero");
        run.RemainingUntilFinale = 0; tick();
        Check(status.text.Contains("Fusion in: 0s"), "countdown exact zero");

        // Invoke actual listeners before Update to prove callbacks do not trust cached interactability.
        Check(finale.interactable, "finale initially enabled");
        Time.timeScale = 0;
        finale.onClick.Invoke();
        Check(run.FinaleRequests == 0, "same-frame pause blocks stale-enabled finale callback");
        tick();
        Check(!finale.interactable && !manual.interactable && automatic.interactable && restart.interactable
            && status.text.Contains("Paused"), "pause updates guards without a countdown change; Auto/restart remain available");
        automatic.onClick.Invoke(); tick();
        Check(!flow.AutomaticSwitchingEnabled && Label(automatic).text == "Auto switch: Off" && Time.timeScale == 0, "paused Auto toggle preserves pause");
        restart.onClick.Invoke();
        Check(run.Restarts == 1, "paused restart reaches existing run API (stub does not reload scene)");
        NoWork(tick, "paused stable debug UI");
        Time.timeScale = 1;
        tick();
        ui.levelUpPanel = new GameObject();
        finale.onClick.Invoke(); tick();
        Check(run.FinaleRequests == 0 && !finale.interactable && !manual.interactable && automatic.interactable, "upgrade pause guards are live before/after Update");
        ui.levelUpPanel = null;
        tick();
        health.IsDead = true;
        finale.onClick.Invoke(); tick();
        Check(run.FinaleRequests == 0 && !finale.interactable && !manual.interactable && restart.interactable, "death guards independent of formatted status");
        health.IsDead = false;
        tick();
        manager.IsWorldTransitioning = true; tick();
        Check(!manual.interactable, "manual transition guard refreshes without text changes");
        manager.IsWorldTransitioning = false; tick();
        manual.onClick.Invoke();
        Check(flow.Requests == 1, "manual callback still delegates to validating runtime API");
        finale.onClick.Invoke();
        Check(run.FinaleRequests == 1, "eligible finale callback still delegates to runtime API");
        run.IsFinaleStarted = true; tick();
        Check(status.text.Contains("Fusion | Running") && status.text.Contains("Final fusion locked") && !finale.interactable, "finale locks countdown and button");
        run.IsBossPhase = true; run.RemainingBosses = 1; tick();
        Check(status.text.Contains("Rift Lord") && status.text.Contains("Bosses: 1"), "boss phase and boss count refresh");
        run.IsCompleted = true; tick();
        Check(status.text.Contains("Complete | Complete"), "completion precedence preserved");
        run.IsCompleted = false; run.IsDefeated = true; tick();
        Check(status.text.Contains("Defeated") && restart.interactable, "defeat display retains restart");
        restart.onClick.Invoke();
        Check(run.Restarts == 2, "defeated restart reaches existing API");

        gui.SetOpen(false);
        run.IsDefeated = false; run.IsBossPhase = false; run.IsFinaleStarted = false;
        run.UsesSharedWaves = false; run.FinaleStartTime = 500; run.RemainingUntilFinale = 123.1f;
        flow.SwitchInterval = 45; flow.AutomaticSwitchingEnabled = true;
        world.WorldId = WorldId.Echo; health.currentHealth = 42;
        int texts = TMP_Text.Writes;
        tick();
        Check(TMP_Text.Writes == texts, "hidden changed state never reaches text setters");
        gui.SetOpen(true);
        Check(status.text.Contains("Survival | Running") && status.text.Contains("124s") && Label(finale).text == "500s Finale"
            && Label(manual).text == "45s Switch" && Label(automatic).text == "Auto switch: On"
            && worldStatus.text.Contains("World: Echo") && worldStatus.text.Contains("42 / 100"), "reopen exposes current run/world/HP/config before next frame");
        NoWork(tick, "reopened stable state");
        texts = TMP_Text.Writes;
        health.currentHealth = 41; tick();
        Check(TMP_Text.Writes == texts + 1 && worldStatus.text.Contains("41 / 100"), "live HP change only updates world status");
        texts = TMP_Text.Writes;
        flow.AutomaticSwitchingEnabled = false; tick();
        Check(TMP_Text.Writes == texts + 1 && Label(automatic).text == "Auto switch: Off", "Auto state change only updates its label");
        texts = TMP_Text.Writes;
        flow.SwitchInterval = 60; tick();
        Check(TMP_Text.Writes == texts + 1 && Label(manual).text == "60s Switch", "interval change only updates manual label");
        texts = TMP_Text.Writes;
        run.FinaleStartTime = 600; tick();
        Check(TMP_Text.Writes == texts + 1 && Label(finale).text == "600s Finale", "finale config change only updates its label in fixed-countdown fixture");
        canvas.enabled = false;
        NoWork(() => { health.currentHealth -= .01f; run.RemainingUntilFinale -= .01f; tick(); }, "externally disabled Canvas");
        canvas.enabled = true; tick();
        Check(worldStatus.text.Contains(health.currentHealth.ToString("0.#")), "Canvas re-enable refreshes invalidated snapshot");
        Set(panel, "runController", null); Set(gui, "worldManager", null); tick();
        Check(status.text == "Run controller unavailable" && Label(finale).text == "Finale" && !restart.interactable
            && worldStatus.text == "World unavailable" && Label(automatic).text == "Auto switch: Unavailable"
            && Label(manual).text == "Switch", "unavailable references update fallbacks");
        NoWork(tick, "stable unavailable state");
        Set(panel, "runController", run); Set(gui, "worldManager", manager); tick();
        Check(status.text.Contains("Survival") && worldStatus.text.Contains("World: Echo"), "restored references invalidate unavailable display");
    }
}

// Deliberately small test doubles: counters observe every setter (unlike TMP's
// internal same-string check). They do not simulate Unity lifecycle, raycasts,
// shared-clock advancement, scene reload, or gameplay API validation.
namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public class Transform { public bool IsChildOf(Transform other) { return false; } }
    public class GameObject
    {
        public bool activeInHierarchy = true, activeSelf = true;
        public Transform transform = new Transform();
    }
    public class Component
    {
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled; } }
        public Transform transform = new Transform();
        public T GetComponent<T>() where T : class { object value; return Components.TryGetValue(typeof(T), out value) ? (T)value : null; }
        public T GetComponentInParent<T>() where T : class { return GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive) where T : class { return GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class { return GetComponent<T[]>() ?? new T[0]; }
    }
    public class MonoBehaviour : Component { }
    public class Canvas : Component { }
    public static class Time { public static float timeScale = 1, deltaTime; }
    public enum KeyCode { BackQuote, Escape }
    public static class Input { public static bool GetKeyDown(KeyCode key) { return false; } }
    public static class Mathf
    {
        public static int FloorToInt(float value) { return (int)Math.Floor(value); }
        public static int CeilToInt(float value) { return (int)Math.Ceiling(value); }
    }
}
namespace TMPro
{
    public class TMP_Text : Component
    {
        public static int Writes;
        string value;
        public string text { get { return value; } set { Writes++; this.value = value; } }
    }
}
namespace UnityEngine.UI
{
    public class Button : Component
    {
        public static int Writes;
        bool value = true;
        public bool interactable { get { return value; } set { Writes++; this.value = value; } }
        public readonly ClickEvent onClick = new ClickEvent();
    }
    public class ClickEvent
    {
        event Action clicked;
        public void AddListener(Action callback) { clicked += callback; }
        public void RemoveListener(Action callback) { clicked -= callback; }
        public void Invoke() { if (clicked != null) clicked(); }
    }
    public class GraphicRaycaster : Component { }
    public class Slider : Component { public float maxValue, value; }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem
    {
        public static EventSystem current;
        public GameObject currentSelectedGameObject;
        public void SetSelectedGameObject(GameObject selected) { currentSelectedGameObject = selected; }
    }
}
public class LevelUpSelectionButton { }
public enum WorldId { Material, Echo }
public class World { public Component Player; public WorldId WorldId; }
public class WorldManager : Component
{
    public bool IsInitialized, IsFinalFusion, IsSwitching, IsWorldTransitioning, IsFusionTransitioning, IsFused;
    public World CurrentWorld;
    public StateSwitchController SwitchFlow;
}
public class StateSwitchController : Component
{
    public bool AutomaticSwitchingEnabled;
    public float SwitchInterval;
    public int Requests;
    public bool RequestNextWorldSwitch() { Requests++; return true; }
}
public class PlayerHealth : Component
{
    public static PlayerHealth instance;
    public bool IsDead;
    public float currentHealth, maxHealth;
}
public class RunStageController
{
    public bool IsRunning, IsCompleted, IsDefeated, IsFinaleStarted, IsBossPhase, UsesSharedWaves;
    public int CurrentStageIndex, RemainingBosses, FinaleRequests, Restarts;
    public float FinaleStartTime, RemainingUntilFinale, ElapsedTime;
    public bool TryStartFinale() { FinaleRequests++; return true; }
    public void RestartRun() { Restarts++; }
}
#endif
