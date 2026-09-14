using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

// External PlayMode host: StartCoroutine(AudioCatalogChecks.Run()); forward real frame yields.
// Disposable menu service: suspend its enabled GameAudioEvents, then restore in Dispose.
// An already-idle service with no enabled binder is also supported; dispose on cancellation (host deadline >= 15s).
// Read-only configured-catalog lookup, then generated clips/temporary SO for policy checks.
// Projectile checks are SOURCE ONLY, not collision execution.
public sealed class AudioCatalogChecks : IDisposable
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }
    public static readonly List<string> Evidence = new List<string>();
    const SoundId Hit = SoundId.ProjectileHit, Other = SoundId.PistolFire;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly float savedScale = Time.timeScale;
    readonly List<AudioClip> clips = new List<AudioClip>();
    readonly List<Action> restore = new List<Action>();
    AudioService service;
    GameAudioEvents suspendedEvents;
    AudioCatalog catalog, savedCatalog;
    AudioSource[] voices;
    string snapshot;
    bool owned, disposed;

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("AudioCatalogChecks already running");
        Running = true; Passed = Failed = 0; Evidence.Clear();
        var test = new AudioCatalogChecks();
        var steps = test.Execute();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool completed = false;
        Application.logMessageReceived += OnLog;
        try
        {
            while (true)
            {
                bool moved = false;
                Exception error = null;
                try
                {
                    if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("10s catalog fixture deadline");
                    moved = steps.MoveNext();
                }
                catch (Exception e) { error = e; }
                if (error != null) { Check(false, "aborted: " + error); break; }
                if (!moved) { completed = true; break; }
                yield return steps.Current;
            }
        }
        finally
        {
            try
            {
                if (!completed && Failed == 0) Check(false, "iterator cancelled before completion");
                try { (steps as IDisposable)?.Dispose(); } finally { test.Dispose(); }
            }
            finally { Application.logMessageReceived -= OnLog; Running = false; }
            Debug.Log("AUDIO CATALOG RESULT " + Passed + " passed / " + Failed + " failed");
        }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "disposable Play session required");
        yield return null;
        service = AudioService.Instance;
        Require(service != null && service.isActiveAndEnabled && service.gameObject.scene.IsValid(), "active scene AudioService");
        SuspendMenuEvents();
        voices = Get<AudioSource[]>(service, "sfxSources");
        var all = voices.Concat(new[] { Get<AudioSource>(service, "musicA"), Get<AudioSource>(service, "musicB") }).ToArray();
        Require(voices.Length == 8 && all.All(s => s != null && s.isActiveAndEnabled && !s.isPlaying && s.clip == null)
            && !Get<bool>(service, "fading"), "idle configured service after explicit menu-binder suspension; never clear unrelated playback");
        savedCatalog = Get<AudioCatalog>(service, "catalog");
        ConfiguredCatalog();
        SaveMap<SoundId, int>("nextClips"); SaveMap<SoundId, double>("indexedCooldowns"); SaveMap<AudioClip, double>("cooldowns");
        foreach (var source in all)
        {
            float volume = source.volume, pitch = source.pitch; bool loop = source.loop;
            restore.Add(() => { source.volume = volume; source.pitch = pitch; source.loop = loop; });
        }
        float rawCooldown = Get<float>(service, "sfxCooldown"); restore.Add(() => Set(service, "sfxCooldown", rawCooldown));
        owned = true; Time.timeScale = 1; Set(service, "sfxCooldown", .04f);
        catalog = ScriptableObject.CreateInstance<AudioCatalog>();
        Set(service, "catalog", catalog);
        for (int i = 0; i < 2; i++) clips.Add(AudioClip.Create("AudioCatalogChecks_Silent_" + i, 441000, 1, 44100, false));
        InvalidEntries();
        Reset(); Install(Entry(Hit, .1f, 2, false, clips[0], null, clips[1]));
        Check(catalog.TryGet(Hit, out var hit) && hit.Cooldown == .1f && hit.MaxVoices == 2, "temporary ProjectileHit policy: global ID cooldown .1s, max two (not asset-default evidence)");
        double start = Time.unscaledTimeAsDouble;
        Check(Play(Hit) && !Play(Hit) && Count(clips[0]) == 1 && Count(clips[1]) == 0, "variants share ID cooldown; rejected selection does not advance");
        Check(Math.Abs(Get<Dictionary<SoundId, double>>(service, "indexedCooldowns")[Hit] - start - .1) < .00001, "accepted ID stores exactly .1s unscaled deadline");
        while (Time.unscaledTimeAsDouble < start + .11) yield return null;
        Check(Play(Hit) && Count(clips[1]) == 1, "after cooldown, next non-null variant plays");
        start = Time.unscaledTimeAsDouble;
        while (Time.unscaledTimeAsDouble < start + .11) yield return null;
        Check(!Play(Hit) && voices.Count(s => s.clip != null) == 2, "third ID voice rejected after cooldown expires");
        Time.timeScale = 0; Invoke(service, "UpdateVoices");
        Check(Get<bool[]>(service, "paused").Count(p => p) == 2 && voices.Where(s => s.clip != null).All(s => !s.isPlaying), "two paused voices remain reserved");
        // Replace the entry, not its serialized fields, to isolate maxVoices from the pause admission gate.
        Install(Entry(Hit, 0, 2, true, clips[0]), Entry(Other, 0, 8, true, clips[1]));
        Check(!Play(Hit), "per-ID max counts paused voices even when new requests may play paused");
        for (int i = 0; i < 6; i++) Check(Play(Other), "paused pool remaining slot " + (i + 1));
        Check(!Play(Other), "global eight includes two paused and six pause-allowed voices");
        service.StopSfx();
        Check(!Get<bool[]>(service, "paused").Any(p => p) && voices.All(s => s.clip == null), "StopSfx releases paused reservations");
        Reset(); Install(Entry(Hit, 10, 2, false, clips[0]), Entry(Other, 10, 2, false, clips[0]));
        Check(Play(Hit) && Play(Other) && !Play(Hit) && !Play(Other) && Count(clips[0]) == 2, "different IDs sharing one clip have independent cooldowns");
        Check(service.PlaySfx(clips[0]) && !service.PlaySfx(clips[0]), "indexed play does not consume raw per-clip cooldown");
        Reset(); Check(service.PlaySfx(clips[0]) && Play(Hit), "raw cooldown does not consume indexed cooldown");
        Reset(); Install(Entry(Hit, 10, 2, false, clips[0], clips[1]));
        Time.timeScale = 0;
        Check(!Play(Hit) && Unconsumed(Hit), "pause rejection consumes neither cooldown nor variant cursor");
        Time.timeScale = 1;
        Check(Play(Hit) && Count(clips[0]) == 1, "same-frame retry after unpause succeeds on first variant");
        Reset(); Install(Entry(Other, 0, 8, true, clips[0]), Entry(Hit, 10, 2, true, clips[1], clips[0]));
        for (int i = 0; i < 8; i++) Check(Play(Other), "global slot " + (i + 1));
        Check(!Play(Hit) && Unconsumed(Hit) && voices.Count(s => s.clip != null) == 8, "global eight full: ninth ID rejected without cooldown/cursor");
        voices[0].Stop(); // Free a real voice, NOT StopSfx (which would hide rejection consuming cooldown).
        Check(Play(Hit) && Count(clips[1]) == 1, "full-pool retry succeeds immediately on first variant after one voice stops");
        Time.timeScale = 0; service.StopSfx();
        Check(voices.All(s => s.clip == null && !s.isPlaying) && !Get<bool[]>(service, "busy").Any(b => b)
            && !Get<bool[]>(service, "paused").Any(b => b) && Get<SoundId[]>(service, "voiceIds").All(id => id == SoundId.None)
            && Get<Dictionary<SoundId, double>>(service, "indexedCooldowns").Count == 0
            && Get<Dictionary<AudioClip, double>>(service, "cooldowns").Count == 0, "StopSfx clears voices, paused flags, IDs and both cooldown maps");
        Check(Play(Hit) && service.PlaySfx(clips[1], playWhilePaused: true), "StopSfx permits immediate indexed/raw restart while paused");
        Reset();
        var looping = Entry(Other, 0, 2, false, clips[1]); Set(looping, "loop", true); Install(looping);
        Check(Play(Other) && voices.Single(s => s.clip != null).loop, "looping indexed SFX remains supported");
        foreach (var frame in SharedImpactBudget()) yield return frame;
        foreach (var frame in CriticalPreemption()) yield return frame;
        Reset(); Set(service, "catalog", null);
        Check(!Play(Hit) && service.PlaySfx(clips[0]) && service.PlayMusic(clips[1], fadeDuration: 0), "missing catalog rejects IDs but raw SFX/music still work");
        service.StopMusic(0); service.StopSfx();
        Unchanged(); ProjectileHooks();
    }

    void ConfiguredCatalog()
    {
        Require(savedCatalog != null, "configured GameAudioCatalog required");
        var ids = Enum.GetValues(typeof(SoundId)).Cast<SoundId>().Where(id => id != SoundId.None).ToArray();
        string before = JsonUtility.ToJson(savedCatalog);
        Check(ids.Length == 17 && ids.Distinct().Count() == 17 && Get<List<SoundEntry>>(savedCatalog, "entries").Count == 17
            && ids.All(id => savedCatalog.TryGet(id, out _)), "configured catalog: clips lookup succeeds for 17/17 stable IDs");
        var configuredClips = Get<List<SoundEntry>>(savedCatalog, "entries")
            .SelectMany(entry => Get<AudioClip[]>(entry, "clips")).ToArray();
        Check(configuredClips.Length == 18 && configuredClips.All(clip => clip != null)
            && configuredClips.Distinct().Count() == 18, "configured 17 IDs retain 18 distinct real clip references, including DaggerImpact and MusicMenu");
        Check((int)SoundId.MusicMenu == 404 && savedCatalog.TryGet(SoundId.MusicMenu, out var menu)
            && menu.Channel == SoundChannel.Music && menu.Volume == .4f && menu.Cooldown == 0f
            && menu.MaxVoices == 1 && menu.Loop && menu.PlayWhilePaused
            && Get<AudioClip[]>(menu, "clips").Length == 1 && Get<AudioClip[]>(menu, "clips")[0].name == "Main Menu",
            "configured MusicMenu (404): Main Menu clip, Music channel, .4 gain, loop, zero cooldown, one voice, pause allowed");
        Check(savedCatalog.TryGet(SoundId.MusicEcho, out var echo)
            && Get<AudioClip[]>(echo, "clips").Length == 1 && Get<AudioClip[]>(echo, "clips")[0].name == "Ruined World",
            "MusicEcho retains the renamed Ruined World clip, not the new menu track");
        Check(savedCatalog.TryGet(Hit, out var impact) && impact.Channel == SoundChannel.Sfx
            && impact.Cooldown == .1f && impact.MaxVoices == 2,
            "production ProjectileHit owns shared DaggerImpact/ProjectileHit .1s cooldown and two-voice cap");
        Check((int)SoundId.LanternFire == 101 && (int)SoundId.DaggerImpact == 106, "renamed LanternFire/DaggerImpact retain serialized IDs 101/106");
        foreach (var id in new[] { SoundId.LanternFire, SoundId.DaggerImpact })
        {
            string clipName = id == SoundId.LanternFire ? "weapon - fireball spawn" : "weapon - dagger impact";
            float cooldown = id == SoundId.LanternFire ? .12f : .1f;
            Check(savedCatalog.TryGet(id, out var entry) && entry.Channel == SoundChannel.Sfx
                && entry.Volume == .35f && entry.Cooldown == cooldown && entry.MaxVoices == 2
                && !entry.Loop && !entry.PlayWhilePaused
                && Get<AudioClip[]>(entry, "clips").Length == 1 && Get<AudioClip[]>(entry, "clips")[0].name == clipName,
                "configured " + id + ": existing package clip, .35 gain, " + cooldown + "s cooldown, two voices");
        }
        Check(savedCatalog.TryGet(SoundId.PlayerDeath, out var death) && death.Channel == SoundChannel.Sfx
            && death.PlayWhilePaused && death.Cooldown == .5f && death.MaxVoices == 1,
            "configured PlayerDeath retains pause permission, .5s cooldown and one voice");
        Check(JsonUtility.ToJson(savedCatalog) == before, "configured catalog lookup leaves serialized asset unchanged");
    }

    IEnumerable<object> SharedImpactBudget()
    {
        const SoundId Dagger = SoundId.DaggerImpact;
        foreach (bool daggerFirst in new[] { false, true })
        {
            Reset();
            var dagger = Entry(Dagger, 0, 8, true, clips[1], clips[0]);
            Set(dagger, "volume", .23f); Set(dagger, "loop", true);
            Install(Entry(Hit, .1f, 2, false, clips[0], clips[1]), dagger, Entry(Other, 0, 8, true, clips[0]));
            SoundId first = daggerFirst ? Dagger : Hit, second = daggerFirst ? Hit : Dagger;
            var deadlines = Get<Dictionary<SoundId, double>>(service, "indexedCooldowns");
            var cursors = Get<Dictionary<SoundId, int>>(service, "nextClips");
            double start = Time.unscaledTimeAsDouble;
            Require(Play(first), "shared impact first request " + first);
            double deadline = deadlines[Hit];
            Check(Math.Abs(deadline - start - .1) < .00001 && !deadlines.ContainsKey(Dagger),
                "either impact stores only the generic .1s cooldown key despite dagger's zero local cooldown");
            Check(!Play(second) && deadlines[Hit] == deadline && !cursors.ContainsKey(second)
                && cursors[first] == 1 && voices.Count(s => s.clip != null) == 1,
                "alternating " + first + " -> " + second + " cannot bypass or extend cooldown or advance rejected variants");
            while (Time.unscaledTimeAsDouble < deadline + .01) yield return null;
            Require(Play(second), "alternate impact accepted after shared cooldown");
            deadline = deadlines[Hit];
            var ids = Get<SoundId[]>(service, "voiceIds");
            int daggerSlot = Array.IndexOf(ids, Dagger);
            Check(Count(clips[0]) == 1 && Count(clips[1]) == 1 && cursors[Hit] == 1 && cursors[Dagger] == 1
                && voices[daggerSlot].clip == clips[1] && voices[daggerSlot].volume == .23f && voices[daggerSlot].loop,
                "grouped requests retain selected clips, volume, loop, separate variant cursors and original voice IDs");
            while (Time.unscaledTimeAsDouble < deadline + .01) yield return null;
            Check(!Play(Hit) && !Play(Dagger) && deadlines[Hit] == deadline
                && cursors[Hit] == 1 && cursors[Dagger] == 1 && voices.Count(s => s.clip != null) == 2,
                "alternating IDs cannot exceed generic two-voice cap even after cooldown; rejection leaves deadline/cursors unchanged");
            Time.timeScale = 0; Invoke(service, "UpdateVoices");
            Check(Get<bool[]>(service, "paused")[Array.IndexOf(ids, Hit)] && voices[daggerSlot].isPlaying
                && !Play(Dagger) && deadlines[Hit] == deadline,
                "paused generic plus active pause-allowed dagger still exhaust the shared two-voice cap");
            Check(Play(Other) && !service.PlaySfx(clips[0])
                && service.PlaySfx(clips[0], playWhilePaused: true),
                "unrelated indexed budget and direct clip playback remain independent; raw pause admission unchanged");
            Time.timeScale = 1; Invoke(service, "UpdateVoices");
            voices[Array.IndexOf(ids, Hit)].Stop();
            Check(Play(Dagger) && cursors[Dagger] == 0 && cursors[Hit] == 1
                && ids.Count(id => id == Dagger) == 2,
                "freeing one grouped voice permits immediate dagger retry on its own next variant, without generic double-play");
        }
        Reset();
        var pausedDagger = Entry(Dagger, 0, 8, true, clips[1]);
        Install(Entry(Hit, .1f, 2, false, clips[0]), pausedDagger);
        Time.timeScale = 0;
        Check(!Play(Hit) && Unconsumed(Hit) && Play(Dagger) && Count(clips[1]) == 1,
            "selected dagger pause permission is not replaced by generic budget entry's pause rejection");
        Reset(); Install(Entry(Hit, .1f, 2, true, clips[0]), Entry(Dagger, 0, 8, false, clips[1]));
        Time.timeScale = 0;
        Check(!Play(Dagger) && Unconsumed(Dagger) && Unconsumed(Hit) && Play(Hit),
            "generic pause permission cannot admit a pause-disallowed dagger or consume its shared budget");
        Reset(); Install(Entry(Dagger, .2f, 1, false, clips[1], clips[0]));
        double fallbackStart = Time.unscaledTimeAsDouble;
        Require(Play(Dagger), "synthetic dagger-only catalog accepts selected entry fallback");
        var fallbackDeadlines = Get<Dictionary<SoundId, double>>(service, "indexedCooldowns");
        double fallbackDeadline = fallbackDeadlines[Hit];
        Check(Math.Abs(fallbackDeadline - fallbackStart - .2) < .00001 && !fallbackDeadlines.ContainsKey(Dagger)
            && !Play(Dagger) && fallbackDeadlines[Hit] == fallbackDeadline,
            "missing generic policy uses selected .2s cooldown under the same shared key; rejection does not extend it");
        while (Time.unscaledTimeAsDouble < fallbackDeadline + .01) yield return null;
        Check(!Play(Dagger) && Count(clips[1]) == 1 && fallbackDeadlines[Hit] == fallbackDeadline,
            "missing generic policy also uses selected one-voice cap after cooldown expires");
        Reset();
    }

    IEnumerable<object> CriticalPreemption()
    {
        const SoundId Death = SoundId.PlayerDeath;
        foreach (bool gamePaused in new[] { false, true })
        {
            Reset(); Install(Entry(Hit, 10, 2, false, clips[0]), Entry(Other, 0, 8, false, clips[0]),
                Entry(Death, 10, 1, true, clips[1], clips[0]));
            Require(Play(Hit), "ordinary cooldown-owning victim starts");
            for (int i = 1; i < 8; i++) Require(Play(Other), "ordinary fill " + i);
            yield return null;
            Time.timeScale = gamePaused ? 0 : 1;
            Invoke(service, "UpdateVoices");
            var deadlines = Get<Dictionary<SoundId, double>>(service, "indexedCooldowns");
            double ordinaryDeadline = deadlines[Hit];
            int sources = service.GetComponentsInChildren<AudioSource>(true).Length;
            Check(Get<bool[]>(service, "busy").Count(b => b) == 8
                && Get<bool[]>(service, "paused").Count(p => p) == (gamePaused ? 8 : 0),
                "preemption setup: exactly eight occupied voices; paused=" + gamePaused);
            Check(!service.PlaySfx(clips[1], playWhilePaused: true)
                && !Get<Dictionary<AudioClip, double>>(service, "cooldowns").ContainsKey(clips[1]),
                "raw death clip has no priority and full-pool rejection consumes no raw cooldown");
            Require(Play(Death), "indexed death admitted at full pool; paused=" + gamePaused);
            Check(voices[0].clip == clips[1] && voices.Skip(1).All(s => s.clip == clips[0])
                && Get<SoundId[]>(service, "voiceIds")[0] == Death
                && Get<bool[]>(service, "busy").Count(b => b) == 8
                && !Get<bool[]>(service, "paused")[0] && Get<bool[]>(service, "allowPaused")[0]
                && service.GetComponentsInChildren<AudioSource>(true).Length == sources,
                "death replaces exactly one ordinary voice without growing pool or retaining victim pause flags");
            double deathDeadline = deadlines[Death];
            int cursor = Get<Dictionary<SoundId, int>>(service, "nextClips")[Death];
            Check(!Play(Death) && deadlines[Death] == deathDeadline
                && Get<Dictionary<SoundId, int>>(service, "nextClips")[Death] == cursor
                && deadlines[Hit] == ordinaryDeadline, "preemption/rejected repeat preserve victim and death ID cooldowns and variant cursor");
            yield return null;
            Check(voices[0].isPlaying && voices.Count(s => s.isPlaying) == (gamePaused ? 1 : 8),
                "death plays across real frame while other voices retain pause policy");
            Time.timeScale = 1; Invoke(service, "UpdateVoices");
            Check(!Play(Hit) && deadlines[Hit] == ordinaryDeadline, "preempted ordinary ID still rejects during its cooldown after unpause");
            // Isolate the max-voice gate from cooldown without a ten-second fixture delay.
            deadlines[Death] = Time.unscaledTimeAsDouble;
            Check(!Play(Death) && Count(clips[1]) == 1, "death maxVoices still rejects repeat after cooldown deadline");
            deadlines[Death] = deathDeadline;
            voices[0].Stop(); Invoke(service, "UpdateVoices");
            Check(voices[0].clip == null && !Get<bool[]>(service, "busy")[0]
                && !Get<bool[]>(service, "paused")[0] && !Get<bool[]>(service, "allowPaused")[0]
                && Get<SoundId[]>(service, "voiceIds")[0] == SoundId.None
                && !Play(Death) && deadlines[Death] == deathDeadline,
                "stopped critical voice releases ownership, not its SoundId cooldown");
            yield return null;
            Check(voices[0].clip == null && voices.Skip(1).All(s => s.isPlaying),
                "unpause resumes seven survivors, never resurrects preempted playback");
        }
        Reset(); Install(Entry(Other, 0, 8, true, clips[0]), Entry(Death, 0, 1, true, clips[1]));
        for (int i = 0; i < 7; i++) Require(Play(Other), "free-slot setup " + i);
        Check(Play(Death) && voices[7].clip == clips[1] && voices.Take(7).All(s => s.clip == clips[0]),
            "death prefers the last free slot over preempting any ordinary voice");
        Check(!Play(Other) && voices[7].clip == clips[1], "ordinary indexed request cannot preempt death or ordinary voices");
        Reset(); Install(Entry(Death, 0, 9, true, clips[1]));
        for (int i = 0; i < 8; i++) Require(Play(Death), "critical-only pool setup " + i);
        Check(!Play(Death) && Count(clips[1]) == 8, "even with relaxed per-ID cap, death never preempts another death voice");
        Reset();
    }

    void InvalidEntries()
    {
        Reset(); Install(Entry(Hit, 0, 2, false, clips[0]));
        Check(catalog.TryGet(Hit, out _), "OnValidate invalidates empty OnEnable cache after reflection setup");
        foreach (int count in new[] { 2, 3 })
        {
            Install(Enumerable.Range(0, count).Select(_ => Entry(Hit, 0, 2, false, clips[0])).ToArray());
            Check(!catalog.TryGet(Hit, out var found) && found == null && !Play(Hit), count + " duplicate IDs fail closed");
        }
        Install(Entry(Hit, 0, 2, false, clips[0]), Entry(Hit, -1, 2, false, clips[0]), Entry(Hit, 0, 2, false, clips[1]));
        Check(!Play(Hit), "invalid middle duplicate cannot erase ambiguity tombstone");
        Install(null, Entry(SoundId.None, 0, 2, false, clips[0]), Entry((SoundId)99999, 0, 2, false, clips[0]));
        Check(!Play(SoundId.None) && !Play((SoundId)99999) && !Play(Other), "None, undefined and missing ID rejected; null entry safe");
        foreach (var missing in new[] { null, new AudioClip[0], new AudioClip[] { null } })
        { Install(Entry(Hit, 0, 2, false, missing)); Check(!Play(Hit), "null/empty/all-null clips rejected"); }
        var invalid = new Dictionary<string, object[]> {
            { "volume", new object[] { -1f, 1.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } },
            { "cooldown", new object[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } },
            { "maxVoices", new object[] { 0, -1 } }, { "channel", new object[] { (SoundChannel)99 } }
        };
        foreach (var field in invalid) foreach (var value in field.Value)
        {
            var entry = Entry(Hit, 0, 2, false, clips[0]); Set(entry, field.Key, value); Install(entry);
            Check(!catalog.TryGet(Hit, out var found) && found == null && !Play(Hit), "invalid " + field.Key + "=" + value);
        }
        Check(voices.All(s => s.clip == null), "invalid requests allocate no voices");
    }

    static void ProjectileHooks()
    {
        foreach (var name in new[] { "ArrowController", "BulletController", "DaggerProjectile", "LanternProjController", "PhysicsBullet" })
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Game/Features/Weapons/Projectiles", name + ".cs"));
            text = Regex.Replace(text, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
            string expectedId = name == "DaggerProjectile" ? "DaggerImpact" : "ProjectileHit";
            string hook = @"if\s*\(damageDealt > 0f && !float\.IsNaN\(damageDealt\) && !float\.IsInfinity\(damageDealt\)\)\s*AudioService\.Instance\?\.Play\(SoundId\." + expectedId + @"\);";
            int audio = text.IndexOf("AudioService.Instance", StringComparison.Ordinal);
            int damage = text.IndexOf(".TakeDamage(", StringComparison.Ordinal);
            int buff = text.IndexOf("if (buffSource != null", StringComparison.Ordinal);
            Check(Regex.Matches(text, hook).Count == 1 && Regex.Matches(text, @"Play\(SoundId\.(ProjectileHit|DaggerImpact)\)").Count == 1
                && damage >= 0 && damage < audio && audio < buff, "SOURCE ONLY " + name + ": one guarded " + expectedId + " request after damage, independent of buff source; never both impact IDs");
        }
    }

    static SoundEntry Entry(SoundId id, float cooldown, int max, bool allowPaused, params AudioClip[] variants)
    {
        var entry = new SoundEntry(); Set(entry, "id", id); Set(entry, "clips", variants);
        Set(entry, "cooldown", cooldown); Set(entry, "maxVoices", max); Set(entry, "playWhilePaused", allowPaused); return entry;
    }
    void Install(params SoundEntry[] entries)
    {
        Unchanged(); Set(catalog, "entries", entries.ToList()); Invoke(catalog, "OnValidate"); snapshot = JsonUtility.ToJson(catalog);
    }
    void Unchanged() { if (snapshot != null) Check(JsonUtility.ToJson(catalog) == snapshot, "serialized temporary catalog entries unchanged by lookup/playback"); }
    bool Play(SoundId id)
    {
        var state = UnityEngine.Random.state;
        try { return service.Play(id); }
        finally
        {
            bool unchanged = UnityEngine.Random.state.Equals(state);
            if (!unchanged) UnityEngine.Random.state = state; // Do not leak a regression into gameplay RNG.
            Check(unchanged, "Play(" + id + ") leaves Unity.Random.state untouched");
        }
    }
    int Count(AudioClip clip) => voices.Count(s => s.clip == clip);
    bool Unconsumed(SoundId id) => !Get<Dictionary<SoundId, double>>(service, "indexedCooldowns").ContainsKey(id == SoundId.DaggerImpact ? Hit : id)
        && !Get<Dictionary<SoundId, int>>(service, "nextClips").ContainsKey(id);
    void Reset() { Time.timeScale = 1; service.StopSfx(); Get<Dictionary<SoundId, int>>(service, "nextClips").Clear(); }
    void SaveMap<K, V>(string name)
    {
        var map = Get<Dictionary<K, V>>(service, name); var saved = new Dictionary<K, V>(map);
        restore.Add(() => { map.Clear(); foreach (var pair in saved) map.Add(pair.Key, pair.Value); });
    }
    static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    static void Invoke(object obj, string name) => obj.GetType().GetMethod(name, Private).Invoke(obj, null);
    static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Evidence.Add((ok ? "PASS " : "FAIL ") + text); Debug.Log("AUDIO CATALOG " + Evidence[Evidence.Count - 1]); }
    static void OnLog(string text, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { Failed++; Evidence.Add("FAIL Unity " + type + ": " + text + "\n" + stack); } }
    void SuspendMenuEvents()
    {
        var binder = service.GetComponent<GameAudioEvents>();
        if (binder == null || !binder.isActiveAndEnabled) return;
        Require(service.gameObject.scene.name == "Main Menu"
            && Get<WorldManager>(binder, "worldManager") == null && Get<RunStageController>(binder, "run") == null
            && Get<SoundId>(binder, "currentMusic") == SoundId.MusicMenu,
            "only a running menu binder may be suspended; gameplay and broken menu startup are not synthetic fixtures");
        suspendedEvents = binder;
        binder.enabled = false; // OnDisable unbinds and stops only music; retain the strict idle/SFX precondition.
        Check(!binder.isActiveAndEnabled, "explicitly suspend menu GameAudioEvents for synthetic catalog policy checks");
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try
        {
            if (owned && service != null)
            { service.StopMusic(0); service.StopSfx(); Set(service, "catalog", savedCatalog); foreach (var action in restore) action(); }
        }
        finally
        {
            try { Time.timeScale = savedScale; foreach (var clip in clips) Object.Destroy(clip); if (catalog != null) Object.Destroy(catalog); }
            finally
            {
                // Rebind only after the real catalog, source policy and time scale have been restored.
                if (suspendedEvents != null)
                {
                    suspendedEvents.enabled = true;
                    Check(suspendedEvents.isActiveAndEnabled && Get<SoundId>(suspendedEvents, "currentMusic") == SoundId.MusicMenu,
                        "restore previously enabled menu GameAudioEvents after catalog fixture, including cancellation");
                }
            }
        }
    }
}
