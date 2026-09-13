using System;
using UnityEngine;

// the clock owns transition progress; presentation never decides when a world commits
[DisallowMultipleComponent]
public class StateSwitchController : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField, Min(0.1f)] private float timer = 15f;
    [SerializeField] private bool automaticSwitchingEnabled = true;
    [SerializeField, Min(0f)] private float warningDuration = 5f;
    [SerializeField, Min(0.1f)] private float flipDuration = 0.8f;

    public float WarningProgress { get; private set; }
    public float FlipProgress { get; private set; }
    public bool IsFlipping { get; private set; }
    public float RemainingTime => timerCounter;
    public float SwitchInterval => timer;

    // only automatic scheduling is disabled; accepted requests and flips finish normally
    public bool AutomaticSwitchingEnabled
    {
        get => automaticSwitchingEnabled;
        set
        {
            automaticSwitchingEnabled = value;
            if (!value && !requested && !IsFlipping)
                CancelTransition();
        }
    }
    // observers see the committed world at the narrowest rendered frame
    public event Action TransitionMidpoint;

    private float timerCounter;
    private float flipElapsed;
    private bool initialized;
    private bool midpointCommitted;
    private bool requested;
    private WorldId targetWorldId;

    // wait until manager initialization has completed before starting the timer
    private void Start()
    {
        if (worldManager == null || !worldManager.IsInitialized
            || (worldManager.SwitchFlow != null && worldManager.SwitchFlow != this)
            || !worldManager.IsSharedObject(transform)
            || !IsPositive(timer) || !IsPositive(flipDuration)
            || float.IsNaN(warningDuration) || float.IsInfinity(warningDuration)
            || warningDuration < flipDuration * 0.5f || warningDuration > timer
            || flipDuration >= timer)
        {
            Debug.LogError("Use a positive interval and flip duration, with half-flip <= warning <= interval and flip < interval.", this);
            enabled = false;
            return;
        }
        worldManager.SwitchFlow = this;
        initialized = true;
        timerCounter = timer;
    }

    private static bool IsPositive(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public bool RequestNextWorldSwitch()
    {
        return worldManager != null && RequestSwitch(NextWorldId);
    }

    private WorldId NextWorldId => worldManager.CurrentWorldId == WorldId.Material ? WorldId.Echo : WorldId.Material;

    // true means accepted, not committed; manual requests use the same warning and midpoint
    public bool RequestSwitch(WorldId target)
    {
        if (!initialized || !isActiveAndEnabled || requested || IsFlipping || !CanAdvance()
            || !Enum.IsDefined(typeof(WorldId), target) || target == worldManager.CurrentWorldId)
            return false;
        requested = true;
        targetWorldId = target;
        timerCounter = Mathf.Min(timerCounter, warningDuration);
        return true;
    }

    private bool CanAdvance()
    {
        if (worldManager == null || !worldManager.IsInitialized || !worldManager.isActiveAndEnabled
            || worldManager.IsSwitching || worldManager.IsFused || Time.deltaTime <= 0f)
            return false;
        World world = worldManager.CurrentWorld;
        PlayerHealth health = world == null || world.Player == null ? null : world.Player.GetComponent<PlayerHealth>();
        return health != null && health.isActiveAndEnabled && !health.IsDead
            && (UIController.instance == null || UIController.instance.levelUpPanel == null
                || !UIController.instance.levelUpPanel.activeInHierarchy);
    }

    private void Update()
    {
        if (!initialized)
            return;
        World world = worldManager == null ? null : worldManager.CurrentWorld;
        if (worldManager == null || !worldManager.isActiveAndEnabled || !worldManager.IsInitialized
            || world == null || world.Player == null || world.Player.GetComponent<PlayerHealth>().IsDead)
        {
            CancelTransition();
            return;
        }
        // fusion preserves the remaining interval, but does not retain a world warning on screen
        if (worldManager.IsFused)
        {
            WarningProgress = 0f;
            return;
        }
        // clear an automatic warning without interrupting a requested or already started flip
        if (!automaticSwitchingEnabled && !requested && !IsFlipping)
        {
            CancelTransition();
            return;
        }
        if (!CanAdvance())
            return;

        timerCounter -= Time.deltaTime;
        float half = flipDuration * 0.5f;
        if (!IsFlipping)
        {
            WarningProgress = Mathf.Clamp01((warningDuration - timerCounter) / warningDuration);
            if (timerCounter > half)
                return;
            if (!requested)
                targetWorldId = NextWorldId;
            IsFlipping = true;
            midpointCommitted = false;
            flipElapsed = Mathf.Max(0f, half - timerCounter);
        }
        else
        {
            flipElapsed += Time.deltaTime;
        }

        if (!midpointCommitted)
        {
            WarningProgress = Mathf.Clamp01((warningDuration - timerCounter) / warningDuration);
            if (flipElapsed >= half)
            {
                // clamp to the edge-on frame even after a long frame; never skip the midpoint
                flipElapsed = half;
                FlipProgress = 0.5f;
                midpointCommitted = true;
                WarningProgress = 1f;
                timerCounter += timer;
                if (!worldManager.CommitWorldSwitch(targetWorldId))
                {
                    Debug.LogWarning("World switch midpoint was rejected; restoring the current view.", this);
                    CancelTransition();
                    return;
                }
                TransitionMidpoint?.Invoke();
            }
        }
        FlipProgress = Mathf.Clamp01(flipElapsed / flipDuration);
        if (flipElapsed >= flipDuration)
            ResetVisualProgress();
    }

    // cancellation never rolls back a committed world or replays its midpoint
    public void CancelTransition()
    {
        ResetVisualProgress();
        timerCounter = timer;
    }

    private void ResetVisualProgress()
    {
        WarningProgress = FlipProgress = flipElapsed = 0f;
        IsFlipping = midpointCommitted = requested = false;
    }

    private void OnDisable()
    {
        CancelTransition();
    }

    private void OnDestroy()
    {
        if (worldManager != null && worldManager.SwitchFlow == this)
            worldManager.SwitchFlow = null;
    }
}
