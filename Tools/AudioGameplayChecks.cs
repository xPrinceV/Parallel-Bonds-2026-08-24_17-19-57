using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Disposable native Play Mode only. GameAudioEvents stays enabled during gameplay/menu integration.
// Each separately labelled synthetic suite temporarily suspends and restores the menu binder.
// No production instrumentation: accepted SFX are observed via ID cooldown-deadline changes,
// music via target/ID transitions plus native sample continuity. These are not request counts.
public static class AudioGameplayChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static readonly List<string> Evidence = new List<string>();
    static AudioService audio;
    static GameAudioEvents events;
    static WorldManager manager;
    static RunStageController run;
    static readonly Dictionary<SoundId, double> lastDeadlines = new Dictionary<SoundId, double>();
    static readonly Dictionary<SoundId, int> accepted = new Dictionary<SoundId, int>();
    static readonly List<Object> owned = new List<Object>();
    static SoundId lastMusic;
    static int musicTransitions;

    public static IEnumerator Run()
    {
        Passed = Failed = 0; Evidence.Clear();
        string suite = Arg("-audioSuite", "Menu");
        if (Arg("-audioMode", "Full") == "RestartOnly")
        {
            Require(suite == "DebugRun" && Arg("-audioEntry", "Material") == "Echo", "RestartOnly requires DebugRun/Echo");
            yield return RestartOnly();
            yield break;
        }
        yield return RunSuite(suite, Arg("-audioEntry", "Material"));
        // The combined path starts in unsaved DebugRun/Echo, then reaches the playing menu
        // through production navigation before running the synthetic suites and Main.
        if (suite == "DebugRun") yield return RunSuite("Main", "Material");
    }

    static IEnumerator RestartOnly()
    {
        accepted.Clear(); lastDeadlines.Clear(); lastMusic = SoundId.None; musicTransitions = 0;
        Application.runInBackground = true;
        Note("FIXTURE RestartOnly DebugRun/Echo: unsaved Play Mode snapshot; one public RestartRun; no full suites, world flip, finale or direct music calls; Unity="
            + Application.unityVersion + "; graphics=" + SystemInfo.graphicsDeviceType);
        Bind(true); Control();
        WorldId snapshotEntry = Get<WorldId>(manager, "initialWorldId");
        Check(SceneManager.GetActiveScene().name == "DebugRun" && snapshotEntry == WorldId.Echo && manager.CurrentWorldId == snapshotEntry,
            "RestartOnly initial snapshot and committed world are Echo");
        int worldBindings = Subscriptions(manager, "WorldChanged", events), runBindings = Subscriptions(run, "StateChanged", events);
        Check(worldBindings == 1 && runBindings == 1, "RestartOnly initial bindings: WorldChanged=" + worldBindings + ", StateChanged=" + runBindings);
        yield return Wait(.7f);
        CheckMusic(SoundId.MusicEcho, true, "RestartOnly initial Echo native playback from enabled game hook");
        Check(Get<SoundId>(events, "currentMusic") == SoundId.MusicEcho, "RestartOnly initial GameAudioEvents state is MusicEcho");

        var oldAudio = audio; var oldEvents = events; var oldManager = manager; var oldRun = run;
        var oldSources = audio.GetComponentsInChildren<AudioSource>(true);
        Note("RestartOnly outgoing instance IDs: service=" + audio.GetInstanceID() + ", events=" + events.GetInstanceID()
            + ", manager=" + manager.GetInstanceID() + ", run=" + run.GetInstanceID() + "; sources="
            + string.Join(",", oldSources.Select(s => s.GetInstanceID().ToString())));
        run.RestartRun();
        yield return Frames(4);
        Check(oldAudio == null && oldEvents == null && oldManager == null && oldRun == null,
            "RestartOnly old service/events/world/run destroyed by production RestartRun");
        Check(oldSources.Length == 10 && oldSources.All(s => s == null), "RestartOnly all ten outgoing native AudioSources destroyed");
        Bind(true); Control();
        Check(!ReferenceEquals(audio, oldAudio) && !ReferenceEquals(events, oldEvents)
            && !ReferenceEquals(manager, oldManager) && !ReferenceEquals(run, oldRun), "RestartOnly distinct fresh service/events/world/run instances");
        var newSources = audio.GetComponentsInChildren<AudioSource>(true);
        Check(newSources.Length == 10 && newSources.All(s => !oldSources.Any(old => ReferenceEquals(s, old))),
            "RestartOnly ten fresh native sources, no outgoing source reused");
        Note("RestartOnly incoming instance IDs: service=" + audio.GetInstanceID() + ", events=" + events.GetInstanceID()
            + ", manager=" + manager.GetInstanceID() + ", run=" + run.GetInstanceID() + "; sources="
            + string.Join(",", newSources.Select(s => s.GetInstanceID().ToString())));
        Check(SceneManager.GetActiveScene().name == "DebugRun" && Get<WorldId>(manager, "initialWorldId") == snapshotEntry
            && manager.CurrentWorldId == snapshotEntry && Time.timeScale == 1,
            "RestartOnly reload retains captured snapshot initial world=" + snapshotEntry + "; current=" + manager.CurrentWorldId);
        worldBindings = Subscriptions(manager, "WorldChanged", events); runBindings = Subscriptions(run, "StateChanged", events);
        Check(worldBindings == 1 && runBindings == 1, "RestartOnly fresh bindings: WorldChanged=" + worldBindings + ", StateChanged=" + runBindings);
        yield return Wait(.7f);
        CheckMusic(snapshotEntry == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho, true,
            "RestartOnly corrected expectation: music follows captured Play Mode snapshot=" + snapshotEntry);
        var target = MusicTarget(); int samples = target.timeSamples;
        yield return Wait(.15f);
        Check(target == MusicTarget() && target.isPlaying && target.timeSamples > samples
            && Get<SoundId>(events, "currentMusic") == SoundId.MusicEcho,
            "RestartOnly fresh Echo samples advance without track restart: " + samples + " -> " + target.timeSamples);
        Check(events.isActiveAndEnabled && audio.isActiveAndEnabled && Subscriptions(manager, "WorldChanged", events) == 1
            && Subscriptions(run, "StateChanged", events) == 1,
            "RestartOnly enabled game hook retains exactly one subscription per event across subsequent frames");
    }

    static IEnumerator RunSuite(string suite, string entry)
    {
        accepted.Clear(); lastDeadlines.Clear(); lastMusic = SoundId.None; musicTransitions = 0;
        Application.runInBackground = true;
        Note("FIXTURE " + suite + ": native Unity " + Application.unityVersion + "; graphics=" + SystemInfo.graphicsDeviceType
            + "; no subjective listening; no source/scene saves; no synthetic game-music requests");
        if (suite == "Menu" || suite == "Main")
        {
            Bind(false);
            yield return MenuMusic("initial menu before synthetic fixtures");
            Note("SEPARATE SYNTHETIC SERVICE SUITE: generated native clips, direct calls, explicit menu-binder suspension/restoration; NOT game hook evidence");
            yield return AudioServiceChecks.Run();
            Passed += AudioServiceChecks.Passed; Failed += AudioServiceChecks.Failed;
            yield return MenuMusic("menu restored after service fixture");
            Note("SEPARATE CATALOG SUITE: configured catalog read-only; temporary catalogs/generated clips, explicit menu-binder suspension/restoration; projectile source-only checks");
            yield return AudioCatalogChecks.Run();
            Passed += AudioCatalogChecks.Passed; Failed += AudioCatalogChecks.Failed;
            yield return MenuMusic("menu restored after catalog fixture");
            if (suite == "Menu") yield break;
            accepted.Clear(); lastDeadlines.Clear(); musicTransitions = 0; lastMusic = SoundId.None;
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return Frames(4);
        }
        Bind(true);
        var expected = entry == "Echo" ? SoundId.MusicEcho : SoundId.MusicMaterial;
        Check(MusicId() == expected && Get<SoundId>(events, "currentMusic") == expected,
            "actual Start initial " + expected + "; scene=" + SceneManager.GetActiveScene().name);
        Check(Subscriptions(manager, "WorldChanged", events) == 1 && Subscriptions(run, "StateChanged", events) == 1,
            "one actual WorldChanged and StateChanged subscription after Start");
        Control();
        yield return Wait(.7f);
        CheckMusic(expected, true, "initial native playback");
        yield return WorldClock();
        yield return Finale();

        var oldAudio = audio; var oldEvents = events; var oldManager = manager; var oldRun = run;
        // Unity Play Mode reloads its scene snapshot, including the unsaved Echo entry fixture.
        WorldId restartEntry = Get<WorldId>(manager, "initialWorldId");
        run.RestartRun();
        yield return Frames(4);
        Check(oldAudio == null && oldEvents == null && oldManager == null && oldRun == null, "RestartRun destroys outgoing scene-root audio/events/world/run");
        Bind(true); Control();
        yield return Wait(.7f);
        CheckMusic(restartEntry == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho, true,
            "restart uses Play Mode snapshot initial world=" + restartEntry);
        Check(Subscriptions(manager, "WorldChanged", events) == 1 && Subscriptions(run, "StateChanged", events) == 1,
            "restart binds fresh event handlers exactly once");
        yield return WeaponsAndImpacts();
        yield return HealthAndDefeat();
        oldAudio = audio; oldEvents = events; oldManager = manager; oldRun = run;
        var oldSources = audio.GetComponentsInChildren<AudioSource>(true);
        run.ReturnToMainMenu();
        yield return Frames(4);
        Check(oldAudio == null && oldEvents == null && oldManager == null && oldRun == null && oldSources.All(s => s == null),
            "ReturnToMainMenu destroys all outgoing native sources and event owners");
        Bind(false);
        Check(Time.timeScale == 1, "ReturnToMainMenu restores timeScale");
        yield return MenuMusic("production ReturnToMainMenu: fresh service, no stale bindings");
        Note("OBSERVED ACCEPTED SFX deadline transitions (controlled windows, not request counts): "
            + string.Join(", ", accepted.OrderBy(p => (int)p.Key).Select(p => p.Key + "=" + p.Value))
            + "; music target transitions=" + musicTransitions);
        Check(events.isActiveAndEnabled, "music event component remained enabled through integration and navigation");
    }

    static IEnumerator MenuMusic(string label)
    {
        // Observe production playback before any suspension and after each independent fixture restores it.
        yield return Wait(.7f);
        Check(SceneManager.GetActiveScene().name == "Main Menu" && audio.gameObject.scene == SceneManager.GetActiveScene()
            && events.isActiveAndEnabled && Get<WorldManager>(events, "worldManager") == null
            && Get<RunStageController>(events, "run") == null && Get<SoundId>(events, "currentMusic") == SoundId.MusicMenu,
            label + ": enabled scene-local menu hook selects MusicMenu without world/run bindings");
        CheckMusic(SoundId.MusicMenu, true, label + ": native looping menu playback");
        var target = MusicTarget();
        Check(target != null && target.clip != null && target.clip.name == "Main Menu"
            && Mathf.Abs(target.volume - .4f) < .001f && target.outputAudioMixerGroup != null
            && target.outputAudioMixerGroup.name == "Music" && MusicSources().Count(s => s.isPlaying) == 1
            && Get<AudioSource[]>(audio, "sfxSources").All(s => s.clip == null && !s.isPlaying)
            && Find<AudioSource>().Length == 10 && Find<AudioSource>().All(s => s.transform.IsChildOf(audio.transform)),
            label + ": .4 menu gain via service/Music mixer only, no bare menu source or synthetic SFX left over");
        int samples = target == null ? -1 : target.timeSamples;
        yield return Wait(.15f);
        Check(target != null && target == MusicTarget() && target.isPlaying && target.timeSamples > samples
            && MusicId() == SoundId.MusicMenu && events.isActiveAndEnabled,
            label + ": native menu samples advance without restarting the track");
    }

    static IEnumerator WorldClock()
    {
        var timer = manager.GetComponent<StateSwitchController>();
        Require(timer != null && timer.enabled && timer.SwitchInterval == 15, "serialized enabled 15s world clock");
        Note("FIXTURE full 15s interval reset via CancelTransition; automatic enabled in memory (DebugRun may serialize automatic off); timeScale=1");
        timer.CancelTransition(); timer.AutomaticSwitchingEnabled = true;
        var before = MusicId(); var target = MusicTarget(); var clip = target.clip;
        var entry = manager.CurrentWorldId;
        float start = Time.time, commitTime = -1, midpointProgress = -1;
        int worldChanges = 0, midpoints = 0, warningFrames = 0, preMidFrames = 0, earlyMusicChanges = 0;
        bool callbackMusic = false;
        Action changed = () => { worldChanges++; commitTime = Time.time - start; callbackMusic = MusicId() != before; };
        Action midpoint = () => { midpoints++; midpointProgress = timer.FlipProgress; };
        manager.WorldChanged += changed; timer.TransitionMidpoint += midpoint;
        try
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (worldChanges == 0)
            {
                Require(Time.realtimeSinceStartup < deadline, "natural full world interval completed within 20 real seconds");
                if (timer.WarningProgress > 0) warningFrames++;
                if (timer.IsFlipping && timer.FlipProgress < .5f) preMidFrames++;
                if (MusicId() != before || MusicTarget() != target || target.clip != clip) earlyMusicChanges++;
                Sample(); yield return null;
            }
            Sample();
            Check(warningFrames > 0 && preMidFrames > 0 && earlyMusicChanges == 0,
                "warning and pre-midpoint flip never change music; warningFrames=" + warningFrames + ", preMidFrames=" + preMidFrames);
            Check(worldChanges == 1 && midpoints == 1 && midpointProgress == .5f && callbackMusic
                && manager.CurrentWorldId != entry && commitTime >= 14.95f && commitTime < 15.5f,
                "one committed world change at 15s midpoint; elapsed=" + commitTime + ", progress=" + midpointProgress + ", synchronous game-hook music=" + callbackMusic);
            yield return Wait(.7f);
            CheckMusic(before == SoundId.MusicMaterial ? SoundId.MusicEcho : SoundId.MusicMaterial, true, "post-midpoint incoming native track");
        }
        finally { manager.WorldChanged -= changed; timer.TransitionMidpoint -= midpoint; timer.AutomaticSwitchingEnabled = false; }
        Control();
    }

    static IEnumerator Finale()
    {
        var entrance = manager.GetComponent<FusionTransitionController>();
        var entryId = MusicId(); var entrySource = MusicTarget(); var entryClip = entrySource.clip;
        int worldChanges = 0, faceChanges = 0, thrash = 0, frames = 0;
        World face = null;
        Action changed = () => worldChanges++;
        manager.WorldChanged += changed;
        Note("FIXTURE public TryEnterStage(3) starts actual finale early; not an eight-minute soak; entrance/boss lifecycle NOT invoked reflectively");
        Require(run.TryEnterStage(3), "public stage3 accepted");
        try
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (!run.IsBossPhase)
            {
                Require(Time.realtimeSinceStartup < deadline && !run.IsDefeated, "actual fusion completion and native boss spawn");
                frames++;
                if (entrance.DisplayWorld != face) { face = entrance.DisplayWorld; faceChanges++; }
                if (MusicId() != entryId || MusicTarget() != entrySource || entrySource.clip != entryClip) thrash++;
                Sample(); yield return null;
            }
            Sample();
            Check(frames > 0 && faceChanges >= 3 && worldChanges == 0 && thrash == 0,
                "fusion presentation faces do not thrash BGM: frames=" + frames + ", faces=" + faceChanges + ", committedWorldChanges=" + worldChanges + ", thrash=" + thrash);
        }
        finally { manager.WorldChanged -= changed; }
        Check(MusicId() == SoundId.MusicBoss && Get<SoundId>(events, "currentMusic") == SoundId.MusicBoss, "actual boss spawn StateChanged selects MusicBoss");
        yield return Wait(.7f);
        CheckMusic(SoundId.MusicBoss, true, "boss native playback");
        var bosses = Get<HashSet<EnemyController>>(run, "bosses").ToArray();
        Require(bosses.Length == 1 && bosses[0] != null && bosses[0].health > 0, "one living production boss");
        int deaths = 0; bosses[0].Died += _ => deaths++;
        bosses[0].TakeDamage(bosses[0].health);
        Check(deaths == 1 && !run.IsCompleted, "actual lethal boss damage publishes Died once; victory deferred out of damage stack");
        yield return Until(() => run.IsCompleted, 3, "actual run completion");
        Check(Time.timeScale == 0 && MusicId() == SoundId.Victory && Get<SoundId>(events, "currentMusic") == SoundId.Victory,
            "Victory requested by StateChanged after run pauses at timeScale zero");
        yield return Wait(.7f);
        CheckMusic(SoundId.Victory, false, "paused victory native one-shot");
        var victory = MusicTarget(); var victoryClip = victory.clip; int sample = victory.timeSamples;
        var beforeTransitions = musicTransitions;
        yield return Wait(.15f);
        Check(victory.timeSamples > sample && victory.clip == victoryClip, "Victory samples advance at timeScale zero");
        float remaining = Mathf.Max(0, victoryClip.length - victory.time) + .2f;
        Require(remaining < 45, "victory clip fits bounded natural-completion observation; length=" + victoryClip.length);
        yield return Wait(remaining);
        Check(!victory.isPlaying && !victory.loop && MusicTarget() == victory && musicTransitions == beforeTransitions,
            "Victory naturally finishes once and is not replayed by terminal run frames; clip=" + victoryClip.name + ", length=" + victoryClip.length);
    }

    static IEnumerator WeaponsAndImpacts()
    {
        Control(); audio.StopSfx(); lastDeadlines.Clear();
        var world = manager.CurrentWorld; var hero = world.Player;
        Vector3 home = hero.transform.position;
        var origin = new Vector3(1000, 1000, 0);
        hero.transform.position = origin;
        Note("FIXTURE native starter-controller Update windows at isolated runtime position; attackCounter reset only; real scene prefabs/stats, no manual Update/trigger invocation; Sniper/Aura not added");
        try
        {
            foreach (var id in new[] { SoundId.PistolFire, SoundId.LanternFire, SoundId.LightningStrike })
            {
                Control(); audio.StopSfx(); lastDeadlines.Clear(); Cleanup(); yield return Frames(2);
                Type type = id == SoundId.PistolFire ? typeof(PistolController) : id == SoundId.LanternFire ? typeof(LanternController) : typeof(LightningController);
                var weapon = hero.GetComponentsInChildren<Weapon>(true).SingleOrDefault(w => w.GetType() == type);
                Require(weapon != null && weapon.gameObject.activeInHierarchy && hero.assignedWeapons.Contains(weapon), id + " is a real activated starter weapon");
                var target = Enemy(world, origin + Vector3.right * 2);
                float damage = hero.GetComponent<BuffController>().CalculateWeaponDamage(Get<float>(weapon, "attackDamage") * weapon.stats.damage);
                int before = Count(id);
                var oldBullets = new HashSet<BulletController>(Find<BulletController>());
                var oldLanterns = new HashSet<LanternProjController>(Find<LanternProjController>());
                Physics2D.SyncTransforms(); Set(weapon, "attackCounter", 0f); weapon.enabled = true;
                yield return Until(() => Deadline(id) > 0, 2, id + " accepted via native Update");
                weapon.enabled = false;
                Sample();
                Check(Count(id) == before + 1 && Voice(id), id + " one observed acceptance; correct indexed clip actually playing");
                if (id == SoundId.PistolFire)
                {
                    var bullets = Find<BulletController>().Where(b => !oldBullets.Contains(b)).ToArray();
                    int count = hero.GetComponent<BuffController>().CalculateProjectileCount(Mathf.Max(0, Mathf.FloorToInt(weapon.stats.amount)));
                    Check(bullets.Length == count && count > 0 && bullets.All(b => Mathf.Approximately(b.damage, damage)),
                        "Pistol real volley count=" + bullets.Length + ", unchanged snapshot damage=" + damage);
                    foreach (var b in bullets) { owned.Add(b.gameObject); b.speed = 0; b.transform.position = target.transform.position; }
                    Physics2D.SyncTransforms(); yield return Wait(.08f);
                    Check(Mathf.Approximately(target.health, 1000 - count * damage), "Pistol real prefab collision damage unaffected; HP=" + target.health);
                }
                else if (id == SoundId.LanternFire)
                {
                    var projectiles = Find<LanternProjController>().Where(p => !oldLanterns.Contains(p)).ToArray();
                    int count = hero.GetComponent<BuffController>().CalculateProjectileCount(Mathf.Max(0, Mathf.FloorToInt(Get<float>(weapon, "amount") + weapon.stats.amount)));
                    Check(projectiles.Length == count && count > 0 && projectiles.All(p => Mathf.Approximately(p.damage, damage)),
                        "Lantern real volley count=" + projectiles.Length + ", unchanged snapshot damage=" + damage);
                    foreach (var p in projectiles) owned.Add(p.gameObject);
                }
                else Check(Mathf.Approximately(target.health, 1000 - damage), "Lightning actual native strike damage unaffected; HP=" + target.health + ", damage=" + damage);
                double deadline = Deadline(id);
                yield return Wait(.12f);
                Check(Deadline(id) == deadline, id + " no second request from disabled post-shot observation window");
            }
            Control(); Cleanup(); audio.StopSfx(); lastDeadlines.Clear(); yield return Frames(2);
            var daggerConfig = hero.GetComponentInChildren<DaggerController>(true);
            Require(daggerConfig != null, "existing scene dagger configuration");
            var prefab = Get<GameObject>(daggerConfig, "dagger");
            var victim = Enemy(world, origin + Vector3.right * 3);
            foreach (float damage in new[] { 0f, -5f, 10f })
            {
                audio.StopSfx(); lastDeadlines.Clear();
                float hp = victim.health; int before = Count(SoundId.ProjectileHit);
                var go = Object.Instantiate(prefab, victim.transform.position, Quaternion.identity, world.ContentRoot); owned.Add(go);
                var knife = go.GetComponent<DaggerProjectile>();
                knife.SetDamage(damage); knife.SetSpeed(0); knife.SetDuration(0); knife.SetBounces(0); knife.SetRange(10); knife.SetTarget(victim);
                Physics2D.SyncTransforms(); yield return Until(() => knife == null || !knife.gameObject.activeSelf, 2, "native Dagger trigger damage=" + damage);
                Sample();
                Check(Mathf.Approximately(victim.health, hp - damage), "Dagger native accepted contact applies original damage=" + damage + "; HP=" + victim.health);
                if (damage > 0)
                {
                    Check(Voice(SoundId.DaggerImpact) && !Voice(SoundId.ProjectileHit) && Count(SoundId.ProjectileHit) == before + 1,
                        "finite positive Dagger contact plays ONLY DaggerImpact; shared ProjectileHit budget observed once");
                    double until = Deadline(SoundId.ProjectileHit);
                    // Separate policy probe immediately after an actual collision, not a second game hook.
                    Check(Time.unscaledTimeAsDouble < until && !audio.Play(SoundId.ProjectileHit) && Deadline(SoundId.ProjectileHit) == until,
                        "SEPARATE DIRECT policy probe: actual Dagger acceptance blocks generic impact within shared .1s CD");
                }
                else Check(!Voice(SoundId.DaggerImpact) && Deadline(SoundId.ProjectileHit) == 0,
                    "Dagger zero/healing contact consumes no impact audio budget");
                yield return Frames(2);
            }
        }
        finally { hero.transform.position = home; Control(); Cleanup(); }
    }

    static IEnumerator HealthAndDefeat()
    {
        Control(); audio.StopSfx(); lastDeadlines.Clear(); yield return Frames(2);
        var world = manager.CurrentWorld;
        var enemy = Enemy(world, world.Player.transform.position + Vector3.right * 20);
        enemy.TakeDamage(1); Sample();
        Check(enemy.health == 999 && !Voice(SoundId.EnemyDeath), "enemy nonlethal damage is death-audio silent");
        Object.Destroy(enemy.gameObject); yield return Frames(2);
        Check(Deadline(SoundId.EnemyDeath) == 0, "enemy Destroy/despawn without lethal damage is silent");
        enemy = Enemy(world, world.Player.transform.position + Vector3.right * 20);
        int died = 0; enemy.Died += _ => died++;
        enemy.TakeDamage(1000); Sample(); double enemyDeadline = Deadline(SoundId.EnemyDeath);
        enemy.TakeDamage(1000); Sample();
        Check(died == 1 && Voice(SoundId.EnemyDeath) && enemyDeadline > 0 && Deadline(SoundId.EnemyDeath) == enemyDeadline,
            "enemy actual lethal path: one Died, one accepted EnemyDeath, repeated lethal call latched");
        yield return Frames(2); audio.StopSfx(); lastDeadlines.Clear();
        var hp = world.Player.GetComponent<PlayerHealth>();
        hp.DamageHandler(0); hp.DamageHandler(-5); hp.currentHealth -= 1;
        Check(Deadline(SoundId.PlayerHurt) == 0, "player zero damage, healing and nonlethal setter do not emit hurt");
        float before = hp.currentHealth; hp.DamageHandler(7); Sample();
        Check(hp.currentHealth == before - 7 && Voice(SoundId.PlayerHurt), "positive nonlethal DamageHandler emits indexed PlayerHurt with exact HP loss");
        audio.StopSfx(); lastDeadlines.Clear();
        Note("SEPARATE RAW fixture setup fills eight long native voices; actual lethal PlayerHealth setter must preempt a full pool");
        for (int i = 0; i < 8; i++)
        {
            var clip = AudioClip.Create("AudioGameplay_FullPool_" + i, 441000, 1, 44100, false); owned.Add(clip);
            Require(audio.PlaySfx(clip), "raw full-pool setup slot " + i);
        }
        Check(Get<bool[]>(audio, "busy").All(b => b), "all eight native SFX slots occupied before lethal setter");
        // Setter path deliberately tested here; direct DamageHandler's shared setter is production death entry.
        hp.currentHealth = 0; Sample();
        double deathDeadline = Deadline(SoundId.PlayerDeath);
        Check(hp.IsDead && hp.currentHealth == 0 && Voice(SoundId.PlayerDeath) && deathDeadline > 0
            && Get<SoundId[]>(audio, "voiceIds").Count(id => id == SoundId.PlayerDeath) == 1,
            "actual lethal shared-health setter preempts exactly one full-pool voice with critical PlayerDeath");
        var worlds = Get<World[]>(manager, "worlds");
        foreach (var w in worlds) { var other = w.Player.GetComponent<PlayerHealth>(); other.currentHealth = 0; other.currentHealth = 100; other.DamageHandler(1); }
        yield return Wait(.7f);
        foreach (var w in worlds) { var other = w.Player.GetComponent<PlayerHealth>(); other.currentHealth = 0; other.DamageHandler(1); }
        Sample();
        Check(worlds.All(w => w.Player.GetComponent<PlayerHealth>().IsDead && w.Player.GetComponent<PlayerHealth>().currentHealth == 0)
            && Deadline(SoundId.PlayerDeath) == deathDeadline && Deadline(SoundId.PlayerHurt) == 0,
            "both heroes' death latch prevents revival/repeated death/hurt even after .5s audio CD expires");
        Check(run.IsDefeated && !run.IsCompleted && Time.timeScale == 0 && MusicId() == SoundId.None
            && Get<SoundId>(events, "currentMusic") == SoundId.None && MusicSources().All(s => s.clip == null && !s.isPlaying),
            "actual defeat StateChanged stops music; unscaled fade completes while run paused");
        Cleanup();
    }

    static void Bind(bool gameplay)
    {
        audio = AudioService.Instance;
        Require(audio != null, "real scene AudioService.Instance");
        events = audio.GetComponent<GameAudioEvents>();
        Require(events != null && events.isActiveAndEnabled && audio.transform.parent == null, "enabled scene-root GameAudioEvents on real service");
        Check(Find<AudioService>().Length == 1 && Find<GameAudioEvents>().Length == 1
            && audio.GetComponentsInChildren<AudioSource>(true).Length == 10, "one scene-root audio/events pair, ten native sources");
        manager = Object.FindAnyObjectByType<WorldManager>();
                run = manager == null ? null : (RunStageController)typeof(WorldManager).GetProperty("RunController", Flags).GetValue(manager);
        if (gameplay) Require(manager != null && manager.IsInitialized && run != null && run.IsRunning
            && Get<WorldManager>(events, "worldManager") == manager && Get<RunStageController>(events, "run") == run,
            "GameAudioEvents is bound to this initialized scene's actual manager/run");
        lastDeadlines.Clear(); lastMusic = SoundId.None; Sample();
    }
    static void Control()
    {
        foreach (var weapon in Find<Weapon>()) weapon.enabled = false;
        // Spawners stay enabled: RunStageController validates them before public stage entry.
        foreach (var spawner in Find<EnemySpawner>()) spawner.StopSpawning(true);
        if (manager != null && manager.IsInitialized && run != null && !run.IsCompleted && !run.IsDefeated)
        {
            foreach (var world in Get<World[]>(manager, "worlds"))
            { var hp = world.Player.GetComponent<PlayerHealth>(); if (!hp.IsDead) { hp.maxHealth = 1000000; hp.currentHealth = 1000000; } }
        }
        if (UIController.instance != null && UIController.instance.levelUpPanel != null) UIController.instance.levelUpPanel.SetActive(false);
    }
    static EnemyController Enemy(World world, Vector3 position)
    {
        var go = new GameObject("AudioGameplay controlled native enemy"); owned.Add(go);
        go.transform.SetParent(world.ContentRoot); go.transform.position = position; go.tag = "Enemy";
        var body = go.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.constraints = RigidbodyConstraints2D.FreezeAll;
        go.AddComponent<CircleCollider2D>().radius = .35f;
        var enemy = go.AddComponent<EnemyController>(); enemy.RB = body; enemy.health = 1000; enemy.expDrop = 0; enemy.enabled = false;
        return enemy;
    }
    public static void Cleanup() { foreach (var item in owned) if (item != null) Object.Destroy(item); owned.Clear(); }
    static void Sample()
    {
        if (audio == null) return;
        foreach (var pair in Get<Dictionary<SoundId, double>>(audio, "indexedCooldowns"))
        {
            double previous;
            if (!lastDeadlines.TryGetValue(pair.Key, out previous) || previous != pair.Value)
            {
                lastDeadlines[pair.Key] = pair.Value; accepted[pair.Key] = Count(pair.Key) + 1;
                Note("ACCEPT budget=" + pair.Key + " deadline=" + pair.Value.ToString("F6") + "; voices="
                    + string.Join(",", Get<SoundId[]>(audio, "voiceIds").Select(id => id.ToString())));
            }
        }
        var music = MusicId();
        if (music != lastMusic) { musicTransitions++; lastMusic = music; Note("MUSIC target=" + music + "; scale=" + Time.timeScale + "; clip=" + (MusicTarget() == null || MusicTarget().clip == null ? "null" : MusicTarget().clip.name)); }
    }
    static int Count(SoundId id) { int count; return accepted.TryGetValue(id, out count) ? count : 0; }
    static double Deadline(SoundId id) { double value; return Get<Dictionary<SoundId, double>>(audio, "indexedCooldowns").TryGetValue(id, out value) ? value : 0; }
    static AudioSource MusicTarget() { return Get<AudioSource>(audio, "musicTarget"); }
    static SoundId MusicId()
    {
        var target = MusicTarget();
        return target == null ? SoundId.None : Get<SoundId>(audio, target == Get<AudioSource>(audio, "musicA") ? "musicAId" : "musicBId");
    }
    static AudioSource[] MusicSources() { return new[] { Get<AudioSource>(audio, "musicA"), Get<AudioSource>(audio, "musicB") }; }

    static bool Voice(SoundId id)
    {
        var sources = Get<AudioSource[]>(audio, "sfxSources"); var ids = Get<SoundId[]>(audio, "voiceIds");
        SoundEntry entry; if (!Get<AudioCatalog>(audio, "catalog").TryGet(id, out entry)) return false;
        var clips = Get<AudioClip[]>(entry, "clips");
        return Enumerable.Range(0, sources.Length).Any(i => ids[i] == id && sources[i].isPlaying && clips.Contains(sources[i].clip));
    }
    static void CheckMusic(SoundId id, bool loop, string label)
    {
        SoundEntry entry; Require(Get<AudioCatalog>(audio, "catalog").TryGet(id, out entry), "configured music entry " + id);
        var source = MusicTarget();
        Check(events.isActiveAndEnabled && MusicId() == id && source != null && source.isPlaying && source.timeSamples > 0
            && source.loop == loop && Get<AudioClip[]>(entry, "clips").Contains(source.clip)
            && Mathf.Abs(source.volume - entry.Volume) < .001f, label + ": id=" + MusicId() + ", clip=" + (source == null || source.clip == null ? "null" : source.clip.name)
            + ", samples=" + (source == null ? -1 : source.timeSamples));
    }
    static int Subscriptions(object source, string field, object target)
    { var callback = Get<Delegate>(source, field); return callback == null ? 0 : callback.GetInvocationList().Count(d => ReferenceEquals(d.Target, target)); }
    static T[] Find<T>() where T : Object { return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
    static T Get<T>(object source, string field) { return (T)source.GetType().GetField(field, Flags).GetValue(source); }
    static void Set(object source, string field, object value) { source.GetType().GetField(field, Flags).SetValue(source, value); }
    static IEnumerator Wait(float seconds) { for (float until = Time.realtimeSinceStartup + seconds; Time.realtimeSinceStartup < until;) { Sample(); yield return null; } }
    static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) { Sample(); yield return null; } }
    static IEnumerator Until(Func<bool> predicate, float seconds, string label)
    { float until = Time.realtimeSinceStartup + seconds; while (!predicate()) { Require(Time.realtimeSinceStartup < until, label + " deadline"); Sample(); yield return null; } Sample(); }
    public static string Arg(string key, string fallback) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
    static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Note((ok ? "PASS " : "FAIL ") + text); }
    static void Note(string text) { Evidence.Add(text); Debug.Log("AUDIO_GAMEPLAY " + text); }
}
