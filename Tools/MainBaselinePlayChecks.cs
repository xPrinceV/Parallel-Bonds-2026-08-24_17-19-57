using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Only staged in the disposable validation project. No production state is saved.
public static class MainBaselinePlayChecks
{
    public static int Passed, Failed;
    public static readonly List<string> Evidence = new List<string>();
    public static readonly List<CaseResult> Cases = new List<CaseResult>();
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static string Output => MainBaselinePlayChecksBatch.Arg("-baselineOutput");
    [Serializable] public sealed class CaseResult
    {
        public string name, status, error;
        public int passed, failed;
        public double realSeconds;
    }
    public static T Get<T>(object owner, string name)
    {
        for (Type t = owner.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Fields | BindingFlags.DeclaredOnly);
            if (f != null) return (T)f.GetValue(owner);
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    public static void Note(string text) { Evidence.Add(text); Debug.Log("BASELINE " + text); }
    public static void Check(bool ok, string text)
    { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + text); }
    public static void Require(bool ok, string text)
    { Check(ok, text); if (!ok) throw new InvalidOperationException(text); }
    static T[] Find<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include)
        .Where(c => c.gameObject.scene == SceneManager.GetActiveScene()).ToArray();
    static IEnumerator Wait(float seconds)
    { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
    static IEnumerator Until(Func<bool> condition, float seconds, string label)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
        Check(true, label);
    }
    // Flatten here, not only at the batch boundary: a nested timeout belongs to this
    // case, must remain a failure, and must not prevent the next independent case.
    static IEnumerator RunCase(string name, IEnumerator root)
    {
        var result = new CaseResult { name = name, status = "RUNNING" };
        Cases.Add(result);
        int initialPassed = Passed, initialFailed = Failed;
        double start = Time.realtimeSinceStartupAsDouble;
        bool completed = false;
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        Note("CASE START " + name);
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
                if (error != null)
                {
                    result.error = error.ToString();
                    if (Failed == initialFailed) Check(false, name + " aborted: " + error.Message);
                    Note("CASE ABORT " + name + " " + error);
                    break;
                }
                if (current is IEnumerator child) stack.Push(child); else yield return current;
            }
            completed = stack.Count == 0 && result.error == null;
        }
        finally
        {
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception e) { result.error += "\nCleanup: " + e; Check(false, name + " cleanup exception"); }
            }
            if (!completed && result.error == null)
            { result.error = "Case interrupted by outer deadline/termination"; Check(false, name + " interrupted"); }
            result.passed = Passed - initialPassed; result.failed = Failed - initialFailed;
            result.status = result.error != null ? "ABORTED" : result.failed > 0 ? "FAILED" : "PASSED";
            result.realSeconds = Time.realtimeSinceStartupAsDouble - start;
            File.AppendAllText(Path.Combine(Output, "cases.jsonl"), JsonUtility.ToJson(result) + "\n");
            Note("CASE RESULT " + JsonUtility.ToJson(result));
        }
    }
    static void Mesh(TMP_Text text, string label)
    {
        Require(text != null && text.font != null, label + " font binding");
        text.ForceMeshUpdate(true, true);
        Check(text.textInfo.characterCount > 0 && text.textInfo.meshInfo.Any(m => m.vertexCount > 0), label + " actual TMP vertices");
    }
    static AudioSource Music(SoundId id)
    {
        var service = AudioService.Instance;
        Require(service != null && service.isActiveAndEnabled, "live audio service");
        var events = service.GetComponent<GameAudioEvents>();
        var target = Get<AudioSource>(service, "musicTarget");
        SoundEntry entry;
        Require(Get<AudioCatalog>(service, "catalog").TryGet(id, out entry), "catalog entry " + id);
        Check(events != null && events.isActiveAndEnabled && Get<SoundId>(events, "currentMusic") == id
            && target != null && target.isPlaying && target.timeSamples > 0 && target.loop
            && Get<AudioClip[]>(entry, "clips").Contains(target.clip) && Mathf.Abs(target.volume - entry.Volume) < .001f,
            "native music " + id + " samples=" + (target == null ? -1 : target.timeSamples));
        return target;
    }
    public static IEnumerator Run()
    {
        Require(Application.isPlaying && SceneManager.GetActiveScene().name == "Main", "actual Main Play Mode");
        Application.runInBackground = true;
        Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && !Application.isBatchMode,
            "native graphics/audio session, not headless or batch");
        var manager = Find<WorldManager>().Single();
        var run = Find<RunStageController>().Single();
        var worlds = Get<World[]>(manager, "worlds");
        Require(manager.IsInitialized && worlds.Length == 2 && worlds.All(w => w.IsConfigured && w.Player != null && w.Map != null),
            "Main live two-world configuration initialized");
        var audioEvents = AudioService.Instance.GetComponent<GameAudioEvents>();
        Check(Get<WorldManager>(audioEvents, "worldManager") == manager && Get<RunStageController>(audioEvents, "run") == run,
            "actual game audio hook bound to current world/run");
        Note("FIXTURE: disable weapon components, stop spawns via public StopSpawning (spawner components stay enabled), survival HP=1000000; no synthetic music calls, no clock injection");
        foreach (var weapon in Find<Weapon>()) weapon.enabled = false;
        foreach (var spawner in Find<EnemySpawner>()) spawner.StopSpawning(true);
        foreach (var world in worlds)
        { var hp = world.Player.GetComponent<PlayerHealth>(); hp.maxHealth = hp.currentHealth = 1000000; }
        var timer = (StateSwitchController)typeof(WorldManager).GetProperty("SwitchFlow", Fields).GetValue(manager);
        Require(timer != null && Get<WorldManager>(timer, "worldManager") == manager && timer.isActiveAndEnabled && timer.SwitchInterval == 15
            && Get<float>(timer, "warningDuration") == 5 && Get<float>(timer, "flipDuration") == .8f && run.FinaleStartTime == 480,
            "live 15/5/.8/480 timings");
        timer.AutomaticSwitchingEnabled = false; timer.CancelTransition();
        yield return Fonts();
        yield return RunCase("Main automatic 15s switch / HUD / audio", WorldSwitch(manager, timer));
        yield return RunCase("Main official finale / fusion / Boss", Finale(manager, run));
        yield return RunCase("Main pause / boss HUD", Pause(run));
        Note("FIXTURE: resume scaled time for independent Hollow observations after core cases; no stage reset or production changes");
        Time.timeScale = 1;
        yield return RunCase("Hollow serialized RB / natural shot", Hollow(manager.CurrentWorld, false));
        yield return RunCase("Hollow missing RB fallback / natural shot", Hollow(manager.CurrentWorld, true));
        if (MainBaselinePlayChecksBatch.RemainingSeconds < 15)
        { Note("SKIP optional restart: insufficient remaining budget; previous case failures remain failures"); yield break; }
        yield return RunCase("Main paused restart", Restart());
    }
    static IEnumerator WorldSwitch(WorldManager manager, StateSwitchController timer)
    {
        yield return Wait(.7f);
        Music(manager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho);
        var ui = UIController.instance;
        Require(ui != null && ui.levelUpPanel != null && ui.timeText != null && ui.expLvlSlider != null && ui.expLvlText != null,
            "Main HUD and upgrade pause bindings");
        Mesh(ui.timeText, "initial HUD timer");
        var beforeHud = ui.timeText.text;
        WorldId before = manager.CurrentWorldId;
        int changes = 0, midpoints = 0, warningFrames = 0;
        float elapsed = -1, midpoint = -1, start = Time.time;
        Action changed = () => { changes++; elapsed = Time.time - start; };
        Action mid = () => { midpoints++; midpoint = timer.FlipProgress; };
        manager.WorldChanged += changed; timer.TransitionMidpoint += mid;
        try
        {
            timer.CancelTransition(); timer.AutomaticSwitchingEnabled = true;
            float limit = Time.realtimeSinceStartup + 22;
            while (changes == 0)
            {
                if (Time.realtimeSinceStartup > limit) throw new TimeoutException("full 15s automatic switch");
                if (timer.WarningProgress > 0) warningFrames++;
                yield return null;
            }
            yield return Until(() => !timer.IsFlipping, 2, "actual flip completed");
            Check(changes == 1 && midpoints == 1 && midpoint == .5f && warningFrames > 0 && elapsed >= 14.95f && elapsed < 15.5f
                && manager.CurrentWorldId != before, "one natural 15s midpoint; elapsed=" + elapsed + "; warnings=" + warningFrames);
        }
        finally { manager.WorldChanged -= changed; timer.TransitionMidpoint -= mid; timer.AutomaticSwitchingEnabled = false; }
        yield return Wait(.7f);
        var source = Music(manager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho);
        int samples = source.timeSamples;
        yield return Wait(.15f);
        Check(source.isPlaying && source.timeSamples != samples, "native music sample progression after switch");
        Check(Get<World[]>(manager, "worlds").Count(w => w.IsActive) == 1 && manager.CurrentWorld.IsActive && PlayerController.instance == manager.CurrentWorld.Player,
            "switch commits world and actual active player");
        Check(ui.timeText.text != beforeHud, "HUD timer advanced through real interval");
        Mesh(ui.timeText, "post-switch HUD");
    }
    static IEnumerator Finale(WorldManager manager, RunStageController run)
    {
        var entrance = manager.GetComponent<FusionTransitionController>();
        int completedFrame = -1, bossFrame = -1;
        Action completed = () => completedFrame = Time.frameCount;
        Action state = () => { if (run.IsBossPhase && bossFrame < 0) bossFrame = Time.frameCount; };
        entrance.Completed += completed; run.StateChanged += state;
        try
        {
            Note("FIXTURE: official TryEnterStage(3), not an eight-minute soak and no private lifecycle invocation");
            Require(run.TryEnterStage(3), "official finale stage accepted");
            Check(manager.IsFinalFusion && run.IsFinaleStarted && entrance.IsPlaying && run.RemainingBosses == 0,
                "fusion entrance precedes boss");
            yield return Until(() => run.IsBossPhase, 12, "real fusion completion and boss spawn");
            var bosses = Get<HashSet<EnemyController>>(run, "bosses").ToArray();
            Require(bosses.Length == 1 && bosses[0] != null && bosses[0].health > 0, "one living actual boss");
            Check(completedFrame >= 0 && bossFrame > completedFrame, "boss spawn ordered after entrance completion; frames=" + completedFrame + "/" + bossFrame);
            var renderers = bosses[0].GetComponentsInChildren<SpriteRenderer>(true);
            Check(renderers.Any(r => r.enabled && r.gameObject.activeInHierarchy && r.sprite != null && r.sortingLayerName == "Enemy"),
                "visible boss sprite uses Enemy sorting layer");
            Check(manager.IsFinalFusion && Get<World[]>(manager, "worlds").All(w => w.IsActive), "final fusion retains both world contents");
        }
        finally { entrance.Completed -= completed; run.StateChanged -= state; }
        yield return Wait(.7f); Music(SoundId.MusicBoss);
    }
    static IEnumerator Pause(RunStageController run)
    {
        Require(run.IsBossPhase, "pause fixture has an actual living boss phase");
        var panel = Find<RunStagePanel>().Single(); var gui = Find<DeveloperDebugGui>().Single();
        Require(Get<RunStageController>(panel, "runController") == run && Get<UIController>(panel, "uiController") == UIController.instance,
            "actual run panel bindings");
        gui.SetOpen(true);
        Note("FIXTURE: timeScale=0 models pause; no dedicated pause button is asserted");
        Time.timeScale = 0; float clock = run.ElapsedTime;
        yield return Wait(.25f);
        var status = Get<TMP_Text>(panel, "statusText");
        Check(run.ElapsedTime == clock && status.text.Contains("Paused") && status.text.Contains("Bosses: 1"), "pause freezes clock and updates live boss HUD");
        Mesh(status, "paused boss HUD");
    }
    static IEnumerator Restart()
    {
        var oldRun = Find<RunStageController>().Single(); var oldAudio = AudioService.Instance;
        var panel = Find<RunStagePanel>().Single(); Find<DeveloperDebugGui>().Single().SetOpen(true);
        Time.timeScale = 0; yield return null;
        var restart = Get<Button>(panel, "restartButton");
        Require(restart != null && restart.isActiveAndEnabled && restart.interactable, "restart reachable while paused");
        restart.onClick.Invoke();
        yield return Until(() => oldRun == null && Find<RunStageController>().Length == 1, 5, "production restart button reloads Main");
        yield return null; yield return null;
        var run = Find<RunStageController>().Single();
        Check(oldAudio == null && run.IsRunning && !run.IsFinaleStarted && !run.IsDefeated && Time.timeScale == 1,
            "restart resets run, pause and audio ownership");
        yield return Wait(.7f);
        var manager = Find<WorldManager>().Single();
        Music(manager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho);
        Mesh(UIController.instance.timeText, "restarted HUD");
    }
    [Serializable] sealed class HollowSample
    {
        public string phase, world, target, targetWorld, interactionPlayer;
        public bool fallback, active, worldActive, fused, targetActive, startAttack, bodySimulated;
        public double realElapsed, scaledElapsed;
        public float timeScale, deltaTime, unscaledDeltaTime, distance3d, distance2d, privateDistance, range, channelCounter, channelTime;
        public int frame, activeShots, inactiveShots, newActiveShots, newInactiveShots;
        public Vector3 transformPosition3d, bodyTransformPosition3d, targetPosition3d, playerPosition3d;
        public Vector2 bodyPosition2d, velocity;
    }
    static void ObserveHollow(RangedEnemyController enemy, World world, bool fallback, string phase,
        double realStart, double scaledStart, HashSet<HollowProjectile> prior)
    {
        var target = Get<Transform>(enemy, "target"); var targetWorld = World.GetFor(target);
        var shots = world.ContentRoot.GetComponentsInChildren<HollowProjectile>(true);
        var sample = new HollowSample { phase = phase, fallback = fallback, frame = Time.frameCount,
            realElapsed = Time.realtimeSinceStartupAsDouble - realStart, scaledElapsed = Time.timeAsDouble - scaledStart,
            timeScale = Time.timeScale, deltaTime = Time.deltaTime, unscaledDeltaTime = Time.unscaledDeltaTime,
            active = enemy.isActiveAndEnabled, world = world.name + "/" + world.WorldId, worldActive = world.IsActive,
            fused = world.Manager != null && world.Manager.IsFused,
            target = target == null ? "null" : target.name, targetWorld = targetWorld == null ? "null" : targetWorld.name + "/" + targetWorld.WorldId,
            targetActive = target != null && target.gameObject.activeInHierarchy,
            interactionPlayer = world.InteractionPlayer == null ? "null" : world.InteractionPlayer.name,
            transformPosition3d = enemy.transform.position, bodyTransformPosition3d = enemy.RB == null ? Vector3.zero : enemy.RB.transform.position,
            bodyPosition2d = enemy.RB == null ? Vector2.zero : enemy.RB.position, velocity = enemy.RB == null ? Vector2.zero : enemy.RB.linearVelocity,
            bodySimulated = enemy.RB != null && enemy.RB.simulated,
            targetPosition3d = target == null ? Vector3.zero : target.position,
            playerPosition3d = world.InteractionPlayer == null ? Vector3.zero : world.InteractionPlayer.transform.position,
            distance3d = target == null ? -1 : Vector3.Distance(enemy.transform.position, target.position),
            distance2d = target == null ? -1 : Vector2.Distance(enemy.transform.position, target.position),
            privateDistance = Get<float>(enemy, "distance"), range = enemy.range,
            startAttack = Get<bool>(enemy, "startAttack"), channelCounter = Get<float>(enemy, "attackChannelCounter"), channelTime = enemy.attackChannelTime,
            activeShots = shots.Count(p => p.gameObject.activeInHierarchy), inactiveShots = shots.Count(p => !p.gameObject.activeInHierarchy),
            newActiveShots = shots.Count(p => !prior.Contains(p) && p.gameObject.activeInHierarchy),
            newInactiveShots = shots.Count(p => !prior.Contains(p) && !p.gameObject.activeInHierarchy) };
        string json = JsonUtility.ToJson(sample);
        File.AppendAllText(Path.Combine(Output, "hollow-observations.jsonl"), json + "\n");
        Note("HOLLOW OBSERVATION " + json);
    }
    static IEnumerator Hollow(World world, bool fallback)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Hollow.prefab");
        Require(prefab != null, "real Hollow prefab imported");
        var authored = prefab.GetComponent<RangedEnemyController>();
        Require(authored != null && authored.RB == prefab.GetComponent<Rigidbody2D>() && authored.RB != null
            && authored.health == 25 && authored.expDrop == 15 && authored.projectile != null, "Hollow authored RB/health25/exp15/projectile");
        Require(world.InteractionPlayer != null && world.IsActive && Time.timeScale > 0, "Hollow actual active interaction target / scaled time");
        var holder = new GameObject("Baseline Hollow inactive fixture"); holder.SetActive(false); holder.transform.SetParent(world.ContentRoot);
        var clone = Object.Instantiate(prefab, holder.transform); var enemy = clone.GetComponent<RangedEnemyController>();
        if (fallback) enemy.RB = null;
        Check(!clone.activeInHierarchy && (!fallback || enemy.RB == null), "Hollow pre-Awake inactive / serialized RB removed=" + fallback);
        clone.transform.position = world.InteractionPlayer.transform.position + Vector3.right * (enemy.range + 3);
        var origin = clone.transform.position;
        var prior = new HashSet<HollowProjectile>(world.ContentRoot.GetComponentsInChildren<HollowProjectile>(true));
        double realStart = Time.realtimeSinceStartupAsDouble, scaledStart = Time.timeAsDouble;
        Note("FIXTURE Hollow fallback=" + fallback + ": inactive clone before Awake; actual InteractionPlayer after core flow; same range-1 x/y reposition and authored channel+2 real-second window; no private lifecycle calls");
        try
        {
            holder.SetActive(true);
            Require(enemy.RB != null && enemy.RB == clone.GetComponent<Rigidbody2D>(), "Hollow inherited Awake body fallback=" + fallback);
            ObserveHollow(enemy, world, fallback, "activated", realStart, scaledStart, prior);
            yield return Wait(.2f);
            Check(enemy.RB.linearVelocity.sqrMagnitude > 0 && (clone.transform.position - origin).sqrMagnitude > .001f,
                "Hollow real ranged movement fallback=" + fallback);
            ObserveHollow(enemy, world, fallback, "before-reposition", realStart, scaledStart, prior);
            // Keep the previous observation fixture unchanged; diagnostics expose 3D
            // distance and physics readback rather than assuming this enters range.
            enemy.RB.position = (Vector2)world.InteractionPlayer.transform.position + Vector2.right * (enemy.range - 1);
            realStart = Time.realtimeSinceStartupAsDouble; scaledStart = Time.timeAsDouble;
            ObserveHollow(enemy, world, fallback, "after-reposition", realStart, scaledStart, prior);
            double nextSample = realStart + .25;
            try
            {
                while (!world.ContentRoot.GetComponentsInChildren<HollowProjectile>().Any(p => !prior.Contains(p)))
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - realStart > enemy.attackChannelTime + 2)
                        throw new TimeoutException("Hollow Update naturally channels and shoots fallback=" + fallback);
                    if (now >= nextSample)
                    { ObserveHollow(enemy, world, fallback, "waiting", realStart, scaledStart, prior); nextSample = now + .25; }
                    yield return null;
                }
                Check(true, "Hollow Update naturally channels and shoots fallback=" + fallback);
            }
            finally { ObserveHollow(enemy, world, fallback, "window-end", realStart, scaledStart, prior); }
            var shot = world.ContentRoot.GetComponentsInChildren<HollowProjectile>().First(p => !prior.Contains(p));
            Check(World.GetFor(shot) == world && Get<float>(shot, "damage") == enemy.attack && Get<float>(shot, "speed") == enemy.projectileSpeed,
                "real Hollow projectile damage/speed/world binding");
            var position = shot.transform.position;
            yield return Wait(shot.channelTime + .15f);
            Check(shot != null && (shot.transform.position - position).sqrMagnitude > .001f, "Hollow real projectile movement");
        }
        finally
        {
            Object.Destroy(holder);
            foreach (var shot in world.ContentRoot.GetComponentsInChildren<HollowProjectile>(true))
                if (!prior.Contains(shot)) shot.Despawn();
        }
        yield return null;
    }
    static IEnumerator Fonts()
    {
        foreach (string name in new[] { "Kenney Pixel SDF", "Kenney Pixel SDF - Outline" })
            yield return RunCase("Font " + name, Font(name));
    }
    static IEnumerator Font(string name)
    {
        const string sample = "GAME OVER Wave 2 480s Finale PLAY RESTART \u2026";
        uint[] repertoire = File.ReadAllLines(Path.Combine(Output, "source-repertoire.txt")).Select(uint.Parse).ToArray();
        Require(repertoire.Length == 209 && repertoire.Distinct().Count() == 209, "209 unique characters from source TTF cmap, not an invented range");
        var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Vampire Survival Assets/Fonts/Kenney Fonts/" + name + ".asset");
        Require(font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic && font.sourceFontFile != null
            && font.atlasTextures.All(t => t != null && t.isReadable), name + " imported dynamic/source/readable");
        int pendingSample = sample.Distinct().Count(c => !font.characterLookupTable.ContainsKey(c));
        string missing;
        bool sampleAdded = font.TryAddCharacters(sample, out missing);
        Note("FONT sample request " + name + " uncached=" + pendingSample + "; returned=" + sampleAdded + "; reported=" + missing);
        // This installed TMP returns false + the input on a no-new-glyph cache hit
        // (TMP_FontAsset.cs:2116-2120). Coverage, not that return, proves a no-op.
        Check((pendingSample == 0 || (sampleAdded && string.IsNullOrEmpty(missing))) && sample.All(c => font.characterLookupTable.ContainsKey(c)),
            name + " native UI request covered; uncached=" + pendingSample);
        int pendingAll = repertoire.Count(c => !font.characterLookupTable.ContainsKey(c));
        int glyphsBefore = font.glyphTable.Count;
        uint[] missingCodes;
        bool populated = font.TryAddCharacters(repertoire, out missingCodes);
        Check(populated && (missingCodes == null || missingCodes.Length == 0) && repertoire.All(code => font.characterLookupTable.ContainsKey(code))
            && pendingAll > 0 && font.glyphTable.Count > glyphsBefore,
            name + " real dynamic generation covers 209 source characters; uncached=" + pendingAll + "; glyph growth=" + (font.glyphTable.Count - glyphsBefore)
            + "; missing=" + (missingCodes == null ? "none" : string.Join(",", missingCodes)));
        var canvas = new GameObject("Baseline font canvas", typeof(Canvas), typeof(CanvasScaler));
        var display = canvas.GetComponent<Canvas>(); display.renderMode = RenderMode.ScreenSpaceOverlay; display.sortingOrder = 32760;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0;
        var backdrop = new GameObject("Font contrast background", typeof(RectTransform), typeof(Image)); backdrop.transform.SetParent(canvas.transform, false);
        backdrop.GetComponent<RectTransform>().sizeDelta = new Vector2(1160, 300); backdrop.GetComponent<Image>().color = new Color(.03f, .03f, .03f, 1);
        var go = new GameObject("Baseline font text", typeof(RectTransform)); go.transform.SetParent(canvas.transform, false);
        var text = go.AddComponent<TextMeshProUGUI>(); text.font = font;
        text.text = sample + "\n" + name + "\n209 source characters populated";
        text.rectTransform.sizeDelta = new Vector2(1120, 260); text.fontSize = 36; text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
        try
        {
            yield return null; Canvas.ForceUpdateCanvases(); Mesh(text, name + " sample");
            Check(!text.isTextOverflowing && text.textInfo.characterInfo.Take(text.textInfo.characterCount)
                .Where(c => !char.IsWhiteSpace(c.character)).All(c => c.isVisible && c.fontAsset == font),
                name + " visible non-whitespace glyphs use requested font without fallback or overflow");
            var visible = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c => c.isVisible).ToArray();
            Check(visible.Length > 0 && visible.All(c => {
                var glyph = c.textElement.glyph; var atlas = font.atlasTextures[glyph.atlasIndex]; var rect = glyph.glyphRect;
                return rect.width > 0 && rect.height > 0 && atlas.GetPixels(rect.x, rect.y, rect.width, rect.height).Any(p => p.a > .1f);
            }), name + " actual atlas glyph rectangles contain rasterized pixels");
            Note("FONT " + name + " characterTable=" + font.characterTable.Count + "; atlases="
                + string.Join(",", font.atlasTextures.Select(t => t.width + "x" + t.height)) + "; vertices=" + text.textInfo.meshInfo.Sum(m => m.vertexCount));
            string capture = Path.Combine(Output, name.Replace(' ', '-') + ".png");
            ScreenCapture.CaptureScreenshot(capture);
            yield return Until(() => File.Exists(capture), 5, name + " native Game-view screenshot captured");
        }
        finally { Object.Destroy(canvas); }
        yield return null;
    }
}
