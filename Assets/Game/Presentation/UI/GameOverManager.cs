using UnityEngine;

public class GameOverManager : MonoBehaviour
{
    [SerializeField] private RunStageController runController;
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

    // the run owns defeat and pause; this component only presents its result
    private void RefreshDisplay()
    {
        bool visible = runController.IsDefeated;
        if (gameOverUI.activeSelf != visible)
            gameOverUI.SetActive(visible);
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
