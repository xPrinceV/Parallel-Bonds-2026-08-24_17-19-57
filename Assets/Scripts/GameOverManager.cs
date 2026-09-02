using UnityEngine;

public class GameOverManager : MonoBehaviour
{
    public static GameOverManager instance;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }
    public void GameOver()
    {
        UIController.Instance.gameOverManager.SetActive(true);
    }
}
