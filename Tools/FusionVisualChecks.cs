using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Isolated rendered Play sessions only. Pointer dispatch uses real UI raycast hits, not OS input.
public static class FusionVisualChecks
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    const string Asset = "Assets/Vampire Survival Assets/Art/fusion.png";
    static T[] All<T>() where T : Object { return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
    static T Field<T>(object o, string n) { return (T)o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
    static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Debug.Log("FUSION_VISUAL " + (ok ? "PASS " : "FAIL ") + SceneManager.GetActiveScene().name + " " + text); }
    static SpriteRenderer Fusion(PlayerController p) { return Field<SpriteRenderer>(p.GetComponent<PlayerFusionVisual>(), "fusionRenderer"); }
    static SpriteRenderer[] Bodies(PlayerController p) { return Field<SpriteRenderer[]>(p.GetComponent<PlayerFusionVisual>(), "bodyRenderers"); }
    static Renderer[] Core(PlayerController p) { return p.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<Weapon>() == null).ToArray(); }
    static void Click(DeveloperDebugGui gui, string name)
    {
        gui.SetOpen(true); Canvas.ForceUpdateCanvases();
        var button = gui.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        var canvas = Field<Canvas>(gui, "canvas");
        var rect = (RectTransform)button.transform;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        bool reachable = button.IsInteractable() && hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button;
        Check(reachable, "reachable GUI pointer " + name + " interactable=" + button.IsInteractable() + " position=" + pointer.position + " screen=" + Screen.width + "x" + Screen.height + " top=" + (hits.Count == 0 ? "none" : hits[0].gameObject.name));
        if (!reachable) throw new Exception("Blocked GUI pointer " + name);
        var target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        gui.SetOpen(false);
    }
    static void Quiet()
    {
        foreach (var b in All<MonoBehaviour>())
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController || b is EnemyController) b.enabled = false;
        UIController.instance.levelUpPanel.SetActive(false); Time.timeScale = 1;
    }
    static void Defaults(World[] worlds)
    {
        Check(All<PlayerFusionVisual>().Length == 2, "exactly two visual components after load");
        foreach (var w in worlds)
        {
            var p = w.Player; var f = Fusion(p);
            Check(p.GetComponents<PlayerFusionVisual>().Length == 1 && p.transform.Find("Characters").Cast<Transform>().Count(t => t.name == "Fusion Visual") == 1
                && f.transform == p.transform.Find("Characters/Fusion Visual") && !f.enabled, w.WorldId + " single serialized fusion child disabled by default");
            string guid; long id; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f.sprite, out guid, out id);
            var supplied = AssetDatabase.LoadAllAssetsAtPath(Asset).OfType<Sprite>().Single();
            string expectedGuid; long expectedId; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(supplied, out expectedGuid, out expectedId);
            Check(f.sprite == supplied && guid == expectedGuid && id == expectedId, w.WorldId + " supplied sprite GUID=" + guid + " fileID=" + id);
            var body = Bodies(p).Single(r => r.gameObject.activeSelf);
            Check(Mathf.Abs(f.bounds.size.y - body.bounds.size.y) < .001f && Mathf.Abs(f.bounds.min.y - body.bounds.min.y) < .001f,
                w.WorldId + " matched height/feet " + f.bounds.size.y + "/" + body.bounds.size.y + " feet=" + f.bounds.min.y + "/" + body.bounds.min.y);
            Check(f.sharedMaterial == body.sharedMaterial && f.sortingLayerID == body.sortingLayerID && f.sortingOrder == body.sortingOrder, w.WorldId + " body material/sorting retained");
        }
    }
    static void Fused(WorldManager m, PlayerController hero, PlayerController other)
    {
        Check(m.IsFused && m.FusionPlayer == hero && Fusion(hero).enabled && Fusion(hero).gameObject.activeInHierarchy && Bodies(hero).All(r => !r.enabled)
            && Core(hero).Count(r => r.enabled && r.gameObject.activeInHierarchy) == 1 && Core(other).All(r => !r.enabled)
            && other.GetComponentsInChildren<Collider2D>(true).Where(c => c.GetComponentInParent<Weapon>() == null).All(c => !c.enabled), "synchronous sole entry sprite and secondary core suppression " + m.CurrentWorldId);
        Check(All<PlayerFusionVisual>().Length == 2 && All<SpriteRenderer>().Count(r => r.sprite == Fusion(hero).sprite && r.enabled && r.gameObject.activeInHierarchy) == 1, "no duplicate active fusion sprites");
    }
    static void Capture(string state, DeveloperDebugGui gui, PlayerController hero)
    {
        Check(!gui.IsOpen && !Field<Canvas>(gui, "canvas").enabled, "capture GUI closed " + state);
        Check(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "real graphics device " + SystemInfo.graphicsDeviceType);
        var camera = Camera.main;
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Check(image != null && image.width == Screen.width && image.height == Screen.height, "end-of-frame rendered Game view dimensions");
            Directory.CreateDirectory("Logs");
            string path = Path.GetFullPath("Logs/FusionVisual-" + SceneManager.GetActiveScene().name + "-" + state + ".png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            // A pixel-for-pixel crop of the actual Game view, not a sprite composite.
            var visible = Core(hero).OfType<SpriteRenderer>().Single(r => r.enabled && r.gameObject.activeInHierarchy);
            var low = camera.WorldToScreenPoint(visible.bounds.min); var high = camera.WorldToScreenPoint(visible.bounds.max);
            int x = Mathf.Clamp(Mathf.FloorToInt(low.x) - 24, 0, image.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(low.y) - 24, 0, image.height - 1);
            int width = Mathf.Clamp(Mathf.CeilToInt(high.x) + 24 - x, 1, image.width - x);
            int height = Mathf.Clamp(Mathf.CeilToInt(high.y) + 24 - y, 1, image.height - y);
            var crop = new Texture2D(width, height, TextureFormat.RGB24, false);
            try { crop.SetPixels(image.GetPixels(x, y, width, height)); crop.Apply(); File.WriteAllBytes(path.Replace(".png", "-PlayerCrop.png"), crop.EncodeToPNG()); }
            finally { Object.DestroyImmediate(crop); }
            var point = camera.WorldToViewportPoint(hero.transform.position);
            Check(point.z > 0 && point.x > 0 && point.x < 1 && point.y > 0 && point.y < 1 && new FileInfo(path).Length > 1000, "rendered capture " + path + " player viewport=" + point);
        }
        finally { Object.DestroyImmediate(image); }
    }
    public static IEnumerator Run()
    {
        Passed = Failed = 0;
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            if (scene == "DebugRun") SceneManager.LoadScene(scene);
            else UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null; yield return null;
            Quiet();
            var manager = All<WorldManager>().Single(); var gui = All<DeveloperDebugGui>().Single();
            var worlds = Field<World[]>(manager, "worlds"); Defaults(worlds);
            foreach (var w in worlds)
            {
                if (manager.CurrentWorld != w) { gui.SetOpen(true); yield return null; yield return null; Click(gui, w.WorldId.ToString()); yield return new WaitForSecondsRealtime(.6f); }
                Quiet();
                var hero = w.Player; var other = worlds.Single(x => x != w).Player;
                var renderers = Core(hero); var flags = renderers.Select(r => r.enabled).ToArray();
                var colliders = hero.GetComponentsInChildren<Collider2D>(true); var collisions = colliders.Select(c => c.enabled).ToArray();
                yield return new WaitForEndOfFrame();
                Capture("Normal-" + w.WorldId, gui, hero);
                gui.SetOpen(true); yield return null; yield return null;
                Click(gui, "Fusion"); Fused(manager, hero, other);
                Check(collisions.SequenceEqual(colliders.Select(c => c.enabled)), "entry collider states unchanged");
                yield return null; Quiet(); yield return new WaitForEndOfFrame();
                Fused(manager, hero, other);
                Capture("Fused-" + w.WorldId, gui, hero);
                gui.SetOpen(true); yield return null; yield return null;
                Click(gui, "Fusion");
                Check(!manager.IsFused && flags.SequenceEqual(renderers.Select(r => r.enabled)), "GUI exit exact synchronous entry restoration");
                // No yielded frame: catch stale snapshots when yesterday's entry becomes secondary.
                Check(manager.SwitchWorld(World.GetFor(other).WorldId) && manager.TryEnterFusion(), "same-frame exit-switch-enter");
                Fused(manager, other, hero);
                Check(manager.TryExitFusion() && flags.SequenceEqual(renderers.Select(r => r.enabled)), "same-frame secondary snapshot restores original flags");
                Check(manager.SwitchWorld(w.WorldId) && manager.TryEnterFusion(), "enter for visual disable");
                var visual = hero.GetComponent<PlayerFusionVisual>(); visual.enabled = false;
                Check(flags.SequenceEqual(renderers.Select(r => r.enabled)), "component disable exact restoration");
                visual.enabled = true; Fused(manager, hero, other);
                Check(manager.TryExitFusion(), "exit after reenable");
            }
            Check(manager.TryEnterFusion(), "enter before death");
            var dying = manager.FusionPlayer;
            dying.GetComponent<PlayerHealth>().DamageHandler(dying.GetComponent<PlayerHealth>().currentHealth + 1);
            Check(!manager.IsFused && worlds.All(w => !Fusion(w.Player).enabled) && !dying.gameObject.activeSelf, "death restores fusion renderers synchronously");
            yield return null; yield return null;
            int oldManager = manager.GetInstanceID();
            if (scene == "DebugRun") { gui.SetOpen(true); yield return null; yield return null; Click(gui, "Restart"); }
            else { Time.timeScale = 1; UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single)); }
            yield return null; yield return null; yield return null;
            Quiet(); manager = All<WorldManager>().Single(); gui = All<DeveloperDebugGui>().Single();
            Check(manager.GetInstanceID() != oldManager && !manager.IsFused && !gui.IsOpen, "fresh restart/reload normal hidden GUI");
            Defaults(Field<World[]>(manager, "worlds"));
            gui.SetOpen(true); yield return null; yield return null;
            Click(gui, "Fusion");
            worlds = Field<World[]>(manager, "worlds"); Fused(manager, manager.FusionPlayer, worlds.Single(w => w != manager.CurrentWorld).Player);
            Check(manager.TryExitFusion(), "fresh reload fusion exits");
        }
    }
}
