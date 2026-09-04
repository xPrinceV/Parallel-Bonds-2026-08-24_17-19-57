using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public string sceneToLoad;


    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("Game has been quit");
    }

    public void StartGame()
    {
        SceneManager.LoadScene(sceneToLoad);
    }
}
