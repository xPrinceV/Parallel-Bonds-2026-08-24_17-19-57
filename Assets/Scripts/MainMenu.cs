using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    //Create Main menu button function
    public void PlayGame()
    {   //Load Main Game when PLAY button is pressed
        SceneManager.LoadSceneAsync("Main Game");
    }

    //Create Quit button function
    public void QuitGame()
    {
        //Quits game when QUIT button is pressed
        Application.Quit();
        Debug.Log("Quit");
    }
}
