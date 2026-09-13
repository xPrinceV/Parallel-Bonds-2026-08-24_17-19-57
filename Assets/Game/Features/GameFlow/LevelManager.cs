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
    private RunStageController stages;

    public void ConfigureStages(RunStageController controller)
    {
        stages = controller;
    }

    void Start()
    {
        gameIsActive = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (stages != null)
        {
            timer = stages.ElapsedTime;
            if (UIController.instance != null && UIController.instance.timeText != null)
                UIController.instance.UpdateTimer(timer);
            return;
        }
        if (gameIsActive
            && (UIController.instance == null || UIController.instance.levelUpPanel == null
                || !UIController.instance.levelUpPanel.activeSelf))
        {
            timer += Time.deltaTime;
            if (UIController.instance != null && UIController.instance.timeText != null)
                UIController.instance.UpdateTimer(timer);
        }
    }
}
