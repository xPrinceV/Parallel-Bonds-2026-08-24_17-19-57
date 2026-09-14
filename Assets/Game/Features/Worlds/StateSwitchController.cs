using System;
using UnityEngine;

// the clock owns transition progress; presentation never decides when a world commits
// Publish the flip state before weapon Updates can launch this frame's shots.
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class StateSwitchController : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    // Scene/default contract; the serialized interval remains configurable for assets and fixtures.
    public const float ResidenceDuration = 90f;
    public const int RequiredResidencesPerWorld = 3;
    [SerializeField, Min(0.1f)] private float timer = ResidenceDuration;
    [SerializeField] private bool automaticSwitchingEnabled = true;
    [SerializeField, Min(0f)] private float warningDuration = 5f;
    [SerializeField, Min(0.1f)] private float flipDuration = 0.8f;

    public float WarningProgress { get; private set; }
    public float FlipProgress { get; private set; }
    public bool IsFlipping { get; private set; }
    public float RemainingTime => timerCounter;
    public float SwitchInterval => timer;
    public int CompletedMaterialResidences { get; private set; }
    public int CompletedEchoResidences { get; private set; }
    public int RemainingResidences => RequiredResidencesPerWorld * 2
        - CompletedMaterialResidences - CompletedEchoResidences;
    public bool IsFinalResidence => worldManager != null && !requested && RemainingResidences == 1
        && CompletedResidences(worldManager.CurrentWorldId) < RequiredResidencesPerWorld;

    // Active-time estimate assuming automatic alternation resumes; not a wall-clock deadline.
    public float RemainingUntilFinale
    {
        get
        {
            if (worldManager == null || RemainingResidences == 0 || worldManager.IsFinalFusion)
                return 0f;
            bool manualPending = requested && !midpointCommitted;
            WorldId next = manualPending ? targetWorldId : worldManager.CurrentWorldId;
            int here = RequiredResidencesPerWorld - CompletedResidences(next);
            int there = RequiredResidencesPerWorld - CompletedResidences(
                next == WorldId.Material ? WorldId.Echo : WorldId.Material);
            int intervals = Mathf.Max(here * 2 - 1, there * 2);
            return Mathf.Max(0f, timerCounter) + (intervals - (manualPending ? 0 : 1)) * timer;
        }
    }

    private int CompletedResidences(WorldId world)
    {
        return world == WorldId.Material ? CompletedMaterialResidences : CompletedEchoResidences;
    }

    private void CompleteResidence(WorldId world)
    {
        if (world == WorldId.Material)
            CompletedMaterialResidences = Mathf.Min(RequiredResidencesPerWorld, CompletedMaterialResidences + 1);
        else
            CompletedEchoResidences = Mathf.Min(RequiredResidencesPerWorld, CompletedEchoResidences + 1);
    }

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
            || worldManager.IsSwitching || worldManager.IsFused || Time.timeScale <= 0f || Time.deltaTime <= 0f)
            return false;
        RunStageController run = worldManager.RunController;
        if (run != null && (!run.isActiveAndEnabled || !run.IsRunning || run.IsFinaleStarted
            || run.IsCompleted || run.IsDefeated))
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
            // The last residence keeps its warning, never starts an ordinary half-flip.
            if (IsFinalResidence && worldManager.RunController != null)
            {
                if (timerCounter > 0f)
                    return;
                WorldId completedWorld = worldManager.CurrentWorldId;
                if (worldManager.RunController.TryStartFinale())
                    CompleteResidence(completedWorld);
                else
                {
                    // A rejected fusion may cancel the flow; retain the completed boundary for retry.
                    timerCounter = 0f;
                    WarningProgress = 1f;
                }
                return;
            }
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
                WorldId completedWorld = worldManager.CurrentWorldId;
                bool automaticCommit = !requested;
                // Manual entry starts a full residence; only automatic commits retain clock debt.
                timerCounter = automaticCommit ? timerCounter + timer : timer;
                if (!worldManager.CommitWorldSwitch(targetWorldId))
                {
                    Debug.LogWarning("World switch midpoint was rejected; restoring the current view.", this);
                    CancelTransition();
                    return;
                }
                if (automaticCommit)
                    CompleteResidence(completedWorld);
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
