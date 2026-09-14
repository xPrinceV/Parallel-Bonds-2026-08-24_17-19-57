using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Compile this file in memory and StartCoroutine(WorldFlipChecks.Run()) on a shared host.
// Use a disposable, freshly opened Main Play session, focused Game view, and no input.
// Tools is deliberately outside Assets. This does not install a scene component or exit Unity.
public sealed class ProjectileProbe : MonoBehaviour, IWorldProjectile
{
    public int Calls { get; private set; }
    public int Despawns { get; private set; }
    public void Despawn()
    {
        Calls++;
        if (Despawns != 0) return;
        Despawns++;
        gameObject.SetActive(false);
    }
}

public static class WorldFlipChecks
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static bool running;

    public static IEnumerator Run()
    {
        if (running) throw new InvalidOperationException("WorldFlipChecks already running");
        running = true;
        Passed = Failed = 0;
        // Flatten nested iterators for hosts which only forward Current to Unity. This also
        // records exceptions and disposes the fixture on failure, including screenshot errors.
        var stack = new Stack<IEnumerator>();
        stack.Push(new Session().Execute());
        try
        {
            while (stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                Exception error = null;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception e) { error = e; }
                if (error != null)
                {
                    Check(false, "aborted: " + error);
                    break;
                }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            running = false;
            Debug.Log("WorldFlipChecks: " + Passed + " Passed / " + Failed + " Failed. " +
                "Limits: structural HUD/RT checks plus PNGs, not pixel-sharpness proof; " +
                "probes validate interface dispatch/idempotence, not the five real projectile lifecycles. " +
                "Requires rendered Game frames; headless/unfocused EndOfFrame can stall. Use a disposable session.");
        }
    }

    sealed class Session
    {
        StateSwitchController flow;
        WorldFlipPresentation view;
        WorldManager manager;
        Camera camera;
        RenderTexture target;
        Matrix4x4 projection;
        Quaternion rotation;
        Vector3 scale;
        float cameraZ, deadline, origin;
        int commits, midpoints;
        WorldId lastWorld;
        readonly List<float> commitTimes = new List<float>();
        readonly List<float> commitFrames = new List<float>();
        readonly Dictionary<string, bool> evidence = new Dictionary<string, bool>();
        readonly HashSet<string> shots = new HashSet<string>();
        readonly List<HudState> hud = new List<HudState>();
        readonly List<GameObject> created = new List<GameObject>();
        ProjectileProbe outgoing, incoming;
        World outgoingWorld;
        bool natural;
        int clearFrames, warningFrames, contractFrames, expandFrames, edgeFrames;
        float previousScale = 1f;
        bool previousFlip;
        float previousProgress;

        public IEnumerator Execute()
        {
            deadline = Time.realtimeSinceStartup + 95f;
            for (int i = 0; i < 3; i++) yield return null;
            manager = Object.FindFirstObjectByType<WorldManager>();
            flow = Object.FindFirstObjectByType<StateSwitchController>();
            view = Object.FindFirstObjectByType<WorldFlipPresentation>();
            Require(Application.isPlaying && SceneManager.GetActiveScene().name == "Main" &&
                manager != null && manager.IsInitialized && !manager.IsFused && flow != null &&
                flow.isActiveAndEnabled && view != null && view.isActiveAndEnabled && Time.timeScale == 1f,
                "fresh initialized Main after three frames, normal speed");
            Require((bool)Get(flow, "initialized") && flow.RemainingTime > 29f && !flow.IsFlipping,
                "start near beginning of untouched first interval");
            Require((float)Get(flow, "timer") == 30f && (float)Get(flow, "warningDuration") == 5f &&
                (float)Get(flow, "flipDuration") == 0.8f && (float)Get(view, "blurMaxPixels") == 2f,
                "production timer=30 warning=5 flip=.8 blur=2 (read only)");
            Require(Get(view, "stateSwitchController") == flow, "presentation observes tested flow");
            camera = (Camera)Get(view, "worldCamera");
            Require(camera != null && camera.GetComponent<CameraController>() != null, "source follow camera exists");
            var saved = new Dictionary<Behaviour, bool>();
            var health = manager.CurrentWorld.Player.GetComponent<PlayerHealth>();
            float hp = health.currentHealth, maxHp = health.maxHealth, timeScale = Time.timeScale;
            bool background = Application.runInBackground;
            bool flowEnabled = flow.enabled, viewEnabled = view.enabled;
            var panel = UIController.instance == null ? null : UIController.instance.levelUpPanel;
            Require(panel == null || !panel.activeSelf, "no upgrade panel");
            target = camera.targetTexture; projection = camera.projectionMatrix;
            rotation = camera.transform.rotation; scale = camera.transform.localScale; cameraZ = camera.transform.position.z;
            try
            {
                foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (b is Weapon || b is EnemySpawner) { saved.Add(b, b.enabled); b.enabled = false; }
                health.maxHealth = health.currentHealth = 1000000f;
                Application.runInBackground = true;
                foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder >= 0)
                        hud.Add(new HudState(canvas));
                Require(hud.Count > 0, "screen-space HUD found");
                outgoingWorld = manager.CurrentWorld;
                outgoing = Probe(outgoingWorld);
                foreach (var world in Object.FindObjectsByType<World>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (world.Manager == manager && world != outgoingWorld) incoming = Probe(world);
                Require(incoming != null, "outgoing and incoming probe fixtures");
                lastWorld = manager.CurrentWorldId;
                origin = Time.time - (30f - flow.RemainingTime);
                manager.WorldChanged += Changed;
                flow.TransitionMidpoint += Midpoint;
                natural = true;
                while (Time.time - origin < 60.6f) yield return Frame();
                natural = false;
                Check(commits == 2 && midpoints == 2 && commitTimes.Count == 2, "two automatic commits/events only");
                for (int i = 0; i < commitTimes.Count; i++)
                    Check(Mathf.Abs(commitTimes[i] - 30f * (i + 1)) <= 0.1f + commitFrames[i],
                        "automatic commit " + (i + 1) + " at " + commitTimes[i].ToString("F4") + "s (0.1s + one frame)");
                Check(clearFrames > 0 && warningFrames > 0 && contractFrames > 0 && expandFrames > 0 && edgeFrames == 2,
                    "sample coverage: clear/warning/contract/expand and exactly two rendered midpoints");
                Check(shots.Count == 5, "five rendered PNGs written to Logs/WorldFlip-*.png");
                Check(outgoing.Calls == 1 && incoming.Calls == 1 && outgoing.Despawns == 1 && incoming.Despawns == 1,
                    "each world probe cleared exactly once when outgoing");
                // Repeat the interface entry on the mock only, never clear real combat twice.
                ((IWorldProjectile)outgoing).Despawn();
                Check(outgoing.Calls == 2 && outgoing.Despawns == 1 && !outgoing.gameObject.activeSelf,
                    "probe Despawn entry is idempotent");
                Restored("automatic clear");

                int before = commits;
                Require(flow.RequestSwitch(Other()), "manual request accepted without direct commit");
                Check(commits == before && Mathf.Abs(flow.RemainingTime - 5f) < 0.001f, "manual request retains five-second warning");
                yield return Until(() => flow.WarningProgress > 0.1f, 2f);
                yield return Pause("warning");
                yield return Until(() => flow.IsFlipping && flow.FlipProgress > 0f, 6f);
                Require(flow.FlipProgress < 0.5f, "pause/cancel fixture still before midpoint");
                yield return Pause("contract");
                flow.enabled = false;
                yield return Frame();
                Check(commits == before && midpoints == before && !flow.IsFlipping, "disable flow before midpoint cancels without commit");
                Restored("flow disable next frame");
                flow.enabled = true;
                Check(flow.RemainingTime == 30f, "reenable resets full 30-second interval");
                yield return Frame();
                Check(flow.RemainingTime <= 30f && flow.RemainingTime >= 30f - Time.deltaTime - 0.001f,
                    "reenabled clock resumes normally");

                Require(flow.RequestSwitch(Other()), "renderer-disable manual request accepted");
                yield return Until(() => flow.IsFlipping && flow.FlipProgress > 0f, 6f);
                Require(flow.FlipProgress < 0.5f, "renderer fixture before commit");
                view.enabled = false;
                Check(commits == before, "disabling renderer does not synchronously commit");
                yield return Frame();
                Restored("renderer disable next frame");
                yield return Until(() => commits > before && !flow.IsFlipping, 2f);
                Check(commits == before + 1 && midpoints == before + 1, "disabled renderer leaves exactly one scheduled gameplay commit");
                Restored("disabled renderer after flip");
                view.enabled = true;
                yield return Frame();
                Restored("renderer reenable");
                Check(commits == before + 1, "renderer reenable does not replay commit");
                Check((float)Get(flow, "timer") == 30f && (float)Get(flow, "warningDuration") == 5f &&
                    (float)Get(flow, "flipDuration") == 0.8f, "production time parameters remain unchanged");
                foreach (var pair in evidence) Check(pair.Value, pair.Key);
                Check(Time.realtimeSinceStartup < deadline, "completed within 95 real seconds");
            }
            finally
            {
                manager.WorldChanged -= Changed; flow.TransitionMidpoint -= Midpoint;
                Time.timeScale = timeScale;
                // Cancel only test-owned remaining progress, restoring RT through the real lifecycle.
                flow.enabled = false; view.enabled = false;
                flow.enabled = flowEnabled; view.enabled = viewEnabled;
                foreach (var go in created) if (go != null) Object.Destroy(go);
                if (health != null) { health.maxHealth = maxHp; health.currentHealth = hp; }
                foreach (var pair in saved) if (pair.Key != null) pair.Key.enabled = pair.Value;
                Application.runInBackground = background;
            }
        }

        ProjectileProbe Probe(World world)
        {
            var go = new GameObject("WorldFlipChecks_" + world.WorldId);
            created.Add(go); go.transform.SetParent(world.ContentRoot, false);
            return go.AddComponent<ProjectileProbe>();
        }
        WorldId Other() => manager.CurrentWorldId == WorldId.Material ? WorldId.Echo : WorldId.Material;
        void Changed()
        {
            commits++;
            Record("world changes only at FlipProgress=.5", flow.IsFlipping && flow.FlipProgress == 0.5f && manager.CurrentWorldId != lastWorld);
            lastWorld = manager.CurrentWorldId;
            if (natural)
            {
                commitTimes.Add(Time.time - origin); commitFrames.Add(Time.deltaTime);
                Record("midpoint dispatch clears only outgoing", commits == 1
                    ? outgoing.Calls == 1 && !outgoing.gameObject.activeSelf && incoming.Calls == 0 && incoming.gameObject.activeSelf
                    : outgoing.Calls == 1 && incoming.Calls == 1 && !incoming.gameObject.activeSelf);
            }
        }
        void Midpoint()
        {
            midpoints++;
            Record("one midpoint notification per committed world", midpoints == commits && flow.FlipProgress == 0.5f);
        }
        IEnumerator Frame()
        {
            // Must be nested-yielded, never busy-drained: reads same-frame state AFTER LateUpdate/render.
            yield return new WaitForEndOfFrame();
            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("95-second rendered-frame deadline");
            Sample();
        }
        IEnumerator Until(Func<bool> done, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            do
            {
                yield return Frame();
                if (done()) yield break;
            } while (Time.realtimeSinceStartup < end);
            throw new TimeoutException("Expected transition state within " + seconds + "s");
        }
        IEnumerator Pause(string label)
        {
            Time.timeScale = 0f;
            yield return Frame(); // allow the pre-pause deltaTime frame to finish
            float remaining = flow.RemainingTime, p = flow.FlipProgress, warning = flow.WarningProgress;
            float blur = view.BlurPixels, width = view.HorizontalScale, frozenTime = Time.time;
            int count = commits;
            float until = Time.realtimeSinceStartup + 0.2f;
            bool frozen = true;
            do
            {
                yield return Frame();
                frozen &= remaining == flow.RemainingTime && p == flow.FlipProgress && warning == flow.WarningProgress &&
                    blur == view.BlurPixels && width == view.HorizontalScale && Time.time == frozenTime && count == commits;
            } while (Time.realtimeSinceStartup < until);
            Check(frozen, label + " pause freezes time/remaining/progress/blur/scale/commits across real frames");
            Check(!flow.RequestSwitch(Other()), label + " pause rejects request");
            Time.timeScale = 1f;
        }
        void Sample()
        {
            bool geometry = MatrixNear(camera.projectionMatrix, projection) &&
                Quaternion.Angle(camera.transform.rotation, rotation) < 0.001f &&
                Vector3.Distance(camera.transform.localScale, scale) < 0.0001f && Mathf.Abs(camera.transform.position.z - cameraZ) < 0.0001f;
            Record("camera projection/rotation/scale/z never distorted", geometry);
            Vector3 player = manager.CurrentWorld.Player.transform.position;
            Record("follow camera matches current player XY (not historical camera position)",
                Vector2.Distance(camera.transform.position, player) < 0.02f);
            foreach (var state in hud) Record("HUD canvas/rect/material unchanged and outside RT", state.Matches());
            bool displayOutput = false;
            foreach (var output in Camera.allCameras)
                if (output.isActiveAndEnabled && output.targetTexture == null && output.targetDisplay == camera.targetDisplay)
                    displayOutput = true;
            Record("Game View always has a camera rendering to the target display", displayOutput);
            if (view.enabled && (view.BlurPixels > 0f || view.HorizontalScale < 1f))
            {
                var rt = (RenderTexture)Get(view, "capture");
                var canvas = (Canvas)Get(view, "runtimeCanvas");
                var image = (RawImage)Get(view, "worldImage");
                bool onlySource = rt != null && rt.IsCreated() && camera.targetTexture == rt &&
                    (Camera)Get(view, "capturedCamera") == camera && canvas != null && canvas.enabled &&
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder == -200 &&
                    image != null && image.texture == rt && !image.raycastTarget;
                foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (other != camera && other.targetTexture == rt) onlySource = false;
                Record("only source camera targets capture RT, displayed below sharp HUD", onlySource);
                var outputCamera = (Camera)Get(view, "displayCamera");
                Record("display camera presents UI without rerendering world geometry", outputCamera != null
                    && outputCamera.isActiveAndEnabled && outputCamera.targetTexture == null
                    && outputCamera.cullingMask == 0 && outputCamera.depth > camera.depth
                    && outputCamera.GetComponent<AudioListener>() == null);
            }
            if (!natural) return;
            float elapsed = Time.time - origin;
            if (elapsed < 25f)
            {
                clearFrames++;
                Record("first 25 seconds no blur/capture/contraction", view.BlurPixels == 0f && view.HorizontalScale == 1f && camera.targetTexture == target);
            }
            if (commits == 0 && flow.RemainingTime <= 5f)
            {
                warningFrames++;
                Record("25-30s blur=2*(5-RemainingTime)/5", Mathf.Abs(view.BlurPixels - 2f * (5f - flow.RemainingTime) / 5f) < 0.001f);
                if (flow.RemainingTime > 0.4f) Record("no contraction before 29.6s", !flow.IsFlipping && view.HorizontalScale == 1f);
                if (flow.WarningProgress > 0.45f && !flow.IsFlipping) Shot("warning");
            }
            if (flow.IsFlipping)
            {
                float p = flow.FlipProgress;
                float expected = Mathf.Lerp((float)Get(view, "minimumHorizontalScale"), 1f,
                    Mathf.SmoothStep(0f, 1f, Mathf.Abs(Mathf.Cos(Mathf.PI * p))));
                Record("rendered horizontal scale follows contract/expand curve", Mathf.Abs(expected - view.HorizontalScale) < 0.001f);
                if (p < 0.5f)
                {
                    contractFrames++;
                    if (!previousFlip) Record("contraction begins at 29.6s each cycle", Mathf.Abs(elapsed - (commits * 30f + 29.6f)) <= 0.1f + Time.deltaTime);
                    if (previousFlip && previousProgress < 0.5f) Record("contraction is monotonic", view.HorizontalScale <= previousScale + 0.0001f);
                    if (p > 0.1f) Shot("contract");
                }
                else if (p == 0.5f)
                {
                    edgeFrames++;
                    Record("rendered midpoint is edge-on and maximally blurred", Mathf.Abs(view.HorizontalScale - (float)Get(view, "minimumHorizontalScale")) < 0.001f && view.BlurPixels == 2f);
                    Shot("midpoint");
                }
                else
                {
                    expandFrames++;
                    Record("expansion clears blur smoothly", Mathf.Abs(view.BlurPixels - 2f * (1f - Mathf.SmoothStep(0f, 1f, (p - 0.5f) * 2f))) < 0.001f);
                    if (previousFlip && previousProgress >= 0.5f) Record("expansion is monotonic", view.HorizontalScale >= previousScale - 0.0001f);
                    if (p > 0.65f) Shot("expand");
                }
            }
            else if (commits > 0 && flow.WarningProgress == 0f)
            {
                Record("clear frames restore original camera target and visual state", IsRestored());
                Shot("clear");
            }
            previousFlip = flow.IsFlipping; previousProgress = flow.FlipProgress; previousScale = view.HorizontalScale;
        }
        bool IsRestored()
        {
            var canvas = (Canvas)Get(view, "runtimeCanvas");
            var outputCamera = (Camera)Get(view, "displayCamera");
            return (outputCamera == null || !outputCamera.enabled)
                && camera.targetTexture == target && !(bool)Get(view, "capturing") &&
                (RenderTexture)Get(view, "capture") == null && (canvas == null || !canvas.enabled) &&
                view.BlurPixels == 0f && view.HorizontalScale == 1f && MatrixNear(camera.projectionMatrix, projection) &&
                Quaternion.Angle(camera.transform.rotation, rotation) < 0.001f && Vector3.Distance(camera.transform.localScale, scale) < 0.0001f &&
                Mathf.Abs(camera.transform.position.z - cameraZ) < 0.0001f &&
                Vector2.Distance(camera.transform.position, manager.CurrentWorld.Player.transform.position) < 0.02f;
        }
        void Restored(string label) => Check(IsRestored(), label + ": RT/projection/transform restored, camera follows current player");
        void Record(string label, bool ok) { evidence[label] = (!evidence.ContainsKey(label) || evidence[label]) && ok; }
        void Shot(string stage)
        {
            if (!shots.Add(stage)) return;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(directory);
            Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Require(texture != null, "capture " + stage);
                string path = Path.Combine(directory, "WorldFlip-" + stage + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log("WorldFlipChecks screenshot " + path + "; t=" + (Time.time - origin) + "; p=" + flow.FlipProgress);
            }
            finally { if (texture != null) Object.Destroy(texture); }
        }
    }

    sealed class HudState
    {
        readonly Canvas canvas;
        readonly RectTransform rect;
        readonly RenderMode mode;
        readonly int order, display;
        readonly Camera camera;
        readonly Transform parent;
        readonly Vector2 min, max;
        Vector2 size, pivot;
        Vector3 position, scale;
        readonly Quaternion rotation;
        readonly Graphic[] graphics;
        readonly Material[] materials;
        readonly string canvasPath;
        bool awaitingFirstLayout;
        bool reportedFailure;
        public HudState(Canvas value)
        {
            canvas = value; rect = value.GetComponent<RectTransform>(); mode = value.renderMode;
            order = value.sortingOrder; display = value.targetDisplay; camera = value.worldCamera; parent = rect.parent;
            min = rect.anchorMin; max = rect.anchorMax; size = rect.sizeDelta; pivot = rect.pivot;
            position = rect.anchoredPosition3D; scale = rect.localScale; rotation = rect.localRotation;
            canvasPath = PathOf(value.transform) + " (#" + value.GetInstanceID() + ")";
            awaitingFirstLayout = !value.isActiveAndEnabled;
            graphics = value.GetComponentsInChildren<Graphic>(true); materials = new Material[graphics.Length];
            for (int i = 0; i < graphics.Length; i++) materials[i] = graphics[i].material;
        }
        public bool Matches()
        {
            if (canvas == null) return Mismatch("Canvas destroyed", "present", "null");
            if (rect == null) return Mismatch("RectTransform destroyed", "present", "null");
            if (canvas.renderMode != mode) return Mismatch("Canvas.renderMode", mode, canvas.renderMode);
            if (canvas.sortingOrder != order) return Mismatch("Canvas.sortingOrder", order, canvas.sortingOrder);
            if (canvas.targetDisplay != display) return Mismatch("Canvas.targetDisplay", display, canvas.targetDisplay);
            if (canvas.worldCamera != camera) return Mismatch("Canvas.worldCamera", Describe(camera), Describe(canvas.worldCamera));
            if (rect.parent != parent) return Mismatch("RectTransform.parent", Describe(parent), Describe(rect.parent));
            if (rect.anchorMin != min) return Mismatch("RectTransform.anchorMin", min, rect.anchorMin);
            if (rect.anchorMax != max) return Mismatch("RectTransform.anchorMax", max, rect.anchorMax);

            if (rect.localRotation != rotation) return Mismatch("RectTransform.localRotation", rotation, rect.localRotation);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null) return Mismatch("Graphic[" + i + "] destroyed", "present", "null");
                if (graphics[i].material != materials[i])
                    return Mismatch("Graphic.material " + PathOf(graphics[i].transform) + " (#" + graphics[i].GetInstanceID() + ")",
                        Describe(materials[i]), Describe(graphics[i].material));
            }

            // Inactive root overlay canvases have not received their display/CanvasScaler
            // layout yet. Only their screen-derived size, pivot, position and scale get a one-time
            // baseline at the first active EndOfFrame. Ownership, anchors, rotation and
            // materials above remain guarded even while inactive. Never rebase on reactivation.
            if (awaitingFirstLayout)
            {
                if (!canvas.isActiveAndEnabled) return true;
                Debug.Log("WorldFlipChecks HUD first active rendered layout: " + canvasPath +
                    "; sizeDelta " + size.ToString("F6") + " -> " + rect.sizeDelta.ToString("F6") +
                    "; anchoredPosition3D " + position.ToString("F6") + " -> " + rect.anchoredPosition3D.ToString("F6") +
                    "; localScale " + scale.ToString("F6") + " -> " + rect.localScale.ToString("F6") +
                    "; frame=" + Time.frameCount);
                size = rect.sizeDelta; position = rect.anchoredPosition3D; scale = rect.localScale; pivot = rect.pivot;
                awaitingFirstLayout = false;
            }
            if (rect.pivot != pivot) return Mismatch("RectTransform.pivot", pivot, rect.pivot);
            if (rect.sizeDelta != size) return Mismatch("RectTransform.sizeDelta", size.ToString("F6"), rect.sizeDelta.ToString("F6"));
            if (rect.anchoredPosition3D != position) return Mismatch("RectTransform.anchoredPosition3D", position.ToString("F6"), rect.anchoredPosition3D.ToString("F6"));
            if (rect.localScale != scale) return Mismatch("RectTransform.localScale", scale.ToString("F6"), rect.localScale.ToString("F6"));
            return true;
        }
        bool Mismatch(string field, object expected, object actual)
        {
            if (!reportedFailure)
            {
                reportedFailure = true;
                Debug.LogWarning("WorldFlipChecks HUD FIRST MISMATCH: canvas=" + canvasPath +
                    "; field=" + field + "; expected=" + expected + "; actual=" + actual +
                    "; active=" + (canvas != null && canvas.isActiveAndEnabled) +
                    "; awaitingFirstLayout=" + awaitingFirstLayout + "; frame=" + Time.frameCount + "; time=" + Time.time);
            }
            return false;
        }
        static string Describe(Object value) => value == null ? "null" : value.name + " (#" + value.GetInstanceID() + ")";
        static string PathOf(Transform value)
        {
            string path = value.name;
            while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
    }
    static object Get(object value, string name) => value.GetType().GetField(name, Fields).GetValue(value);
    static bool MatrixNear(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 0.0001f) return false;
        return true;
    }
    static void Require(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Check(true, label);
    }
    static void Check(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("WorldFlipChecks " + (ok ? "PASS " : "FAIL ") + label);
    }
}
