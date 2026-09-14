using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    //Create Main Menu button Function
    public void PlayGame()
    {
        //Load "Main" (Main Game) when PLAY button is pressed
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
