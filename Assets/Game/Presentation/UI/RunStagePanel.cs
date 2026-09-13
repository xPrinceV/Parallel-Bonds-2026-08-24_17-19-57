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

    private bool CanChangeStage => runController != null && runController.IsRunning
        && !runController.IsCompleted && !runController.IsDefeated && !runController.IsFinaleStarted
        && Time.timeScale > 0f
        && (PlayerHealth.instance == null || !PlayerHealth.instance.IsDead)
        && (uiController == null || uiController.levelUpPanel == null
            || !uiController.levelUpPanel.activeInHierarchy);

    private void Awake()
    {
        for (int i = 0; i < stageButtons.Length; i++)
        {
            int stage = i;
            stageButtons[i].onClick.AddListener(() => EnterStage(stage));
            if (i == 3)
                stageButtons[i].GetComponentInChildren<TMP_Text>(true).text = "Finale";
        }
        nextButton.onClick.AddListener(AdvanceStage);
        restartButton.onClick.AddListener(Restart);
    }

    private void Update()
    {
        bool available = CanChangeStage;
        for (int i = 0; i < stageButtons.Length; i++)
            stageButtons[i].interactable = available && (i == 3 || runController.UsesSharedWaves)
                && runController.CurrentStageIndex != i;
        nextButton.interactable = available && runController.UsesSharedWaves && runController.CurrentStageIndex < 3;
        // Restart must remain reachable while an upgrade or death has stopped scaled time.
        restartButton.interactable = runController != null;

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

    private void EnterStage(int stage)
    {
        if (CanChangeStage && (stage == 3 || runController.UsesSharedWaves)
            && runController.CurrentStageIndex != stage)
            runController.TryEnterStage(stage);
    }

    private void AdvanceStage()
    {
        if (CanChangeStage && runController.UsesSharedWaves && runController.CurrentStageIndex < 3)
            runController.TryAdvanceStage();
    }

    private void Restart()
    {
        if (runController != null)
            runController.RestartRun();
    }
}
