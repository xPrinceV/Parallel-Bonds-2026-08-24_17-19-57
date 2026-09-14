using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Disposable Play Mode only: real serialized events and public gameplay entry points, never scene saves.
public static class MainUiPlayChecks
{
    public static int Passed, Failed;
    public static readonly List<string> Evidence = new List<string>();
    public static readonly List<string> CompletedScenarios = new List<string>();
    [Serializable] public sealed class ScenarioResult { public string name, status; public int passed, failed; }
    public static readonly List<ScenarioResult> Results = new List<ScenarioResult>();
    static int scenarioPassed, scenarioFailed;
    public static string CurrentScenario = "preflight";
    public static readonly string[] Scenarios = { "Menu layout/audio/capture", "PLAY and natural world switch/HUD", "Official fusion boss death/Victory/audio/capture", "VictoryRestart", "Lethal player damage/GameOver", "GameOver MainMenu/cleanup", "Second menu PLAY" };
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static RunStageController run;
    static WorldManager manager;
    static GameOverManager over;
    static GameObject victory;
    public static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Flags).GetValue(owner);
    public static T[] Find<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(c => c.gameObject.scene == SceneManager.GetActiveScene()).ToArray();
    public static void Note(string text) { Evidence.Add(text); Debug.Log("UI_NATIVE " + text); }
    public static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + text); }
    public static void Require(bool ok, string text) { Check(ok, text); if (!ok) throw new InvalidOperationException(text); }
    static void Begin(int index) { CurrentScenario = Scenarios[index]; scenarioPassed = Passed; scenarioFailed = Failed; Note("SCENARIO START " + CurrentScenario); }
    static void Done()
    {
        CompletedScenarios.Add(CurrentScenario);
        var result = new ScenarioResult { name = CurrentScenario, passed = Passed - scenarioPassed, failed = Failed - scenarioFailed,
            status = Failed == scenarioFailed ? "PASSED" : "FAILED" };
        Results.Add(result); Note("SCENARIO COMPLETE " + JsonUtility.ToJson(result));
    }
    public static int Subscriptions(object owner, string field, object target) => Get<Delegate>(owner, field)?.GetInvocationList().Count(d => ReferenceEquals(d.Target, target)) ?? 0;
    static IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
    static IEnumerator Until(Func<bool> ready, float seconds, string label)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
        Check(true, label);
    }
    public static void ResultButtons(GameOverManager target)
    {
        var win = Get<GameObject>(target, "victoryUI");
        Require(target.gameOverUI != null && win != null, "both result panels bound");
        foreach (var panel in new[] { target.gameOverUI, win })
        {
            var buttons = panel.GetComponentsInChildren<Button>(true);
            Check(buttons.Length == 3 && new[] { "Restart", "MainMenu", "Quit" }.All(method => buttons.Count(b =>
                b.onClick.GetPersistentEventCount() == 1 && b.onClick.GetPersistentTarget(0) == target
                && b.onClick.GetPersistentMethodName(0) == method
                && b.onClick.GetPersistentListenerState(0) != UnityEventCallState.Off) == 1), panel.name + " exactly three enabled persistent result callbacks");
        }
    }
    static Button ButtonFor(Object target, string method, GameObject panel = null)
    {
        var buttons = (panel == null ? Find<Button>() : panel.GetComponentsInChildren<Button>(true)).Where(b =>
            Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentTarget(i) == target
                && b.onClick.GetPersistentMethodName(i) == method)).ToArray();
        Require(buttons.Length == 1, "one callback within selected panel: " + method);
        var button = buttons[0];
        Require(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentListenerState(0) != UnityEventCallState.Off
            && button.IsActive() && button.IsInteractable(), "real active/interactable persistent button: " + button.name);
        return button;
    }
    static PointerEventData Raycast(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>();
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>();
        Require(EventSystem.current != null, "live EventSystem");
        var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left,
            clickCount = 1, eligibleForClick = true };
        EventSystem.current.RaycastAll(pointer, hits);
        string detail = string.Join(" | ", hits.Take(6).Select(h => Hierarchy(h.gameObject.transform)
            + "; receiver=" + (ExecuteEvents.GetEventHandler<IPointerClickHandler>(h.gameObject)?.name ?? "none")
            + "; text=" + (h.gameObject.GetComponent<TMP_Text>()?.text.Replace('\n', ' ') ?? "none")));
        Note("RAYCAST point=" + point + "; expected=" + Hierarchy(button.transform) + "; orderedHits=" + detail);
        // Do not bypass a real blocker merely because invoking onClick directly still navigates.
        Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            "button center raycast unobstructed: " + button.name + "; top=" + (hits.Count == 0 ? "none" : Hierarchy(hits[0].gameObject.transform)));
        if (hits.Count > 0) pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = hits[0];
        return pointer;
    }
    static string Hierarchy(Transform transform)
    { var names = new List<string>(); while (transform != null) { names.Add(transform.name); transform = transform.parent; } names.Reverse(); return string.Join("/", names); }
    static void Click(Object target, string method, GameObject panel = null)
    {
        if (method == "Quit" || method == "QuitGame") throw new InvalidOperationException("Quit is inspection-only in this disposable test");
        var button = ButtonFor(target, method, panel); var pointer = Raycast(button);
        var hit = pointer.pointerCurrentRaycast.gameObject;
        var receiver = hit == null ? null : ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
        Require(receiver == button.gameObject, "pointer dispatch receiver is actual first-hit button: " + method);
        Note("POINTER_CLICK left via EventSystem raycast + ExecuteEvents.pointerClickHandler; receiver=" + Hierarchy(receiver.transform) + "; persistent=" + method + "; no direct onClick.Invoke");
        Require(ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerClickHandler), "actual pointerClick handled: " + method);
        Check(Time.timeScale == 1, method + " immediately clears timeScale");
    }
    static IEnumerator Arrived(string scene, int previous)
    {
        yield return Until(() => SceneManager.GetActiveScene().name == scene && SceneManager.GetActiveScene().handle != previous
            && SceneManager.GetActiveScene().isLoaded, 10, "real navigation to " + scene);
        for (int i = 0; i < 4; i++) yield return null;
    }
    static void AudioOwnership(bool gameplay)
    {
        var services = Object.FindObjectsByType<AudioService>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Require(services.Length == 1 && services[0] == AudioService.Instance && services[0].isActiveAndEnabled, "exactly one live GameAudio service globally");
        var audio = services[0]; var events = audio.GetComponent<GameAudioEvents>();
        Check(sources.Length == 10 && sources.All(s => s.transform.IsChildOf(audio.transform)), "exactly ten owned native AudioSources globally");
        Require(events != null && events.isActiveAndEnabled, "production audio hook enabled");
        if (gameplay)
            Check(Get<RunStageController>(events, "run") == run && Get<WorldManager>(events, "worldManager") == manager
                && Subscriptions(run, "StateChanged", events) == 1 && Subscriptions(manager, "WorldChanged", events) == 1
                && Subscriptions(run, "StateChanged", over) == 1, "fresh UI/audio bound once to current run/world");
        else Check(Get<RunStageController>(events, "run") == null && Get<WorldManager>(events, "worldManager") == null, "menu audio has no stale gameplay bindings");
    }
    static AudioSource Music(SoundId id, bool loop)
    {
        var audio = AudioService.Instance; var target = Get<AudioSource>(audio, "musicTarget");
        Require(Get<AudioCatalog>(audio, "catalog").TryGet(id, out var entry), "catalog " + id + "=" + (int)id);
        Check(target != null && target.isPlaying && target.timeSamples > 0 && target.loop == loop
            && Get<AudioClip[]>(entry, "clips").Contains(target.clip) && Mathf.Abs(target.volume - entry.Volume) < .001f
            && Get<SoundId>(audio.GetComponent<GameAudioEvents>(), "currentMusic") == id, "actual native playback " + id);
        if (id == SoundId.MusicMenu)
            Check((int)id == 404 && entry.Volume == .4f && entry.Loop && target.clip.loadType == AudioClipLoadType.Streaming, "Menu404 volume.4 loop streaming");
        if (id == SoundId.Victory) Check((int)id == 403 && !entry.Loop && entry.PlayWhilePaused, "Victory403 one-shot while paused");
        return target;
    }
    static void Mesh(TMP_Text text, string label)
    {
        Require(text != null && text.font != null && text.isActiveAndEnabled, label + " active TMP font binding");
        text.ForceMeshUpdate(true, true);
        Check(text.textInfo.characterCount > 0 && text.textInfo.meshInfo.Any(m => m.vertexCount > 0), label + " nonempty native mesh");
        var characters = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c => !char.IsWhiteSpace(c.character)).ToArray();
        Check(characters.Length > 0 && characters.All(c => c.fontAsset == text.font && c.textElement != null
            && c.textElement.unicode == c.character), label + " every rendered character from primary font, no fallback/substitution");
    }
    static IEnumerator Capture(string name)
    {
        yield return Wait(.2f);
        string path = Path.Combine(MainUiPlayChecksBatch.Arg("-uiOutput"), name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return Until(() => File.Exists(path) && new FileInfo(path).Length > 1024, 8, "asynchronous GameView screenshot " + name);
        Note("CAPTURE " + path + "; Screen=" + Screen.width + "x" + Screen.height);
    }
    static void Bind()
    {
        run = Find<RunStageController>().Single(); manager = Find<WorldManager>().Single(); over = Find<GameOverManager>().Single(); victory = Get<GameObject>(over, "victoryUI");
        Require(run.IsRunning && !run.IsCompleted && !run.IsDefeated && !run.IsFinaleStarted && manager.IsInitialized && Time.timeScale == 1, "fresh Main running and unpaused");
        Check(!over.gameOverUI.activeSelf && !victory.activeSelf, "fresh Main hides both result panels");
        ResultButtons(over); AudioOwnership(true);
        Note("FIXTURE high shared HP=1000000; disabled weapons; public StopSpawning(true); no clock edits or manual state notification");
        foreach (var weapon in Find<Weapon>()) weapon.enabled = false;
        foreach (var spawner in Find<EnemySpawner>()) spawner.StopSpawning(true);
        foreach (var world in Find<World>()) { var hp = world.Player.GetComponent<PlayerHealth>(); hp.maxHealth = hp.currentHealth = 1000000; }
    }
    sealed class Outgoing
    {
        readonly AudioService audio = AudioService.Instance;
        readonly GameAudioEvents events = AudioService.Instance.GetComponent<GameAudioEvents>();
        readonly AudioSource[] sources = AudioService.Instance.GetComponentsInChildren<AudioSource>(true);
        readonly RunStageController oldRun = Find<RunStageController>().SingleOrDefault();
        readonly WorldManager oldWorld = Find<WorldManager>().SingleOrDefault();
        readonly GameOverManager oldOver = Find<GameOverManager>().SingleOrDefault();
        public void CheckGone()
        {
            Check(audio == null && events == null && sources.Length == 10 && sources.All(s => s == null)
                && oldRun == null && oldWorld == null && oldOver == null, "all outgoing audio sources/hooks/run/world/UI destroyed");
            // Scene unload can destroy publishers before subscribers' OnDisable. These wrappers are
            // held only by this observer: inspect live publishers for leaks, and report dead lists separately.
            if (!ReferenceEquals(oldRun, null)) Note("DESTROYED_PUBLISHER retained run delegates: audio=" + Subscriptions(oldRun, "StateChanged", events) + "; UI=" + Subscriptions(oldRun, "StateChanged", oldOver));
            if (!ReferenceEquals(oldWorld, null)) Note("DESTROYED_PUBLISHER retained world audio delegates=" + Subscriptions(oldWorld, "WorldChanged", events));
            Check(Object.FindObjectsByType<RunStageController>(FindObjectsInactive.Include).All(r => Subscriptions(r, "StateChanged", events) == 0
                && Subscriptions(r, "StateChanged", oldOver) == 0), "no live run retains outgoing UI/audio subscriptions");
            Check(Object.FindObjectsByType<WorldManager>(FindObjectsInactive.Include).All(w => Subscriptions(w, "WorldChanged", events) == 0), "no live world retains outgoing audio subscriptions");
        }
    }
    public static IEnumerator Run()
    {
        Application.runInBackground = true;
        Require(Application.isPlaying && SceneManager.GetActiveScene().name == "Main Menu" && !Application.isBatchMode
            && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "native Menu entry with real graphics/audio");
        Begin(0); yield return Wait(.7f); AudioOwnership(false); Music(SoundId.MusicMenu, true);
        var decorations = Find<Image>().Where(i => i.GetComponent<Button>() == null).ToArray();
        Check(decorations.Length > 0 && decorations.All(i => !i.raycastTarget), "all menu decorative Images non-raycast");
        foreach (var button in Find<Button>().Where(b => b.IsActive())) Raycast(button);
        ButtonFor(Find<MainMenu>().Single(), "QuitGame");
        Note("QUIT inspection only: first-hit pointer receiver and single enabled MainMenu.QuitGame persistent callback checked; no Quit event dispatched");
        yield return Capture("menu"); Done();

        Begin(1); var outgoing = new Outgoing(); int previous = SceneManager.GetActiveScene().handle;
        Click(Find<MainMenu>().Single(), "PlayGame"); yield return Arrived("Main", previous); outgoing.CheckGone(); Bind();
        var timer = Find<StateSwitchController>().Single(t => t.isActiveAndEnabled && Get<WorldManager>(t, "worldManager") == manager);
        Require(timer.SwitchInterval == 30 && Get<float>(timer, "warningDuration") == 5 && Get<float>(timer, "flipDuration") == .8f
            && run.FinaleStartTime == 480 && timer.AutomaticSwitchingEnabled, "live automatic 30/5/.8/480 timing");
        int changes = 0, midpoints = 0, warnings = 0; float elapsed = 0, progress = 0; float start = Time.time; float realStart = Time.realtimeSinceStartup;
        WorldId initial = manager.CurrentWorldId; string hud = UIController.instance.timeText.text;
        Mesh(UIController.instance.timeText, "initial HUD");
        Action changed = () => { changes++; elapsed = Time.time - start; };
        Action midpoint = () => { midpoints++; progress = timer.FlipProgress; };
        manager.WorldChanged += changed; timer.TransitionMidpoint += midpoint;
        try
        {
            float end = Time.realtimeSinceStartup + 37;
            while (changes == 0) { if (Time.realtimeSinceStartup > end) throw new TimeoutException("natural 30s switch"); if (timer.WarningProgress > 0) warnings++; yield return null; }
            yield return Until(() => !timer.IsFlipping, 3, "natural flip settled");
            Check(changes == 1 && midpoints == 1 && progress == .5f && warnings > 0 && elapsed >= 29.5f && elapsed < 30.5f
                && manager.CurrentWorldId != initial, "one natural 30s world commit; scaled=" + elapsed + "; real=" + (Time.realtimeSinceStartup - realStart) + "; warningFrames=" + warnings);
        }
        finally { manager.WorldChanged -= changed; timer.TransitionMidpoint -= midpoint; }
        Check(Find<World>().Count(w => w.IsActive) == 1 && PlayerController.instance == manager.CurrentWorld.Player
            && UIController.instance.timeText.text != hud, "active player/world/HUD advance together");
        Mesh(UIController.instance.timeText, "post-switch HUD"); yield return Wait(.7f);
        Music(manager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho, true); Done();

        Begin(2); var winProbe = victory.AddComponent<MainUiPanelProbe>(); var loseProbe = over.gameOverUI.AddComponent<MainUiPanelProbe>();
        Require(run.TryStartFinale(), "official TryStartFinale accepted before 480s without clock modification");
        Check(manager.IsFinalFusion && manager.GetComponent<FusionTransitionController>().IsPlaying && run.RemainingBosses == 0, "real fusion entrance before boss spawn");
        yield return Until(() => run.IsBossPhase || run.IsDefeated, 15, "real finale entrance completed");
        var bosses = Get<HashSet<EnemyController>>(run, "bosses").ToArray();
        Require(run.IsBossPhase && bosses.Length == 1 && bosses[0] is TitanEnemyController && bosses[0].health > 0, "one actual living finale Titan");
        int completions = 0; Action completed = () => { if (run.IsCompleted) completions++; }; run.StateChanged += completed;
        try
        {
            bosses[0].TakeDamage(bosses[0].health + 1);
            yield return Until(() => run.IsCompleted || run.IsDefeated, 5, "real boss lethal damage resolves run");
            Check(run.IsCompleted && !run.IsDefeated && !run.IsRunning && run.RemainingBosses == 0 && Time.timeScale == 0
                && victory.activeInHierarchy && !over.gameOverUI.activeSelf && winProbe.Enables == 1 && loseProbe.Enables == 0, "completed paused Victory only, displayed once");
            yield return Wait(.7f); var cue = Music(SoundId.Victory, false); int samples = cue.timeSamples;
            yield return Wait(.25f);
            Check(completions == 1 && winProbe.Enables == 1 && cue == Get<AudioSource>(AudioService.Instance, "musicTarget") && cue.timeSamples > samples,
                "one completion notification / one Victory activation / continuous one-shot samples (not synthetic request counts)");
            var texts = victory.GetComponentsInChildren<TMP_Text>().Where(t => !string.IsNullOrWhiteSpace(t.text)).ToArray();
            Require(texts.Length > 0, "Victory has rendered TMP text"); foreach (var text in texts) Mesh(text, "Victory/" + text.name);
            yield return Capture("victory");
        }
        finally { run.StateChanged -= completed; }
        Done();

        Begin(3); outgoing = new Outgoing(); previous = SceneManager.GetActiveScene().handle;
        Click(over, "Restart", victory); yield return Arrived("Main", previous); outgoing.CheckGone(); Bind();
        Mesh(UIController.instance.timeText, "restarted HUD"); yield return Wait(.7f); Music(manager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho, true); Done();

        Begin(4); winProbe = victory.AddComponent<MainUiPanelProbe>(); loseProbe = over.gameOverUI.AddComponent<MainUiPanelProbe>();
        var health = PlayerHealth.instance; int defeats = 0; Action defeated = () => { if (run.IsDefeated) defeats++; }; run.StateChanged += defeated;
        try
        {
            health.DamageHandler(health.currentHealth + 1);
            yield return Until(() => run.IsDefeated, 5, "production PlayerHealth lethal damage defeated run");
            health.DamageHandler(1); yield return Wait(.7f);
            Check(health.IsDead && Find<World>().All(w => w.Player.GetComponent<PlayerHealth>().IsDead) && defeats == 1 && loseProbe.Enables == 1
                && winProbe.Enables == 0 && over.gameOverUI.activeInHierarchy && !victory.activeSelf && !run.IsCompleted && Time.timeScale == 0, "shared lethal damage / repeat hit: GameOver only once");
            Check(Get<AudioSource>(AudioService.Instance, "musicTarget") == null, "defeat stops music target");
        }
        finally { run.StateChanged -= defeated; }
        Done();

        Begin(5); outgoing = new Outgoing(); previous = SceneManager.GetActiveScene().handle;
        Click(over, "MainMenu", over.gameOverUI); yield return Arrived("Main Menu", previous); outgoing.CheckGone();
        yield return Wait(.7f); AudioOwnership(false); Music(SoundId.MusicMenu, true); Done();
        Begin(6); outgoing = new Outgoing(); previous = SceneManager.GetActiveScene().handle;
        Click(Find<MainMenu>().Single(), "PlayGame"); yield return Arrived("Main", previous); outgoing.CheckGone(); Bind();
        yield return Wait(.7f); Music(manager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho, true);
        Mesh(UIController.instance.timeText, "second menu PLAY HUD"); Done();
    }
}
public sealed class MainUiPanelProbe : MonoBehaviour { public int Enables; void OnEnable() { Enables++; } }
