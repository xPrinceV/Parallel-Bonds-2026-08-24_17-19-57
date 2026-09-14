using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Staged only in the disposable Editor assembly; never attached to source scenes.
public static class HealthUiPlayChecks
{
    public static int Passed, Failed;
    public static readonly List<string> Evidence = new List<string>();
    public static readonly List<string> Completed = new List<string>();
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string Art = "Assets/Vampire Survival Assets/Art/Hp bar.png";
    public static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Flags).GetValue(owner);
    static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    public static void Note(string message) { Evidence.Add(message); Debug.Log("HEALTH_UI " + message); }
    public static void Require(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Note((ok ? "PASS " : "FAIL ") + label);
        if (!ok) throw new InvalidOperationException(label);
    }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
    static Canvas Shared(PlayerHealth hp) => hp.healthSlider.GetComponentInParent<Canvas>(true);
    static Canvas[] HealthCanvases() => All<Canvas>().Where(c => c.GetComponentsInChildren<Slider>(true)
        .Any(s => s.name == "Slider") && c.name == "Health Bar Canvas").ToArray();

    public static void Preflight(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        foreach (var t in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0, scene.name + " script import " + t.name);
        var components = roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
        foreach (var component in components)
        {
            var property = new SerializedObject(component).GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Require(property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0,
                        scene.name + " binding " + component.GetType().Name + "/" + property.propertyPath);
        }
        var health = components.OfType<PlayerHealth>().ToArray();
        Require(health.Length == 2 && health.All(h => h.healthSlider == health[0].healthSlider && h.healthText == health[0].healthText), scene.name + " two heroes share exact Slider/TMP");
        Require(health.All(h => Get<float>(h, "storedCurrentHealth") == 0 && Get<float>(h, "storedMaxHealth") == 100), scene.name + " authored HP0/max100 unchanged");
        var canvas = Shared(health[0]);
        Require(canvas.transform.parent == null && World.GetFor(canvas) == null && canvas.gameObject.activeSelf
            && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null && canvas.sortingOrder == 0,
            scene.name + " shared root Overlay sorting0 outside worlds");
        var over = components.OfType<GameOverManager>().Single();
        Require(canvas.transform.GetSiblingIndex() < over.transform.root.GetSiblingIndex(), scene.name + " health root before result HUD");
        var secondary = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c != canvas && c.name == "Health Bar Canvas");
        Require(!secondary.gameObject.activeSelf && secondary.GetComponentsInChildren<Slider>(true).Length == 1
            && secondary.GetComponentsInChildren<TMP_Text>(true).Length == 1, scene.name + " secondary subtree retained but disabled");
        Require(!health[0].healthText.gameObject.activeSelf, scene.name + " upstream hides numeric HP");
        Decorative(health[0]);
        var design = canvas.GetComponentsInChildren<Image>(true).Single(i => i.name == "Health Bar Design");
        Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(design.sprite, out string guid, out long id)
            && guid == "d8665a9e010ab704c8a5c6f1e6784dd7" && id == 21300000
            && AssetDatabase.GetAssetPath(design.sprite) == Art && design.sprite.texture.width == 280 && design.sprite.texture.height == 160,
            scene.name + " actual native sprite GUID/fileID21300000, texture280x160");
        var rect = design.rectTransform;
        Require(rect.anchoredPosition == new Vector2(-50, 0) && rect.sizeDelta == new Vector2(380, 250)
            && rect.GetSiblingIndex() == 2, scene.name + " exact upstream frame layout after fill");
        foreach (string name in new[] { "Fill Area", "Background" })
        {
            var r = canvas.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == name);
            Require(r.localScale == new Vector3(.5f, .5f, 1) && r.anchoredPosition == new Vector2(-16.5f, -5)
                && r.sizeDelta == new Vector2(33, -10), scene.name + " upstream " + name + " geometry");
        }
        var flow = components.OfType<StateSwitchController>().Single(f => f.isActiveAndEnabled);
        var run = components.OfType<RunStageController>().Single();
        Require(flow.SwitchInterval == 30 && Get<float>(flow, "warningDuration") == 5 && Near(Get<float>(flow, "flipDuration"), .8f)
            && run.FinaleStartTime == 480 && Get<float>(run, "bossHealth") == 600, scene.name + " native 30/5/.8/480 boss600 preserved");
        Completed.Add(scene.name + " native preflight");
    }
    static void Decorative(PlayerHealth hp)
    {
        var c = Shared(hp); var graphics = c.GetComponentsInChildren<Graphic>(true);
        Require(!hp.healthSlider.interactable && hp.healthSlider.navigation.mode == Navigation.Mode.None,
            "Slider noninteractive/navigation none");
        Require(graphics.Length == 4 && graphics.All(g => !g.raycastTarget) && !c.GetComponent<GraphicRaycaster>().enabled,
            "all four graphics non-raycast; canonical GraphicRaycaster disabled");
    }
    static void Health(float value, string label)
    {
        var all = All<PlayerHealth>(); var hp = PlayerHealth.instance; var c = Shared(hp);
        Canvas.ForceUpdateCanvases();
        Require(all.Length == 2 && all.All(h => Near(h.currentHealth, value) && Near(h.maxHealth, 100)
            && h.healthSlider == hp.healthSlider && h.healthText == hp.healthText), label + " shared HP=" + value + "/100, one binding");
        Require(Near(hp.healthSlider.value, value) && Near(hp.healthSlider.maxValue, 100)
            && Near(hp.healthSlider.normalizedValue, value / 100f), label + " actual Slider value and normalized fill");
        var fill = hp.healthSlider.fillRect;
        Require(Near(fill.anchorMax.x, value / 100f) && Near(fill.anchorMin.x, 0), label + " real fill RectTransform proportion");
        Require(HealthCanvases().Count(x => x.isActiveAndEnabled) == 1 && c.isActiveAndEnabled
            && !hp.healthText.gameObject.activeSelf && c.transform.parent == null, label + " exactly one live health canvas, hidden text");
        var image = c.GetComponentsInChildren<Image>().Single(i => i.name == "Health Bar Design");
        var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
        Require(!image.canvasRenderer.cull && image.canvasRenderer.GetAlpha() > .99f
            && corners[0].x >= -1 && corners[0].y >= -1 && corners[2].x <= Screen.width + 1 && corners[2].y <= Screen.height + 1,
            label + " imported frame visible within GameView " + Screen.width + "x" + Screen.height);
        Note(label + " frame screen bounds=" + corners[0] + ".." + corners[2] + "; fillWidth=" + fill.rect.width + "; scale=" + c.transform.localScale);
        Decorative(hp);
    }
    static IEnumerator Until(Func<bool> ready, float seconds, string label)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
    static IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        string path = Path.Combine(HealthUiPlayChecksBatch.Arg("-healthOutput"), name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        yield return Until(() => File.Exists(path) && new FileInfo(path).Length > 1024, 8, "GameView capture " + name);
        Note("SCREENSHOT " + path + "; " + Screen.width + "x" + Screen.height);
    }
    static void QuietSpawns()
    {
        foreach (var spawner in All<EnemySpawner>()) spawner.StopSpawning(true);
        All<StateSwitchController>().Single(f => f.isActiveAndEnabled).AutomaticSwitchingEnabled = false;
        Note("Isolated fixture: public StopSpawning(true), automatic scheduling off; no HP/maxHP, weapon, inventory, timing or runtime script edits");
    }
    static void Panel(GameObject panel)
    {
        Require(panel.activeInHierarchy, panel.name + " shown by production result state");
        var c = Shared(All<PlayerHealth>()[0]);
        var panelCanvas = panel.GetComponentInParent<Canvas>();
        Require(panelCanvas.renderOrder > c.renderOrder, panel.name + " native canvas renderOrder above health");
        foreach (var button in panel.GetComponentsInChildren<Button>()) Raycast(button);
        var rect = c.GetComponentsInChildren<Image>().Single(i => i.name == "Health Bar Design").rectTransform;
        var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Require(hits.All(h => !h.gameObject.transform.IsChildOf(c.transform)), panel.name + " health frame cannot intercept raycasts");
    }
    static PointerEventData Raycast(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Note("RAYCAST " + button.name + " point=" + point + "; hits=" + string.Join(" | ", hits.Select(h =>
            h.gameObject.name + " parent=" + h.gameObject.transform.parent?.name + " depth=" + h.depth + " module=" + h.module.name)));
        Require(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            "result first raycast reaches " + button.name);
        pointer.pointerCurrentRaycast = hits[0]; return pointer;
    }
    static void Restart(GameObject panel)
    {
        var button = panel.GetComponentsInChildren<Button>().Single(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount())
            .Any(i => b.onClick.GetPersistentMethodName(i) == "Restart"));
        Require(ExecuteEvents.Execute(button.gameObject, Raycast(button), ExecuteEvents.pointerClickHandler), "native pointer Restart dispatched");
    }
    public static IEnumerator Run()
    {
        Require(Application.isPlaying && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
            "native graphical " + SceneManager.GetActiveScene().name + " Play Mode");
        string scenePrefix = SceneManager.GetActiveScene().name.ToLowerInvariant();
        Application.runInBackground = true;
        QuietSpawns(); yield return null; yield return null;
        var manager = All<WorldManager>().Single(); var flow = All<StateSwitchController>().Single(f => f.isActiveAndEnabled);
        Require(manager.IsInitialized && manager.CurrentWorldId == WorldId.Material && Time.timeScale == 1, "normal Material entry");
        Health(100, "Material full"); yield return Capture(scenePrefix + "-full-hp"); Completed.Add("full HP screenshot");
        PlayerHealth.instance.DamageHandler(50); yield return null;
        Health(50, "Material half"); yield return Capture(scenePrefix + "-half-hp"); Completed.Add("half HP screenshot");
        var canvas = Shared(PlayerHealth.instance); var originalScale = canvas.transform.localScale;
        var frame = canvas.GetComponentsInChildren<Image>().Single(i => i.name == "Health Bar Design").rectTransform;
        var corners = new Vector3[4]; frame.GetWorldCorners(corners);
        Require(flow.RequestSwitch(WorldId.Echo), "production switch to Echo accepted");
        var flip = All<WorldFlipPresentation>().Single();
        yield return Until(() => flow.IsFlipping && flip.IsCapturing, 10, "world camera capture active");
        float scale = Time.timeScale; Time.timeScale = 0;
        try
        {
            yield return null; Canvas.ForceUpdateCanvases();
            var during = new Vector3[4]; frame.GetWorldCorners(during);
            Require(canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null
                && canvas.transform.localScale == originalScale && corners.Zip(during, (a, b) => Vector3.Distance(a, b)).All(d => d < .01f)
                && flip.WorldCamera.targetTexture != null && canvas.sortingOrder > Get<Canvas>(flip, "runtimeCanvas").sortingOrder,
                "world capture/blur/flip excludes shared Overlay; frame screen geometry unchanged");
            Health(50, "during world capture"); yield return Capture(scenePrefix + "-flip-health-untransformed");
        }
        finally { Time.timeScale = scale; }
        yield return Until(() => manager.CurrentWorldId == WorldId.Echo && !flow.IsFlipping && !manager.IsWorldTransitioning, 8, "Echo switch complete");
        Require(World.GetFor(PlayerHealth.instance).WorldId == WorldId.Echo, "Echo owns active health alias");
        Health(50, "Echo inherited half"); PlayerHealth.instance.DamageHandler(10); yield return null;
        Health(40, "Echo damage"); yield return Capture("echo-shared-damage"); Completed.Add("Echo switch/shared damage and capture exclusion");
        var run = All<RunStageController>().Single();
        Require(run.TryStartFinale(), "production final fusion accepted");
        yield return null; Health(40, "fusion entrance");
        Require(manager.IsFused && All<PlayerHealth>().All(h => h.gameObject.activeInHierarchy), "both heroes active in fusion with only one health canvas");
        yield return Capture("fusion-one-health-bar");
        yield return Until(() => run.IsBossPhase || run.IsDefeated, 20, "fusion entrance completes");
        Require(run.IsBossPhase && manager.IsFused, "settled final fusion");
        Health(40, "settled fusion"); yield return Capture("fusion-settled-one-health-bar"); Completed.Add("fusion single health bar");
        var bosses = Get<HashSet<EnemyController>>(run, "bosses").ToArray();
        Require(bosses.Length == 1, "one production finale boss"); bosses[0].TakeDamage(bosses[0].health + 1);
        yield return Until(() => run.IsCompleted, 5, "Victory result"); yield return null;
        var over = All<GameOverManager>().Single(); var victory = Get<GameObject>(over, "victoryUI");
        yield return Capture("victory-above-health"); Panel(victory); Completed.Add("Victory above health and unobstructed raycasts");
        var old = manager; Restart(victory);
        yield return Until(() => old == null && All<WorldManager>().Any(m => m.IsInitialized), 12, "same-scene restart");
        yield return null; yield return null; QuietSpawns();
        Require(Time.timeScale == 1 && !All<WorldManager>().Single().IsFused, "reload normal, unpaused, unfused");
        Health(100, "reloaded " + SceneManager.GetActiveScene().name); yield return Capture("reloaded-full-health"); Completed.Add("normal same-scene reload");
        PlayerHealth.instance.DamageHandler(100); yield return null;
        over = All<GameOverManager>().Single();
        Require(All<RunStageController>().Single().IsDefeated && Time.timeScale == 0, "production defeat paused");
        // Check pointer geometry only after the newly enabled panel has rendered.
        // Retain the actual result screenshot even if a non-health graphic blocks a button.
        yield return Capture("defeat-above-health"); Panel(over.gameOverUI);
        Completed.Add("defeat above health and unobstructed raycasts");
    }
}
