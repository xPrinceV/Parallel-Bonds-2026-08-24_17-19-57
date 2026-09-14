using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

// Disposable Play session, real scene singleton, external host: StartCoroutine(AudioServiceChecks.Run()).
// Forward real frame yields; dispose the iterator on timeout/cancellation (host deadline >= 20s).
// Suspends an enabled menu GameAudioEvents for idle synthetic checks; Dispose restores it.
// Playback/sample/fade evidence only: no human listening or gameplay-event integration coverage.
public sealed class AudioServiceChecks : IDisposable
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static bool Running { get; private set; }
    public static readonly List<string> Evidence = new List<string>();
    static readonly string[] Parameters = { "MasterVolume", "MusicVolume", "SfxVolume" };
    readonly List<AudioClip> clips = new List<AudioClip>();
    readonly float[] savedMixer = new float[3];
    readonly bool[] captured = new bool[3];
    readonly float savedScale = Time.timeScale;
    AudioService service;
    GameAudioEvents suspendedEvents;
    AudioMixer mixer;
    AudioSource[] sfx, all;
    bool owned, disposed;

    public static IEnumerator Run()
    {
        if (Running) throw new InvalidOperationException("AudioServiceChecks already running");
        Running = true; Passed = Failed = 0; Evidence.Clear();
        var test = new AudioServiceChecks();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var steps = test.Execute();
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
                    if (clock.Elapsed.TotalSeconds > 15) throw new TimeoutException("15s audio fixture deadline");
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
            Debug.Log("AUDIO RESULT " + Passed + " passed / " + Failed + " failed; generated clips Destroy scheduled");
        }
    }

    IEnumerator Execute()
    {
        Require(Application.isPlaying, "disposable Play session required");
        yield return null; // Let the real service Start before mixer writes.
        service = AudioService.Instance;
        Require(service != null && service.isActiveAndEnabled && service.gameObject.scene.IsValid()
            && service.transform.parent == null, "active scene-root AudioService.Instance");
        SuspendMenuEvents();
        sfx = Field<AudioSource[]>("sfxSources");
        var a = Field<AudioSource>("musicA"); var b = Field<AudioSource>("musicB");
        all = new[] { a, b }.Concat(sfx).ToArray();
        Require(sfx.Length == 8 && all.All(s => s != null && s.isActiveAndEnabled)
            && all.Distinct().Count() == 10 && service.GetComponentsInChildren<AudioSource>(true).Length == 10,
            "ten distinct native sources: two music, eight SFX");
        Require(Idle(all) && all.All(s => !s.playOnAwake), "synthetic fixture sources idle after explicit binder suspension, no clips/playOnAwake");
        Check(Find<AudioListener>().Count(l => l.isActiveAndEnabled) == 1, "one active native listener");
        mixer = Field<AudioMixer>("mixer");
        Require(mixer != null, "configured mixer");
        for (int i = 0; i < 3; i++) { captured[i] = mixer.GetFloat(Parameters[i], out savedMixer[i]); Require(captured[i], "exposed " + Parameters[i]); }
        Check(a.outputAudioMixerGroup?.name == "Music" && b.outputAudioMixerGroup?.name == "Music"
            && sfx.All(s => s.outputAudioMixerGroup?.name == "SFX")
            && all.All(s => s.outputAudioMixerGroup.audioMixer == mixer), "native Music/SFX mixer routing");
        owned = true; Time.timeScale = 1;
        for (int i = 0; i < 9; i++)
        {
            var clip = AudioClip.Create("AudioServiceChecks_Quiet_" + i, 88200, 1, 44100, false);
            clips.Add(clip);
            var data = new float[88200];
            for (int n = 0; n < data.Length; n++) data[n] = .005f * Mathf.Sin(2 * Mathf.PI * (220 + i * 30) * n / 44100);
            Require(clip.SetData(data, 0), "generated two-second quiet clip " + i);
        }
        int objects = Find<GameObject>().Length, sources = Find<AudioSource>().Length;
        Check(!service.PlayMusic(null) && !service.PlayMusic(clips[0], fadeDuration: float.NaN)
            && !service.PlaySfx(null) && !service.PlaySfx(clips[0], volume: float.NaN)
            && new[] { 0f, -1f, float.NaN, float.PositiveInfinity }.All(p => !service.PlaySfx(clips[0], pitch: p))
            && Idle(all), "missing clips/nonfinite inputs/bad pitch start nothing");
        Require(service.PlaySfx(clips[0]), "first SFX accepted");
        Check(!service.PlaySfx(clips[0]), "same-frame per-clip cooldown rejects repeat");
        for (int i = 1; i < 8; i++) Check(service.PlaySfx(clips[i]), "same-frame SFX slot " + i);
        Check(!service.PlaySfx(clips[8]) && sfx.Select(s => s.clip).Distinct().Count() == 8
            && sfx.All(s => s.clip != null), "eight distinct occupied slots; ninth rejected (startup cleanup race)");
        Check(Find<GameObject>().Length == objects && Find<AudioSource>().Length == sources, "burst creates no GameObjects/AudioSources");
        yield return null; yield return null;
        Check(sfx.Count(s => s.isPlaying) == 8, "eight native sources playing after real frames");
        service.StopSfx();
        Check(Idle(sfx), "StopSfx clears clips/playback");
        Require(service.PlaySfx(clips[0]), "StopSfx clears busy flags/cooldown immediately");
        for (float end = Time.realtimeSinceStartup + .08f; Time.realtimeSinceStartup < end;) yield return null;
        var voice = sfx.Single(s => s.clip == clips[0]);
        Require(voice.isPlaying && voice.timeSamples > 0, "ordinary voice advances natively");
        Time.timeScale = 0;
        Check(!service.PlaySfx(clips[8]), "paused ordinary SFX rejected");
        Require(service.PlaySfx(clips[1], playWhilePaused: true), "allowWhilePaused starts in pause-transition frame");
        for (int i = 2; i < 8; i++) Check(service.PlaySfx(clips[i], playWhilePaused: true), "paused allocation " + i);
        Check(voice.clip == clips[0] && !service.PlaySfx(clips[8], playWhilePaused: true), "paused native voice retains its slot during same-frame allocation");
        yield return null; yield return null;
        int pausedSample = voice.timeSamples;
        for (int i = 0; i < 3; i++) yield return null;
        Check(!voice.isPlaying && voice.clip == clips[0] && voice.timeSamples == pausedSample
            && pausedSample > 0 && sfx.Count(s => s.isPlaying) == 7, "paused samples frozen across frames; seven allowed sources play");
        Time.timeScale = 1;
        for (float end = Time.realtimeSinceStartup + .08f; Time.realtimeSinceStartup < end;) yield return null;
        Check(voice.clip == clips[0] && voice.isPlaying && voice.timeSamples > pausedSample, "same native source resumes samples");
        service.StopSfx();
        foreach (bool gamePaused in new[] { false, true })
        {
            Time.timeScale = 1;
            Require(service.PlaySfx(clips[0]), "cleanup setup ordinary voice");
            yield return null;
            Time.timeScale = gamePaused ? 0 : 1;
            yield return null;
            var disabledVoice = sfx[0];
            disabledVoice.enabled = false;
            try
            {
                yield return null; yield return null;
                Check(disabledVoice.clip == null && !Field<bool[]>("busy")[0]
                    && !Field<bool[]>("paused")[0] && !Field<bool[]>("allowPaused")[0]
                    && Field<SoundId[]>("voiceIds")[0] == SoundId.None,
                    "disabled " + (gamePaused ? "paused" : "active") + " voice releases clip and all ownership flags");
            }
            finally { disabledVoice.enabled = true; }
            Require(service.PlaySfx(clips[1], playWhilePaused: true), "cleaned slot immediately reusable");
            Check(sfx[0].clip == clips[1] && sfx[0].isPlaying && !Field<bool[]>("paused")[0],
                "re-enabled slot starts fresh, not stale paused playback");
            sfx[0].Stop();
            yield return null; yield return null;
            Check(sfx[0].clip == null && !Field<bool[]>("busy")[0] && !Field<bool[]>("allowPaused")[0],
                "stopped pause-allowed voice cleaned while gamePaused=" + gamePaused);
            service.StopSfx();
        }
        Time.timeScale = 1;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < 8; i++) Check(service.PlaySfx(clips[i]), "StopSfx same-frame refill " + pass + ":" + i);
            service.StopSfx(); Check(Idle(sfx), "StopSfx resets all slots/clips/cooldowns " + pass);
        }
        objects = Find<GameObject>().Length; sources = Find<AudioSource>().Length;
                Require(service.PlayMusic(clips[0], fadeDuration: 0), "immediate music accepted");
                Check(Find<GameObject>().Length == objects && Find<AudioSource>().Length == sources, "music start creates no GameObjects/AudioSources");
        var first = all.Single(s => s.clip == clips[0]);
        Check(first.isPlaying && Near(first.volume, 1), "immediate music full source volume");
        for (float end = Time.realtimeSinceStartup + .08f; Time.realtimeSinceStartup < end;) yield return null;
        int sample = first.timeSamples;
        Check(sample > 0 && service.PlayMusic(clips[0], fadeDuration: 0) && first.timeSamples >= sample, "same music request does not restart");
        yield return null;
        Check(first.timeSamples >= sample && first.isPlaying, "idempotent music samples retained next frame");
        objects = Find<GameObject>().Length; sources = Find<AudioSource>().Length;
                Require(service.PlayMusic(clips[1], fadeDuration: .15f), "crossfade accepted");
                Check(Find<GameObject>().Length == objects && Find<AudioSource>().Length == sources, "crossfade creates no GameObjects/AudioSources");
        var second = all.Single(s => s.clip == clips[1]);
        bool progress = false;
                Vector2 intermediate = Vector2.zero;
        while (first.clip != null)
        {
            if (first.isPlaying && second.isPlaying && first.volume > 0 && first.volume < 1
                && second.volume > 0 && second.volume < 1)
            { progress = true; intermediate = new Vector2(first.volume, second.volume); }
            yield return null;
        }
        Check(progress && !first.isPlaying && second.isPlaying && Near(second.volume, 1), "0.15s crossfade dual playback at " + intermediate + "; outgoing stopped, incoming volume 1");
        service.StopMusic(.15f);
        while (Near(second.volume, 1)) yield return null;
        Require(second.clip == clips[1] && second.volume > 0, "stop fade has observable intermediate volume");
        sample = second.timeSamples;
        Require(service.PlayMusic(clips[1], fadeDuration: .15f), "same clip reverses StopMusic fade");
        Check(second.timeSamples >= sample, "reversal preserves native playback position");
        while (!Near(second.volume, 1)) yield return null;
        Check(second.clip == clips[1] && second.isPlaying, "reversed fade completes on same source");
        Time.timeScale = 0; service.StopMusic(.15f);
        while (!Idle(new[] { a, b })) yield return null;
        Check(Near(a.volume, 0) && Near(b.volume, 0), "music stop fade completes at timeScale zero");
        Require(service.PlayMusic(clips[0], fadeDuration: .15f), "paused music fade-in accepted");
        while (!Near(a.volume + b.volume, 1)) yield return null;
        Check(a.isPlaying || b.isPlaying, "music fade-in completes at timeScale zero");
        var setters = new Action<float>[] { service.SetMasterVolume, service.SetMusicVolume, service.SetSfxVolume };
        var expected = (float[])savedMixer.Clone();
        for (int i = 0; i < 3; i++) foreach (float linear in new[] { .5f, 0f })
        {
            setters[i](linear); expected[i] = linear == 0 ? -80 : 20 * Mathf.Log10(linear);
            Check(Enumerable.Range(0, 3).All(j => mixer.GetFloat(Parameters[j], out float db) && Mathf.Abs(db - expected[j]) < .02f),
                Parameters[i] + " linear " + linear + " -> " + expected[i] + " dB; other parameters unchanged");
        }
        Require(service.PlaySfx(clips[8], playWhilePaused: true), "active SFX before disable");
        service.enabled = false;
        Check(AudioService.Instance == null && Idle(all), "OnDisable clears singleton and all playback/clips");
        Check(!service.PlayMusic(clips[0]) && !service.PlaySfx(clips[0], playWhilePaused: true), "disabled service rejects playback");
        service.enabled = true; yield return null;
        Check(AudioService.Instance == service && Idle(all) && all.All(s => !s.playOnAwake), "service OnEnable reacquires singleton and stays idle while GameAudioEvents is suspended; not a menu playback expectation");
    }

    T Field<T>(string name) => (T)typeof(AudioService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
    static T[] Find<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    static bool Idle(IEnumerable<AudioSource> sources) => sources.All(s => !s.isPlaying && s.clip == null);
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
    static void Check(bool ok, string text) { if (ok) Passed++; else Failed++; Evidence.Add((ok ? "PASS " : "FAIL ") + text); Debug.Log("AUDIO " + Evidence[Evidence.Count - 1]); }
    static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    static void OnLog(string text, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Failed++; Evidence.Add("FAIL Unity " + type + ": " + text + "\n" + stack); // Observe, never suppress engine errors.
    }
    void SuspendMenuEvents()
    {
        var binder = service.GetComponent<GameAudioEvents>();
        if (binder == null || !binder.isActiveAndEnabled) return;
        Require(service.gameObject.scene.name == "Main Menu"
            && EventField<WorldManager>(binder, "worldManager") == null && EventField<RunStageController>(binder, "run") == null
            && EventField<SoundId>(binder, "currentMusic") == SoundId.MusicMenu,
            "only a running menu binder may be suspended; gameplay and broken menu startup are not synthetic fixtures");
        suspendedEvents = binder;
        binder.enabled = false; // OnDisable unbinds and stops only music; retain the strict idle/SFX precondition.
        Check(!binder.isActiveAndEnabled, "explicitly suspend menu GameAudioEvents for synthetic service checks");
    }

    static T EventField<T>(GameAudioEvents binder, string name) =>
        (T)typeof(GameAudioEvents).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(binder);

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try
        {
            try { if (owned && service != null) { service.enabled = true; service.StopMusic(0); service.StopSfx(); } }
            finally { if (mixer != null) for (int i = 0; i < 3; i++) if (captured[i]) Check(mixer.SetFloat(Parameters[i], savedMixer[i]), "restore " + Parameters[i]); }
            // GetFloat cannot distinguish an existing runtime override; restore its numeric value, never edit assets.
        }
        finally
        {
            try { Time.timeScale = savedScale; foreach (var clip in clips) if (clip != null) Object.Destroy(clip); }
            finally
            {
                // The service and mixer must be restored before OnEnable requests real menu music.
                if (suspendedEvents != null)
                {
                    suspendedEvents.enabled = true;
                    Check(suspendedEvents.isActiveAndEnabled && EventField<SoundId>(suspendedEvents, "currentMusic") == SoundId.MusicMenu,
                        "restore previously enabled menu GameAudioEvents after service fixture, including cancellation");
                }
            }
        }
    }
}
