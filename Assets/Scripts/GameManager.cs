using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public GameObject gameOverUI;

    public void gameOver()
    {
        gameOverUI.SetActive(true);
    }
    //Restart button function
    public void restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    //Main menu button function
    public void mainMenu()
    {
        SceneManager.LoadScene("Main Menu");
    }

    //Quit button function
    public void quit()
    {
        Application.Quit();
    }
}
