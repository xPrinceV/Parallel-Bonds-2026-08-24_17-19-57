using System;
using System.Collections.Generic;
using UnityEngine;

public enum SoundChannel
{
    Music = 0,
    Sfx = 1
}

[Serializable]
public sealed class SoundEntry
{
    [SerializeField] private SoundId id;
    [SerializeField] private SoundChannel channel = SoundChannel.Sfx;
    [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField, Min(0f)] private float cooldown;
    [SerializeField, Min(1)] private int maxVoices = 1;
    [SerializeField] private bool loop;
    [SerializeField] private bool playWhilePaused;

    public SoundId Id => id;
    public SoundChannel Channel => channel;
    public float Volume => volume;
    public float Cooldown => cooldown;
    public int MaxVoices => maxVoices;
    public bool Loop => loop;
    public bool PlayWhilePaused => playWhilePaused;

    internal bool IsValid => id != SoundId.None &&
        (channel == SoundChannel.Music || channel == SoundChannel.Sfx) &&
        !float.IsNaN(volume) && !float.IsInfinity(volume) && volume >= 0f && volume <= 1f &&
        !float.IsNaN(cooldown) && !float.IsInfinity(cooldown) && cooldown >= 0f && maxVoices > 0;

    internal bool TryGetClip(int start, out AudioClip clip, out int next)
    {
        clip = null;
        next = 0;
        if (clips == null || clips.Length == 0) return false;
        int index = start >= 0 && start < clips.Length ? start : 0;
        for (int visited = 0; visited < clips.Length; visited++)
        {
            clip = clips[index];
            index = index + 1 == clips.Length ? 0 : index + 1;
            if (clip == null || clip.loadState == AudioDataLoadState.Failed) continue;
            next = index;
            return true;
        }
        clip = null;
        return false;
    }
}

[CreateAssetMenu(fileName = "AudioCatalog", menuName = "Audio/Audio Catalog")]
public sealed class AudioCatalog : ScriptableObject
{
    [SerializeField] private List<SoundEntry> entries = new List<SoundEntry>();
    [NonSerialized] private Dictionary<SoundId, SoundEntry> lookup;

    private void OnEnable() => RebuildLookup();
    private void OnValidate() => lookup = null;

    private void RebuildLookup()
    {
        lookup = new Dictionary<SoundId, SoundEntry>();
        if (entries == null) return;
        foreach (SoundEntry entry in entries)
        {
            if (entry == null || entry.Id == SoundId.None || !Enum.IsDefined(typeof(SoundId), entry.Id)) continue;
            // Keep an ambiguity tombstone, even if a third duplicate or invalid entry follows.
            if (lookup.ContainsKey(entry.Id)) lookup[entry.Id] = null;
            else lookup.Add(entry.Id, entry);
        }
    }

    public bool TryGet(SoundId id, out SoundEntry entry)
    {
        if (lookup == null) RebuildLookup();
        if (id != SoundId.None && lookup.TryGetValue(id, out entry) && entry != null &&
            entry.IsValid && entry.TryGetClip(0, out _, out _)) return true;
        entry = null;
        return false;
    }
}
