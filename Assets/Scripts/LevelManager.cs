using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager instance;
    void Awake()
    {
        instance = this;
    }

    public float timer;
    private bool gameIsActive;
    void Start()
    {
        gameIsActive = true;
    }

    // Update is called once per frame
    void Update()
    {
        if(gameIsActive)
        {
            timer += Time.deltaTime;
            UIController.instance.UpdateTimer(timer);
        }
    }
}
