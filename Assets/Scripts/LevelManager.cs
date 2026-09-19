using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager instance;
    void Awake()
    {
        instance = this;
    }

    public UIController ui;

    public float timer;

    //Boss incoming UI
    public BossIncoming bossIncoming;

    //Warning appears 5 seconds before the boss
    public float bossWarningTime = 475f;

    private bool bossWarningShown = false;

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
            ui.UpdateTimer(timer);

            //Show boss warning at 7:55
            if (timer >= bossWarningTime && !bossWarningShown)
            {
                bossWarningShown = true;

                if (bossIncoming != null)
                {
                    bossIncoming.ShowBossIncoming();
                }
            }
        }
    }
}
