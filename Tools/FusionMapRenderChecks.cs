using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Disposable rendered Play fixture. Run through FusionMapRenderChecksBatch in the isolated project.
// Camera callbacks only latch metadata; the SAME raw WorldFlipPresentation capture is read at EOF.
public sealed class FusionMapRenderChecks : IDisposable
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static string OutputDirectory { get; set; }
    public static string Termination { get; set; } = "Complete";
    public static string Position { get; set; } = "Center";
    // Face-on fusion frames always enforce <=1% clear. Opt in to that gate for the normal reference too.
    public static bool VisualNoClear { get; set; }
    public static bool CameraGeometry { get; set; }
    const float FaceOnScale = .7f;
    const double MaxClearFraction = .01;
    [Serializable] public sealed class Frame
    {
        public string kind, entryWorld, displayWorld, image, position;
        public Color32 calibratedClearRgb;
        public int fullPixels;
        public double fullClearFraction;
        public int frame, cameraId, textureId, width, height, endCameraFrame = -1, endFrame = -1;
        public float turns, progress, horizontalScale, timeScale, cameraAspect, orthographicSize;
        public Rect cameraRect;
        public Vector3 cameraPosition, heroPosition;
        public bool flipping, capturing, secondaryActiveAtBegin, secondaryActiveAtEOF, backgroundOnly;
        public int secondaryEffectiveColliders, secondaryNativeHits, eofEffectiveColliders, eofNativeHits;
        public int pixels, uniqueColors;
        public double meanR, meanG, meanB, variance, blueFraction, clearFraction;
        public int minR = 255, minG = 255, minB = 255, maxR, maxG, maxB;
    }
    [Serializable] public sealed class Summary
    {
        public string gpu, api, unity, entryWorld, termination, position;
        public Vector3 fixtureHeroPosition;
        public bool visualNoClear, cameraGeometry;
        public int geometryPassed, geometryFailed;
        public double referenceFullClearFraction, entryFaceOnClearMin, entryFaceOnClearMax, otherFaceOnClearMin, otherFaceOnClearMax;
        public int entryFaceOnClearFailures, otherFaceOnClearFailures;
        public int passed, failed, rendered, readbacks, entryFrames, otherFrames, entryBad, otherBad;
        public int entryFaceOn, otherFaceOn, physicsFailures, metadataFailures;
    }
    WorldManager manager;
    RunStageController run;
    FusionTransitionController flow;
    FusionTransitionPresentation view;
    WorldFlipPresentation flip;
    World entry, other;
    Camera camera;
    Vector3 fixturePosition;
    Rect playableBounds, originalCameraRect;
    float fixtureAspect, fixtureOrtho, fixtureZ, originalAspect;
    int originalPixelWidth, originalPixelHeight, geometryPassed, geometryFailed;
    RenderTexture fixtureTarget, originalTarget, pendingTexture;
    Texture2D readback;
    Frame pending;
    readonly List<Frame> samples = new List<Frame>();
    readonly HashSet<string> images = new HashSet<string>();
    Dictionary<Collider2D, bool> colliders;
    Dictionary<Rigidbody2D, bool> bodies;
    Dictionary<Renderer, bool> renderers;
    readonly Dictionary<Behaviour, bool> behaviours = new Dictionary<Behaviour, bool>();
    readonly List<Action> restore = new List<Action>();
    StreamWriter rows;
    string kind;
    bool observing, disposed, secondaryOriginallyActive;
    float originalScale;
    bool originalBackground;
    int rendered, readbacks, physicsFailures, metadataFailures;
    Color32 clearColor;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    Exception callbackError;

    public static IEnumerator Run()
    {
        Passed = Failed = 0;
        var test = new FusionMapRenderChecks();
        var stack = new Stack<IEnumerator>();
        stack.Push(test.Execute());
        try
        {
            while (stack.Count > 0)
            {
                bool moved = false;
                object next = null;
                Exception error = null;
                try
                {
                    if (test.clock.Elapsed.TotalSeconds > 65) throw new TimeoutException("65s focused Play deadline");
                    if (test.callbackError != null) throw test.callbackError;
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                }
                catch (Exception e) { error = e; }
                if (error != null) { Check(false, "ABORT " + error); break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                var nested = next as IEnumerator;
                if (nested != null) stack.Push(nested);
                else yield return next;
            }
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            test.SaveSummary();
            test.Dispose();
        }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying && !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
            "native rendered editor required (no batchmode/nographics)");
        Require(!string.IsNullOrEmpty(OutputDirectory), "explicit artifact directory required");
        Directory.CreateDirectory(OutputDirectory);
        rows = new StreamWriter(Path.Combine(OutputDirectory, "frames.jsonl"));
        rows.AutoFlush = true;
        originalScale = Time.timeScale; originalBackground = Application.runInBackground;
        Time.timeScale = 1; Application.runInBackground = true;
        manager = All<WorldManager>().Single(m => m.isActiveAndEnabled);
        run = All<RunStageController>().Single(r => r.isActiveAndEnabled);
        flow = All<FusionTransitionController>().Single(f => f.isActiveAndEnabled);
        view = All<FusionTransitionPresentation>().Single(v => v.isActiveAndEnabled);
        flip = All<WorldFlipPresentation>().Single(f => f.isActiveAndEnabled);
        entry = manager.CurrentWorld;
        other = All<World>().Single(w => w.Manager == manager && w != entry);
        camera = flip.WorldCamera;
        Require(manager.IsInitialized && !manager.IsFused && camera != null && camera.orthographic, "initialized normal world and orthographic camera");
        Quiet();
        var switchFlow = All<StateSwitchController>().Single(s => s.isActiveAndEnabled);
        bool automatic = switchFlow.AutomaticSwitchingEnabled;
        restore.Add(() => { if (switchFlow != null) switchFlow.AutomaticSwitchingEnabled = automatic; });
        switchFlow.AutomaticSwitchingEnabled = false;
        Require(Position == "Center" || Position == "RightEdge", "known position fixture");
        fixturePosition = Vector3.zero;
        if (Position == "RightEdge")
        {
            Rect playable = new Rect(); Vector2 offset; float radius;
            Require(entry.Map != null && entry.Map.TryGetPlayableRect(out playable), "RightEdge playable rectangle");
            Require(WorldMap.TryGetFootprint(entry.Player, out offset, out radius), "RightEdge actual player footprint");
            fixturePosition = new Vector3(playable.xMax - radius - .1f, playable.center.y, 0);
            Debug.Log("FUSION_MAP_RENDER FIXTURE RightEdge hero=" + fixturePosition.ToString("F6")
                + "; radius=" + radius + "; offset=" + offset + "; playable=" + playable + "; real camera follow remains enabled");
        }
        Require(camera.GetComponent<CameraController>() != null && camera.GetComponent<CameraController>().isActiveAndEnabled,
            "real production CameraController remains enabled for before/after comparison");
        foreach (var w in new[] { entry, other })
        {
            Vector3 position = w.Player.transform.position;
            float speed = w.Player.moveSpeed;
            restore.Add(() => { if (w != null && w.Player != null) { w.Player.transform.position = position; w.Player.moveSpeed = speed; } });
            w.Player.moveSpeed = 0;
            w.Player.transform.position = fixturePosition;
            var body = w.Player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                var constraints = body.constraints; Vector2 bodyPosition = body.position, velocity = body.linearVelocity;
                restore.Add(() => { if (body != null) { body.constraints = constraints; body.position = bodyPosition; body.linearVelocity = velocity; } });
                body.position = fixturePosition; body.linearVelocity = Vector2.zero; body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }
        Vector3 cameraPosition = camera.transform.position;
        restore.Add(() => { if (camera != null) camera.transform.position = cameraPosition; });
        camera.transform.position = new Vector3(fixturePosition.x, fixturePosition.y, cameraPosition.z);
        originalTarget = camera.targetTexture;
        originalPixelWidth = camera.pixelWidth; originalPixelHeight = camera.pixelHeight;
        originalCameraRect = camera.rect; originalAspect = camera.aspect;
        fixtureTarget = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32) { name = "FusionMapRenderChecks reference target" };
        Require(fixtureTarget.Create(), "640x360 reference target created");
        camera.targetTexture = fixtureTarget;
        fixtureAspect = camera.aspect; fixtureOrtho = camera.orthographicSize; fixtureZ = camera.transform.position.z;
        Require(entry.Map.TryGetPlayableRect(out playableBounds), "camera expectation playable rectangle");
        Physics2D.SyncTransforms();
        foreach (var w in new[] { entry, other })
        {
            Rect rect = new Rect();
            Require(w.Map != null && w.Map.TryGetPlayableRect(out rect), "authored playable rectangle " + w.WorldId);
            float halfY = camera.orthographicSize * .5f, halfX = halfY * (640f / 360f);
            Vector2 center = camera.transform.position;
            bool inside = rect.Contains(center - new Vector2(halfX, halfY)) && rect.Contains(center + new Vector2(halfX, halfY));
            string containment = "central 50% initial viewport inside " + w.WorldId + " arena=" + inside
                + "; rect=" + rect + "; camera=" + center + "; ortho=" + camera.orthographicSize;
            if (Position == "RightEdge") Debug.Log("FUSION_MAP_RENDER INFO " + containment);
            else Check(inside, containment);
        }
        colliders = other.Map.GetComponentsInChildren<Collider2D>(true).ToDictionary(c => c, c => c.enabled);
        bodies = other.Map.GetComponentsInChildren<Rigidbody2D>(true).ToDictionary(b => b, b => b.simulated);
        renderers = new[] { entry, other }.SelectMany(w => w.Map.GetComponentsInChildren<Renderer>(true)).ToDictionary(r => r, r => r.forceRenderingOff);
        secondaryOriginallyActive = other.Map.gameObject.activeSelf;
        // Subscribe after production OnEnable. This also measures the render-only physics state.
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
        RenderPipelineManager.endFrameRendering += EndFrame;
        observing = true;
        kind = "clear-reference";
        int mask = camera.cullingMask;
        restore.Add(() => { if (camera != null) camera.cullingMask = mask; });
        camera.cullingMask = 0;
        yield return FrameEOF();
        camera.cullingMask = mask;
        kind = "entry-reference";
        yield return FrameEOF(); yield return FrameEOF(); yield return FrameEOF();
        Frame reference = samples.Last();
        Check(!reference.backgroundOnly && reference.uniqueColors >= 32 && reference.variance > 25,
            "known active entry map has rich pixels: variance=" + reference.variance + " colors=" + reference.uniqueColors);
        Debug.Log("FUSION_MAP_RENDER REFERENCE " + Position + " fullViewportClear=" + reference.fullClearFraction.ToString("P3")
            + "; frozen unbounded RightEdge baseline is expected to exceed 20% (informational)");
        if (VisualNoClear) Check(reference.fullClearFraction <= MaxClearFraction,
            "visualNoClear normal reference <=1% full-viewport calibrated clear; actual=" + reference.fullClearFraction.ToString("P3"));
        Check(!other.Map.IsActive, "secondary map remains cold before real finale");
        kind = "finale";
        // Configuration validation requires enabled spawners. Enable only around the synchronous API,
        // with no yielded frame; the real TryStartFinale clears existing enemies and stops waves.
        foreach (var spawner in All<EnemySpawner>()) spawner.enabled = true;
        bool accepted;
        try { accepted = run.TryStartFinale(); }
        finally { foreach (var spawner in All<EnemySpawner>()) spawner.enabled = false; }
        Require(accepted && flow.IsPlaying && manager.IsFused && flow.EntryWorld == entry, "real TryStartFinale accepted for " + entry.WorldId);
        Quiet();
        bool paused = false, terminated = false;
        while (flow.IsPlaying)
        {
            yield return FrameEOF();
            if (!flow.IsPlaying) break;
            if (!paused && flow.DisplayWorld == other && flip.HorizontalScale > FaceOnScale
                && (Termination == "Complete" || samples.Any(f => f.kind == "finale" && f.displayWorld == entry.WorldId.ToString() && f.horizontalScale > FaceOnScale)))
            {
                paused = true;
                float turns = flow.Turns, progress = flow.Progress;
                Time.timeScale = 0;
                for (int i = 0; i < 3; i++)
                {
                    yield return FrameEOF();
                    Check(flow.Turns == turns && flow.Progress == progress, "pause freezes actual fusion clock, rendered frame=" + Time.frameCount);
                }
                Time.timeScale = 1;
                if (Termination != "Complete")
                {
                    terminated = true;
                    if (Termination == "Cancel") flow.Cancel();
                    else if (Termination == "Disable") view.enabled = false;
                    else throw new InvalidOperationException("Unknown termination " + Termination);
                }
            }
        }
        kind = "restored";
        yield return FrameEOF(); yield return FrameEOF();
        Check(paused, "pause exercised on an alternate face");
        Check(Termination == "Complete" ? flow.Progress == 1 && flow.Turns == 4 : terminated && !flow.IsPlaying,
            "lifecycle endpoint " + Termination);
        Check(!flip.IsCapturing && camera.targetTexture == fixtureTarget, "capture lifecycle restores exact pre-existing target");
        Check(fixtureTarget.IsCreated() && fixtureTarget.width == 640 && fixtureTarget.height == 360
            && camera.pixelWidth == 640 && camera.pixelHeight == 360
            && samples.All(f => f.width == 640 && f.height == 360), "source capture and restored target dimensions remain 640x360");
        Check(samples.All(f => Near(f.cameraAspect, fixtureAspect) && Near(f.orthographicSize, fixtureOrtho)
            && Near(f.cameraPosition.z, fixtureZ) && f.cameraRect == originalCameraRect), "rendered camera Z, ortho, aspect and viewport rect unchanged");
        if (VisualNoClear)
            Check(samples.Where(f => f.kind == "finale").All(f => NearPosition(f.cameraPosition,
                ExpectedCamera(f.heroPosition, f.orthographicSize, f.cameraAspect, .05f))),
                "rendered camera matches viewport-aware bounds, including expected off-center position near walls");
        Check(other.Map.gameObject.activeSelf == secondaryOriginallyActive && entry.Map.IsActive,
            "lifecycle restores entry-map-only activation");
        Check(colliders.All(p => p.Key != null && p.Key.enabled == p.Value) && bodies.All(p => p.Key != null && p.Key.simulated == p.Value)
            && renderers.All(p => p.Key != null && p.Key.forceRenderingOff == p.Value), "lifecycle restores exact map collider/body/renderer flags");
        Check(physicsFailures == 0, "secondary effective collision and native query failures=" + physicsFailures);
        Check(metadataFailures == 0 && rendered == readbacks, "every rendered capture has same-frame EOF readback: rendered=" + rendered + ", readbacks=" + readbacks + ", mismatch=" + metadataFailures);
        Check(samples.Where(f => f.kind == "finale").All(f => (f.heroPosition - fixturePosition).sqrMagnitude < .000001f),
            "fixed hero coordinates throughout rendered finale: " + fixturePosition.ToString("F6"));
        foreach (var world in new[] { entry, other })
        {
            var face = samples.Where(f => f.kind == "finale" && f.flipping && f.displayWorld == world.WorldId.ToString()).ToArray();
            int bad = face.Count(f => f.backgroundOnly);
            Check(face.Length >= 3 && face.Any(f => f.horizontalScale > .4f), "actual face coverage " + world.WorldId + ": " + face.Length);
            Check(face.Length > 0 && bad == 0, "no background-only raw faces " + world.WorldId + ": " + bad + "/" + face.Length);
            var faceOn = face.Where(f => f.horizontalScale > FaceOnScale).ToArray();
            int clearFailures = faceOn.Count(f => f.fullClearFraction > MaxClearFraction);
            Check(faceOn.Length > 0 && clearFailures == 0, "face-on " + world.WorldId + " <=1% full-viewport calibrated clear: "
                + clearFailures + "/" + faceOn.Length + " fail; min=" + faceOn.Select(f => f.fullClearFraction).DefaultIfEmpty(0).Min().ToString("P3")
                + "; max=" + faceOn.Select(f => f.fullClearFraction).DefaultIfEmpty(0).Max().ToString("P3"));
        }
        observing = false;
        if (CameraGeometry) CheckCameraGeometry();
        camera.targetTexture = originalTarget;
        Check(camera.targetTexture == originalTarget && camera.pixelWidth == originalPixelWidth && camera.pixelHeight == originalPixelHeight
            && Near(camera.aspect, originalAspect) && camera.rect == originalCameraRect,
            "fixture teardown restores original source target, pixel dimensions, aspect and viewport");
    }

    static bool Near(float a, float b) { return Mathf.Abs(a - b) < .0001f; }
    static bool NearPosition(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < .000001f; }
    Vector3 ExpectedCamera(Vector3 hero, float size, float aspect, float padding)
    {
        float xExtent = size * aspect + padding, yExtent = size + padding;
        return new Vector3(playableBounds.width <= 2 * xExtent ? playableBounds.center.x
                : Mathf.Clamp(hero.x, playableBounds.xMin + xExtent, playableBounds.xMax - xExtent),
            playableBounds.height <= 2 * yExtent ? playableBounds.center.y
                : Mathf.Clamp(hero.y, playableBounds.yMin + yExtent, playableBounds.yMax - yExtent), fixtureZ);
    }
    void CheckCameraGeometry()
    {
        // Synchronous calls to the actual production Update; these are geometry/cache probes,
        // not additional GPU frames. No frame is yielded while the temporary configuration exists.
        int passedBefore = Passed, failedBefore = Failed;
        var controller = camera.GetComponent<CameraController>();
        var update = typeof(CameraController).GetMethod("Update", Fields);
        var mapField = typeof(World).GetField("map", Fields);
        var boundaryField = typeof(WorldMap).GetField("boundaryRoot", Fields);
        var hero = entry.Player;
        Vector3 heroPosition = hero.transform.position, cameraPosition = camera.transform.position;
        float size = camera.orthographicSize;
        var temporaryRoot = new GameObject("Camera cache probe (isolated)");
        var temporaryMap = temporaryRoot.AddComponent<WorldMap>();
        WorldMap originalMap = entry.Map;
        try
        {
            Require(update != null && mapField != null && boundaryField != null, "camera geometry probe APIs");
            foreach (float aspect in new[] { 9f / 16f, 16f / 9f, 32f / 9f })
                foreach (Vector2 corner in new[] { playableBounds.min, playableBounds.max,
                    new Vector2(playableBounds.xMin, playableBounds.yMax), new Vector2(playableBounds.xMax, playableBounds.yMin) })
                {
                    camera.aspect = aspect;
                    hero.transform.position = corner;
                    update.Invoke(controller, null);
                    Check(NearPosition(camera.transform.position, ExpectedCamera(hero.transform.position, size, aspect, .05f))
                        && (Vector2)hero.transform.position == corner && Near(camera.orthographicSize, size) && Near(camera.aspect, aspect)
                        && Near(camera.transform.position.z, fixtureZ), "camera corner/aspect=" + aspect + "; hero=" + corner
                        + "; camera=" + camera.transform.position);
                }
            foreach (float aspect in new[] { .1f, 1f, 10f })
            {
                camera.aspect = aspect;
                camera.orthographicSize = Mathf.Max(playableBounds.width, playableBounds.height);
                hero.transform.position = playableBounds.max;
                update.Invoke(controller, null);
                Check(NearPosition(camera.transform.position, ExpectedCamera(hero.transform.position, camera.orthographicSize, aspect, .05f))
                    && (Vector2)hero.transform.position == playableBounds.max && Near(camera.aspect, aspect)
                    && Near(camera.orthographicSize, Mathf.Max(playableBounds.width, playableBounds.height)),
                    "oversized viewport centers only oversized axes without moving hero/zooming; aspect=" + aspect);
            }
            camera.orthographicSize = size; camera.ResetAspect();
            hero.transform.position = playableBounds.max;
            // Same-reference invalid-to-valid configuration: a failed initial bounds lookup must retry.
            mapField.SetValue(entry, temporaryMap);
            update.Invoke(controller, null);
            boundaryField.SetValue(temporaryMap, originalMap.BoundaryRoot);
            update.Invoke(controller, null);
            Check(NearPosition(camera.transform.position, ExpectedCamera(hero.transform.position, size, camera.aspect, .05f)),
                "camera cache recovers when the same map configuration becomes valid");
            // Refresh via actual reference changes, then destroy only the disposable map component.
            mapField.SetValue(entry, originalMap); update.Invoke(controller, null);
            mapField.SetValue(entry, temporaryMap); update.Invoke(controller, null);
            Object.DestroyImmediate(temporaryRoot);
            update.Invoke(controller, null);
            Check(NearPosition(camera.transform.position, new Vector3(hero.transform.position.x, hero.transform.position.y, fixtureZ)),
                "camera cache clears destroyed-map bounds and falls back to target follow");
            mapField.SetValue(entry, originalMap); update.Invoke(controller, null);
            Check(NearPosition(camera.transform.position, ExpectedCamera(hero.transform.position, size, camera.aspect, .05f)),
                "camera cache refreshes when original map reference is restored");
        }
        finally
        {
            mapField.SetValue(entry, originalMap);
            hero.transform.position = heroPosition;
            camera.orthographicSize = size; camera.ResetAspect();
            update.Invoke(controller, null);
            camera.transform.position = cameraPosition;
            if (temporaryRoot != null) Object.DestroyImmediate(temporaryRoot);
            Physics2D.SyncTransforms();
            geometryPassed = Passed - passedBefore; geometryFailed = Failed - failedBefore;
        }
        Check(NearPosition(hero.transform.position, heroPosition) && Near(camera.orthographicSize, size)
            && Near(camera.aspect, fixtureAspect) && entry.Map == originalMap,
            "camera geometry fixture restores hero, dimensions and original map configuration");
    }

    IEnumerator FrameEOF()
    {
        yield return new WaitForEndOfFrame();
        Flush();
    }
    void BeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (!observing || cam != camera) return;
        try
        {
            if (pending != null) { metadataFailures++; return; }
            pendingTexture = camera.targetTexture;
            Require(pendingTexture != null, "actual world camera target exists");
            pending = new Frame {
                kind = kind == "finale" && !flow.IsPlaying ? "handoff" : kind, entryWorld = entry.WorldId.ToString(), displayWorld = flow.IsPlaying ? flow.DisplayWorld.WorldId.ToString() : manager.CurrentWorldId.ToString(),
                position = Position,
                frame = Time.frameCount, cameraId = camera.GetInstanceID(), textureId = pendingTexture.GetInstanceID(), width = pendingTexture.width, height = pendingTexture.height,
                turns = flow.Turns, progress = flow.Progress, horizontalScale = flip.HorizontalScale, timeScale = Time.timeScale,
                cameraPosition = camera.transform.position, heroPosition = entry.Player.transform.position,
                cameraAspect = camera.aspect, orthographicSize = camera.orthographicSize, cameraRect = camera.rect,
                flipping = flow.IsFlipping, capturing = flip.IsCapturing, secondaryActiveAtBegin = other.Map.IsActive,
                secondaryEffectiveColliders = EffectiveColliders(), secondaryNativeHits = NativeHits()
            };
            if (pending.kind == "finale")
            {
                rendered++;
                Require(pending.capturing && pendingTexture == (RenderTexture)Field(flip, "capture"), "sample is actual WorldFlipPresentation.capture");
                if (pending.secondaryEffectiveColliders != 0 || pending.secondaryNativeHits != 0) physicsFailures++;
            }
        }
        catch (Exception e) { callbackError = e; }
    }
    void EndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (pending != null && cam == camera) pending.endCameraFrame = Time.frameCount;
    }
    void EndFrame(ScriptableRenderContext context, Camera[] cameras)
    {
        if (pending != null && cameras.Contains(camera)) pending.endFrame = Time.frameCount;
    }
    void Flush()
    {
        if (callbackError != null) throw callbackError;
        Require(pending != null, "world camera rendered before EOF");
        Frame f = pending;
        if (f.frame != Time.frameCount || f.endCameraFrame != f.frame || f.endFrame != f.frame || pendingTexture == null
            || pendingTexture.GetInstanceID() != f.textureId) metadataFailures++;
        Require(f.frame == Time.frameCount && f.endFrame == f.frame, "refuse stale/mislabeled GPU readback");
        if (readback == null || readback.width != f.width || readback.height != f.height)
        {
            if (readback != null) Object.Destroy(readback);
            readback = new Texture2D(f.width, f.height, TextureFormat.RGB24, false);
        }
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = pendingTexture;
            readback.ReadPixels(new Rect(0, 0, f.width, f.height), 0, 0, false);
            readback.Apply(false, false);
        }
        finally { RenderTexture.active = previous; }
        Color32[] pixels = readback.GetPixels32();
        if (f.kind == "clear-reference") clearColor = pixels[(f.height / 2) * f.width + f.width / 2];
        Measure(f, pixels, clearColor);
        f.secondaryActiveAtEOF = other.Map.IsActive;
        f.eofEffectiveColliders = EffectiveColliders(); f.eofNativeHits = NativeHits();
        if (f.kind == "finale")
        {
            readbacks++;
            if (f.eofEffectiveColliders != 0 || f.eofNativeHits != 0) physicsFailures++;
        }
        string key = f.kind + "-" + f.displayWorld + (f.backgroundOnly ? "-background" : "-terrain")
            + (f.horizontalScale > FaceOnScale ? "-wide" : "-narrow") + (f.fullClearFraction > MaxClearFraction ? "-partial-clear" : "-no-clear");
        if (images.Add(key))
        {
            f.image = "frame-" + f.frame.ToString("D5") + ".png";
            File.WriteAllBytes(Path.Combine(OutputDirectory, f.image), readback.EncodeToPNG());
            File.WriteAllText(Path.Combine(OutputDirectory, "frame-" + f.frame.ToString("D5") + ".json"), JsonUtility.ToJson(f, true));
        }
        rows.WriteLine(JsonUtility.ToJson(f));
        samples.Add(f);
        pending = null; pendingTexture = null;
    }
    static void Measure(Frame f, Color32[] pixels, Color32 clear)
    {
        f.calibratedClearRgb = clear;
        f.fullPixels = pixels.Length;
        int fullClear = 0;
        foreach (Color32 c in pixels)
            if (Math.Abs(c.r - clear.r) <= 4 && Math.Abs(c.g - clear.g) <= 4 && Math.Abs(c.b - clear.b) <= 4) fullClear++;
        f.fullClearFraction = (double)fullClear / f.fullPixels;
        var colors = new HashSet<int>();
        double sumSquares = 0;
        int blue = 0, background = 0;
        for (int y = f.height / 4; y < f.height * 3 / 4; y++)
            for (int x = f.width / 4; x < f.width * 3 / 4; x++)
            {
                Color32 c = pixels[y * f.width + x];
                f.pixels++; f.meanR += c.r; f.meanG += c.g; f.meanB += c.b;
                sumSquares += c.r * c.r + c.g * c.g + c.b * c.b;
                f.minR = Math.Min(f.minR, c.r); f.minG = Math.Min(f.minG, c.g); f.minB = Math.Min(f.minB, c.b);
                f.maxR = Math.Max(f.maxR, c.r); f.maxG = Math.Max(f.maxG, c.g); f.maxB = Math.Max(f.maxB, c.b);
                colors.Add((c.r << 16) | (c.g << 8) | c.b);
                if (c.b > c.r + 20 && c.b > c.g + 10) blue++;
                if (Math.Abs(c.r - clear.r) <= 4 && Math.Abs(c.g - clear.g) <= 4 && Math.Abs(c.b - clear.b) <= 4) background++;
            }
        f.meanR /= f.pixels; f.meanG /= f.pixels; f.meanB /= f.pixels;
        f.variance = Math.Max(0, (sumSquares / f.pixels - f.meanR * f.meanR - f.meanG * f.meanG - f.meanB * f.meanB) / 3);
        f.uniqueColors = colors.Count; f.blueFraction = (double)blue / f.pixels; f.clearFraction = (double)background / f.pixels;
        f.backgroundOnly = f.clearFraction > .97 && f.variance < 16;
    }
    int EffectiveColliders() { return colliders.Keys.Count(c => c != null && c.enabled && c.gameObject.activeInHierarchy && (c.attachedRigidbody == null || c.attachedRigidbody.simulated)); }
    int NativeHits()
    {
        Physics2D.SyncTransforms();
        return Physics2D.OverlapCircleAll(Vector2.zero, 100).Count(c => c.transform.IsChildOf(other.Map.transform));
    }
    void Quiet()
    {
        foreach (var b in All<MonoBehaviour>())
            if (b is Weapon || b is EnemySpawner || b is EnemyController)
            {
                if (!behaviours.ContainsKey(b)) behaviours.Add(b, b.enabled);
                b.enabled = false;
            }
        foreach (var enemy in All<EnemyController>())
        {
            var body = enemy.GetComponent<Rigidbody2D>();
            if (body != null) { bool simulated = body.simulated; restore.Add(() => { if (body != null) body.simulated = simulated; }); body.simulated = false; }
        }
        foreach (var hp in All<PlayerHealth>())
        {
            float current = hp.currentHealth, max = hp.maxHealth;
            restore.Add(() => { if (hp != null) { hp.maxHealth = max; hp.currentHealth = current; } });
            hp.maxHealth = hp.currentHealth = 1000000;
        }
        if (UIController.instance != null) UIController.instance.levelUpPanel.SetActive(false);
        foreach (var gui in All<DeveloperDebugGui>()) gui.SetOpen(false);
    }
    void SaveSummary()
    {
        if (string.IsNullOrEmpty(OutputDirectory)) return;
        var faces = samples.Where(f => f.kind == "finale" && f.flipping).ToArray();
        var entryFace = faces.Where(f => f.displayWorld == f.entryWorld && f.horizontalScale > FaceOnScale).ToArray();
        var otherFace = faces.Where(f => f.displayWorld != f.entryWorld && f.horizontalScale > FaceOnScale).ToArray();
        var s = new Summary {
            position = Position, fixtureHeroPosition = fixturePosition, visualNoClear = VisualNoClear,
            cameraGeometry = CameraGeometry, geometryPassed = geometryPassed, geometryFailed = geometryFailed,
            referenceFullClearFraction = samples.Where(f => f.kind == "entry-reference").Select(f => f.fullClearFraction).DefaultIfEmpty(0).Last(),
            entryFaceOnClearFailures = entryFace.Count(f => f.fullClearFraction > MaxClearFraction),
            otherFaceOnClearFailures = otherFace.Count(f => f.fullClearFraction > MaxClearFraction),
            entryFaceOnClearMin = entryFace.Select(f => f.fullClearFraction).DefaultIfEmpty(0).Min(),
            entryFaceOnClearMax = entryFace.Select(f => f.fullClearFraction).DefaultIfEmpty(0).Max(),
            otherFaceOnClearMin = otherFace.Select(f => f.fullClearFraction).DefaultIfEmpty(0).Min(),
            otherFaceOnClearMax = otherFace.Select(f => f.fullClearFraction).DefaultIfEmpty(0).Max(),
            gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion,
            entryWorld = entry == null ? "unbound" : entry.WorldId.ToString(), termination = Termination,
            passed = Passed, failed = Failed, rendered = rendered, readbacks = readbacks,
            entryFrames = faces.Count(f => f.displayWorld == f.entryWorld), otherFrames = faces.Count(f => f.displayWorld != f.entryWorld),
            entryBad = faces.Count(f => f.displayWorld == f.entryWorld && f.backgroundOnly), otherBad = faces.Count(f => f.displayWorld != f.entryWorld && f.backgroundOnly),
            entryFaceOn = entryFace.Length, otherFaceOn = otherFace.Length,
            physicsFailures = physicsFailures, metadataFailures = metadataFailures
        };
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, "summary.json"), JsonUtility.ToJson(s, true));
        Debug.Log("FUSION_MAP_RENDER SUMMARY " + JsonUtility.ToJson(s));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; observing = false;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        RenderPipelineManager.endFrameRendering -= EndFrame;
        if (flow != null && flow.IsPlaying) flow.Cancel();
        // Stop capture before releasing its pre-existing target, including failed/aborted fixtures.
        if (flip != null) flip.enabled = false;
        if (view != null) view.enabled = false;
        if (camera != null) camera.targetTexture = originalTarget;
        for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
        foreach (var p in behaviours) if (p.Key != null) p.Key.enabled = p.Value;
        if (readback != null) Object.Destroy(readback);
        if (fixtureTarget != null) { fixtureTarget.Release(); Object.Destroy(fixtureTarget); }
        if (rows != null) rows.Dispose();
        Time.timeScale = originalScale; Application.runInBackground = originalBackground;
    }
    static T[] All<T>() where T : Object { return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
    static object Field(object o, string name) { return o.GetType().GetField(name, Fields).GetValue(o); }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Check(bool ok, string message)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("FUSION_MAP_RENDER " + (ok ? "PASS " : "FAIL ") + message);
    }
}
