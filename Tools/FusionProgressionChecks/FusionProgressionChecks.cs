using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class FusionProgressionChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static int Passed, Failed;
    public static readonly List<string> Evidence = new List<string>();
    public static readonly List<string> Completed = new List<string>();
    static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    static T Get<T>(object owner, string field) => (T)(owner.GetType().GetField(field, Fields)
        ?? throw new MissingFieldException(owner.GetType().Name, field)).GetValue(owner);
    static void Set(object owner, string field, object value) => (owner.GetType().GetField(field, Fields)
        ?? throw new MissingFieldException(owner.GetType().Name, field)).SetValue(owner, value);
    static WorldManager Manager => All<WorldManager>().Single();
    static StateSwitchController Flow
    {
        get
        {
            var property = typeof(WorldManager).GetProperty("SwitchFlow", Fields)
                ?? throw new MissingMemberException(nameof(WorldManager), "SwitchFlow");
            return (StateSwitchController)property.GetValue(Manager)
                ?? throw new InvalidOperationException("WorldManager has no bound shared switch flow");
        }
    }
    static RunStageController RunController => All<RunStageController>().Single();
    static PlayerController Hero(WorldId id) => All<World>().Single(w => w.WorldId == id).Player;
    static ExperienceLevelController XP(PlayerController p) => p.GetComponent<ExperienceLevelController>();
    static Weapon[] Starters(PlayerController p) => Get<List<Weapon>>(p, "startingWeapons").ToArray();
    public static void Note(string text)
    {
        Evidence.Add(text);
        File.AppendAllText(Path.Combine(FusionProgressionChecksBatch.Arg("-fusionOutput"), "checks.log"), text + Environment.NewLine);
    }
    static void Require(bool condition, string label)
    {
        Note((condition ? "PASS " : "FAIL ") + label);
        if (!condition) { Failed++; throw new InvalidOperationException(label); }
        Passed++;
    }
    static IEnumerator Until(Func<bool> ready, float seconds, string label)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
    static void Quiet()
    {
        foreach (var spawner in All<EnemySpawner>())
        {
            spawner.ConfigureExternalStages();
            spawner.StopSpawning(true);
        }
        Note("FIXTURE runtime only: ConfigureExternalStages and StopSpawning(true), maintained before spawner Updates. Components stay enabled for finale configuration validation; production boss spawn untouched. No asset save, weapon disabling, or HP invulnerability.");
    }
    public static void SuppressOrdinarySpawns()
    {
        foreach (var spawner in All<EnemySpawner>()) spawner.StopSpawning(true);
    }
    static void Hud(PlayerController player, int count, string label)
    {
        Require(PlayerController.instance == player && ExperienceLevelController.instance == XP(player)
            && PlayerHealth.instance == player.GetComponent<PlayerHealth>(), label + " current aliases");
        var equipped = player.EquippedWeapons;
        Require(equipped.Count == count && equipped.Distinct().Count() == count, label + " equipped count=" + count);
        var inventories = All<InventoryUI>().Where(i => i.isActiveAndEnabled).ToArray();
        Require(inventories.Length > 0, label + " live inventory");
        Canvas.ForceUpdateCanvases();
        foreach (var inventory in inventories)
        {
            inventory.UpdateInventory();
            Canvas.ForceUpdateCanvases();
            Require(inventory.weaponIcons.Length >= count, label + " enough dynamic slots");
            for (int i = 0; i < inventory.weaponIcons.Length; i++)
            {
                var image = inventory.weaponIcons[i];
                if (i >= count) { Require(image == null || !image.enabled, label + " unused slot hidden " + i); continue; }
                var weapon = equipped[i];
                var expected = weapon.weaponIcon != null ? weapon.weaponIcon : weapon.icon;
                Require(expected != null && image != null && image.isActiveAndEnabled && image.sprite == expected,
                    label + " icon " + i + " = " + weapon.weaponName + " #" + weapon.GetInstanceID());
                if (i >= 4)
                {
                    Rect bounds = IconScreenBounds(image);
                    Require(bounds.width > 1 && bounds.height > 1 && bounds.xMin >= -1 && bounds.yMin >= -1
                        && bounds.xMax <= Screen.width + 1 && bounds.yMax <= Screen.height + 1,
                        label + " extra icon " + (i + 1) + " has nonzero fully on-screen geometry " + bounds
                        + " within " + Screen.width + "x" + Screen.height);
                    for (int earlier = 0; earlier < i; earlier++)
                        Require(!bounds.Overlaps(IconScreenBounds(inventory.weaponIcons[earlier])),
                            label + " extra icon " + (i + 1) + " does not overlap icon " + (earlier + 1));
                }
            }
        }
        var xp = XP(player); var ui = UIController.instance;
        Require(Mathf.Approximately(ui.expLvlSlider.value, xp.currentExperience)
            && Mathf.Approximately(ui.expLvlSlider.maxValue, xp.expLevels[xp.currentLevels])
            && ui.expLvlText.text == "Level: " + xp.currentLevels, label + " XP HUD bound to owner");
    }
    static Rect IconScreenBounds(UnityEngine.UI.Image image)
    {
        var canvas = image.canvas.rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Require(canvas.renderMode == RenderMode.ScreenSpaceOverlay || camera != null,
            "icon geometry has explicit render camera or Overlay canvas");
        var corners = new Vector3[4];
        image.rectTransform.GetWorldCorners(corners);
        var screen = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(camera, corner)).ToArray();
        Require(screen.All(point => !float.IsNaN(point.x) && !float.IsInfinity(point.x)
            && !float.IsNaN(point.y) && !float.IsInfinity(point.y)), "finite icon screen coordinates");
        return Rect.MinMaxRect(screen.Min(p => p.x), screen.Min(p => p.y), screen.Max(p => p.x), screen.Max(p => p.y));
    }
    static void Initial()
    {
        Require(Flow.isActiveAndEnabled, "manager-bound shared switch flow active; disabled legacy controllers ignored");
        Require(Manager.IsInitialized && Manager.CurrentWorldId == WorldId.Material && !Manager.IsFused
            && Time.timeScale == 1, "fresh Material scene entry, normal time");
        Require(Starters(Hero(WorldId.Material)).Length == 3 && Starters(Hero(WorldId.Echo)).Length == 2,
            "serialized native starter loadouts Material=3 / Echo=2");
        Require(Hero(WorldId.Material).assignedWeapons.SequenceEqual(Starters(Hero(WorldId.Material))), "Material actual starter references/order");
        Hud(Hero(WorldId.Material), 3, "Material initial");
    }
    static IEnumerator Reload(bool resultButton = false)
    {
        var old = Manager;
        if (resultButton) All<GameOverManager>().Single().Restart();
        else { Time.timeScale = 1; SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
        yield return Until(() => old == null && All<WorldManager>().Any(m => m.IsInitialized), 15, "native same-scene reload");
        yield return null; yield return null;
        Quiet(); Initial();
        Require(Flow.CompletedMaterialResidences == 0 && Flow.CompletedEchoResidences == 0
            && !RunController.IsFinaleStarted && !RunController.IsCompleted && !RunController.IsDefeated
            && Flow.AutomaticSwitchingEnabled && All<PlayerHealth>().All(h => Mathf.Approximately(h.currentHealth, 100)),
            "reload resets residences, finale, auto mode, shared HP and time");
        Require(XP(Hero(WorldId.Material)).TotalExperience == 0 && !UIController.instance.levelUpPanel.activeSelf,
            "reload clears fusion XP and upgrade panel");
    }
    static IEnumerator SwitchEcho()
    {
        Require(Flow.RequestSwitch(WorldId.Echo), "production manual Echo switch accepted");
        yield return Until(() => Manager.CurrentWorldId == WorldId.Echo && !Flow.IsFlipping, 10, "Echo manual switch");
        yield return null;
        Require(Hero(WorldId.Echo).assignedWeapons.SequenceEqual(Starters(Hero(WorldId.Echo))), "Echo actual two starter references/order");
        Hud(Hero(WorldId.Echo), 2, "Echo switched");
        Require(Flow.CompletedMaterialResidences == 0 && Flow.CompletedEchoResidences == 0, "manual switch earns no residence credit");
    }
    static IEnumerator Capture(string label)
    {
        yield return new WaitForEndOfFrame();
        string path = Path.Combine(FusionProgressionChecksBatch.Arg("-fusionOutput"), label + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return Until(() => File.Exists(path) && new FileInfo(path).Length > 1024, 8, "screenshot " + label);
        Note("SCREENSHOT " + path);
    }
    static FusionTransitionController watchedTransition;
    static Action completionObserver;
    static int completedFrame;
    static bool bossAbsentAtCompletion;
    static void WatchBossGate()
    {
        if (watchedTransition != null) watchedTransition.Completed -= completionObserver;
        watchedTransition = All<FusionTransitionController>().Single();
        completedFrame = -1; bossAbsentAtCompletion = false;
        var run = RunController;
        completionObserver = () =>
        {
            completedFrame = Time.frameCount;
            bossAbsentAtCompletion = !run.IsBossPhase && run.RemainingBosses == 0;
            Note("OBSERVED fusion Completed frame=" + completedFrame + " bossAbsent=" + bossAbsentAtCompletion);
        };
        watchedTransition.Completed += completionObserver;
    }
    static IEnumerator PauseEntrance()
    {
        Require(Manager.IsFusionTransitioning && !RunController.IsBossPhase && RunController.RemainingBosses == 0,
            "fusion entrance precedes boss");
        float progress = watchedTransition.Progress;
        Time.timeScale = 0;
        try
        {
            yield return new WaitForSecondsRealtime(.15f);
            Require(Mathf.Approximately(watchedTransition.Progress, progress) && RunController.RemainingBosses == 0,
                "pause freezes native fusion and boss gate");
        }
        finally { Time.timeScale = 1; }
    }
    static IEnumerator BossCompletion()
    {
        var run = RunController;
        yield return Until(() => run.IsBossPhase || run.IsDefeated, 18, "production fusion completion/boss");
        Require(completedFrame >= 0 && bossAbsentAtCompletion && Time.frameCount > completedFrame
            && run.IsBossPhase && run.RemainingBosses == 1,
            "exactly one boss strictly after actual fusion Completed event (none in event frame)");
        watchedTransition.Completed -= completionObserver;
        var bosses = Get<HashSet<EnemyController>>(run, "bosses").ToArray();
        Require(bosses.Length == 1 && bosses[0] is TitanEnemyController, "production Titan finale boss");
        bosses[0].TakeDamage(bosses[0].health + 1);
        yield return Until(() => run.IsCompleted, 5, "boss death victory");
        yield return null;
        Require(Time.timeScale == 0 && Get<GameObject>(All<GameOverManager>().Single(), "victoryUI").activeInHierarchy,
            "native victory panel and result pause");
    }
    static IEnumerator Early(WorldId entryId)
    {
        Flow.AutomaticSwitchingEnabled = false;
        if (entryId == WorldId.Echo) yield return SwitchEcho();
        var manager = Manager;
        var material = Hero(WorldId.Material); var echo = Hero(WorldId.Echo);
        var entry = Hero(entryId); var secondary = entry == material ? echo : material;
        var weapons = Starters(material).Concat(Starters(echo)).ToArray();
        Require(weapons.Length == 5 && weapons.All(w => w != null) && weapons.Distinct().Count() == 5, "five distinct scene starter instances");
        var allIds = All<Weapon>().Select(w => w.GetInstanceID()).OrderBy(i => i).ToArray();
        var parents = weapons.Select(w => w.transform.parent).ToArray();
        // Distinct live stat sentinels prove fusion does not reset, copy or replace existing upgrades.
        for (int i = 0; i < weapons.Length; i++) { weapons[i].stats.damage += .125f * (i + 1); weapons[i].weaponLevel += i + 1; }
        var stats = weapons.Select(w => w.stats).ToArray();
        var statJson = weapons.Select(w => JsonUtility.ToJson(w.stats)).ToArray();
        var levels = weapons.Select(w => w.weaponLevel).ToArray();
        XP(material).currentLevels = 3; XP(material).currentExperience = 4;
        XP(echo).currentLevels = 2; XP(echo).currentExperience = 2;
        XP(entry).BindAsCurrent();
        Require(XP(material).TotalExperience == 17 && XP(echo).TotalExperience == 7, "seed total XP 17 + 7 with thresholds 5,8,11");
        long secondaryPool = XP(secondary).TotalExperience;
        int secondaryLevel = XP(secondary).currentLevels, secondaryRemainder = XP(secondary).currentExperience;
        Action secondaryUnchanged = () => Require(XP(secondary).TotalExperience == secondaryPool
            && XP(secondary).currentLevels == secondaryLevel && XP(secondary).currentExperience == secondaryRemainder,
            "secondary stored XP pool unchanged: " + secondaryPool + " level" + secondaryLevel + " remainder" + secondaryRemainder);
        bool observed = false;
        Action committed = () =>
        {
            if (!manager.IsFused) return;
            observed = true;
            Require(manager.FusionWeapons.Count == 5 && entry.EquippedWeapons.Count == 5
                && XP(entry).TotalExperience == 24 && PlayerController.instance == entry
                && ExperienceLevelController.instance == XP(entry),
                "FusionStateChanged observes combined weapons and entry-only merged XP with entry aliases");
            secondaryUnchanged();
        };
        WatchBossGate();
        manager.FusionStateChanged += committed;
        try { Require(RunController.TryStartFinale(), "early final fusion accepted from " + entryId); }
        finally { manager.FusionStateChanged -= committed; }
        Require(observed && manager.FusionPlayer == entry && PlayerController.instance == entry, "fusion keeps entry ownership " + entryId);
        Require(manager.FusionWeapons.Count == 5 && manager.FusionWeapons.Distinct().Count() == 5
            && new HashSet<Weapon>(manager.FusionWeapons).SetEquals(weapons), "combined five original weapons, no duplicates");
        Require(XP(entry).TotalExperience == 24 && XP(entry).currentLevels == 4 && XP(entry).currentExperience == 0,
            "entry-only merged XP = 24 -> level4 remainder0 on " + entryId);
        secondaryUnchanged();
        Require(!UIController.instance.levelUpPanel.activeSelf && Time.timeScale == 1, "merge gives no bonus upgrade selection or pause");
        yield return PauseEntrance();
        yield return null; yield return null;
        Require(!UIController.instance.levelUpPanel.activeSelf && Time.timeScale == 1, "secondary Start gives no delayed bonus selection");
        secondaryUnchanged();
        Require(allIds.SequenceEqual(All<Weapon>().Select(w => w.GetInstanceID()).OrderBy(i => i)), "fusion creates/destroys no Weapon components");
        for (int i = 0; i < weapons.Length; i++)
            Require(weapons[i].transform.parent == parents[i] && ReferenceEquals(weapons[i].stats, stats[i])
                && JsonUtility.ToJson(weapons[i].stats) == statJson[i] && weapons[i].weaponLevel == levels[i]
                && weapons[i].gameObject.activeInHierarchy && entry.HasEquippedWeapon(weapons[i]),
                "original weapon parent/stat identity, values, level and authorization retained " + weapons[i].weaponName);
        Require(material.assignedWeapons.SequenceEqual(Starters(material)) && echo.assignedWeapons.SequenceEqual(Starters(echo)),
            "per-hero assigned collections remain original 3/2");
        Hud(entry, 5, entryId + " fused");
        PlayerHealth.instance.DamageHandler(10); yield return null;
        Require(All<PlayerHealth>().All(h => Mathf.Approximately(h.currentHealth, 90)
            && h.healthSlider == PlayerHealth.instance.healthSlider), "fusion shared HP damage and one slider binding");
        yield return Capture(entryId + "-fusion-five-icons");
        var weapon = secondary.assignedWeapons.First();
        Require(weapon.GetComponentInParent<PlayerController>() != entry && entry.HasEquippedWeapon(weapon), "secondary-owned weapon authorized through fused equipment");
        XP(secondary).GetExp(34); // Level 4 cost15, level5 cost19: exactly two selections, not one lost remainder.
        Require(UIController.instance.levelUpPanel.activeSelf && Time.timeScale == 0 && XP(entry).currentLevels == 5
            && XP(entry).currentExperience == 19 && XP(entry).TotalExperience == 58,
            "secondary XP pickup redirects to entry and opens first pending threshold with19 remainder");
        secondaryUnchanged();
        var button = UIController.instance.levelUpButtons[0];
        button.UpdateButtonDisplay(weapon);
        Set(button, "selectedUpgradeType", UpgradeType.Damage); Set(button, "selectedUpgrade", .25f);
        float damage = weapon.stats.damage;
        button.SelectUpgrade();
        Require(Mathf.Approximately(weapon.stats.damage, damage + .25f), "production SelectUpgrade applies to secondary-owned weapon");
        Require(UIController.instance.levelUpPanel.activeSelf && Time.timeScale == 0 && XP(entry).currentLevels == 6
            && XP(entry).currentExperience == 0, "CompleteUpgradeSelection processes next pending threshold without losing XP");
        button = UIController.instance.levelUpButtons[0];
        button.UpdateButtonDisplay(weapon);
        Set(button, "selectedUpgradeType", UpgradeType.Damage); Set(button, "selectedUpgrade", .5f);
        yield return new WaitForSecondsRealtime(.25f);
        button.SelectUpgrade();
        Require(Mathf.Approximately(weapon.stats.damage, damage + .75f),
            "second production SelectUpgrade applies +0.5 damage; neither earned upgrade skipped");
        Require(!UIController.instance.levelUpPanel.activeSelf && Time.timeScale == 1
            && XP(entry).TotalExperience == 58 && XP(entry).currentLevels == 6 && XP(entry).currentExperience == 0,
            "second selection completes pending queue, closes panel and unpauses; entry XP conserved at58");
        secondaryUnchanged();
        Hud(entry, 5, entryId + " upgraded");
        yield return BossCompletion();
        yield return Reload(true);
    }
    static IEnumerator Boundaries()
    {
        var flow = Flow; var manager = Manager; var run = RunController;
        WatchBossGate();
        Require(flow.SwitchInterval == 90 && run.FinaleStartTime == 540, "native serialized 90-second residence / six residence contract");
        Note("ACCELERATED BOUNDARY INJECTION: set private timerCounter to 0.75 seconds before each boundary; native Update, warning, midpoint, fusion and boss lifecycle run normally at timeScale1. This is NOT 540 elapsed seconds or a real nine-minute acceptance.");
        flow.AutomaticSwitchingEnabled = false;
        yield return new WaitForSecondsRealtime(.15f);
        Require(flow.CompletedMaterialResidences == 0 && flow.CompletedEchoResidences == 0 && !run.IsFinaleStarted
            && Mathf.Approximately(flow.RemainingTime, 90), "auto-off suppresses residence countdown/finale");
        flow.AutomaticSwitchingEnabled = true;
        Set(flow, "timerCounter", .75f);
        Time.timeScale = 0; yield return new WaitForSecondsRealtime(.15f);
        Require(Mathf.Approximately(flow.RemainingTime, .75f) && !flow.IsFlipping && !run.IsFinaleStarted, "pause holds injected residence boundary");
        Time.timeScale = 1;
        for (int residence = 1; residence <= 6; residence++)
        {
            if (residence > 1) Set(flow, "timerCounter", .75f);
            int expected = residence;
            yield return Until(() => flow.CompletedMaterialResidences + flow.CompletedEchoResidences >= expected, 6, "native residence " + residence);
            Require(flow.CompletedMaterialResidences == (residence + 1) / 2 && flow.CompletedEchoResidences == residence / 2,
                "residence " + residence + " exact alternating credits M=" + flow.CompletedMaterialResidences + " E=" + flow.CompletedEchoResidences);
            if (residence < 6)
            {
                Require(!run.IsFinaleStarted && !manager.IsFused && run.RemainingBosses == 0
                    && manager.CurrentWorldId == (residence % 2 == 1 ? WorldId.Echo : WorldId.Material), "no premature fusion/boss at boundary " + residence);
                yield return Until(() => !flow.IsFlipping, 5, "native flip settled");
                yield return null;
                Hud(PlayerController.instance, residence % 2 == 1 ? 2 : 3, "residence " + residence);
            }
        }
        Require(run.IsFinaleStarted && manager.IsFinalFusion && !flow.IsFlipping && flow.RemainingResidences == 0
            && manager.CurrentWorldId == WorldId.Echo, "sixth Echo residence enters final fusion, no ordinary final flip");
        Hud(PlayerController.instance, 5, "automatic final fusion");
        yield return PauseEntrance();
        yield return BossCompletion();
        yield return Reload(true);
    }
    static IEnumerator Scenario(string name, IEnumerator scenario)
    {
        Note("BEGIN " + name);
        var stack = new Stack<IEnumerator>(); stack.Push(scenario);
        bool failed = false;
        int failuresBefore = Failed;
        try
        {
            while (stack.Count > 0)
            {
                object current = null; Exception error = null;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    current = stack.Peek().Current;
                }
                catch (Exception e) { error = e; }
                if (error != null) { if (Failed == failuresBefore) Failed++; Note("SCENARIO ERROR " + name + "\n" + error); failed = true; break; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }
        finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
        if (!failed) { Completed.Add(name); Note("COMPLETE " + name); }
    }
    public static IEnumerator Run()
    {
        Require(Application.isPlaying && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
            "actual graphical native Play Mode " + SceneManager.GetActiveScene().name);
        Application.runInBackground = true;
        Quiet(); Initial();
        yield return Scenario("early Material fusion / XP / upgrade / boss / restart", Early(WorldId.Material));
        yield return Reload();
        yield return Scenario("early Echo fusion / switch HUD / XP / upgrade / boss / restart", Early(WorldId.Echo));
        yield return Reload();
        yield return Scenario("accelerated six native boundaries / pause / auto-off / boss / restart", Boundaries());
    }
}
