using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ExperienceLevelController : MonoBehaviour
{
    public static ExperienceLevelController instance;

    void Awake()
    {
        instance = this;
    }

    public int currentExperience;
    public ExpPickup pickup;
    public List<int> expLevels;
    public int currentLevels = 1, levelCount = 100;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        while (expLevels.Count < levelCount)
        {
            expLevels.Add(Mathf.CeilToInt(expLevels[expLevels.Count - 1] * 1.1f));
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void GetExp(int amountToGet)
    {
        currentExperience += amountToGet;

        if (currentExperience >= expLevels[currentLevels])
        {
            LevelUp();
        }

        UIController.instance.UpdateExperience(currentExperience, expLevels[currentLevels], currentLevels);
    }

    public void SpawnExp(Vector3 position, int expValue)
    {
        Instantiate(pickup, position, Quaternion.identity).expValue = expValue;
    }

    public void LevelUp()
    {
        currentExperience -= expLevels[currentLevels];
        currentLevels++;

        if (currentLevels >= expLevels.Count)
        {
            currentLevels = expLevels.Count - 1;
        }

        //Activate Level Up UI
        UIController.instance.levelUpPanel.SetActive(true);
        //Freezes Time
        Time.timeScale = 0f;

        
        //For now its hardcoded to 3 since there is only plans for 3 buttons on a page
        for (var i = 0; i < 3; i++)
        {
            int randomIndex = Random.Range(0, PlayerController.instance.assignedWeapons.Count);
            Weapon weapon = PlayerController.instance.assignedWeapons[randomIndex];
            Debug.Log("Button " + i + " got weapon: " + weapon);
            UIController.instance.levelUpButtons[i].UpdateButtonDisplay(PlayerController.instance.assignedWeapons[randomIndex]);
        }

        // UIController.instance.levelUpButtons[0].UpdateButtonDisplay(PlayerController.instance.activeWeapon);

    }
}
