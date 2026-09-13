using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Compiled in memory by DebugRunChecksBatch; never imported into the source project's Assets.
public static class DebugRunChecks
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static RunStageController run;
    static readonly Dictionary<int, string> automatic = new Dictionary<int, string>();
    static int upgrades;
    static T[] All<T>() where T : Object { return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    static string RunState() { return "id=" + run.GetInstanceID() + ",stage=" + run.CurrentStageIndex + ",running=" + run.IsRunning + ",complete=" + run.IsCompleted + ",defeated=" + run.IsDefeated + ",scale=" + Time.timeScale + ",remaining=" + run.RemainingStageTime; }
    static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Debug.Log("DEBUG_RUN " + (ok ? "PASS " : "FAIL ") + text); }
    static Button Button(string name) { return All<RunStagePanel>().Single().GetComponentsInChildren<Button>(true).Single(b => b.name == name); }
    static void OpenDeveloperGui()
    {
        var gui = All<DeveloperDebugGui>().Single();
        Check(!gui.IsOpen && gui.gameObject.activeInHierarchy && !Field<Canvas>(gui, "canvas").enabled
            && !Field<GraphicRaycaster>(gui, "raycaster").enabled, "startup/reload developer GUI hidden with active root");
        gui.SetOpen(true);
        Check(gui.IsOpen && Field<Canvas>(gui, "canvas").enabled && Field<GraphicRaycaster>(gui, "raycaster").enabled, "explicitly open developer GUI for UI checks");
    }
    static void RestartPointer(string state, bool click)
    {
        Canvas.ForceUpdateCanvases();
        var button = Button("Restart");
        var rect = (RectTransform)button.transform;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        bool reachable = button.IsActive() && button.IsInteractable() && point.x >= 0 && point.y >= 0
            && point.x < Screen.width && point.y < Screen.height && hits.Count > 0
            && hits[0].gameObject.GetComponentInParent<Button>() == button;
        Check(reachable, "Restart pointer reachable during " + state + "; point=" + point + "; top="
            + (hits.Count == 0 ? "none" : hits[0].gameObject.name) + "; interactable=" + button.IsInteractable());
        if (click && reachable)
        {
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Debug.Log("DEBUG_RUN POINTER Restart down/up/click delivered during " + state);
        }
    }
    static void Upgrade()
    {
        var ui = UIController.instance;
        if (ui != null && ui.levelUpPanel.activeInHierarchy)
        {
            RestartPointer("upgrade", false);
            var button = ui.levelUpPanel.GetComponentsInChildren<Button>().FirstOrDefault(b => b.interactable);
            if (button == null) throw new Exception("Upgrade panel has no usable Button");
            button.onClick.Invoke(); upgrades++;
            Check(!ui.levelUpPanel.activeSelf && Time.timeScale > 0, "upgrade real Button.onClick resumes play");
        }
    }
    static string Snapshot()
    {
        return string.Join("|", All<World>().OrderBy(w => w.WorldId).Select(w => {
            var s = w.ContentRoot.GetComponentInChildren<EnemySpawner>(true);
            bool spawning = Field<bool>(s, "isSpawning");
            var enemies = w.ContentRoot.GetComponentsInChildren<EnemyController>(true).Where(e => e.gameObject.activeSelf).ToArray();
            return w.WorldId + ":external=" + s.UsesExternalStages + ",spawning=" + spawning
                + ",phase=" + (spawning ? Field<int>(s, "currentWave") : -1)

                + ",enemies=" + enemies.Length + ",titans=" + enemies.Count(e => e is TitanEnemyController);
        }).ToArray()) + "|bosses=" + run.RemainingBosses;
    }
    static void ClickStage(string name, int stage)
    {
        string captured = null;
        Action capture = () => captured = Snapshot();
        run.StateChanged += capture;
        Button(name).onClick.Invoke();
        run.StateChanged -= capture;
        Check(run.CurrentStageIndex == stage && captured == automatic[stage], name + " onClick matches automatic encounter: " + captured + "; expected=" + automatic[stage]);
    }
    static void Raycasts()
    {
        Canvas.ForceUpdateCanvases();
        Check(All<EventSystem>().Count(e => e.isActiveAndEnabled) == 1 && EventSystem.current.currentInputModule != null, "one active EventSystem with input module");
        foreach (var b in All<RunStagePanel>().Single().GetComponentsInChildren<Button>(true))
        {
            var rt = (RectTransform)b.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, results);
            Check(point.x >= 0 && point.y >= 0 && point.x < Screen.width && point.y < Screen.height
                && results.Count > 0 && results[0].gameObject.GetComponentInParent<Button>() == b,
                "layout/top raycast " + b.name + " at " + point + " screen=" + Screen.width + "x" + Screen.height
                + " hit=" + (results.Count == 0 ? "none" : results[0].gameObject.name));
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                Check(b.onClick.GetPersistentTarget(i) != null && !string.IsNullOrEmpty(b.onClick.GetPersistentMethodName(i)), "persistent callback " + b.name);
        }
        var panel = All<RunStagePanel>().Single();
        Check(Field<RunStageController>(panel, "runController") == run && Field<UIController>(panel, "uiController") == UIController.instance
            && Field<TMPro.TMP_Text>(panel, "statusText") != null && Field<Button[]>(panel, "stageButtons").Length == 4
            && Field<Button>(panel, "nextButton") == Button("Next") && Field<Button>(panel, "restartButton") == Button("Restart"), "serialized panel references; listeners are runtime Awake bindings");
    }
    public static IEnumerator Run()
    {
        Passed = Failed = upgrades = 0;
        run = All<RunStageController>().Single();
        var manager = All<WorldManager>().Single();
        var initialWorld = manager.CurrentWorldId;
        float initialMax = PlayerHealth.instance.maxHealth;
        Check(run.IsRunning && run.CurrentStageIndex == 0, "initial Wave 1");
        Check(Field<float[]>(run, "stageDurations").SequenceEqual(new float[] {20,20,20}), "serialized natural 20+20+20 durations unchanged");
        // Controlled survival fixture, not an untouched full gameplay run. No timer edits or stage API calls here.
        PlayerHealth.instance.maxHealth = 100000;
        PlayerHealth.instance.currentHealth = 100000;
        Debug.Log("DEBUG_RUN FIXTURE controlled shared HP=100000; natural timers; no test TryEnterStage/Advance until boss");
        // Normalize only world encounter counts and phase, not frame-dependent countdowns.
        automatic[0] = Snapshot();
        var times = new List<float>();
        float start = Time.time - (20 - run.RemainingStageTime);
        Action natural = () => { automatic[run.CurrentStageIndex] = Snapshot(); times.Add(Time.time - start); Debug.Log("DEBUG_RUN AUTO stage=" + run.CurrentStageIndex + " scaledElapsed=" + (Time.time-start) + " " + Snapshot()); };
        run.StateChanged += natural;
        OpenDeveloperGui();
        // Newly enabled UI graphics register for raycasting on the next canvas/player update.
        yield return null; yield return null;
        Raycasts();
        bool exercised = false;
        float deadline = Time.realtimeSinceStartup + 95;
        while (run.CurrentStageIndex < 3 && !run.IsDefeated && Time.realtimeSinceStartup < deadline)
        {
            Upgrade();
            if (!exercised && run.RemainingStageTime < 17)
            {
                var other = All<World>().Single(w => w.WorldId != manager.CurrentWorldId);
                Check(manager.SwitchWorld(other.WorldId), "switch during natural wave");
                Check(manager.TryEnterFusion(), "fusion during natural wave");
                // Let the newly created fusion hero run Start and select weapons before requesting an upgrade.
                yield return null; yield return null;
                var xp = ExperienceLevelController.instance;
                int level = xp.currentLevels;
                xp.GetExp(xp.expLevels[level]);
                Check(xp.currentLevels == level + 1, "wave fusion XP level-up");
                Upgrade();
                Check(manager.TryExitFusion(), "exit fusion during natural wave");
                exercised = true;
            }
            yield return null;
        }
        run.StateChanged -= natural;
        Check(run.CurrentStageIndex == 3 && times.Count == 3 && Math.Abs(times[0]-20) < .5f && Math.Abs(times[1]-40) < .5f && Math.Abs(times[2]-60) < .5f,
            "natural progression elapsed=" + string.Join(",", times.Select(t => t.ToString("F3")).ToArray()) + "; upgrade clicks=" + upgrades);
        Check(upgrades > 0, "natural path actually resumed at least one upgrade through Button.onClick (XP injection fixture)");
        if (run.CurrentStageIndex != 3) throw new Exception("Natural path failed; remaining checks require boss");
        Upgrade();
        Check(run.RemainingBosses == 2, "natural boss creates two Rift Lords");
        var bossPrefab = Field<EnemyController>(run, "bossPrefab");
        var bossBodies = All<TitanEnemyController>().Select(b => b.transform.Find("Monster_3_0").GetComponent<SpriteRenderer>()).ToArray();
        Check(bossPrefab != null && bossPrefab.name == "RiftLordBoss" && bossBodies.Length == 2
            && bossBodies.All(body => body.sprite != null && body.sprite.name == "Rift Lord_0"
                && body.sprite.texture.filterMode == FilterMode.Point), "both world bosses use the dedicated pixel-filtered Rift Lord sprite");
        string before = Snapshot();
        Button("Boss").onClick.Invoke();
        Check(!run.TryEnterStage(3) && Snapshot() == before, "repeat current API and Boss onClick do not duplicate");
        Check(!run.TryEnterStage(-1) && !run.TryEnterStage(4) && Snapshot() == before, "invalid stage rejected without mutation");
        Time.timeScale = 0;
        foreach (string label in new[] { "Wave 1", "Wave 2", "Wave 3", "Boss", "Next" }) Button(label).onClick.Invoke();
        Check(!run.TryEnterStage(0) && Snapshot() == before, "paused API and buttons rejected");
        Time.timeScale = 1;
        Check(manager.TryEnterFusion(), "fusion during boss");
        var bossXp = ExperienceLevelController.instance;
        int oldLevel = bossXp.currentLevels;
        bossXp.GetExp(bossXp.expLevels[oldLevel]);
        if (UIController.instance.levelUpPanel.activeInHierarchy)
        {
            string upgradeState = Snapshot();
            foreach (string label in new[] { "Wave 1", "Wave 2", "Wave 3", "Boss", "Next" }) Button(label).onClick.Invoke();
            Check(!run.TryEnterStage(0) && Snapshot() == upgradeState, "upgrade pause rejects API and all stage buttons");
        }
        Upgrade();
        Check(bossXp.currentLevels == oldLevel + 1, "boss fusion XP upgrade");
        Check(manager.TryExitFusion(), "exit fusion during boss");
        var bossOther = All<World>().Single(w => w.WorldId != manager.CurrentWorldId);
        Check(manager.SwitchWorld(bossOther.WorldId), "switch during boss");
        Upgrade();
        // Seed a sleeping-world attack to prove inactive hierarchy cleanup, not only active searches.
        var sleeping = All<World>().Single(w => w.WorldId != manager.CurrentWorldId);
        var attack = new GameObject("DebugRun sleeping attack fixture");
        attack.transform.SetParent(sleeping.ContentRoot); attack.AddComponent<HollowProjectile>();
        var oldEnemies = All<EnemyController>();
        PlayerHealth.instance.currentHealth = 12345;
        var holder = PlayerController.instance.GetComponent<BuffController>();
        var recipe = ScriptableObject.CreateInstance<BuffDefinition>();
        Set(recipe, "duration", 60f);
        var atom = new DamageBuffAtom(); Set(atom, "damageMultiplier", 2f);
        Set(recipe, "atoms", new List<BuffAtom> { atom });
        var grant = holder.GrantBuff(recipe, new object());
        Check(grant != null && grant.IsActive, "live timed damage buff fixture granted");
        var buffInstance = (BuffInstance)typeof(BuffHandle).GetProperty("Instance", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(grant, null);
        float buffTime = buffInstance.RemainingDuration;
        float buffDamage = holder.CalculateWeaponDamage(10);
        ClickStage("Wave 1", 0);
        Check(PlayerHealth.instance.currentHealth == 12345, "backward jump does not heal");
        Check(grant.IsActive && buffInstance.RemainingDuration == buffTime && holder.CalculateWeaponDamage(10) == buffDamage, "backward jump preserves live buff handle, remaining duration and damage effect");
        yield return null;
        Check(attack == null && oldEnemies.All(e => e == null), "backward jump clears sleeping enemies and attack");
        Upgrade();
        yield return null; Canvas.ForceUpdateCanvases();
        var pointerNext = Button("Next");
        var pointerRect = (RectTransform)pointerNext.transform;
        var livePointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, pointerRect.TransformPoint(pointerRect.rect.center)), button = PointerEventData.InputButton.Left };
        var liveHits = new List<RaycastResult>(); EventSystem.current.RaycastAll(livePointer, liveHits);
        bool nextHit = liveHits.Count > 0 && liveHits[0].gameObject.GetComponentInParent<Button>() == pointerNext;
        if (nextHit)
        {
            ExecuteEvents.Execute(pointerNext.gameObject, livePointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(pointerNext.gameObject, livePointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(pointerNext.gameObject, livePointer, ExecuteEvents.pointerClickHandler);
        }
        Check(nextHit && run.CurrentStageIndex == 1, "running Next accepts raycast-coordinate pointer down/up/click at " + livePointer.position);
        ClickStage("Wave 1", 0); ClickStage("Wave 2", 1); ClickStage("Wave 3", 2); ClickStage("Boss", 3);
        ClickStage("Wave 1", 0); ClickStage("Next", 1); ClickStage("Next", 2); ClickStage("Next", 3);
        yield return null;
        Upgrade(); Raycasts();
        Check(manager.TryEnterFusion(), "activate both bosses for lethal damage events");
        int deaths = 0;
        var titans = All<TitanEnemyController>();
        Check(titans.Length == 2, "exactly two bosses before lethal hits");
        foreach (var titan in titans) titan.Died += e => deaths++;
        var bossWorlds = titans.Select(t => World.GetFor(t)).ToArray();
        var xpBeforeDeath = bossWorlds.Select(w => w.ContentRoot.GetComponentsInChildren<ExpPickup>(true).Length).ToArray();
        titans[0].TakeDamage(titans[0].health + 1);
        Check(deaths == 1 && run.RemainingBosses == 1 && !run.IsCompleted, "first real boss death does not complete");
        titans[1].TakeDamage(titans[1].health + 1);
        Check(deaths == 2 && run.RemainingBosses == 0, "second real boss Died event removes final boss");
        Check(bossWorlds.Select((w,i) => w.ContentRoot.GetComponentsInChildren<ExpPickup>(true).Length == xpBeforeDeath[i] + 1).All(v => v), "both boss deaths create source-world XP pickups");
        yield return null; yield return null;
        Check(run.IsCompleted && !run.IsRunning && Time.timeScale == 0 && run.CurrentStageIndex == 3, "both deaths complete and hold final stage");
        Time.timeScale = 1; yield return null; yield return null;
        Check(Time.timeScale == 0 && !run.TryEnterStage(0), "completed run resists resume and stage entry");
        Check(manager.IsFused && manager.isActiveAndEnabled && run.IsCompleted, "Restart begins from fused completion with live manager");
        int oldId = run.GetInstanceID();
        RestartPointer("complete", true);
        Check(!manager.enabled && !manager.IsFused && !run.enabled && Time.timeScale == 1,
            "Restart ends fusion and disables outgoing manager/controller before unload");
        yield return null; yield return null; yield return null;
        run = All<RunStageController>().Single(); manager = All<WorldManager>().Single();
        OpenDeveloperGui();
        Check(run.GetInstanceID() != oldId && run.CurrentStageIndex == 0 && run.IsRunning && !run.IsCompleted && Time.timeScale == 1,
            "Restart onClick reloads scene and first stage/timeScale; oldId=" + oldId + "; " + RunState());
        Check(manager.CurrentWorldId == initialWorld && !manager.IsFused && PlayerHealth.instance.currentHealth == initialMax
            && All<PlayerHealth>().All(h => h.currentHealth == initialMax) && ExperienceLevelController.instance.currentLevels == 1,
            "restart restores shared HP/world/XP; fixture and fusion do not persist");
        // Allow a bounded settling interval without repairing timeScale or entering a stage.
        yield return new WaitForSecondsRealtime(.5f);
        Check(run.CurrentStageIndex == 0 && run.IsRunning && Time.timeScale == 1, "restart settled after 0.5 real seconds: " + RunState());
        // Dispatch a coordinate-derived UI pointer sequence through the real Button handler, not its private helper.
        yield return null; Canvas.ForceUpdateCanvases();
        var next = Button("Next");
        var rect = (RectTransform)next.transform;
        var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)), button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        if (hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == next)
        {
            ExecuteEvents.Execute(next.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(next.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(next.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Check(run.CurrentStageIndex == 1, "raycast-coordinate synthetic pointer click executes Next (not OS input); interactable=" + next.interactable + "; " + RunState());
        }
        else Check(false, "pointer target blocked");
        // Independent death fixture: record restart failure above first, then resume the freshly reloaded
        // scene only to exercise death from a genuinely running/spawning wave rather than the stalled restart.
        if (!run.IsRunning)
        {
            Debug.Log("DEBUG_RUN FIXTURE independent death case resumes stalled reload AFTER restart failures recorded");
            Time.timeScale = 1;
        }
        yield return new WaitForSecondsRealtime(1f);
        Upgrade();
        Check(run.IsRunning && run.CurrentStageIndex >= 0 && run.CurrentStageIndex < 3 && run.RemainingStageTime > 0
            && All<EnemyController>().Length > 0, "death fixture starts in running wave with enemies and positive timer");
        PlayerHealth.instance.DamageHandler(PlayerHealth.instance.currentHealth + 1);
        yield return null; yield return null;
        Check(run.IsDefeated && !run.IsRunning && Time.timeScale == 0, "player lethal damage defeats run");
        float timer = run.RemainingStageTime;
        string deadState = Snapshot();
        foreach (string label in new[] { "Wave 1", "Wave 2", "Wave 3", "Boss", "Next" }) Button(label).onClick.Invoke();
        Check(!run.TryEnterStage(3), "dead API rejected");
        yield return new WaitForSecondsRealtime(.5f);
        Check(run.RemainingStageTime == timer && Snapshot() == deadState && All<EnemySpawner>().All(s => !Field<bool>(s,"isSpawning")), "death stops timer/spawns and rejects UI transitions");
        oldId = run.GetInstanceID(); RestartPointer("dead", true);
        yield return null; yield return null; yield return null;
        run = All<RunStageController>().Single();
        Check(run.GetInstanceID() != oldId && !run.IsDefeated && run.CurrentStageIndex == 0 && Time.timeScale == 1 && PlayerHealth.instance.currentHealth == initialMax, "Restart from death restores fresh run; oldId=" + oldId + "; " + RunState());
        OpenDeveloperGui();
        Check(!grant.IsActive, "old buff grant does not survive scene reload");
        Object.Destroy(recipe);
        yield return null; yield return null;
        var upgradeXp = ExperienceLevelController.instance;
        upgradeXp.GetExp(upgradeXp.expLevels[upgradeXp.currentLevels]);
        yield return null;
        Check(UIController.instance.levelUpPanel.activeInHierarchy && Time.timeScale == 0, "separate upgrade Restart fixture is paused with real upgrade overlay");
        oldId = run.GetInstanceID();
        RestartPointer("upgrade", true);
        yield return null; yield return null; yield return null;
        run = All<RunStageController>().Single();
        Check(run.GetInstanceID() != oldId && run.CurrentStageIndex == 0 && run.IsRunning && Time.timeScale == 1
            && !UIController.instance.levelUpPanel.activeSelf && PlayerHealth.instance.currentHealth == initialMax
            && ExperienceLevelController.instance.currentLevels == 1, "Restart pointer during upgrade reloads fresh running scene without overlay/XP persistence; " + RunState());
        OpenDeveloperGui();
        // Separate legacy fixture after recording restart failures; do not repair DebugRun to make it pass.
        Time.timeScale = 1;
        // Main is intentionally absent from the shared build list; editor-only loading avoids changing it.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
        yield return null; yield return null; yield return null;
        Check(All<RunStageController>().Length == 0 && All<RunStagePanel>().Length == 0, "Main has no DebugRun controller/panel");
        var legacy = All<EnemySpawner>();
        Check(legacy.Length == 2 && legacy.All(s => !s.UsesExternalStages), "Main spawners retain independent legacy mode");
        foreach (var s in legacy)
        {
            for (int i = 0; i < s.waves.Count + 2; i++) s.GoToNextWave();
            Check(Field<int>(s,"currentWave") == s.waves.Count - 1 && Field<bool>(s,"isSpawning"), "Main legacy last wave repeats: " + World.GetFor(s).WorldId);
        }
        Debug.Log("DEBUG_RUN COVERAGE NOTE synthetic pointer dispatch, no OS mouse injection; controlled survival fixture");
    }
}
