using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Live frames, with explicitly injected automatic/finale boundaries; NOT a 30s/8min soak.
public sealed class TimedEventChecks : IDisposable
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }
    public static readonly List<string> Evidence = new List<string>();
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly Dictionary<Weapon, bool> weapons = new Dictionary<Weapon, bool>();
    readonly Dictionary<PlayerHealth, Vector2> health = new Dictionary<PlayerHealth, Vector2>();
    readonly Dictionary<DeveloperDebugGui, bool> guis = new Dictionary<DeveloperDebugGui, bool>();
    WorldManager manager;
    StateSwitchController flow;
    RunStageController run;
    DeveloperDebugGui gui;
    Button switchButton, echoButton, finaleButton, restartButton;
    EnemySpawner[] spawners;
    string label;
    float savedScale;
    int midpoints, changes;
    WorldId expectedWorld;
    bool savedAuto;

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("TimedEventChecks already running");
        Running = true;
        Passed = Failed = 0;
        Evidence.Clear();
        var test = new TimedEventChecks { savedScale = Time.timeScale };
        try { yield return test.Execute(); }
        finally { test.Dispose(); Running = false; }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "Live Play Mode required");
        Note("FIXTURE: timerCounter near boundary and ElapsedTime=479.99 injected via reflection; configuration 30/5/0.8/480 unchanged. Manual warning runs live. HP=1000000; weapons disabled; spawners NEVER disabled by test. Not full 30s/8min soak; GameView focus requires manual validation.");
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            label = scene;
            Unhook();
            Time.timeScale = 1;
            Require(Application.CanStreamedLevelBeLoaded(scene), "scene enabled in Build Settings: " + scene);
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return Frames(3);
            Bind();
            Check(flow.AutomaticSwitchingEnabled && Read<bool>(flow, "automaticSwitchingEnabled"), "fresh serialized auto=true");
            Check(flow.SwitchInterval == 30 && Read<float>(flow, "warningDuration") == 5
                && Read<float>(flow, "flipDuration") == .8f && run.FinaleStartTime == 480,
                "production defaults remain 30/5/0.8/480");
            Check(typeof(StateSwitchController).GetProperty("SwitchInterval").GetSetMethod(true) == null
                && typeof(RunStageController).GetProperty("FinaleStartTime").GetSetMethod(true) == null,
                "SwitchInterval and FinaleStartTime are readonly");
            yield return OpenGui();
            yield return AutomaticButtonRoundTrip();
            yield return AutoOffAndManual();
            yield return AutomaticWarningAndFlip();
            yield return PauseGuards(false);
            yield return PauseGuards(true);

            float elapsed = run.ElapsedTime;
            Require(finaleButton.IsActive() && finaleButton.IsInteractable(), "actual finale button ready");
            finaleButton.onClick.Invoke();
            Check(run.ElapsedTime == elapsed && run.IsFinaleStarted && manager.IsFinalFusion,
                "actual 480s Finale listener starts early without ElapsedTime jump");
            yield return FusedAutomaticButtonGuard();
            yield return Restart();

            echoButton.onClick.Invoke();
            Check(!flow.AutomaticSwitchingEnabled, "actual Auto switch button disables auto before480 fixture");
            Inject(run, "<ElapsedTime>k__BackingField", 479.99f);
            Check(!run.IsFinaleStarted && run.ElapsedTime < 480, "autooff finale fixture initially below480");
            yield return Until(() => run.IsFinaleStarted, 2, "live Update crosses480 with autooff");
            Check(run.ElapsedTime >= 480 && manager.IsFinalFusion && !flow.AutomaticSwitchingEnabled,
                "world autooff does not suppress automatic 480 finale");
            yield return FusedAutomaticButtonGuard();
            yield return Restart();
            Check(spawners.All(s => s.enabled && s.gameObject.activeSelf), "restart restores enabled spawners; fixture never disabled them");
        }
    }

    IEnumerator AutomaticButtonRoundTrip()
    {
        Require(flow.AutomaticSwitchingEnabled && echoButton.IsActive() && echoButton.IsInteractable(),
            "actual Auto switch button ready with auto on");
        var initial = manager.CurrentWorldId;
        int before = midpoints, worldBefore = changes;
        echoButton.onClick.Invoke();
        Check(!flow.AutomaticSwitchingEnabled, "actual Auto switch listener toggles off");
        yield return Frames(2);
        Check(echoButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim() == "Auto switch: Off",
            "Auto switch label refreshes to Off");
        echoButton.onClick.Invoke();
        Check(flow.AutomaticSwitchingEnabled, "actual Auto switch listener toggles back on");
        yield return Frames(2);
        Check(echoButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim() == "Auto switch: On"
            && manager.CurrentWorldId == initial && midpoints == before && changes == worldBefore,
            "Auto switch label refreshes to On without committing a world switch");
    }

    IEnumerator FusedAutomaticButtonGuard()
    {
        Require(manager.IsFused && manager.IsFinalFusion, "fused callback fixture active");
        yield return Frames(2);
        bool automatic = flow.AutomaticSwitchingEnabled;
        Check(echoButton.IsActive() && !echoButton.IsInteractable(), "Auto switch visible but disabled while fused");
        echoButton.onClick.Invoke();
        Check(flow.AutomaticSwitchingEnabled == automatic, "direct Auto switch Invoke rejected while fused");
        yield return Frames(2);
        Check(echoButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim()
            == (automatic ? "Auto switch: On" : "Auto switch: Off"), "fused rejection preserves Auto switch label");
    }

    IEnumerator AutoOffAndManual()
    {
        Require(flow.AutomaticSwitchingEnabled && echoButton.IsActive() && echoButton.IsInteractable(),
            "actual Auto switch button ready before manual fixture");
        echoButton.onClick.Invoke();
        Check(!flow.AutomaticSwitchingEnabled, "actual Auto switch button disables auto before manual request");
        var initial = manager.CurrentWorldId;
        int before = midpoints, worldBefore = changes;
        Check(flow.RemainingTime == 30 && flow.WarningProgress == 0 && flow.FlipProgress == 0,
            "disable idle clears transition and resets full30");
        Inject(flow, "timerCounter", .01f);
        yield return Seconds(.2f);
        Check(manager.CurrentWorldId == initial && midpoints == before && changes == worldBefore
            && !flow.IsFlipping && flow.WarningProgress == 0 && flow.RemainingTime == 30,
            "autooff crosses injected near-zero boundary without switching");

        expectedWorld = initial == WorldId.Material ? WorldId.Echo : WorldId.Material;
        float elapsed = run.ElapsedTime;
        Require(switchButton.IsActive() && switchButton.IsInteractable(), "actual switch button ready");
        switchButton.onClick.Invoke();
        Check(run.ElapsedTime == elapsed && Read<bool>(flow, "requested") && flow.RemainingTime == 5
            && manager.CurrentWorldId == initial && !flow.IsFlipping,
            "actual 30s Switch listener accepts manual autooff with5s warning, no clock/world jump");
        flow.AutomaticSwitchingEnabled = false;
        Check(Read<bool>(flow, "requested") && flow.RemainingTime == 5, "setting false again preserves accepted manual request");
        Check(!flow.RequestNextWorldSwitch(), "duplicate manual request rejected");
        yield return Seconds(.2f);
        Check(flow.WarningProgress > 0 && !flow.IsFlipping && manager.CurrentWorldId == initial && midpoints == before,
            "manual request advances warning before midpoint");
        yield return Until(() => midpoints > before, 7, "manual live5s midpoint");
        yield return Until(() => !flow.IsFlipping, 2, "manual flip completes");
        yield return Seconds(.2f);
        Check(midpoints == before + 1 && changes == worldBefore + 1 && manager.CurrentWorldId == expectedWorld
            && !flow.AutomaticSwitchingEnabled && flow.RemainingTime == 30 && flow.WarningProgress == 0 && flow.FlipProgress == 0
            && echoButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim() == "Auto switch: Off",
            "manual autooff commits exactly once and completes/reset30, preserving Auto Off");
    }

    IEnumerator AutomaticWarningAndFlip()
    {
        flow.AutomaticSwitchingEnabled = true;
        int before = midpoints, worldBefore = changes;
        var initial = manager.CurrentWorldId;
        Inject(flow, "timerCounter", 4f);
        yield return Frames(1);
        Require(flow.WarningProgress > 0 && !flow.IsFlipping && !Read<bool>(flow, "requested"), "automatic warning active");
        flow.AutomaticSwitchingEnabled = false;
        Check(flow.RemainingTime == 30 && flow.WarningProgress == 0 && flow.FlipProgress == 0 && !flow.IsFlipping,
            "disable automatic warning cancels immediately and resets30");
        yield return Seconds(.2f);
        Check(midpoints == before && changes == worldBefore && manager.CurrentWorldId == initial, "cancelled automatic warning never commits");
        flow.AutomaticSwitchingEnabled = true;
        Check(flow.RemainingTime == 30, "reenable starts from full30, no stale warning");
        yield return Seconds(.2f);
        Check(flow.RemainingTime < 30 && flow.RemainingTime > 25 && flow.WarningProgress == 0 && midpoints == before,
            "reenabled auto counts down fresh interval (short sample, not30s soak)");
        expectedWorld = initial == WorldId.Material ? WorldId.Echo : WorldId.Material;
        Inject(flow, "timerCounter", .39f);
        yield return Until(() => flow.IsFlipping, 2, "automatic flip begins");
        Require(midpoints == before && flow.FlipProgress < .5f, "disable fixture is before flip midpoint");
        flow.AutomaticSwitchingEnabled = false;
        Check(flow.IsFlipping, "disable auto preserves started flip");
        yield return Until(() => midpoints > before, 2, "disabled-auto flip midpoint");
        yield return Until(() => !flow.IsFlipping, 2, "disabled-auto flip completion");
        yield return Seconds(.2f);
        Check(midpoints == before + 1 && changes == worldBefore + 1 && manager.CurrentWorldId == expectedWorld
            && flow.RemainingTime == 30, "started automatic flip finishes exactly once after disable");
    }

    IEnumerator PauseGuards(bool upgrade)
    {
        var panel = UIController.instance.levelUpPanel;
        Require(panel != null, "upgrade panel bound");
        bool savedPanel = panel.activeSelf;
        try
        {
            flow.AutomaticSwitchingEnabled = true;
            flow.CancelTransition();
            if (upgrade) panel.SetActive(true); else Time.timeScale = 0;
            yield return Frames(2);
            float elapsed = run.ElapsedTime, remaining = flow.RemainingTime;
            int before = midpoints, worldBefore = changes;
            Check(!switchButton.IsInteractable() && !finaleButton.IsInteractable() && restartButton.IsInteractable(),
                "pause/upgrade disables timed buttons but preserves Restart: upgrade=" + upgrade);
            Check(!flow.RequestNextWorldSwitch(), "public manual request rejected under pause guard");
            switchButton.onClick.Invoke();
            finaleButton.onClick.Invoke();
            yield return Frames(3);
            Check(run.ElapsedTime == elapsed && flow.RemainingTime == remaining && !Read<bool>(flow, "requested")
                && !run.IsFinaleStarted && midpoints == before && changes == worldBefore,
                "actual Invoke cannot bypass pause/upgrade; both clocks frozen");
            yield return AutomaticButtonRoundTrip();
            Check(run.ElapsedTime == elapsed && midpoints == before && changes == worldBefore,
                "paused Auto switch round trip does not resume gameplay: upgrade=" + upgrade);
            Inject(run, "<ElapsedTime>k__BackingField", 479.99f);
            yield return Frames(2);
            Check(run.ElapsedTime == 479.99f && !run.IsFinaleStarted, "pause guard freezes near480 automatic finale");
            Inject(run, "<ElapsedTime>k__BackingField", elapsed);
        }
        finally { panel.SetActive(savedPanel); Time.timeScale = 1; }
        yield return Frames(2);
        flow.AutomaticSwitchingEnabled = false;
        Require(flow.RequestNextWorldSwitch(), "manual accepted after resume");
        yield return Frames(1);
        Time.timeScale = 0;
        yield return Frames(2);
        float warning = flow.WarningProgress, timer = flow.RemainingTime;
        yield return Frames(2);
        Check(flow.WarningProgress == warning && flow.RemainingTime == timer && Read<bool>(flow, "requested"),
            "pause preserves accepted warning without advancing");
        Time.timeScale = 1;
        flow.CancelTransition();
        yield return Frames(2);
    }

    IEnumerator Restart()
    {
        flow.AutomaticSwitchingEnabled = false;
        Time.timeScale = 0;
        yield return Frames(2);
        var oldRun = run;
        var oldFlow = flow;
        Require(restartButton.IsActive() && restartButton.IsInteractable(), "actual Restart active while paused");
        Unhook();
        restartButton.onClick.Invoke();
        yield return Frames(3);
        Bind();
        Check(oldRun == null && oldFlow == null && Time.timeScale == 1 && run.IsRunning && !run.IsFinaleStarted
            && run.ElapsedTime < 5 && flow.AutomaticSwitchingEnabled && Read<bool>(flow, "automaticSwitchingEnabled")
            && flow.RemainingTime > 25 && flow.RemainingTime <= 30 && !flow.IsFlipping && flow.WarningProgress == 0,
            "actual Restart reloads serialized true and fresh30 countdown, not previous runtime false");
        yield return OpenGui();
    }

    void Bind()
    {
        run = InScene<RunStageController>().Single();
        manager = InScene<WorldManager>().Single(m => m.isActiveAndEnabled);
        flow = Property<StateSwitchController>(manager, "SwitchFlow");
        spawners = Read<EnemySpawner[]>(run, "spawners");
        Require(run.IsRunning && run.isActiveAndEnabled && manager.IsInitialized && flow != null && flow.isActiveAndEnabled,
            "initialized live controllers");
        Check(run.UsesSharedWaves == (label == "DebugRun"), "Main independent / DebugRun shared waves unchanged");
        Require(spawners.Length == 2 && spawners.All(s => s.enabled && s.gameObject.activeSelf), "both spawners enabled");
        savedAuto = flow.AutomaticSwitchingEnabled;
        midpoints = changes = 0;
        flow.TransitionMidpoint += Midpoint;
        manager.WorldChanged += WorldChanged;
        Protect();
    }

    IEnumerator OpenGui()
    {
        gui = InScene<DeveloperDebugGui>().Single();
        var panel = InScene<RunStagePanel>().Single();
        Require(gui.IsAvailable && panel.transform.IsChildOf(gui.transform)
            && Read<RunStageController>(panel, "runController") == run, "production GUI bound to current run");
        if (!guis.ContainsKey(gui)) guis.Add(gui, gui.IsOpen);
        gui.SetOpen(true);
        yield return Frames(2);
        switchButton = Read<Button>(gui, "materialButton");
        echoButton = Read<Button>(gui, "echoButton");
        var stages = Read<Button[]>(panel, "stageButtons");
        Require(stages.Length == 4, "four serialized stage slots");
        finaleButton = stages[3];
        restartButton = Read<Button>(panel, "restartButton");
        Check(gui.IsOpen && Read<Canvas>(gui, "canvas").isActiveAndEnabled
            && Read<GraphicRaycaster>(gui, "raycaster").isActiveAndEnabled && switchButton.IsActive()
            && switchButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim() == "30s Switch"
            && echoButton.IsActive() && echoButton.IsInteractable()
            && echoButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim()
                == (flow.AutomaticSwitchingEnabled ? "Auto switch: On" : "Auto switch: Off")
            && !Read<Button>(gui, "fusionButton").gameObject.activeSelf, "GUI exposes30s Switch and Auto switch; old Fusion hidden");
        Check(stages.Take(3).All(b => !b.gameObject.activeSelf) && !Read<Button>(panel, "nextButton").gameObject.activeSelf
            && finaleButton.IsActive() && finaleButton.GetComponentInChildren<TMPro.TMP_Text>(true).text.Trim() == "480s Finale"
            && restartButton.IsActive(), "stage0..2/Next hidden; stageButtons[3] label480s Finale and Restart visible");
    }

    void Midpoint()
    {
        midpoints++;
        Check(flow.IsFlipping && flow.FlipProgress == .5f && manager.CurrentWorldId == expectedWorld,
            "midpoint event observes committed target at exactly0.5");
    }
    void WorldChanged() { changes++; }
    void Protect()
    {
        foreach (var hp in InScene<PlayerHealth>())
        {
            if (!hp.HasInitialized || hp.IsDead) continue;
            var owner = Property<PlayerHealth>(hp, "HealthOwner");
            if (!health.ContainsKey(owner))
            {
                health.Add(owner, new Vector2(owner.currentHealth, owner.maxHealth));
                owner.maxHealth = owner.currentHealth = 1000000;
            }
        }
        foreach (var weapon in InScene<Weapon>())
        {
            if (!weapons.ContainsKey(weapon)) weapons.Add(weapon, weapon.enabled);
            weapon.enabled = false;
        }
    }
    IEnumerator Frames(int count) { for (int i = 0; i < count; i++) { Protect(); yield return null; } }
    IEnumerator Seconds(float seconds)
    {
        float end = Time.time + seconds;
        yield return Until(() => Time.time >= end, seconds + 2, "scaled short sample");
    }
    IEnumerator Until(Func<bool> ready, float seconds, string message)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready()) { Require(Time.realtimeSinceStartup < end, "timeout: " + message); Protect(); yield return null; }
    }
    static T[] InScene<T>() where T : Component
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene == SceneManager.GetActiveScene()).ToArray();
    }
    static FieldInfo Field(object target, string name)
    {
        return target.GetType().GetField(name, Members) ?? throw new MissingFieldException(target.GetType().FullName, name);
    }
    static T Read<T>(object target, string name) { return (T)Field(target, name).GetValue(target); }
    static T Property<T>(object target, string name)
    {
        var p = target.GetType().GetProperty(name, Members);
        if (p == null) throw new MissingMemberException(target.GetType().FullName, name);
        return (T)p.GetValue(target, null);
    }
    void Inject(object target, string name, float value)
    {
        Field(target, name).SetValue(target, value);
        Note("INJECT " + target.GetType().Name + "." + name + "=" + value);
    }
    void Note(string message) { string text = "TIMED_EVENT " + label + " " + message; Evidence.Add(text); Debug.Log(text); }
    void Check(bool ok, string message) { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + message); }
    void Require(bool ok, string message) { if (!ok) { Check(false, message); throw new InvalidOperationException(message); } }
    void Unhook()
    {
        if (flow != null) flow.TransitionMidpoint -= Midpoint;
        if (manager != null) manager.WorldChanged -= WorldChanged;
    }
    public void Dispose()
    {
        Unhook();
        if (flow != null) { flow.CancelTransition(); flow.AutomaticSwitchingEnabled = savedAuto; }
        foreach (var p in guis) if (p.Key != null) p.Key.SetOpen(p.Value);
        foreach (var p in weapons) if (p.Key != null) p.Key.enabled = p.Value;
        foreach (var p in health) if (p.Key != null && !p.Key.IsDead) { p.Key.maxHealth = p.Value.y; p.Key.currentHealth = p.Value.x; }
        Time.timeScale = savedScale;
    }
}
