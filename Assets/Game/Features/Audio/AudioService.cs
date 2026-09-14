using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public sealed class AudioService : MonoBehaviour
{
    [SerializeField] private AudioSource musicA, musicB;
    [SerializeField] private AudioSource[] sfxSources;
    [SerializeField, Min(0)] private float defaultFadeDuration = .5f;
    [SerializeField, Min(0)] private float sfxCooldown = .04f;
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioCatalog catalog;

    public static AudioService Instance { get; private set; }

    private const int VoiceCount = 8;
    private readonly bool[] busy = new bool[VoiceCount];
    private readonly bool[] paused = new bool[VoiceCount];
    private readonly bool[] allowPaused = new bool[VoiceCount];
    private readonly Dictionary<AudioClip, double> cooldowns = new Dictionary<AudioClip, double>();
    private readonly List<AudioClip> expiredClips = new List<AudioClip>();
    private readonly SoundId[] voiceIds = new SoundId[VoiceCount];
    private readonly Dictionary<SoundId, double> indexedCooldowns = new Dictionary<SoundId, double>();
    private readonly Dictionary<SoundId, int> nextClips = new Dictionary<SoundId, int>();
    private SoundId musicAId, musicBId;
    private float musicTargetGain = 1f;
    private AudioSource musicTarget;
    private bool initialized, started, errorLogged, fading;
    private float fadeElapsed, fadeDuration, fadeStartA, fadeStartB;
    private bool Ready => initialized && isActiveAndEnabled && Instance == this;

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            DisableWithError("Another AudioService is already enabled.");
            return;
        }
        if (!ValidConfiguration())
        {
            DisableWithError("Assign two music and exactly eight SFX sources, all non-null and distinct; timings must be finite and non-negative.");
            return;
        }
        Instance = this;
        ConfigureSource(musicA);
        ConfigureSource(musicB);
        musicA.volume = musicB.volume = 0f;
        foreach (AudioSource source in sfxSources) ConfigureSource(source);
        initialized = true;
    }

    private void Start() => started = true;

    private bool ValidConfiguration()
    {
        if (musicA == null || musicB == null || musicA == musicB || sfxSources == null ||
            sfxSources.Length != VoiceCount || !Finite(defaultFadeDuration) || defaultFadeDuration < 0f ||
            !Finite(sfxCooldown) || sfxCooldown < 0f) return false;
        for (int i = 0; i < VoiceCount; i++)
        {
            AudioSource source = sfxSources[i];
            if (source == null || source == musicA || source == musicB) return false;
            for (int j = 0; j < i; j++)
                if (source == sfxSources[j]) return false;
        }
        return true;
    }

    private void DisableWithError(string message)
    {
        if (!errorLogged) Debug.LogError("AudioService: " + message, this);
        errorLogged = true;
        enabled = false;
    }

    private static void ConfigureSource(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.loop = false;
        source.pitch = 1f;
        // Time-scale pause is owned here, not by AudioListener.pause.
        source.ignoreListenerPause = true;
    }

    private void OnDisable()
    {
        if (initialized && Instance == this)
        {
            ResetMusic();
            StopSfx();
            nextClips.Clear();
        }
        initialized = false;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        UpdateFade();
        UpdateVoices();
        ExpireCooldowns();
    }

    public bool Play(SoundId id)
    {
        if (!Ready || catalog == null || !catalog.TryGet(id, out SoundEntry entry) ||
            (Time.timeScale == 0f && !entry.PlayWhilePaused)) return false;

        AudioSource existingMusic = null;
        if (entry.Channel == SoundChannel.Music)
        {
            if (musicAId == id && musicA.isPlaying) existingMusic = musicA;
            else if (musicBId == id && musicB.isPlaying) existingMusic = musicB;
            // Dedup before cooldown/selection: repeating the target is not a new playback.
            if (existingMusic != null && existingMusic == musicTarget &&
                existingMusic.loop == entry.Loop && musicTargetGain == entry.Volume) return true;
        }

        SoundId budgetId = GetBudgetId(id);
        SoundEntry budget = entry;
        if (budgetId != id && catalog.TryGet(budgetId, out SoundEntry sharedBudget)) budget = sharedBudget;
        double now = Time.unscaledTimeAsDouble;
        if (indexedCooldowns.TryGetValue(budgetId, out double until) && now < until) return false;
        nextClips.TryGetValue(id, out int start);
        AudioClip clip;
        int next = start;
        if (existingMusic != null) clip = existingMusic.clip;
        else if (!entry.TryGetClip(start, out clip, out next)) return false;

        bool accepted = entry.Channel == SoundChannel.Music
            ? TryPlayMusic(clip, entry.Loop, -1f, entry.Volume, id)
            : TryPlaySfx(clip, entry.Volume, 1f, entry.PlayWhilePaused, entry.Loop, id, budget.MaxVoices);
        if (!accepted) return false;
        if (existingMusic == null) nextClips[id] = next;
        if (budget.Cooldown > 0f) indexedCooldowns[budgetId] = now + budget.Cooldown;
        return true;
    }

    // Dagger selects its own feedback, but cannot evade the shared impact budget.
    private static SoundId GetBudgetId(SoundId id) => id == SoundId.DaggerImpact ? SoundId.ProjectileHit : id;

    public bool PlayMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f)
        => TryPlayMusic(clip, loop, fadeDuration, 1f, SoundId.None);

    private bool TryPlayMusic(AudioClip clip, bool loop, float fadeDuration, float volume, SoundId id)
    {
        if (!Ready || clip == null || !Finite(fadeDuration)) return false;
        if (musicTarget != null && musicTarget.clip == clip && musicTarget.loop == loop &&
            musicTarget.isPlaying && (id == SoundId.None || (musicTargetGain == volume &&
            (musicTarget == musicA ? musicAId : musicBId) == id))) return true;

        AudioSource next;
        if (musicA.clip == clip && musicA.isPlaying) next = musicA;
        else if (musicB.clip == clip && musicB.isPlaying) next = musicB;
        else
        {
            // A third track must replace one voice; retain the louder outgoing track.
            next = !musicA.isPlaying ? musicA : !musicB.isPlaying ? musicB :
                musicA.volume <= musicB.volume ? musicA : musicB;
            if (!next.isActiveAndEnabled) return false;
            next.Stop();
            next.clip = clip;
            next.volume = 0f;
        }
        next.loop = loop;
        if (!next.isPlaying) next.Play();
        if (next == musicA) musicAId = id;
        else musicBId = id;
        BeginFade(next, fadeDuration, volume);
        return true;
    }

    public void StopMusic(float fadeDuration = -1f)
    {
        if (!Ready || !Finite(fadeDuration)) return;
        BeginFade(null, fadeDuration);
    }

    private void BeginFade(AudioSource target, float duration, float volume = 1f)
    {
        musicTarget = target;
        musicTargetGain = volume;
        fadeStartA = musicA.volume;
        fadeStartB = musicB.volume;
        fadeElapsed = 0f;
        fadeDuration = duration < 0f ? defaultFadeDuration : duration;
        fading = true;
        if (fadeDuration == 0f) UpdateFade();
    }

    private void UpdateFade()
    {
        if (!fading) return;
        fadeElapsed += Time.unscaledDeltaTime;
        float t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / fadeDuration);
        musicA.volume = Mathf.Lerp(fadeStartA, musicTarget == musicA ? musicTargetGain : 0f, t);
        musicB.volume = Mathf.Lerp(fadeStartB, musicTarget == musicB ? musicTargetGain : 0f, t);
        if (t < 1f) return;
        fading = false;
        if (musicTarget != musicA) { StopSource(musicA); musicAId = SoundId.None; }
        if (musicTarget != musicB) { StopSource(musicB); musicBId = SoundId.None; }
    }

    private void ResetMusic()
    {
        fading = false;
        musicTarget = null;
        musicAId = musicBId = SoundId.None;
        musicTargetGain = 1f;
        fadeElapsed = fadeDuration = fadeStartA = fadeStartB = 0f;
        StopSource(musicA);
        StopSource(musicB);
        if (musicA != null) musicA.volume = 0f;
        if (musicB != null) musicB.volume = 0f;
    }

    public bool PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f, bool playWhilePaused = false)
    {
        if (!Ready || clip == null || !Finite(volume) || !Finite(pitch) || pitch <= 0f ||
            (Time.timeScale == 0f && !playWhilePaused)) return false;
        double now = Time.unscaledTimeAsDouble;
        ExpireCooldowns();
        if (cooldowns.TryGetValue(clip, out double until) && now < until) return false;
        if (!TryPlaySfx(clip, volume, pitch, playWhilePaused, false, SoundId.None, VoiceCount)) return false;
        if (sfxCooldown > 0f) cooldowns[clip] = now + sfxCooldown;
        return true;
    }

    private bool TryPlaySfx(AudioClip clip, float volume, float pitch, bool playWhilePaused,
        bool loop, SoundId id, int maxVoices)
    {
        UpdateVoices();
        if (id != SoundId.None)
        {
            SoundId budgetId = GetBudgetId(id);
            int count = 0;
            for (int i = 0; i < VoiceCount; i++)
                if (busy[i] && GetBudgetId(voiceIds[i]) == budgetId) count++;
            if (count >= maxVoices) return false;
        }
        int slot = -1;
        for (int i = 0; i < VoiceCount; i++)
        {
            if (busy[i] || !sfxSources[i].isActiveAndEnabled) continue;
            slot = i;
            break;
        }
        // Only indexed death may steal a voice; raw and ordinary requests still fail closed.
        if (slot < 0 && id == SoundId.PlayerDeath)
        {
            for (int i = 0; i < VoiceCount; i++)
            {
                if (!busy[i] || voiceIds[i] == SoundId.PlayerDeath || !sfxSources[i].isActiveAndEnabled) continue;
                slot = i;
                break;
            }
        }
        if (slot < 0) return false;
        AudioSource source = sfxSources[slot];
        StopSource(source); // Also discard a preempted voice's native paused playback.
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Min(pitch, 3f);
        source.loop = loop;
        voiceIds[slot] = id;
        allowPaused[slot] = playWhilePaused;
        paused[slot] = false;
        busy[slot] = true;
        source.Play();
        return true;
    }

    private void UpdateVoices()
    {
        bool gamePaused = Time.timeScale == 0f;
        for (int i = 0; i < VoiceCount; i++)
        {
            if (!busy[i]) continue;
            AudioSource source = sfxSources[i];
            // Pause makes isPlaying false, but the voice remains reserved.
            if (!source.isActiveAndEnabled || (!paused[i] && !source.isPlaying))
            {
                StopSource(source);
                busy[i] = paused[i] = allowPaused[i] = false;
                voiceIds[i] = SoundId.None;
                continue;
            }
            bool shouldPause = gamePaused && !allowPaused[i];
            if (shouldPause == paused[i]) continue;
            if (shouldPause) source.Pause();
            else source.UnPause();
            paused[i] = shouldPause;
        }
    }

    private void ExpireCooldowns()
    {
        double now = Time.unscaledTimeAsDouble;
        expiredClips.Clear();
        foreach (KeyValuePair<AudioClip, double> entry in cooldowns)
            if (entry.Key == null || now >= entry.Value) expiredClips.Add(entry.Key);
        foreach (AudioClip clip in expiredClips) cooldowns.Remove(clip);
        expiredClips.Clear();
    }

    public void StopSfx()
    {
        if (!initialized || Instance != this) return;
        for (int i = 0; i < VoiceCount; i++)
        {
            StopSource(sfxSources[i]);
            busy[i] = paused[i] = allowPaused[i] = false;
            voiceIds[i] = SoundId.None;
        }
        indexedCooldowns.Clear();
        cooldowns.Clear();
        expiredClips.Clear();
    }

    private static void StopSource(AudioSource source)
    {
        if (source == null) return;
        source.Stop();
        source.clip = null;
    }

    public void SetMasterVolume(float volume) => SetVolume("MasterVolume", volume);
    public void SetMusicVolume(float volume) => SetVolume("MusicVolume", volume);
    public void SetSfxVolume(float volume) => SetVolume("SfxVolume", volume);

    private void SetVolume(string parameter, float volume)
    {
        // Preserve mixer asset defaults and avoid Unity's pre-Start SetFloat restriction.
        if (!Ready || !started || mixer == null || !Finite(volume)) return;
        volume = Mathf.Clamp01(volume);
        mixer.SetFloat(parameter, volume == 0f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(volume)));
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
