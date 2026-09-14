using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Disposable Main Play session. All temporal samples follow real LateUpdates, never invoke them.
public static class WorldTransitionChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static WorldFilter filter;
    static Image image;
    static HashSet<int> objects;
    static Graphic[] graphics;
    static bool[] raycasts;
    static int sibling, sortingOrder;
    static Transform parent;
    static Material material;

    public static IEnumerator Run()
    {
        Passed = Failed = 0;
        var manager = Object.FindFirstObjectByType<WorldManager>();
        filter = Object.FindFirstObjectByType<WorldFilter>();
        if (!Application.isPlaying || manager == null || !manager.IsInitialized || filter == null)
            throw new InvalidOperationException("Requires initialized Main Play Mode");
        image = (Image)Get(filter, "overlay");
        Check(!filter.IsTransitioning && Near(image.color, manager.CurrentWorld.AmbientColor), "initial scene sample has settled tint without entry pulse");
        Check((float)Get(filter, "worldTransitionDuration") == 0.45f && (float)Get(filter, "fusionTransitionDuration") == 0.6f
            && (float)Get(filter, "fusionTintStrength") == 0.045f, "configured subtle durations and strength");
        var saved = new Dictionary<Behaviour, bool>();
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController)
            { saved.Add(b, b.enabled); b.enabled = false; }
        float scale = Time.timeScale;
        bool panel = UIController.instance.levelUpPanel.activeSelf;
        try
        {
            Time.timeScale = 1; UIController.instance.levelUpPanel.SetActive(false);
            // Warm both heroes before measuring object identity; activation can legitimately initialize gameplay.
            foreach (var id in new[] { WorldId.Echo, WorldId.Material })
            {
                if (manager.CurrentWorldId != id) Check(manager.SwitchWorld(id), "warm " + id);
                yield return new WaitForSeconds(0.7f);
            }
            objects = SceneObjects();
            graphics = Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            raycasts = graphics.Select(g => g.raycastTarget).ToArray();
            sibling = image.transform.GetSiblingIndex(); parent = image.transform.parent;
            sortingOrder = image.canvas.sortingOrder; material = image.material;
            Check(!image.raycastTarget, "overlay never receives input");

            foreach (var id in new[] { WorldId.Echo, WorldId.Material })
            {
                Color start = image.color;
                Check(manager.SwitchWorld(id), "commit " + id);
                                Check(filter.IsTransitioning && filter.TargetColor == manager.CurrentWorld.AmbientColor,
                                    "committed world event starts presentation synchronously");
                Check(Near(image.color, start), "switch commits gameplay without synchronous overlay mutation");
                var run = Animation(start, manager.CurrentWorld.AmbientColor, 0.45f, false, id.ToString());
                while (run.MoveNext()) yield return run.Current;
            }
            for (int i = 0; i < 2; i++)
            {
                Color start = image.color;
                Check(i == 0 ? manager.TryEnterFusion() : manager.TryExitFusion(), "Material fusion toggle");
                var run = Animation(start, Color.clear, 0.6f, true, "Material fusion " + i);
                while (run.MoveNext()) yield return run.Current;
            }
            Check(manager.SwitchWorld(WorldId.Echo), "Echo fusion setup");
            yield return new WaitForSeconds(0.55f);
            foreach (bool enter in new[] { true, false })
            {
                Color start = image.color;
                Check(enter ? manager.TryEnterFusion() : manager.TryExitFusion(), "Echo fusion toggle");
                var run = Animation(start, enter ? Color.clear : manager.CurrentWorld.AmbientColor, 0.6f, true, "Echo fusion " + enter);
                while (run.MoveNext()) yield return run.Current;
            }

            Check(manager.SwitchWorld(WorldId.Material), "pause transition setup");
            yield return new WaitForSeconds(0.12f);
            Time.timeScale = 0;
            // The frame that set timeScale still has its old deltaTime; capture after that LateUpdate.
            yield return null;
            Color frozen = image.color; float elapsed = (float)Get(filter, "elapsed");
            yield return new WaitForSecondsRealtime(0.2f);
            Check(filter.IsTransitioning && Near(image.color, frozen) && (float)Get(filter, "elapsed") == elapsed, "paused real frames freeze overlay and transition clock");
            Check(!manager.SwitchWorld(WorldId.Echo) && !manager.TryEnterFusion(), "paused requests rejected");
            Time.timeScale = 1;
            yield return new WaitForSeconds(0.55f);

            Check(manager.SwitchWorld(WorldId.Echo), "rapid world setup");
            yield return new WaitForSeconds(0.12f);
            Color visible = image.color;
            Check(manager.SwitchWorld(WorldId.Material), "rapid reverse world");
            var rapid = Animation(visible, manager.CurrentWorld.AmbientColor, 0.45f, false, "rapid world retarget");
            while (rapid.MoveNext()) yield return rapid.Current;
            Check(manager.TryEnterFusion(), "rapid fusion setup");
            yield return new WaitForSeconds(0.15f);
            visible = image.color;
            Check(manager.TryExitFusion(), "rapid fusion reverse");
            rapid = Animation(visible, Color.clear, 0.6f, true, "rapid fusion retarget");
            while (rapid.MoveNext()) yield return rapid.Current;

            Check(manager.SwitchWorld(WorldId.Echo), "disable mid-animation setup");
            yield return new WaitForSeconds(0.1f);
            filter.enabled = false;
            Check(image.color == Color.clear && !filter.IsTransitioning, "disable immediately clears overlay and animation");
            yield return new WaitForSeconds(0.1f);
            Check(image.color == Color.clear, "disabled overlay stays clear");
            filter.enabled = true;
            yield return null;
            Check(!filter.IsTransitioning && Near(image.color, manager.CurrentWorld.AmbientColor), "reenable establishes Echo without flash");
            Check(manager.TryEnterFusion(), "disable fusion setup");
            yield return new WaitForSeconds(0.1f);
            filter.enabled = false; filter.enabled = true;
            yield return null;
            Check(!filter.IsTransitioning && image.color == Color.clear, "reenable while fused clears pulse without replay");
            Check(manager.TryExitFusion(), "leave fused cleanup test");
            yield return new WaitForSeconds(0.7f);

            Color stable = image.color;
            Check(!manager.SwitchWorld(manager.CurrentWorldId) && !manager.SwitchWorld((WorldId)int.MaxValue)
                && !manager.TryExitFusion(), "same/invalid world and unfused exit rejected");
            UIController.instance.levelUpPanel.SetActive(true);
            Check(!manager.SwitchWorld(WorldId.Material) && !manager.TryEnterFusion(), "upgrade panel rejects changes");
            yield return null;
            UIController.instance.levelUpPanel.SetActive(false);
            yield return new WaitForSeconds(0.15f);
            Check(!filter.IsTransitioning && Near(image.color, stable), "rejections do not start animation");
            Check(manager.TryEnterFusion(), "duplicate fusion setup");
            yield return new WaitForSeconds(0.7f);
            Check(!manager.TryEnterFusion() && !manager.SwitchWorld(WorldId.Material), "fused duplicate and world switch rejected");
            yield return null;
            Check(!filter.IsTransitioning && image.color == Color.clear, "fused rejection has no pulse");
            Check(manager.TryExitFusion(), "final fusion exit");
            yield return new WaitForSeconds(0.7f);
            Invariants();
        }
        finally
        {
            Time.timeScale = scale; UIController.instance.levelUpPanel.SetActive(panel);
            filter.enabled = true;
            foreach (var pair in saved) if (pair.Key != null) pair.Key.enabled = pair.Value;
        }
        Debug.Log("WorldTransitionChecks: " + Passed + " passed, " + Failed + " failed");
    }

    static IEnumerator Animation(Color start, Color target, float duration, bool fusion, string label)
    {
        float began = Time.time;
        bool midpoint = false, active = false;
        bool colors = true, bounds = true, targetCorrect = true, retarget = false;
        float maxAlpha = 0, settled = -1;
        int samples = 0;
        do
        {
            yield return null; // sample preceding real LateUpdate, once per game frame
            float elapsed = (float)Get(filter, "elapsed");
            float p = Mathf.Clamp01(elapsed / duration);
            if (samples++ == 0) retarget = Near((Color)Get(filter, "startColor"), start);
            targetCorrect &= Near(filter.TargetColor, target);
            active |= filter.IsTransitioning;
            midpoint |= p >= 0.35f && p <= 0.65f;
            Color expected = Color.Lerp(start, target, p * p * (3f - 2f * p));
            float strength = fusion ? 0.045f : 0.06f;
                        float pulse = strength * 16f * p * p * (1f - p) * (1f - p);
            float baseAlpha = expected.a * (1 - pulse), alpha = baseAlpha + pulse;
            if (pulse > 0 && alpha > 0)
            {
                var manager = Object.FindFirstObjectByType<WorldManager>();
                                Color cool = manager.IsFused ? (Color)Get(filter, "fusionTint")
                                    : manager.CurrentWorldId == WorldId.Material ? (Color)Get(filter, "materialTransitionTint")
                                    : manager.CurrentWorld.AmbientColor;
                expected = new Color((expected.r * baseAlpha + cool.r * pulse) / alpha,
                    (expected.g * baseAlpha + cool.g * pulse) / alpha, (expected.b * baseAlpha + cool.b * pulse) / alpha, alpha);
            }
            colors &= Near(image.color, expected);
            bounds &= image.color.a >= 0 && image.color.a <= Mathf.Max(start.a, target.a) + strength + 0.0001f;
            maxAlpha = Mathf.Max(maxAlpha, image.color.a);
            if (!filter.IsTransitioning) { settled = Time.time - began; break; }
        } while (Time.time - began < duration + Mathf.Max(0.1f, 3 * Time.deltaTime));
        Check(retarget, label + " starts from actual visible color");
        Check(active && midpoint && colors && targetCorrect, label + " real-frame midpoint and full composited curve");
        Check(bounds, label + " alpha bounded; peak=" + maxAlpha);
        Check(settled >= duration - Mathf.Max(0.05f, 2 * Time.deltaTime)
            && settled <= duration + Mathf.Max(0.1f, 3 * Time.deltaTime)
            && Near(image.color, target) && !filter.IsTransitioning, label + " actual endpoint within duration tolerance; seconds=" + settled + "; samples=" + samples);
        if (fusion && start.a == 0 && target.a == 0)
            Check(maxAlpha > 0.035f && maxAlpha <= 0.0451f, label + " clear -> subtle cool pulse -> clear");
        Invariants();
    }
    static HashSet<int> SceneObjects() => new HashSet<int>(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(o => o.GetInstanceID()));
    static void Invariants()
    {
        Check(objects.SetEquals(SceneObjects()), "no scene object/effect spawned or removed");
        Check(graphics.Select((g, i) => g != null && g.raycastTarget == raycasts[i]).All(v => v)
            && !image.raycastTarget && image.transform.parent == parent && image.transform.GetSiblingIndex() == sibling
            && image.canvas.sortingOrder == sortingOrder && image.material == material, "UI raycasts, overlay hierarchy, sorting and material unchanged");
    }
    static object Get(object o, string n) => o.GetType().GetField(n, Fields).GetValue(o);
    static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.0002f && Mathf.Abs(a.g - b.g) < 0.0002f && Mathf.Abs(a.b - b.b) < 0.0002f && Mathf.Abs(a.a - b.a) < 0.0002f;
    static void Check(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("WorldTransitionChecks " + (ok ? "PASS " : "FAIL ") + label);
    }
}
