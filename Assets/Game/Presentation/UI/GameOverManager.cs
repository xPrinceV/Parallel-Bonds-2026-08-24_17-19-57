using UnityEngine;

public class GameOverManager : MonoBehaviour
{
    [SerializeField] private RunStageController runController;
    [SerializeField] private GameObject victoryUI;
    public GameObject gameOverUI;

    private void OnEnable()
    {
        if (runController == null || gameOverUI == null)
        {
            Debug.LogError("Assign the run controller and Game Over panel on the shared HUD.", this);
            enabled = false;
            return;
        }
        runController.StateChanged += RefreshDisplay;
        RefreshDisplay();
    }

    private void OnDisable()
    {
        if (runController != null)
            runController.StateChanged -= RefreshDisplay;
    }

    // The run owns completion, defeat and pause; this component only presents its result.
    private void RefreshDisplay()
    {
        bool victoryVisible = runController.IsCompleted;
        bool defeatVisible = runController.IsDefeated && !victoryVisible;
        if (gameOverUI.activeSelf != defeatVisible)
            gameOverUI.SetActive(defeatVisible);
        if (victoryUI != null && victoryUI.activeSelf != victoryVisible)
            victoryUI.SetActive(victoryVisible);
    }

    //Create Restart function
    public void Restart()
    {
        if (runController != null)
            runController.RestartRun();
    }

    //Create Main Menu function
    public void MainMenu()
    {
        if (runController != null)
            runController.ReturnToMainMenu();
    }

    //Create Quit function
    public void Quit()
    {
        Application.Quit();
    }
}
