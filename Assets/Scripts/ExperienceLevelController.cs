using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//Manages the player's experience and level progression
public class ExperienceLevelController : MonoBehaviour
{
    //This is to make it easy to access the ExperienceLevelController
    public static ExperienceLevelController instance;

    void Awake()
    {
        instance = this;
    }

    public PlayerController playerController;
    public UIController ui;

    public int currentExperience;
    public ExpPickup pickup;
    public List<int> expLevels;
    public int currentLevels = 1, levelCount = 100;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //Generate experience required for each level, increases by 10% each level
        while (expLevels.Count < levelCount)
        {
            expLevels.Add(Mathf.CeilToInt(expLevels[expLevels.Count - 1] * 1.1f));
        }
    }

    public void GetExp(int amountToGet)
    {
        currentExperience += amountToGet;

        if (currentExperience >= expLevels[currentLevels])
        {
            LevelUp();
        }
        //Update the experience bar
        ui.UpdateExperience(currentExperience, expLevels[currentLevels], currentLevels);
    }

    //This function is to spawn the exp orb
    public void SpawnExp(Vector3 position, int expValue)
    {
        Instantiate(pickup, position, Quaternion.identity).expValue = expValue;
    }

    //Triggered when the player levels up
    public void LevelUp()
    {
        //Reduce the current experience by the amount required to level up. Example, current xp 50, level up cost is 45, therefore 50-45, the remaining is 5
        currentExperience -= expLevels[currentLevels];
        //Increase the player level
        currentLevels++;

        //This is if in case the player somehow overlevels past the limit
        if (currentLevels >= expLevels.Count)
        {
            currentLevels = expLevels.Count - 1;
        }

        //Activate Level Up UI
        ui.levelUpPanel.SetActive(true);

        //Freezes Time
        Time.timeScale = 0f;


        //This portion is to call the upgrade screen, that triggers when the player levels up
        //For now its hardcoded to 3 since there is only plans for 3 buttons on a page
        // Choose the weapon list belonging to the currently active character
        List<Weapon> currentWeapons;

        if (playerController.stateSwitchController.isChar1)
        {
            currentWeapons = playerController.char1AssignedWeapons;
        }
        else
        {
            currentWeapons = playerController.char2AssignedWeapons;
        }

        // Generate the 3 upgrade buttons
        for (uint i = 0; i < 3; i += 1)
        {
            int randomIndex = Random.Range(0, currentWeapons.Count);
            Weapon weapon = currentWeapons[randomIndex];

            ui.levelUpButtons[i].UpdateButtonDisplay(weapon);
        }

    }
}
