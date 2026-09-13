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

    private bool CanStartFinale => runController != null && runController.IsRunning
        && !runController.IsCompleted && !runController.IsDefeated && !runController.IsFinaleStarted
        && Time.timeScale > 0f
        && (PlayerHealth.instance == null || !PlayerHealth.instance.IsDead)
        && (uiController == null || uiController.levelUpPanel == null
            || !uiController.levelUpPanel.activeInHierarchy);

    private void Awake()
    {
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
        if (stageButtons.Length > 3 && stageButtons[3] != null)
            stageButtons[3].interactable = CanStartFinale;
        // Restart must remain reachable while an upgrade or death has stopped scaled time.
        restartButton.interactable = runController != null;
        if (finaleButtonLabel != null)
            finaleButtonLabel.text = runController != null ? $"{runController.FinaleStartTime:0.#}s Finale" : "Finale";

        if (runController == null)
        {
            statusText.text = "Run controller unavailable";
            return;
        }

        string stage = runController.IsCompleted ? "Complete"
            : runController.IsBossPhase ? "Rift Lord" : runController.IsFinaleStarted ? "Fusion"
            : runController.CurrentStageIndex < 0 ? "Not started"
            : !runController.UsesSharedWaves ? "Survival" : "Wave " + (runController.CurrentStageIndex + 1);
        bool paused = Time.timeScale <= 0f || (uiController != null && uiController.levelUpPanel != null
            && uiController.levelUpPanel.activeInHierarchy);
        string state = runController.IsDefeated ? "Defeated" : runController.IsCompleted ? "Complete"
            : !runController.IsRunning ? "Stopped" : paused ? "Paused" : "Running";
        string countdown = runController.IsFinaleStarted ? "Final fusion locked"
            : $"Fusion in: {Mathf.CeilToInt(runController.RemainingUntilFinale)}s";
        statusText.text = $"{stage} | {state}\n{countdown} | Bosses: {runController.RemainingBosses}";
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
