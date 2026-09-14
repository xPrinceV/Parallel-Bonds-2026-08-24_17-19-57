using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    private bool isLoading;

    //Create Main Menu button Function
    public void PlayGame()
    {
        if (isLoading)
            return;
        if (!Application.CanStreamedLevelBeLoaded("Main"))
        {
            Debug.LogError("Register Main in Build Settings before starting a run.", this);
            return;
        }

        //Load "Main" (Main Game) when PLAY button is pressed
        isLoading = true;
        Time.timeScale = 1f;
        SceneManager.LoadSceneAsync("Main");
    }

    //Create Quit button Function
    public void QuitGame()
    {
        //Quits game when QUIT button is pressed
        Application.Quit();
        Debug.Log("Quit");
    }
}
