using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RunStagePanel : MonoBehaviour
{
    [SerializeField] private RunStageController runController;
    [SerializeField] private UIController uiController;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button[] stageButtons;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button restartButton;

    private TMP_Text finaleButtonLabel;
    private Canvas displayCanvas;
    private DeveloperDebugGui developerGui;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool displayValid;
    private bool displayedController;
    private StageDisplay displayedStage;
    private StateDisplay displayedState;
    private int displayedWave, displayedCountdown, displayedBosses;
    private bool displayedFinale, displayedAutomatic;
    private int displayedMaterial, displayedEcho;

    private enum StageDisplay { Unavailable, Complete, Boss, Fusion, NotStarted, Survival, Wave }
    private enum StateDisplay { Defeated, Complete, Stopped, Paused, Running }
#endif

    private bool CanStartFinale => runController != null && runController.IsRunning
        && !runController.IsCompleted && !runController.IsDefeated && !runController.IsFinaleStarted
        && Time.timeScale > 0f
        && (PlayerHealth.instance == null || !PlayerHealth.instance.IsDead)
        && (uiController == null || uiController.levelUpPanel == null
            || !uiController.levelUpPanel.activeInHierarchy);

    private void Awake()
    {
        displayCanvas = GetComponentInParent<Canvas>();
        developerGui = GetComponentInParent<DeveloperDebugGui>();
        // keep the authored controls unchanged between Edit Mode and Play Mode
        if (stageButtons.Length > 3 && stageButtons[3] != null)
        {
            finaleButtonLabel = stageButtons[3].GetComponentInChildren<TMP_Text>(true);
            stageButtons[3].onClick.AddListener(StartFinale);
        }
        restartButton.onClick.AddListener(Restart);
    }

    private void Update()
    {
        RefreshDisplay();
    }

    // Canvas.enabled does not stop Update on its children. SetOpen also calls this
    // synchronously so opening never exposes the previous hidden snapshot.
    public void RefreshDisplay(bool force = false)
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        return;
#else
        if (!isActiveAndEnabled || displayCanvas == null || !displayCanvas.isActiveAndEnabled
            || (developerGui != null && (!developerGui.IsAvailable || !developerGui.IsOpen)))
        {
            displayValid = false;
            return;
        }

        bool hasController = runController != null;
        bool canStartFinale = CanStartFinale;
        if (stageButtons.Length > 3 && stageButtons[3] != null
            && stageButtons[3].interactable != canStartFinale)
            stageButtons[3].interactable = canStartFinale;
        // Guards are evaluated every visible frame, independently of text caching.
        // Restart must remain reachable while an upgrade or death has stopped scaled time.
        if (restartButton.interactable != hasController)
            restartButton.interactable = hasController;

        if (force || !displayValid || displayedController != hasController)
        {
            if (finaleButtonLabel != null)
                finaleButtonLabel.text = "Finale now (debug)";
            displayedController = hasController;
        }

        StageDisplay stage = !hasController ? StageDisplay.Unavailable
            : runController.IsCompleted ? StageDisplay.Complete
            : runController.IsBossPhase ? StageDisplay.Boss
            : runController.IsFinaleStarted ? StageDisplay.Fusion
            : runController.CurrentStageIndex < 0 ? StageDisplay.NotStarted
            : !runController.UsesSharedWaves ? StageDisplay.Survival : StageDisplay.Wave;
        bool paused = Time.timeScale <= 0f || (uiController != null && uiController.levelUpPanel != null
            && uiController.levelUpPanel.activeInHierarchy);
        StateDisplay state = !hasController ? StateDisplay.Stopped
            : runController.IsDefeated ? StateDisplay.Defeated : runController.IsCompleted ? StateDisplay.Complete
            : !runController.IsRunning ? StateDisplay.Stopped : paused ? StateDisplay.Paused : StateDisplay.Running;
        int wave = stage == StageDisplay.Wave ? runController.CurrentStageIndex + 1 : 0;
        bool finale = hasController && runController.IsFinaleStarted;
        int countdown = hasController && !finale ? Mathf.CeilToInt(runController.RemainingUntilFinale) : 0;
        int bosses = hasController ? runController.RemainingBosses : 0;
        int material = hasController ? runController.CompletedMaterialResidences : 0;
        int echo = hasController ? runController.CompletedEchoResidences : 0;
        bool automatic = hasController && runController.AutomaticFinaleEnabled;
        if (force || !displayValid || displayedStage != stage || displayedState != state
            || displayedWave != wave || displayedFinale != finale
            || displayedCountdown != countdown || displayedBosses != bosses
            || displayedMaterial != material || displayedEcho != echo || displayedAutomatic != automatic)
        {
            if (!hasController)
                statusText.text = "Run controller unavailable";
            else
            {
                string stageText = stage == StageDisplay.Complete ? "Complete"
                    : stage == StageDisplay.Boss ? "Rift Lord" : stage == StageDisplay.Fusion ? "Fusion"
                    : stage == StageDisplay.NotStarted ? "Not started" : stage == StageDisplay.Survival ? "Survival"
                    : "Wave " + wave;
                string stateText = state == StateDisplay.Defeated ? "Defeated" : state == StateDisplay.Complete ? "Complete"
                    : state == StateDisplay.Stopped ? "Stopped" : state == StateDisplay.Paused ? "Paused" : "Running";
                string countdownText = finale ? "Final fusion locked"
                    : automatic ? $"Fusion: ~{countdown}s active" : "Auto fusion: Off";
                int required = StateSwitchController.RequiredResidencesPerWorld;
                statusText.text = $"{stageText} | {stateText} | M {material}/{required}, E {echo}/{required}"
                    + $"\n{countdownText} | Bosses: {bosses}";
            }
            displayedStage = stage;
            displayedState = state;
            displayedWave = wave;
            displayedFinale = finale;
            displayedCountdown = countdown;
            displayedBosses = bosses;
            displayedMaterial = material;
            displayedEcho = echo;
            displayedAutomatic = automatic;
        }
        displayValid = true;
#endif
    }

    private void StartFinale()
    {
        if (CanStartFinale)
            runController.TryStartFinale();
    }

    private void Restart()
    {
        if (runController != null)
            runController.RestartRun();
    }

    private void OnDestroy()
    {
        if (stageButtons != null && stageButtons.Length > 3 && stageButtons[3] != null)
            stageButtons[3].onClick.RemoveListener(StartFinale);
        if (restartButton != null)
            restartButton.onClick.RemoveListener(Restart);
    }
}
