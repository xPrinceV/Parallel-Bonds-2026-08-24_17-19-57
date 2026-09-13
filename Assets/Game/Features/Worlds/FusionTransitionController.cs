using UnityEngine;

// fusion commits once; this clock only describes its entrance presentation
[DisallowMultipleComponent]
public sealed class FusionTransitionController : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField, Min(0.5f)] private float duration = 3f;
    [SerializeField, Range(2, 6)] private int flipCount = 4;

    public event System.Action Completed;

    public bool IsPlaying { get; private set; }
    public float Progress { get; private set; }
    public float Turns { get; private set; }
    public bool IsFlipping => IsPlaying && Progress < 0.8f;
    public float RevealProgress => Mathf.Clamp01((Progress - 0.8f) / 0.2f);
    public bool ShowFusionPortrait => Turns >= flipCount - 0.5f;
    public float WhiteAmount => ShowFusionPortrait
        ? 1f - Mathf.SmoothStep(0f, 1f, RevealProgress)
        : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flipCount - 1f, flipCount - 0.65f, Turns));
    public World EntryWorld { get; private set; }
    public World OtherWorld { get; private set; }
    public PlayerController EntryPlayer => EntryWorld == null ? null : EntryWorld.Player;
    public World DisplayWorld => Mathf.FloorToInt(Turns + 0.5f) % 2 == 0 ? EntryWorld : OtherWorld;

    private WorldManager subscribedManager;
    private float elapsed;
    private bool observedFusion;

    private void OnEnable()
    {
        Bind();
    }

    // late binding does not replay an entrance for an already fused character
    private void Bind()
    {
        if (subscribedManager == worldManager)
            return;
        Unbind();
        subscribedManager = worldManager;
        if (subscribedManager == null)
            return;
        if (subscribedManager.FusionTransition != null && subscribedManager.FusionTransition != this)
        {
            Debug.LogError("Only one fusion transition clock may observe a world manager.", this);
            subscribedManager = null;
            enabled = false;
            return;
        }
        observedFusion = subscribedManager.IsFused;
        subscribedManager.FusionTransition = this;
        subscribedManager.FusionStateChanged += OnFusionChanged;
    }

    private void OnFusionChanged()
    {
        bool fused = subscribedManager != null && subscribedManager.IsFused;
        if (fused && !observedFusion)
            Begin();
        else if (!fused)
            Cancel();
        observedFusion = fused;
    }

    private void Begin()
    {
        Cancel();
        if (!isActiveAndEnabled || !subscribedManager.isActiveAndEnabled)
            return;
        if (duration < 0.5f || float.IsNaN(duration) || float.IsInfinity(duration) || flipCount < 2 || flipCount > 6)
        {
            Debug.LogWarning("Use a finite fusion duration >= 0.5 seconds and 2-6 turns.", this);
            return;
        }
        EntryWorld = subscribedManager.CurrentWorld;
        foreach (World world in FindObjectsByType<World>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (world.Manager == subscribedManager && world != EntryWorld)
                OtherWorld = world;
        if (EntryWorld == null || OtherWorld == null || EntryPlayer == null)
        {
            Cancel();
            return;
        }
        IsPlaying = true;
    }

    private void Update()
    {
        Bind();
        if (!IsPlaying)
            return;
        if (subscribedManager == null || !subscribedManager.isActiveAndEnabled || !subscribedManager.IsFused
            || EntryPlayer == null || !EntryPlayer.isActiveAndEnabled || OtherWorld == null || !OtherWorld.IsActive)
        {
            Cancel();
            return;
        }
        // no independent time scale, input lock, repeated world switch or gameplay timer
        if (Time.deltaTime <= 0f || (UIController.instance != null && UIController.instance.levelUpPanel != null
            && UIController.instance.levelUpPanel.activeInHierarchy))
            return;
        elapsed += Time.deltaTime;
        Progress = Mathf.Clamp01(elapsed / duration);
        float nextTurns = flipCount * (1f - Mathf.Pow(1f - Mathf.Clamp01(Progress / 0.8f), 1.25f));
        float nextFace = Mathf.Floor(Turns + 0.5f) + 0.5f;
        float whiteFrame = flipCount - 0.65f;
        float milestone = Turns < whiteFrame ? Mathf.Min(nextFace, whiteFrame) : nextFace;
        if (milestone < flipCount && nextTurns > milestone)
        {
            // even a long frame must show the side-on swap and the white original silhouette
            nextTurns = milestone;
            Progress = 0.8f * (1f - Mathf.Pow(1f - nextTurns / flipCount, 0.8f));
            elapsed = Progress * duration;
        }
        Turns = nextTurns;
        if (Progress >= 1f)
        {
            IsPlaying = false;
            Completed?.Invoke();
        }
    }

    public void Cancel()
    {
        IsPlaying = false;
        Progress = Turns = elapsed = 0f;
        EntryWorld = OtherWorld = null;
    }

    private void Unbind()
    {
        if (subscribedManager != null)
        {
            subscribedManager.FusionStateChanged -= OnFusionChanged;
            if (subscribedManager.FusionTransition == this)
                subscribedManager.FusionTransition = null;
        }
        subscribedManager = null;
        Cancel();
    }

    private void OnDisable()
    {
        Unbind();
    }
}
