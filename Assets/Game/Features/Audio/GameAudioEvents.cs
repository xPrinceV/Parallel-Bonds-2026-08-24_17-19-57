using UnityEngine;

// Translate committed game state into music requests; animation faces are not world changes.
[DefaultExecutionOrder(10)]
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioService))]
public sealed class GameAudioEvents : MonoBehaviour
{
    private AudioService audioService;
    private WorldManager worldManager;
    private RunStageController run;
    private SoundId currentMusic;
    private bool started;

    private void Start()
    {
        started = true;
        Bind();
    }

    private void OnEnable()
    {
        if (started)
            Bind();
    }

    private void Bind()
    {
        Unbind();
        audioService = GetComponent<AudioService>();
        foreach (WorldManager manager in FindObjectsByType<WorldManager>(FindObjectsSortMode.None))
        {
            if (manager.gameObject.scene != gameObject.scene || !manager.isActiveAndEnabled || !manager.IsInitialized)
                continue;
            worldManager = manager;
            break;
        }
        // Only the named menu gets music without a world; unknown scenes remain untouched.
        if (worldManager == null)
        {
            if (gameObject.scene.name == "Main Menu")
                RefreshMusic();
            return;
        }
        run = worldManager.RunController;
        worldManager.WorldChanged += RefreshMusic;
        if (run != null)
            run.StateChanged += RefreshMusic;
        RefreshMusic();
    }

    private void RefreshMusic()
    {
        if (!isActiveAndEnabled || audioService == null || !audioService.isActiveAndEnabled)
            return;
        SoundId next = SoundId.None;
        if (run != null && run.IsCompleted)
            next = SoundId.Victory;
        else if (run != null && run.IsDefeated)
            next = SoundId.None;
        else if (run != null && run.IsBossPhase)
            next = SoundId.MusicBoss;
        else if (worldManager != null && worldManager.IsInitialized && worldManager.isActiveAndEnabled)
            next = worldManager.CurrentWorldId == WorldId.Material ? SoundId.MusicMaterial : SoundId.MusicEcho;
        else if (worldManager == null && gameObject.scene.name == "Main Menu")
            next = SoundId.MusicMenu;

        // Repeated state notifications must not restart a track or replay the victory cue.
        if (next == currentMusic)
            return;
        if (next == SoundId.None)
        {
            audioService.StopMusic();
            currentMusic = SoundId.None;
        }
        else if (audioService.Play(next))
            currentMusic = next;
    }

    private void Unbind()
    {
        if (worldManager != null)
            worldManager.WorldChanged -= RefreshMusic;
        if (run != null)
            run.StateChanged -= RefreshMusic;
        worldManager = null;
        run = null;
        currentMusic = SoundId.None;
    }

    private void OnDisable()
    {
        Unbind();
        if (audioService != null)
            audioService.StopMusic(0f);
    }
}
