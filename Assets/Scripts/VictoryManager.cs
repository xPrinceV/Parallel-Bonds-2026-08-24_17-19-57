using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class VictoryManager : MonoBehaviour
{
    public VictoryManager victoryUI;

    public void Victory()
    {
        victoryUI.SetActive(true);
    }

    private void SetActive(bool v)
    {
        throw new NotImplementedException();
    }

    //Create Restart function
    public void VictoryRestart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    //Create Main Menu function
    public void VictoryMainMenu()
    {
        SceneManager.LoadScene("Main Menu");
    }
    //Create Quit function
    public void VictoryQuit()
    {
        Application.Quit();
        Debug.Log("Quit");
    }
}
