using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIController : MonoBehaviour
{
    public LevelUpSelectionButton[] levelUpButtons;
    public GameObject levelUpPanel;
    public TMP_Text timeText;

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
    public void UpdateTimer(float time) {
        int minutes = Mathf.FloorToInt (time / 60f);
        int seconds = Mathf.FloorToInt(time % 60); 

        timeText.text = minutes + ":" + seconds.ToString("00");
    }
}
