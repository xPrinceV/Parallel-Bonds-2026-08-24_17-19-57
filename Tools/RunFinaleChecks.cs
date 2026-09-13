using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// In-memory Live Play Mode checks. near480 is an injected fixture, NOT an eight-minute soak.
public sealed class RunFinaleChecks : IDisposable
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }
    public static readonly List<string> Evidence = new List<string>();
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly Dictionary<Weapon, bool> weapons = new Dictionary<Weapon, bool>();
    readonly Dictionary<PlayerHealth, Vector2> health = new Dictionary<PlayerHealth, Vector2>();
    readonly Dictionary<DeveloperDebugGui, bool> guiStates = new Dictionary<DeveloperDebugGui, bool>();
    RunStageController run;
    WorldManager manager;
    FusionTransitionController entrance;
    StateSwitchController flip;
    World[] worlds;
    EnemySpawner[] spawners;
    EnemyController boss;
    string label;
    float savedScale;
    int completedCount, completedFrame, spawnFrame, spawnEvents, worldChanges;
    bool pauseOnCompleted;

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("RunFinaleChecks already running");
        Running = true;
        Passed = Failed = 0;
        Evidence.Clear();
        var test = new RunFinaleChecks { savedScale = Time.timeScale };
        try { yield return test.Execute(); }
        finally { test.Dispose(); Running = false; }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "Live Play Mode required");
        Note("FIXTURE: reflection injects ElapsedTime near480/exact480; runtime shared player HP=1000000; weapons disabled, spawners NEVER disabled by tests; not real 8-minute gameplay");
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            label = scene;
            Unhook();
            Time.timeScale = 1f;
            // Build registration is also a prerequisite for the production RestartRun path.
            Require(Application.CanStreamedLevelBeLoaded(scene), scene + " must be enabled in Build Settings; test does not edit scenes/settings");
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return Frames(3);
            Bind();
            yield return AutomaticBoundaryAndVictory();
            yield return Restart("victory");

            // Public debug/F-key route is allowed to start early; only automatic Update has the 480 gate.
            Require(manager.TryEnterFusion(), "public TryEnterFusion delegates to finale before480");
            Check(run.ElapsedTime < 480 && run.IsFinaleStarted && manager.IsFinalFusion && entrance.IsPlaying,
                "manual entry commits final fusion immediately, not an early-timer rejection test");
            entrance.Cancel();
            yield return Frames(2);
            Check(completedCount == 0 && spawnEvents == 0 && run.RemainingBosses == 0
                && run.IsDefeated && !run.IsCompleted && Time.timeScale == 0,
                "Cancel is not Completed: no boss, defeat not victory");
            yield return Restart("cancelled entrance");

            Require(run.TryEnterStage(3), "stage3 public debug entry accepted");
            Check(entrance.IsPlaying && run.RemainingBosses == 0, "stage3 waits for entrance, no immediate boss");
            yield return Restart("in-flight entrance");
            float staleDeadline = Time.realtimeSinceStartup + Read<float>(entrance, "duration") + 1f;
            while (Time.realtimeSinceStartup < staleDeadline) yield return Frames(1);
            Check(!run.IsFinaleStarted && completedCount == 0 && spawnEvents == 0 && run.RemainingBosses == 0,
                "old entrance cannot spawn a boss after restart");

            // Completion followed by pause must defer spawn, not lose completion or declare cancellation.
            pauseOnCompleted = true;
            SetElapsed(479.99f);
            Note("FIXTURE: near480=479.99; real Update crosses threshold (no explicit start call)");
            yield return Until(() => completedCount == 1, 12, "automatic threshold crossing and entrance completion with pause callback");
            Check(run.ElapsedTime >= 480f && run.IsFinaleStarted && manager.IsFinalFusion,
                "near480 fixture crosses 480 through live deltaTime and starts automatically");
            yield return Frames(3);
            Check(Time.timeScale == 0 && run.RemainingBosses == 0 && spawnEvents == 0 && !run.IsDefeated,
                "Completed while paused retains pending boss without spawning");
            pauseOnCompleted = false;
            Time.timeScale = 1f;
            int resumeFrame = Time.frameCount;
            yield return Until(() => spawnEvents > 0, 3, "pending boss after resume");
            Check(spawnEvents == 1 && spawnFrame == resumeFrame + 1 && spawnFrame > completedFrame,
                "pending boss spawns on first eligible next frame after resume");
            Require(boss != null, "boss exists for non-death removal test");
            Object.Destroy(boss.gameObject);
            yield return Frames(3);
            Check(run.IsDefeated && !run.IsCompleted && Time.timeScale == 0,
                "Destroy without Died fails run; never grants victory");
            yield return Restart("removed boss defeat");

            // Additive cases: keep every original boundary/entrance/death-event/restart check above.
            yield return PausedBossButtonRestart();
            yield return PlayerDeathAndButtonRestart(false);
            yield return PlayerDeathAndButtonRestart(true);
        }
    }

    IEnumerator PausedBossButtonRestart()
    {
        Require(run.TryStartFinale(), "fresh finale for paused live-boss UI restart");
        yield return Until(() => spawnEvents > 0, 12, "live boss before paused UI restart");
        Require(boss != null && run.IsBossPhase && run.RemainingBosses == 1,
            "paused restart fixture has an actual living boss, not an entrance/pending spawn");
        var liveBoss = boss;
        Time.timeScale = 0;
        yield return Frames(1);
        float elapsed = run.ElapsedTime, bossHealth = liveBoss.health;
        Vector3 position = liveBoss.transform.position;
        yield return Frames(3);
        Check(Time.timeScale == 0 && run.ElapsedTime == elapsed && liveBoss.health == bossHealth
            && liveBoss.transform.position == position && run.IsRunning && !run.IsCompleted && !run.IsDefeated
            && run.RemainingBosses == 1 && Enemies().Length == 1 && Enemies()[0] == liveBoss,
            "live boss pause freezes run clock/health/position without completing or removing boss");
        Irreversible();
        yield return CaptureBossAtEOF();
        yield return Restart("paused live boss via restartButton", true);
        Check(liveBoss == null && worlds.All(w => !w.Player.GetComponent<PlayerHealth>().IsDead)
            && spawners.All(s => s.enabled && s.gameObject.activeSelf),
            "UI boss restart destroys living boss and restores living players/enabled spawners");
    }

    IEnumerator PlayerDeathAndButtonRestart(bool duringBoss)
    {
        string phase = duringBoss ? "boss phase" : "entrance";
        Require(run.TryStartFinale(), "fresh finale for player death during " + phase);
        if (duringBoss)
            yield return Until(() => spawnEvents > 0, 12, "live boss before player death");
        else
            Require(entrance.IsPlaying && completedCount == 0 && run.RemainingBosses == 0,
                "player-death fixture is inside unfinished entrance");
        var player = manager.FusionPlayer;
        Require(player != null, "fusion player exists for lethal damage fixture");
        var hp = player.GetComponent<PlayerHealth>();
        Require(hp != null && hp.isActiveAndEnabled && !hp.IsDead && hp.currentHealth > 0,
            "player is alive before DamageHandler fixture");
        var liveBoss = boss;
        if (duringBoss) Require(liveBoss != null && liveBoss.health > 0 && run.RemainingBosses == 1,
            "player dies while actual boss is alive");
        int bossDeaths = 0;
        Action<EnemyController> died = _ => bossDeaths++;
        if (liveBoss != null) liveBoss.Died += died;
        try
        {
            Note("FIXTURE: lethal PlayerHealth.DamageHandler(currentHealth) during " + phase
                + "; not waiting for natural enemy damage against survival HP fixture");
            hp.DamageHandler(hp.currentHealth);
            Check(hp.IsDead && hp.currentHealth == 0 && !player.gameObject.activeInHierarchy
                && worlds.All(w => w.Player.GetComponent<PlayerHealth>().IsDead),
                "real player damage marks shared HP dead and deactivates entry player during " + phase);
            yield return Frames(3);
            Check(run.IsDefeated && !run.IsCompleted && !run.IsRunning && Time.timeScale == 0
                && run.RemainingBosses == 0 && Enemies().Length == 0 && !manager.IsFused && !entrance.IsPlaying,
                "player death during " + phase + " fails/pauses run and clears fusion/entrance/encounter");
            Check(bossDeaths == 0 && (duringBoss ? liveBoss == null : completedCount == 0 && spawnEvents == 0),
                "player-death cleanup is not boss Died/victory and unfinished entrance cannot spawn");
            Check(!run.TryStartFinale() && !manager.TryEnterFusion() && !run.TryEnterStage(0),
                "player-death defeat rejects new finale/fusion/stage requests");
            Time.timeScale = 1;
            yield return Frames(2);
            Check(Time.timeScale == 0 && run.IsDefeated && !run.IsCompleted,
                "player-death defeat resists unpause");
        }
        finally { if (liveBoss != null) liveBoss.Died -= died; }
        yield return Restart("player death during " + phase + " via restartButton", true);
        Check(worlds.All(w => w.Player.GetComponent<PlayerHealth>().HasInitialized
            && !w.Player.GetComponent<PlayerHealth>().IsDead && w.Player.GetComponent<PlayerHealth>().currentHealth > 0),
            "UI restart after player death resets shared death flag and positive HP in both worlds");
    }

    RunStagePanel OpenRunPanel(out DeveloperDebugGui gui)
    {
        gui = InScene<DeveloperDebugGui>().Single();
        var panel = InScene<RunStagePanel>().Single();
        Require(gui.IsAvailable && Read<WorldManager>(gui, "worldManager") == manager
            && panel.transform.IsChildOf(gui.transform)
            && Read<RunStageController>(panel, "runController") == run
            && Read<UIController>(panel, "uiController") == UIController.instance && UIController.instance != null,
            "DeveloperDebugGui contains RunStagePanel bound to this scene's actual run/UI");
        if (!guiStates.ContainsKey(gui)) guiStates.Add(gui, gui.IsOpen);
        gui.SetOpen(true);
        var canvas = Read<Canvas>(gui, "canvas");
        var raycaster = Read<GraphicRaycaster>(gui, "raycaster");
        Require(gui.IsOpen && panel.isActiveAndEnabled && canvas != null && canvas.isActiveAndEnabled
            && panel.transform.IsChildOf(canvas.transform) && raycaster != null && raycaster.isActiveAndEnabled,
            "production SetOpen(true) exposes run panel canvas and raycaster");
        var stages = Read<Button[]>(panel, "stageButtons");
        Require(stages != null && stages.Length == 4 && stages[3] != null,
            "four stage buttons including finale are bound");
        var finaleLabel = stages[3].GetComponentInChildren<TMPro.TMP_Text>(true);
        Check(finaleLabel != null && finaleLabel.text.Trim() == $"{run.FinaleStartTime:0.#}s Finale",
            "finale label matches configured event time");
        return panel;
    }

    IEnumerator CaptureBossAtEOF()
    {
        // Optional graphics evidence cannot be captured in a headless/batch session.
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Note("SKIP optional boss PNG: requires rendered Game view and actual WaitForEndOfFrame");
            yield break;
        }
        DeveloperDebugGui gui;
        var panel = OpenRunPanel(out gui);
        bool wasOpen = guiStates[gui];
        Texture2D image = null;
        try
        {
            yield return Frames(2); // let actual UI Update/layout/graphics registration run while paused
            Canvas.ForceUpdateCanvases();
            var status = Read<TMPro.TMP_Text>(panel, "statusText");
            Check(status != null && status.text.Contains("Rift Lord") && status.text.Contains("Paused")
                && status.text.Contains("Bosses: 1"), "open debug run panel displays paused single Rift Lord");
            Note("Waiting for actual EOF boss PNG; keep the rendered Game view visible (Scene view can stall WaitForEndOfFrame)");
            yield return new WaitForEndOfFrame();
            Require(gui.IsOpen && boss != null && boss.health == 300f && run.IsBossPhase
                && run.RemainingBosses == 1 && Time.timeScale == 0,
                "EOF capture still has open debug GUI and paused living 300HP finale boss");
            // Capture the final framebuffer including overlay UI, never an endFrameRendering callback or Camera.Render.
            image = ScreenCapture.CaptureScreenshotAsTexture();
            Require(image != null && image.width > 0 && image.height > 0, "EOF screenshot texture is valid");
            byte[] png = image.EncodeToPNG();
            Require(png != null && png.Length > 8, "EOF screenshot encodes PNG bytes");
            System.IO.Directory.CreateDirectory("Logs");
            string path = "Logs/RunFinale-" + SceneManager.GetActiveScene().name + ".png";
            System.IO.File.WriteAllBytes(path, png);
            Check(System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length == png.Length,
                "actual EOF boss+debug GUI PNG saved: " + path + "; frame=" + Time.frameCount
                + "; size=" + image.width + "x" + image.height + "; bytes=" + png.Length);
        }
        finally
        {
            if (image != null) Object.Destroy(image);
            if (gui != null) gui.SetOpen(wasOpen);
        }
        yield return null; // resume subsequent tests at a normal coroutine phase, not inside EOF
    }

    IEnumerator AutomaticBoundaryAndVictory()
    {
        int stage = run.CurrentStageIndex;
        Check(!run.TryEnterStage(-1) && !run.TryEnterStage(4) && run.CurrentStageIndex == stage
            && !run.IsFinaleStarted, "invalid stages leave run unchanged");
        SetElapsed(479f);
        Time.timeScale = 0;
        yield return Frames(3);
        Check(run.ElapsedTime == 479f && !run.IsFinaleStarted && !run.TryStartFinale(),
            "near480 fixture: pause freezes clock and rejects explicit finale");
        Time.timeScale = 1f;
        yield return Frames(1);
        Check(run.ElapsedTime > 479f && run.ElapsedTime < 480f && !run.IsFinaleStarted,
            "near480 fixture: one live sub-boundary Update does not start finale; elapsed=" + run.ElapsedTime);

        Require(UIController.instance != null && UIController.instance.levelUpPanel != null,
            "upgrade panel exists for pause fixture");
        var panel = UIController.instance.levelUpPanel;
        panel.SetActive(true);
        Time.timeScale = 1f;
        SetElapsed(480f);
        yield return Frames(3);
        Check(run.ElapsedTime == 480f && !run.IsFinaleStarted && !run.TryStartFinale(),
            "exact480 fixture: upgrade panel blocks automatic and explicit finale even at scale1");
        panel.SetActive(false);
        Time.timeScale = 0f;
        yield return Frames(2);
        Check(run.ElapsedTime == 480f && !run.IsFinaleStarted && !manager.TryEnterFusion(),
            "exact480 fixture: scale0 blocks timer and public fusion shortcut");

        // Also put the normal flip at its next-frame deadline. Do not disable the flip ourselves.
        Write(flip, "timerCounter", 0f);
        Note("FIXTURE: normal flip timerCounter=0 concurrently with ElapsedTime=480 to test finale priority");
        WorldId entryId = manager.CurrentWorldId;
        Time.timeScale = 1f;
        yield return Frames(1);
        Require(run.IsFinaleStarted && manager.IsFinalFusion && manager.IsFused, "automatic Update starts final fusion at >=480");
        Check(run.CurrentStageIndex == 3 && run.IsRunning && !run.IsBossPhase && run.RemainingBosses == 0
            && entrance.IsPlaying && completedCount == 0, "entry commits before boss phase; waits for real Completed");
        Check(!flip.enabled && !flip.IsFlipping && worldChanges == 0 && manager.CurrentWorldId == entryId,
            "finale cancels competing normal flip before midpoint and disables its clock");
        Check(worlds.All(w => w.IsActive) && spawners.All(s => s.enabled && s.gameObject.activeSelf && s.UsesExternalStages),
            "both worlds active; production owns stopped spawners, components remain enabled");
        Check(!run.TryStartFinale() && !manager.TryEnterFusion() && !run.TryEnterStage(3),
            "repeated entrance requests rejected without duplicate commit");

        Time.timeScale = 0;
        yield return Frames(1); // settle deltaTime after changing scale
        float progress = entrance.Progress, elapsed = run.ElapsedTime;
        yield return Frames(3);
        Check(entrance.Progress == progress && run.ElapsedTime == elapsed && completedCount == 0
            && run.RemainingBosses == 0, "paused entrance freezes progress/clock and cannot spawn");
        panel.SetActive(true);
        Time.timeScale = 1;
        yield return Frames(3);
        Check(entrance.Progress == progress && run.ElapsedTime == elapsed && completedCount == 0,
            "upgrade panel also freezes entrance/clock at scale1");
        panel.SetActive(false);
        yield return Until(() => spawnEvents > 0, 12, "real entrance Completed then boss");
        Require(boss != null, "spawn callback captured boss");
        Check(completedCount == 1 && spawnEvents == 1 && spawnFrame == completedFrame + 1,
            "exact next-frame spawn: Completed frame=" + completedFrame + ", spawn frame=" + spawnFrame);
        Check(run.IsBossPhase && run.RemainingBosses == 1 && Enemies().Length == 1 && Enemies()[0] == boss,
            "exactly one boss across BOTH world roots (including inactive objects)");
        Irreversible();
        int bossId = boss.GetInstanceID();
        yield return Frames(5);
        Check(spawnEvents == 1 && run.RemainingBosses == 1 && Enemies().Length == 1
            && boss != null && boss.GetInstanceID() == bossId, "single boss persists; legacy spawners do not resume");
        Require(boss != null && Mathf.Approximately(boss.health, 300f), "isolated boss still has 300HP before damage fixture");
        int deaths = 0;
        Action<EnemyController> died = _ => deaths++;
        boss.Died += died;
        try
        {
            boss.TakeDamage(299f);
            Check(Mathf.Approximately(boss.health, 1f) && deaths == 0 && run.RemainingBosses == 1 && !run.IsCompleted,
                "299 damage is nonlethal; no premature victory");
            boss.TakeDamage(1f);
            Check(deaths == 1 && run.RemainingBosses == 0 && !run.IsCompleted,
                "lethal Died registers once; victory deferred out of damage stack");
            boss.TakeDamage(1f);
            Check(deaths == 1, "repeat damage cannot publish a second death");
        }
        finally { if (boss != null) boss.Died -= died; }
        yield return Frames(2);
        Check(run.IsCompleted && !run.IsDefeated && !run.IsRunning && Time.timeScale == 0
            && Enemies().Length == 0, "real boss death wins and clears encounter on following Update");
        Check(!run.TryStartFinale() && !run.TryEnterStage(0), "victory cannot reenter stages");
        Time.timeScale = 1;
        panel.SetActive(true);
        yield return Frames(2);
        Check(Time.timeScale == 0 && !panel.activeSelf && run.IsCompleted,
            "finished run resists UI/unpause; restart is only exit");
    }

    void Irreversible()
    {
        WorldId before = manager.CurrentWorldId;
        World other = worlds.Single(w => w.WorldId != before);
        bool rejected = !manager.TryExitFusion() && !manager.SwitchWorld(other.WorldId)
            && !flip.RequestSwitch(other.WorldId) && !run.TryAdvanceStage();
        for (int i = 0; i <= 3; i++) rejected &= !run.TryEnterStage(i);
        Check(rejected && manager.IsFinalFusion && manager.IsFused && manager.CurrentWorldId == before
            && run.CurrentStageIndex == 3 && run.RemainingBosses == 1,
            "irreversible: exit/world switch/flip/next/all stage rewinds rejected");
    }

    IEnumerator Restart(string reason, bool viaButton = false)
    {
        Button restartButton = null;
        if (viaButton)
        {
            Time.timeScale = 0;
            DeveloperDebugGui gui;
            var panel = OpenRunPanel(out gui);
            yield return Frames(2);
            restartButton = Read<Button>(panel, "restartButton");
            Require(restartButton != null && restartButton.transform.IsChildOf(panel.transform)
                && restartButton.IsActive() && restartButton.IsInteractable() && gui.IsOpen && Time.timeScale == 0,
                "actual bound restartButton is active/interactable with debug GUI open while paused");
        }
        int oldId = run.GetInstanceID();
        var oldRun = run;
        var oldEntrance = entrance;
        var oldBoss = boss;
        Unhook();
        Time.timeScale = 0; // also verifies restart can escape pause
        if (viaButton)
        {
            Note("Invoking actual restartButton.onClick once for " + reason
                + "; existing Awake listener -> RunStagePanel.Restart -> RunStageController.RestartRun; no fallback/direct call");
            restartButton.onClick.Invoke();
        }
        else oldRun.RestartRun();
        yield return Frames(3);
        Bind();
        Check(oldRun == null && oldEntrance == null && oldBoss == null && run.GetInstanceID() != oldId,
            "restart from " + reason + " destroys old run/entrance/boss");
        Check(run.IsRunning && !run.IsCompleted && !run.IsDefeated && !run.IsFinaleStarted
            && !run.IsBossPhase && run.RemainingBosses == 0 && run.ElapsedTime < 5f
            && !manager.IsFused && !manager.IsFinalFusion && !entrance.IsPlaying && flip.enabled && Time.timeScale == 1f,
            "restart from " + reason + " resets clock, terminal flags, fusion and flip");
    }

    void Bind()
    {
        run = InScene<RunStageController>().Single();
        manager = InScene<WorldManager>().Single(m => m.isActiveAndEnabled);
        // These links are internal in Assembly-CSharp: reflection only, never direct access.
        entrance = Property<FusionTransitionController>(manager, "FusionTransition");
        flip = Property<StateSwitchController>(manager, "SwitchFlow");
        worlds = InScene<World>().Where(w => w.Manager == manager).ToArray();
        spawners = Read<EnemySpawner[]>(run, "spawners");
        Require(run.isActiveAndEnabled && run.IsRunning && manager.IsInitialized && worlds.Length == 2
            && entrance != null && entrance.isActiveAndEnabled && flip != null && flip.isActiveAndEnabled
            && Property<RunStageController>(manager, "RunController") == run, "initialized runtime bindings");
        Require(run.UsesSharedWaves == (SceneManager.GetActiveScene().name == "DebugRun"),
            "configuration: Main useSharedWaves=false; DebugRun=true (never patched by fixture)");
        Require(Read<float>(run, "finaleStartTime") == 480f && Read<float>(run, "bossHealth") == 300f,
            "production finaleStartTime=480 and bossHealth=300 unchanged");
        var prefab = Read<EnemyController>(run, "bossPrefab");
        Require(prefab is TitanEnemyController && prefab.name == "RiftLordBoss", "dedicated RiftLordBoss prefab configured");
        Require(spawners.Length == 2 && spawners.All(s => s != null && s.enabled && s.gameObject.activeSelf),
            "both configured spawners remain enabled, including sleeping world");
        completedCount = spawnEvents = worldChanges = 0;
        completedFrame = spawnFrame = -1;
        boss = null;
        pauseOnCompleted = false;
        entrance.Completed += Completed;
        run.StateChanged += StateChanged;
        manager.WorldChanged += WorldChanged;
        ProtectFixture();
    }

    void Completed()
    {
        completedCount++;
        completedFrame = Time.frameCount;
        Check(run.RemainingBosses == 0 && !run.IsBossPhase && Enemies().Length == 0,
            "Completed callback frame has no boss");
        if (pauseOnCompleted) Time.timeScale = 0;
    }

    void StateChanged()
    {
        if (!run.IsBossPhase || run.RemainingBosses == 0 || spawnEvents != 0) return;
        spawnEvents++;
        spawnFrame = Time.frameCount;
        var enemies = Enemies();
        boss = enemies.Length == 1 ? enemies[0] : null;
        Check(completedCount == 1 && boss is TitanEnemyController && boss.name.StartsWith("RiftLordBoss")
            && boss.health == 300f && World.GetFor(boss) == manager.CurrentWorld,
            "spawn StateChanged: one RiftLordBoss 300HP in entry world, after Completed");
    }

    void WorldChanged() { worldChanges++; }
    EnemyController[] Enemies()
    {
        return worlds.SelectMany(w => w.ContentRoot.GetComponentsInChildren<EnemyController>(true)).ToArray();
    }

    void SetElapsed(float value)
    {
        Write(run, "<ElapsedTime>k__BackingField", value);
        Require(run.ElapsedTime == value, "reflection ElapsedTime fixture=" + value);
    }

    // No spawner/enemy/controller/presentation is disabled. Weapons alone are isolated so HP/death evidence is deterministic.
    void ProtectFixture()
    {
        foreach (var hp in InScene<PlayerHealth>())
        {
            if (!hp.HasInitialized || hp.IsDead) continue;
            var owner = Property<PlayerHealth>(hp, "HealthOwner");
            if (!health.ContainsKey(owner))
            {
                health.Add(owner, new Vector2(owner.currentHealth, owner.maxHealth));
                owner.maxHealth = owner.currentHealth = 1000000f;
            }
        }
        foreach (var weapon in InScene<Weapon>())
        {
            if (!weapons.ContainsKey(weapon)) weapons.Add(weapon, weapon.enabled);
            weapon.enabled = false;
        }
    }

    IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) { ProtectFixture(); yield return null; }
    }

    IEnumerator Until(Func<bool> ready, float seconds, string message)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            Require(Time.realtimeSinceStartup < deadline, "timeout: " + message);
            ProtectFixture();
            yield return null;
        }
    }

    static T[] InScene<T>() where T : Component
    {
        var scene = SceneManager.GetActiveScene();
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene == scene).ToArray();
    }
    static FieldInfo Field(object target, string name)
    {
        var field = target.GetType().GetField(name, Members);
        if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
        return field;
    }
    static T Read<T>(object target, string name) { return (T)Field(target, name).GetValue(target); }
    static void Write(object target, string name, object value) { Field(target, name).SetValue(target, value); }
    static T Property<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name, Members);
        if (property == null) throw new MissingMemberException(target.GetType().FullName, name);
        return (T)property.GetValue(target, null);
    }
    void Note(string text)
    {
        string line = "RUN_FINALE " + label + " " + text;
        Evidence.Add(line);
        Debug.Log(line);
    }
    void Check(bool ok, string message)
    {
        if (ok) Passed++; else Failed++;
        Note((ok ? "PASS " : "FAIL ") + message);
    }
    void Require(bool ok, string message)
    {
        if (!ok) { Check(false, message); throw new InvalidOperationException(message); }
    }
    void Unhook()
    {
        if (entrance != null) entrance.Completed -= Completed;
        if (run != null) run.StateChanged -= StateChanged;
        if (manager != null) manager.WorldChanged -= WorldChanged;
    }
    public void Dispose()
    {
        Unhook();
        foreach (var pair in guiStates) if (pair.Key != null) pair.Key.SetOpen(pair.Value);
        foreach (var pair in weapons) if (pair.Key != null) pair.Key.enabled = pair.Value;
        // Snapshot and restore each shared pool once, rather than each hero alias.
        foreach (var pair in health)
        {
            if (pair.Key == null || pair.Key.IsDead) continue;
            pair.Key.maxHealth = pair.Value.y;
            pair.Key.currentHealth = pair.Value.x;
        }
        Time.timeScale = savedScale;
    }
}
