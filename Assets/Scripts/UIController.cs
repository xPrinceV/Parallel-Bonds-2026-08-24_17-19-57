using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIController : MonoBehaviour
{
    public static UIController instance;
    public LevelUpSelectionButton[] levelUpButtons;
    public GameObject levelUpPanel;
    public TMP_Text timeText;
    void Awake()
    {
        instance = this;
    }

    public Slider expLvlSlider;
    public TMP_Text expLvlText;

    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void UpdateExperience(int currentExp, int levelExp, int currentLvl)
    {
        expLvlSlider.maxValue = levelExp;
        expLvlSlider.value = currentExp;

        expLvlText.text = "Level: " + currentLvl;
    }

    //Function for adding a game timer
    public void UpdateTimer(float time)
    {
        float minutes = Mathf.FloorToInt (time / 60f);
        float seconds = Mathf.FloorToInt(time % 60); 

        timeText.text = minutes + ":" + seconds.ToString("00");
    }
}
