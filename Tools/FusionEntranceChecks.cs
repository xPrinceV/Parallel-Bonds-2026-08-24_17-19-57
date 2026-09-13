using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Compile in memory with the game's assemblies; host advances Run() once per Play frame.
// Requires a rendered URP Game view. No MonoBehaviour, editor exit, or production timer edits.
public sealed class FusionEntranceChecks : IDisposable
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
    readonly Dictionary<Behaviour, bool> enabledStates = new Dictionary<Behaviour, bool>();
    readonly Dictionary<PlayerHealth, Vector2> healthStates = new Dictionary<PlayerHealth, Vector2>();
    readonly List<WorldManager> subscribed = new List<WorldManager>();
    readonly HashSet<string> faults = new HashSet<string>();
    Dictionary<Renderer, bool> rendererFlags;
    Dictionary<SpriteRenderer, bool> spriteFlags;
    Dictionary<SpriteRenderer, Color> spriteColors;
    Dictionary<Canvas, Vector3> hud;
    WorldManager manager;
    FusionTransitionController flow;
    FusionTransitionPresentation view;
    WorldFlipPresentation flip;
    World[] worlds;
    Camera camera;
    RenderTexture originalTarget;
    Matrix4x4 originalProjection;
    World entry, previousDisplay;
    WorldId entryWorldId;
    int fusionEvents, worldEvents, renderedFrame = -1, sideOns;
    float previousTurns, savedScale;
    bool savedBackground, started, observing, whiteOriginal, whiteFusion, compressed, revealColor;
    bool pauseDone;
    readonly HashSet<string> captures = new HashSet<string>();
    readonly List<string> pendingCaptures = new List<string>();
    Action atEndOfFrame;
    Exception renderError;
    string label;
    double Seconds { get { return clock.Elapsed.TotalSeconds; } }

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("FusionEntranceChecks already running");
        Running = true;
        Passed = Failed = 0;
        var test = new FusionEntranceChecks();
        var stack = new Stack<IEnumerator>();
        stack.Push(test.Execute());
        test.clock.Start();
        try
        {
            // A real coroutine host is required so PNGs include the final overlay UI pass.
            while (stack.Count != 0)
            {
                bool moved = false;
                object next = null;
                Exception error = null;
                try
                {
                    if (test.Seconds > 65) throw new TimeoutException("65s overall deadline; blocked Unity frames cannot be preempted");
                    if (test.renderError != null) throw new InvalidOperationException("endFrameRendering failed", test.renderError);
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                }
                catch (Exception e) { error = e; }
                if (error != null) { Check(false, "aborted: " + error); break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                var nested = next as IEnumerator;
                if (nested != null) stack.Push(nested);
                else
                {
                    yield return new WaitForEndOfFrame();
                    try { test.FlushCaptures(); }
                    catch (Exception e) { test.renderError = e; }
                    yield return null;
                }
            }
        }
        finally
        {
            try { while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose(); }
            finally
            {
                test.Dispose();
                Running = false;
                Debug.Log("FusionEntranceChecks RESULT " + Passed + " Passed / " + Failed + " Failed; "
                    + test.Seconds.ToString("F2") + "s; controlled combat/high HP, rendered EOF evidence only; no EditorExit.");
            }
        }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "host must already be in Play mode");
        Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendered graphics device required");
        savedScale = Time.timeScale;
        savedBackground = Application.runInBackground;
        started = true;
        Time.timeScale = 1f;
        Application.runInBackground = true;
        foreach (string scene in new[] { "Main", "DebugRun" })
        {
            UnhookRendering();
            if (scene == "DebugRun") SceneManager.LoadScene("DebugRun", LoadSceneMode.Single);
            else if (Application.CanStreamedLevelBeLoaded("Main")) SceneManager.LoadScene("Main", LoadSceneMode.Single);
            else
            {
#if UNITY_EDITOR
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
                // Some memory compilers do not define UNITY_EDITOR, even inside the editor.
                var editor = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.SceneManagement.EditorSceneManager"))
                    .FirstOrDefault(t => t != null);
                Require(editor != null, "Main is not runtime-loadable and editor scene loader is unavailable");
                var load = editor.GetMethod("LoadSceneInPlayMode", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(LoadSceneParameters) }, null);
                Require(load != null, "editor Play scene load API exists");
                load.Invoke(null, new object[] { "Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single) });
#endif
            }
            yield return ThreeFrames();
            Bind();
            Require(manager.CurrentWorldId == WorldId.Material, scene + " starts in Material");
            yield return Entrance(WorldId.Material);
            int commits = worldEvents;
            Require(manager.SwitchWorld(WorldId.Echo), "real SwitchWorld(Echo) accepted");
            double switchStart = Seconds;
            while (Seconds - switchStart < 5 || manager.CurrentWorldId != WorldId.Echo || manager.IsWorldTransitioning)
            {
                Require(Seconds - switchStart < 7, "Echo switch completes within 7s including full 5s warning");
                yield return null;
            }
            Check(worldEvents == commits + 1 && manager.CurrentWorldId == WorldId.Echo,
                scene + " real Echo switch committed once after >=5s, no timer edits");
            yield return Entrance(WorldId.Echo);
            yield return Cancellations();
            if (scene == "DebugRun") yield return RestartDuringEntrance();
            yield return DeathDuringEntrance();
        }
    }

    void Bind()
    {
        Scene scene = SceneManager.GetActiveScene();
        manager = InScene<WorldManager>().Single(m => m.isActiveAndEnabled);
        flow = InScene<FusionTransitionController>().Single(f => f.isActiveAndEnabled && Field<WorldManager>(f, "worldManager") == manager);
        view = InScene<FusionTransitionPresentation>().Single(v => v.isActiveAndEnabled && Field<FusionTransitionController>(v, "controller") == flow);
        flip = InScene<WorldFlipPresentation>().Single(v => v.isActiveAndEnabled && Field<FusionTransitionController>(v, "fusionTransition") == flow);
        worlds = InScene<World>().Where(w => w.Manager == manager).ToArray();
        var boundFlow = (FusionTransitionController)typeof(WorldManager).GetProperty("FusionTransition", Fields).GetValue(manager, null);
        var switchFlow = (StateSwitchController)typeof(WorldManager).GetProperty("SwitchFlow", Fields).GetValue(manager, null);
        Require(worlds.Length == 2 && manager.IsInitialized && boundFlow == flow
            && switchFlow != null && switchFlow.isActiveAndEnabled, "active manager owns initialized entrance and switch flow");
        Check(Field<float>(flow, "duration") == 3f && Field<int>(flow, "flipCount") == 4, scene.name + " production duration=3 / flipCount=4 untouched");
        Check(manager.GetComponents<FusionTransitionController>().Length <= 1
            && InScene<FusionTransitionController>().Count(f => Field<WorldManager>(f, "worldManager") == manager) == 1
            && InScene<FusionTransitionPresentation>().Count(v => Field<FusionTransitionController>(v, "controller") == flow) == 1
            && worlds.All(w => w.Player.GetComponents<PlayerFusionVisual>().Length == 1), scene.name + " no duplicate bound components");
        camera = flip.WorldCamera;
        Require(camera != null && camera.isActiveAndEnabled, "world camera exists");
        originalTarget = camera.targetTexture;
        originalProjection = camera.projectionMatrix;
        manager.FusionStateChanged += FusionChanged;
        manager.WorldChanged += WorldChanged;
        subscribed.Add(manager);
        Quiet();
        // Subscribe after production OnEnable so its end-frame restoration runs first.
        RenderPipelineManager.endFrameRendering += EndFrame;
        label = scene.name;
    }

    void Quiet()
    {
        foreach (var b in InScene<MonoBehaviour>())
            if (b is Weapon || b is EnemySpawner || b is EnemyController)
            {
                if (!enabledStates.ContainsKey(b)) enabledStates.Add(b, b.enabled);
                b.enabled = false;
                var body = b.GetComponent<Rigidbody2D>();
                if (body != null && b is EnemyController) body.linearVelocity = Vector2.zero;
            }
        foreach (var hp in InScene<PlayerHealth>())
        {
            if (!healthStates.ContainsKey(hp)) healthStates.Add(hp, new Vector2(hp.currentHealth, hp.maxHealth));
            hp.maxHealth = hp.currentHealth = 1000000f;
        }
        if (UIController.instance != null && UIController.instance.levelUpPanel != null)
            UIController.instance.levelUpPanel.SetActive(false);
        foreach (var gui in InScene<DeveloperDebugGui>()) gui.SetOpen(false);
        Time.timeScale = 1f;
    }

    IEnumerator Entrance(WorldId direction)
    {
        Quiet();
        label = SceneManager.GetActiveScene().name + "-" + direction;
        Require(manager.CurrentWorldId == direction && !manager.IsFused, label + " normal entry world");
        // Baselines are sampled at EOF, never during the camera's temporary renderer suppression.
        yield return AtEOF(() =>
        {
            rendererFlags = InScene<Renderer>().ToDictionary(r => r, r => r.forceRenderingOff);
            spriteFlags = InScene<SpriteRenderer>().ToDictionary(r => r, r => r.enabled);
            spriteColors = spriteFlags.Keys.ToDictionary(r => r, r => r.color);
            hud = InScene<Canvas>().Where(c => c.isActiveAndEnabled && c.isRootCanvas
                && c.sortingOrder >= 0 && manager.IsSharedObject(c.transform))
                .ToDictionary(c => c, HudShape);
        });
        entry = manager.CurrentWorld;
        entryWorldId = manager.CurrentWorldId;
        previousDisplay = entry;
        previousTurns = 0;
        sideOns = 0;
        whiteOriginal = whiteFusion = compressed = revealColor = pauseDone = false;
        faults.Clear(); captures.Clear();
        fusionEvents = worldEvents = 0;
        Require(manager.TryEnterFusion(), label + " immediate TryEnterFusion accepted");
        Check(manager.IsFused && manager.FusionPlayer == entry.Player && flow.IsPlaying && flow.EntryWorld == entry
            && fusionEvents == 1 && worldEvents == 0 && manager.CurrentWorldId == entryWorldId
            && worlds.All(w => w.IsActive && w.ContentRoot.gameObject.activeInHierarchy), label + " synchronous single fusion commit; both contents active");
        Check(!manager.TryEnterFusion() && !manager.TryExitFusion() && fusionEvents == 1 && worldEvents == 0,
            label + " repeated enter/exit rejected while playing");
        observing = true;
        double beginning = Seconds;
        while (flow.IsPlaying)
        {
            Require(Seconds - beginning < 5, label + " entrance within tolerant 5s budget (not exact 3s)");
            yield return null;
            if (!pauseDone && flow.IsPlaying && renderedFrame >= 0)
            {
                pauseDone = true;
                Time.timeScale = 0f;
                float p = flow.Progress, turns = flow.Turns, white = flow.WhiteAmount;
                double until = Seconds + .15;
                while (Seconds < until)
                {
                    yield return null;
                    Expect(flow.IsPlaying && Near(flow.Progress, p) && Near(flow.Turns, turns) && Near(flow.WhiteAmount, white), "0.15s pause freezes entrance clock");
                }
                Check(!manager.TryEnterFusion() && !manager.TryExitFusion(), label + " paused playing rejects enter/exit");
                Time.timeScale = 1f;
            }
        }
        yield return AtEOF(() =>
        {
            Expect(Near(flow.Turns, 4) && Near(flow.Progress, 1) && Near(flow.WhiteAmount, 0), "completion reaches Turns=4, p=1, white=0");
            var material = Field<Material>(view, "material");
            // The overlay may turn off before a final LateUpdate writes zero: the exposed gameplay body must retain its color.
            Expect(!flip.IsCapturing && camera.targetTexture == originalTarget, "completion restores main camera target");
            Expect(material != null, "portrait shader material was created");
            MainView("completed entrance");
        });
        observing = false;
        Check(sideOns == 4, label + " four alternating side-on display swaps, observed=" + sideOns);
        Check(whiteOriginal && whiteFusion && compressed && revealColor && pauseDone,
            label + " white-original frame, opaque white-fusion swap, uncompressed portrait, color reveal and pause observed");
        Check(new[] { "normal", "white-original", "white-fusion", "reveal" }.All(captures.Contains), label + " four EOF keyframe PNGs captured");
        Check(fusionEvents == 1 && worldEvents == 0, label + " only one fusion event and zero world events throughout entrance");
        foreach (string fault in faults) Check(false, label + " " + fault);
        Check(faults.Count == 0, label + " all rendered-frame invariants");
        Require(manager.TryExitFusion(), label + " exit accepted after animation");
        Check(!manager.IsFused && fusionEvents == 2 && worldEvents == 0, label + " exit committed once");
        yield return AtEOF(() =>
        {
            Check(FlagsRestored(), label + " exact forceRenderingOff baseline after exit");
            Check(spriteFlags.All(p => p.Key == null || p.Key.enabled == p.Value), label + " sprite.enabled restored by gameplay visual ownership");
        });
    }

    void Sample()
    {
        if (!observing || flow == null) return;
        Expect(FlagsRestored(), "EOF forceRenderingOff matches baseline dictionary exactly");
        Expect(manager.CurrentWorld == entry && manager.CurrentWorldId == entryWorldId && manager.IsFused
            && fusionEvents == 1 && worldEvents == 0, "entry world/ownership/event counts remain unchanged");
        Expect(worlds.All(w => w.IsActive && w.ContentRoot.gameObject.activeSelf && w.ContentRoot.gameObject.activeInHierarchy), "both content roots stay active on every rendered frame");
        Expect(flow.Turns + .0001f >= previousTurns && flow.Turns <= 4.0001f, "Turns monotonic and bounded by four");
        previousTurns = flow.Turns;
        foreach (var pair in hud)
            if (pair.Key != null && pair.Key.isActiveAndEnabled)
                Expect((HudShape(pair.Key) - pair.Value).sqrMagnitude < .00001f, "common active shared HUD canvas aspect/scale stable");
        GameplayVisual();
        if (!flow.IsPlaying) return;
        var image = Field<Image>(view, "portrait");
        var canvas = Field<Canvas>(view, "canvas");
        var display = Field<Camera>(flip, "displayCamera");
        var capture = Field<RenderTexture>(flip, "capture");
        var output = Field<RawImage>(flip, "worldImage");
        Expect(flip.IsCapturing && camera.isActiveAndEnabled && capture != null && capture.IsCreated()
            && camera.targetTexture == capture && Field<Camera>(flip, "capturedCamera") == camera
            && display != null && display.isActiveAndEnabled && display.targetTexture == null
            && display.targetDisplay == camera.targetDisplay && output != null && output.texture == capture,
            "capture camera/RT and real display output exist");
        Expect(image != null && canvas != null && canvas.isActiveAndEnabled, "portrait overlay is visible");
        if (image == null || canvas == null || !canvas.isActiveAndEnabled) return;
        float white = flow.WhiteAmount;
        Expect(Near(image.material.GetFloat("_WhiteAmount"), white), "shader white matches controller white");
        Expect(image.color == Color.white && image.color.a > 0, "portrait remains opaque; Image.color does not fake whitening");
        PlayerFusionVisual visual = (flow.ShowFusionPortrait ? entry.Player : flow.DisplayWorld.Player).GetComponent<PlayerFusionVisual>();
        Expect(image.sprite == (flow.ShowFusionPortrait ? visual.FusionSprite : visual.NormalSprite), "portrait follows DisplayWorld.NormalSprite before fusion face");
        if (flow.IsFlipping)
        {
            Expect(image.rectTransform.anchoredPosition.sqrMagnitude < .001f, "flipping portrait stays centered");
            Vector3 scale = image.rectTransform.lossyScale;
            Expect(Near(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) && Mathf.Abs(scale.x) > 0,
                "UI portrait has no horizontal scale compression");
            if (image.sprite != null)
                Expect(Near(image.rectTransform.rect.width / image.rectTransform.rect.height,
                    image.sprite.rect.width / image.sprite.rect.height, .005f) && image.preserveAspect, "portrait rect preserves sprite aspect");
            if (flip.HorizontalScale < .99f) compressed = true;
        }
        if (flow.DisplayWorld != previousDisplay)
        {
            sideOns++;
            Expect(flow.DisplayWorld == (previousDisplay == entry ? flow.OtherWorld : entry)
                && Near(flow.Turns, sideOns - .5f, .001f) && flip.HorizontalScale < .04f,
                "each alternating display swap occurs at its exact narrow side-on milestone");
            previousDisplay = flow.DisplayWorld;
            Stage("side-on-" + sideOns);
        }
        if (!flow.ShowFusionPortrait && white < .01f) CaptureOnce("normal");
        if (!flow.ShowFusionPortrait && white >= .999f)
        {
            whiteOriginal = true;
            CaptureOnce("white-original");
        }
        if (flow.ShowFusionPortrait && !whiteFusion)
        {
            Expect(whiteOriginal && white >= .999f && image.color.a > 0, "white original is rendered before opaque white fusion swap");
            whiteFusion = white >= .999f && image.color.a > 0;
            CaptureOnce("white-fusion");
        }
        if (flow.ShowFusionPortrait && flow.RevealProgress > 0 && white < .2f)
        {
            revealColor = true;
            CaptureOnce("reveal");
        }
    }

    void GameplayVisual()
    {
        var visual = entry.Player.GetComponent<PlayerFusionVisual>();
        var body = Field<SpriteRenderer>(visual, "fusionRenderer");
        Expect(body != null && body.enabled && spriteColors.ContainsKey(body) && body.color == spriteColors[body]
            && Field<SpriteRenderer[]>(visual, "bodyRenderers").All(r => r == null || !r.enabled),
            "PlayerFusionVisual owns normal/fusion sprite.enabled and final unwhitened body");
        var other = worlds.Single(w => w != entry);
        Expect(other.Player.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<Weapon>(true) == null)
            .All(r => !r.enabled), "secondary gameplay body remains suppressed independently of capture flags");
    }

    IEnumerator Cancellations()
    {
        // Also restore fixture-owned lifecycle switches if an EOF wait or assertion aborts.
        if (!enabledStates.ContainsKey(flow)) enabledStates.Add(flow, flow.enabled);
        if (!enabledStates.ContainsKey(view)) enabledStates.Add(view, view.enabled);
        if (!enabledStates.ContainsKey(manager)) enabledStates.Add(manager, manager.enabled);
        Require(manager.TryEnterFusion(), "enter for controller disable");
        yield return AtEOF(() => Check(flip.IsCapturing, "capture started before controller disable"));
        flow.enabled = false;
        yield return AtEOF(() =>
        {
            Check(!flow.IsPlaying && Near(flow.Progress, 0) && Near(flow.Turns, 0), "controller disable cancels clock");
            MainView("controller disable");
            Check(manager.IsFused, "presentation cancellation does not undo gameplay fusion");
        });
        flow.enabled = true;
        yield return null;
        Check(!flow.IsPlaying, "re-enable does not replay already fused entrance");
        Require(manager.TryExitFusion(), "exit after controller cancellation");

        Require(manager.TryEnterFusion(), "enter for presentation disable");
        yield return AtEOF(() => Check(flip.IsCapturing, "capture started before view disable"));
        Object[] oldOverlay = { Field<Canvas>(view, "canvas"), Field<Material>(view, "material") };
        view.enabled = false;
        // This view owns the portrait, not the clock. Cancel via the clock's lifecycle too.
        flow.enabled = false;
        yield return ThreeFrames();
        yield return AtEOF(() =>
        {
            MainView("view + clock disable");
            Check(oldOverlay.All(o => o == null), "disabled portrait view releases overlay/material");
        });
        flow.enabled = true;
        view.enabled = true;
        UnhookRendering();
        RenderPipelineManager.endFrameRendering += EndFrame;
        Require(manager.TryExitFusion(), "exit after view cancellation");

        Require(manager.TryEnterFusion(), "enter for manager disable");
        yield return AtEOF(() => Check(flow.IsPlaying && flip.IsCapturing, "manager disable starts during playing"));
        Time.timeScale = 0;
        manager.enabled = false;
        Check(!manager.IsFused && !flow.IsPlaying, "manager disable forces synchronous fusion/clock cleanup even paused");
        yield return AtEOF(() => MainView("manager disable"));
        manager.enabled = true;
        Time.timeScale = 1;
        yield return null;
    }

    IEnumerator RestartDuringEntrance()
    {
        Require(SceneManager.GetActiveScene().name == "DebugRun", "never RestartRun Main");
        Require(manager.TryEnterFusion(), "enter for DebugRun restart");
        yield return AtEOF(() => Check(flow.IsPlaying && flip.IsCapturing, "restart fixture is rendered during playing"));
        var oldManager = manager;
        var oldFlow = flow;
        var oldView = view;
        var oldCamera = camera;
        var oldScene = SceneManager.GetActiveScene();
        var resources = RuntimeResources();
        Require(resources.Length > 0, "restart has live runtime resources to release");
        var run = InScene<RunStageController>().Single(r => r.isActiveAndEnabled);
        int loads = 0;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> loaded = (s, mode) => { if (s.name == "DebugRun") loads++; };
        SceneManager.sceneLoaded += loaded;
        try
        {
            UnhookRendering();
            run.RestartRun();
            Check(!oldFlow.IsPlaying && !oldManager.IsFused, "RestartRun synchronously cleans outgoing entrance");
            yield return ThreeFrames();
            Check(loads == 1 && SceneManager.GetActiveScene().handle != oldScene.handle && oldManager == null
                && oldFlow == null && oldView == null && oldCamera == null, "DebugRun restarted exactly once into new scene/instances");
            Check(resources.All(o => o == null) && RuntimeResources().Length == 0, "restart leaves no old overlay/camera/RT/material resources");
            Bind();
            Check(!manager.IsFused && !flow.IsPlaying && !flip.IsCapturing && camera.targetTexture == null,
                "fresh DebugRun has normal main view and no replay");
            double until = Seconds + .2;
            while (Seconds < until) yield return null;
            Check(loads == 1 && !flow.IsPlaying && RuntimeResources().Length == 0, "no delayed second scene load or capture after restart");
            yield return AtEOF(() => rendererFlags = InScene<Renderer>().ToDictionary(r => r, r => r.forceRenderingOff));
        }
        finally { SceneManager.sceneLoaded -= loaded; }
    }

    IEnumerator DeathDuringEntrance()
    {
        Require(manager.TryEnterFusion(), "enter for death cleanup");
        yield return AtEOF(() => Check(flow.IsPlaying && flip.IsCapturing, "death fixture is rendered during playing"));
        var dying = manager.FusionPlayer;
        var health = dying.GetComponent<PlayerHealth>();
        health.DamageHandler(health.currentHealth + 1);
        Check(!manager.IsFused && !flow.IsPlaying && !dying.gameObject.activeSelf, "ordinary DamageHandler death forces synchronous cleanup");
        yield return AtEOF(() => MainView("death"));
        float p = flow.Progress, turns = flow.Turns;
        yield return ThreeFrames();
        Check(!flow.IsPlaying && Near(flow.Progress, p) && Near(flow.Turns, turns), "death clock remains stopped");
        Time.timeScale = 1;
    }

    void MainView(string stage)
    {
        var overlay = Field<Canvas>(view, "canvas");
        var output = Field<Camera>(flip, "displayCamera");
        Check(!flip.IsCapturing && Field<RenderTexture>(flip, "capture") == null
            && camera.isActiveAndEnabled && camera.targetTexture == originalTarget
            && MatrixNear(camera.projectionMatrix, originalProjection)
            && (overlay == null || !overlay.enabled) && (output == null || !output.enabled), label + " " + stage + " restores main view");
        Check(FlagsRestored(), label + " " + stage + " exact EOF renderer flags (not forced all false)");
    }

    IEnumerator AtEOF(Action action)
    {
        Require(atEndOfFrame == null, "single pending EOF request");
        bool done = false;
        atEndOfFrame = () => { action(); done = true; };
        double start = Seconds;
        while (!done)
        {
            Require(Seconds - start < 3, "rendered EOF callback required within 3s; keep Game view rendering");
            yield return null;
        }
    }

    void EndFrame(ScriptableRenderContext context, Camera[] cameras)
    {
        if (renderError != null || renderedFrame == Time.frameCount || camera == null || !cameras.Contains(camera)) return;
        renderedFrame = Time.frameCount;
        try
        {
            Sample();
            var action = atEndOfFrame;
            atEndOfFrame = null;
            if (action != null) action();
        }
        catch (Exception e) { renderError = e; }
    }

    void CaptureOnce(string stage)
    {
        if (!captures.Add(stage)) return;
        Stage(stage);
        pendingCaptures.Add(stage);
    }

    void FlushCaptures()
    {
        foreach (string stage in pendingCaptures)
            SaveCapture(stage);
        pendingCaptures.Clear();
    }

    void SaveCapture(string stage)
    {
        Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
        Require(texture != null, "EOF screenshot returned texture");
        try
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "FusionEntrance-" + label + "-" + stage + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Expect(texture.width == Screen.width && texture.height == Screen.height && new FileInfo(path).Length > 0,
                "rendered screenshot dimensions and PNG output valid");
            Debug.Log("FusionEntranceChecks screenshot " + path);
        }
        finally { Object.Destroy(texture); }
    }

    void Stage(string stage)
    {
        Debug.Log("FusionEntranceChecks " + label + " stage=" + stage + " p=" + flow.Progress.ToString("F4")
            + " Turns=" + flow.Turns.ToString("F4") + " white=" + flow.WhiteAmount.ToString("F4")
            + " display=" + (flow.DisplayWorld == null ? "none" : flow.DisplayWorld.WorldId.ToString()));
    }
    bool FlagsRestored() { return rendererFlags != null && rendererFlags.All(p => p.Key == null || p.Key.forceRenderingOff == p.Value); }
    void Expect(bool ok, string message) { if (!ok) faults.Add(message); }
    void FusionChanged() { fusionEvents++; }
    void WorldChanged() { worldEvents++; }
    static T Field<T>(object value, string name)
    {
        var field = value.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(value.GetType().Name, name);
        return (T)field.GetValue(value);
    }
    static T[] InScene<T>() where T : Component
    {
        Scene scene = SceneManager.GetActiveScene();
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene).ToArray();
    }
    static Vector3 HudShape(Canvas c)
    {
        var rect = (RectTransform)c.transform;
        return new Vector3(c.scaleFactor, rect.rect.width / Mathf.Max(.0001f, rect.rect.height),
            Mathf.Abs(rect.lossyScale.x) / Mathf.Max(.0001f, Mathf.Abs(rect.lossyScale.y)));
    }
    static Object[] RuntimeResources()
    {
        string[] names = { "World Flip Overlay (Runtime)", "World Flip Display Camera (Runtime)", "World Flip Capture (Runtime)",
            "World Flip Material (Runtime)", "Fusion Overlay (Runtime)", "Fusion Portrait Material (Runtime)" };
        return Resources.FindObjectsOfTypeAll<Object>().Where(o => o != null && names.Contains(o.name)).ToArray();
    }
    static bool MatrixNear(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++) if (!Near(a[i], b[i])) return false;
        return true;
    }
    static bool Near(float a, float b, float tolerance = .0001f) { return !float.IsNaN(a) && !float.IsNaN(b) && Mathf.Abs(a - b) <= tolerance; }
    static IEnumerator ThreeFrames() { yield return null; yield return null; yield return null; }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Check(bool ok, string message)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("FusionEntranceChecks " + (ok ? "PASS " : "FAIL ") + message);
    }
    void UnhookRendering()
    {
        RenderPipelineManager.endFrameRendering -= EndFrame;
        atEndOfFrame = null;
        renderedFrame = -1;
        observing = false;
    }
    public void Dispose()
    {
        UnhookRendering();
        // On abort use normal forced cleanup, never repair renderer flags in the test.
        if (manager != null && manager.IsFused)
        {
            bool enabled = manager.enabled;
            manager.enabled = false;
            manager.enabled = enabled;
        }
        foreach (var m in subscribed)
            if (!ReferenceEquals(m, null)) { m.FusionStateChanged -= FusionChanged; m.WorldChanged -= WorldChanged; }
        foreach (var pair in enabledStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in healthStates)
            if (pair.Key != null && !pair.Key.IsDead) { pair.Key.maxHealth = pair.Value.y; pair.Key.currentHealth = pair.Value.x; }
        if (started) { Time.timeScale = savedScale; Application.runInBackground = savedBackground; }
        clock.Stop();
    }
}
