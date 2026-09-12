using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverManager : MonoBehaviour
{
    public GameObject gameOverUI;
 
    public void GameOver()
    {
        gameOverUI.SetActive(true);
    }

    //Create Restart function
    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    //Create Main Menu function
    public void MainMenu()
    {
        SceneManager.LoadScene("Main Menu");
    }
    //Create Quit function
    public void Quit()
    {
        Application.Quit();
        Debug.Log("Quit");
    }
}
