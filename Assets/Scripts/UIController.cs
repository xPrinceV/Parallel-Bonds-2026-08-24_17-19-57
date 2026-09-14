using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UIController : MonoBehaviour
{
    public LevelUpSelectionButton[] levelUpButtons;
    public LevelUpSelectionButton[] chestButtons;
    public GameObject levelUpPanel;
    public GameObject chestPanel;
    public TMP_Text timeText;

    public Slider expLvlSlider;
    public TMP_Text expLvlText;


    public void UpdateExperience(int currentExp, int levelExp, int currentLvl)
    {
        expLvlSlider.maxValue = levelExp;
        expLvlSlider.value = currentExp;

        expLvlText.text = "Level: " + currentLvl;
    }

    //Function for adding a game timer
    public void UpdateTimer(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60);

        timeText.text = minutes + ":" + seconds.ToString("00");
    }

    public void OpenChestPanel()
    {
        List<Weapon> currentWeapons;

        // Get the active character's weapons
        if (PlayerController.instance.stateSwitchController.isChar1)
        {
            currentWeapons = PlayerController.instance.char1AssignedWeapons;
        }
        else
        {
            currentWeapons = PlayerController.instance.char2AssignedWeapons;
        }


        // If this character already has 4 weapons,
        // give them a normal weapon upgrade instead
        if (currentWeapons.Count >= 4)
        {
            levelUpPanel.SetActive(true);
            Time.timeScale = 0f;

            // Populate the 3 level-up buttons
            for (int i = 0; i < levelUpButtons.Length; i++)
            {
                int randomIndex = Random.Range(0, currentWeapons.Count);

                levelUpButtons[i].UpdateButtonDisplay(
                    currentWeapons[randomIndex]
                );
            }

            return;
        }


        // Otherwise open the chest normally
        chestPanel.SetActive(true);
        Time.timeScale = 0f;

        List<Weapon> availableWeapons =
            new List<Weapon>(PlayerController.instance.unassignedWeapons);

        for (int i = 0; i < chestButtons.Length; i++)
        {
            if (PlayerController.instance.unassignedWeapons.Count == 0)
            {
                break;
            }

            // If we've shown every unique weapon,
            // refill so duplicates can be used
            if (availableWeapons.Count == 0)
            {
                availableWeapons =
                    new List<Weapon>(PlayerController.instance.unassignedWeapons);
            }

            int randomIndex = Random.Range(0, availableWeapons.Count);

            Weapon selectedWeapon = availableWeapons[randomIndex];

            chestButtons[i].UpdateChestDisplay(selectedWeapon);

            availableWeapons.RemoveAt(randomIndex);
        }
    }

}
