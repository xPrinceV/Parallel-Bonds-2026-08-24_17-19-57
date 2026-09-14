using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Requires a DISPOSABLE Play session and enabled Main Menu/Main/DebugRun build scenes.
// Run on a temporary DontDestroyOnLoad coroutine host; caller owns host/session teardown.
// Loads replace scenes irreversibly. No asset/PlayerSettings edits, Quit calls, or pointer simulation.
public sealed class MenuFlowChecks : IDisposable
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }
    public static readonly List<string> Evidence = new List<string>();
    readonly Dictionary<Weapon, bool> weapons = new Dictionary<Weapon, bool>();
    RunStageController run;
    GameOverManager over;
    PlayerHealth protectedHealth;
    Vector2 savedHealth;
    float savedScale;

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("MenuFlowChecks already running");
        Running = true;
        Passed = Failed = 0;
        Evidence.Clear();
        var test = new MenuFlowChecks { savedScale = Time.timeScale };
        try { yield return test.Execute(); }
        finally { test.Dispose(); Running = false; }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "disposable live Play session required");
        foreach (string scene in new[] { "Main Menu", "Main", "DebugRun" })
            Require(Application.CanStreamedLevelBeLoaded(scene), "enabled build scene: " + scene);
        Note("FIXTURE: real persistent button callbacks; early public finale entry, not an eight-minute soak. Cancel warning expected. Victory uses boosted shared HP and disabled weapons.");
        yield return Load("Main Menu");
        Time.timeScale = 0;
        yield return Play();
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            if (scene == "DebugRun") yield return Load(scene);
            BindReady();
            yield return Defeat();
            yield return Restart(scene);
            yield return Defeat();
            var oldRun = run;
            int previous = SceneManager.GetActiveScene().handle;
            Click(over, nameof(GameOverManager.MainMenu));
            Check(Time.timeScale == 1, "menu callback immediately releases defeat pause");
            yield return Arrived("Main Menu", previous);
            Check(oldRun == null && Time.timeScale == 1, scene + " defeat -> unpaused menu");
            yield return Play();
            BindReady();
        }

        Require(run.TryStartFinale(), "start production finale for cancellation");
        var entrance = One<FusionTransitionController>();
        Require(entrance.IsPlaying, "cancel an unfinished live entrance");
        entrance.Cancel();
        yield return Until(() => run.IsDefeated, "cancelled finale defeat");
        Check(Shown() && !run.IsCompleted && !run.IsBossPhase && run.RemainingBosses == 0
            && Time.timeScale == 0, "cancelled fusion shows GameOver, not victory");
        yield return Restart("Main");

        // Isolate the real boss death from incidental weapon kills/contact damage.
        protectedHealth = PlayerHealth.instance;
        savedHealth = new Vector2(protectedHealth.currentHealth, protectedHealth.maxHealth);
        protectedHealth.maxHealth = protectedHealth.currentHealth = 1000000;
        foreach (var weapon in InScene<Weapon>())
        {
            weapons.Add(weapon, weapon.enabled);
            weapon.enabled = false;
        }
        Require(run.TryStartFinale(), "start production finale for victory");
        yield return Until(() => run.IsBossPhase || run.IsDefeated, "real entrance completion and boss spawn");
        Require(run.IsBossPhase && run.RemainingBosses == 1, "production spawned one finale boss");
        var boss = One<EnemyController>();
        Require(boss.isActiveAndEnabled && boss.health > 0, "living production boss");
        boss.TakeDamage(boss.health + 1);
        yield return Until(() => run.IsCompleted || run.IsDefeated, "boss death resolves run");
        Check(run.IsCompleted && !run.IsDefeated && !run.IsRunning && Time.timeScale == 0
            && !over.gameOverUI.activeSelf
            && (typeof(GameOverManager).GetField("victoryUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(over) as GameObject).activeInHierarchy,
            "production boss victory shows Victory and does NOT show GameOver");
    }

    IEnumerator Play()
    {
        int previous = SceneManager.GetActiveScene().handle;
        Click(One<MainMenu>(), nameof(MainMenu.PlayGame));
        Check(Time.timeScale == 1, "PlayGame immediately resets timeScale to 1");
        yield return Arrived("Main", previous);
        Check(Time.timeScale == 1, "PlayGame loads Main without a lingering pause");
    }

    IEnumerator Defeat()
    {
        var hp = PlayerHealth.instance;
        Require(hp != null && hp.isActiveAndEnabled && !hp.IsDead, "active shared player before lethal damage");
        hp.DamageHandler(hp.currentHealth + 1);
        yield return Until(() => run.IsDefeated, "DamageHandler defeat");
        Check(hp.IsDead && InScene<World>().All(w => w.Player.GetComponent<PlayerHealth>().IsDead)
            && !run.IsRunning && !run.IsCompleted && Time.timeScale == 0 && Shown(),
            "shared player death -> defeated, paused, active GameOver panel");
    }

    IEnumerator Restart(string scene)
    {
        var oldRun = run;
        int previous = SceneManager.GetActiveScene().handle;
        Click(over, nameof(GameOverManager.Restart));
        Check(Time.timeScale == 1, "Restart callback immediately releases defeat pause");
        yield return Arrived(scene, previous);
        Check(oldRun == null, "Restart replaces the old run in " + scene);
        BindReady();
    }

    void BindReady()
    {
        run = One<RunStageController>();
        over = One<GameOverManager>();
        var manager = One<WorldManager>();
        Require(run.isActiveAndEnabled && manager.isActiveAndEnabled && manager.IsInitialized
            && run.IsRunning && !run.IsDefeated && !run.IsCompleted && !run.IsFinaleStarted,
            "fresh initialized run: " + SceneManager.GetActiveScene().name);
        Require(over.isActiveAndEnabled && over.gameOverUI != null
            && typeof(GameOverManager).GetField("runController", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(over) as RunStageController == run, "enabled GameOverManager bound to current run");
        Check(World.GetFor(over) == null && World.GetFor(over.gameOverUI.transform) == null,
            "manager and panel remain outside world-scoped roots");
        var worlds = InScene<World>();
        Check(worlds.Length == 2 && worlds.All(w => w.Player != null
            && w.Player.GetComponent<PlayerHealth>().HasInitialized && !w.Player.GetComponent<PlayerHealth>().IsDead
            && w.Player.GetComponent<PlayerHealth>().currentHealth > 0)
            && PlayerHealth.instance != null && Time.timeScale == 1, "ready living shared players at timeScale 1");
        var victory = typeof(GameOverManager).GetField("victoryUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(over) as GameObject;
        Require(victory != null, "bound Victory panel");
        foreach (var panel in new[] { over.gameOverUI, victory })
        {
            var buttons = panel.GetComponentsInChildren<Button>(true);
            Check(buttons.Length == 3 && new[] { "Restart", "MainMenu", "Quit" }.All(method => buttons.Count(b =>
                b.onClick.GetPersistentEventCount() == 1 && b.onClick.GetPersistentTarget(0) == over
                && b.onClick.GetPersistentMethodName(0) == method
                && b.onClick.GetPersistentListenerState(0) != UnityEventCallState.Off) == 1), panel.name + " has three bound result buttons");
        }
        Check(!over.gameOverUI.activeSelf && !victory.activeSelf, "fresh run hides both result panels");
    }

    bool Shown() => over.isActiveAndEnabled && over.gameOverUI.activeSelf && over.gameOverUI.activeInHierarchy;

    void Click(Object target, string method)
    {
        // Restart/MainMenu/Quit occur once per result panel, not once per scene.
        var matches = InScene<Button>().Where(b => b.IsActive() && Enumerable.Range(0, b.onClick.GetPersistentEventCount())
            .Any(i => b.onClick.GetPersistentTarget(i) == target && b.onClick.GetPersistentMethodName(i) == method)).ToArray();
        Require(matches.Length == 1, "unique imported button for " + target.GetType().Name + "." + method);
        var button = matches[0];
        Require(button.onClick.GetPersistentEventCount() == 1
            && button.onClick.GetPersistentListenerState(0) != UnityEventCallState.Off,
            "single enabled persistent callback (no accidental Quit/extra calls)");
        Require(button.IsActive() && button.IsInteractable(), "button active/interactable: " + method);
        if (target is GameOverManager gameOver)
        {
            var victory = typeof(GameOverManager).GetField("victoryUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(gameOver) as GameObject;
            var panel = gameOver.gameOverUI.activeInHierarchy ? gameOver.gameOverUI : victory;
            Require(panel != null && panel.activeInHierarchy && button.transform.IsChildOf(panel.transform), "button belongs to visible bound result panel");
        }
        button.onClick.Invoke();
    }

    IEnumerator Load(string scene)
    {
        int previous = SceneManager.GetActiveScene().handle;
        Time.timeScale = 1;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
        yield return Arrived(scene, previous);
    }

    IEnumerator Arrived(string scene, int previous)
    {
        yield return Until(() => SceneManager.GetActiveScene().name == scene
            && SceneManager.GetActiveScene().handle != previous && SceneManager.GetActiveScene().isLoaded, "load " + scene);
        for (int i = 0; i < 3; i++) yield return null; // Allow Awake/Start and outgoing LateUpdate to settle.
    }

    IEnumerator Until(Func<bool> ready, string message)
    {
        float end = Time.realtimeSinceStartup + 15;
        while (!ready()) { Require(Time.realtimeSinceStartup < end, "timeout: " + message); yield return null; }
    }

    static T[] InScene<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(c => c.gameObject.scene == SceneManager.GetActiveScene()).ToArray();
    T One<T>() where T : Component
    {
        var found = InScene<T>();
        Require(found.Length == 1, "exactly one scene " + typeof(T).Name);
        return found[0];
    }
    void Note(string message) { Evidence.Add(message); Debug.Log("MENU_FLOW " + SceneManager.GetActiveScene().name + " " + message); }
    void Check(bool ok, string message) { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + message); }
    void Require(bool ok, string message) { if (!ok) { Check(false, message); throw new InvalidOperationException(message); } }

    public void Dispose()
    {
        foreach (var pair in weapons) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (protectedHealth != null && !protectedHealth.IsDead)
        {
            protectedHealth.maxHealth = savedHealth.y;
            protectedHealth.currentHealth = savedHealth.x;
        }
        Time.timeScale = savedScale;
        // Defeat/victory and scene loads cannot be rolled back; caller must end the disposable session.
    }
}
