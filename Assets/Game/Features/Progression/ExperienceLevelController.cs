using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//Manages the player's experience and level progression
public class ExperienceLevelController : MonoBehaviour
{
    //This is to make it easy to access the ExperienceLevelController
    public static ExperienceLevelController instance;

    private bool experienceInitialized;

    void Awake()
    {
        InitializeExperience();
    }

    void OnEnable()
    {
        BindAsCurrent();
    }

    void OnDisable()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void BindAsCurrent()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        InitializeExperience();
        instance = this;
        RefreshPresentation();
    }

    public int currentExperience;
    public ExpPickup pickup;
    public List<int> expLevels = new List<int> { 0, 5, 8, 11, 15, 19, 24, 29, 35, 42 };
    public int currentLevels = 1, levelCount = 100;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshPresentation();
    }

    private void InitializeExperience()
    {
        if (experienceInitialized)
        {
            return;
        }

        //Own the generated table even if setup supplied a shared list.
        expLevels = expLevels != null && expLevels.Count > 0
            ? new List<int>(expLevels)
            : new List<int> { 0, 5, 8, 11, 15, 19, 24, 29, 35, 42 };

        //Generate experience required for each level, increases by 10% each level
        while (expLevels.Count < levelCount)
        {
            expLevels.Add(Mathf.CeilToInt(expLevels[expLevels.Count - 1] * 1.1f));
        }

        experienceInitialized = true;
    }

    private bool IsCurrentHero()
    {
        return isActiveAndEnabled && instance == this && PlayerController.instance != null &&
            GetComponent<PlayerController>() == PlayerController.instance;
    }

    private void RefreshPresentation()
    {
        if (instance != this || UIController.instance == null)
        {
            return;
        }

        if (UIController.instance.expLvlSlider != null && UIController.instance.expLvlText != null)
        {
            UIController.instance.UpdateExperience(currentExperience, expLevels[currentLevels], currentLevels);
        }
    }

    public void GetExp(int amountToGet)
    {
        if (!IsCurrentHero())
        {
            return;
        }

        currentExperience += amountToGet;

        if (currentExperience >= expLevels[currentLevels])
        {
            LevelUp();
        }
        //Update the experience bar
        RefreshPresentation();
    }

    //This function is to spawn the exp orb
    public void SpawnExp(Vector3 position, int expValue)
    {
        Instantiate(pickup, position, Quaternion.identity, World.GetContentRoot(this)).expValue = expValue;
    }

    //Triggered when the player levels up
    public void LevelUp()
    {
        if (!IsCurrentHero())
        {
            return;
        }

        //Reduce the current experience by the amount required to level up. Example, current xp 50, level up cost is 45, therefore 50-45, the remaining is 5
        currentExperience -= expLevels[currentLevels];
        //Increase the player level
        currentLevels++;

        //This is if in case the player somehow overlevels past the limit
        if (currentLevels >= expLevels.Count)
        {
            currentLevels = expLevels.Count - 1;
        }

        RefreshPresentation();
        UIController ui = UIController.instance;
        if (ui == null || ui.levelUpPanel == null || ui.levelUpButtons == null ||
            PlayerController.instance.assignedWeapons.Count == 0)
        {
            return;
        }

        //Activate Level Up UI
        ui.levelUpPanel.SetActive(true);

        //Freezes Time
        Time.timeScale = 0f;

        
        //This portion is to call the upgrade screen, that triggers when the player levels up
        //For now its hardcoded to 3 since there is only plans for 3 buttons on a page
        for (var i = 0; i < 3 && i < ui.levelUpButtons.Length; i++)
        {
            if (ui.levelUpButtons[i] == null)
            {
                continue;
            }
            int randomIndex = Random.Range(0, PlayerController.instance.assignedWeapons.Count);
            Weapon weapon = PlayerController.instance.assignedWeapons[randomIndex];
            Debug.Log("Button " + i + " got weapon: " + weapon);
            ui.levelUpButtons[i].UpdateButtonDisplay(weapon);
        }

        // UIController.instance.levelUpButtons[0].UpdateButtonDisplay(PlayerController.instance.activeWeapon);

    }
}
