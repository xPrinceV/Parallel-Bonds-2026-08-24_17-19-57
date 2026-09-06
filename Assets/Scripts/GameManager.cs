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
        Debug.Log("Retart");
    }

    //Main menu button function
    public void mainMenu()
    {
        SceneManager.LoadScene("Main Menu");
        Debug.Log("Main menu");
    }

    //Quit button function
    public void quit()
    {
        Application.Quit();
        Debug.Log("Quit");
    }
}
