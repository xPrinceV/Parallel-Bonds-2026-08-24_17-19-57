using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//Manages the player's experience and level progression
public class ExperienceLevelController : MonoBehaviour
{
    //This is to make it easy to access the ExperienceLevelController
    public static ExperienceLevelController instance;

    private bool experienceInitialized;
    private long excessExperience;
    private long fusionContribution;
    private bool hasFusionExperience;
    private bool upgradePending;
    private bool selectionAccepted;
    private float nextSelectionTime;
    internal int SelectionVersion { get; private set; }
    private int MaximumLevel => Mathf.Min(Mathf.Max(2, levelCount), expLevels.Count) - 1;

    // Current experience is a remainder; include every completed level when joining pools.
    public long TotalExperience
    {
        get
        {
            InitializeExperience();
            long total = UnspentExperience;
            for (int i = 1; i < currentLevels; i++)
                total = AddExperience(total, expLevels[i]);
            return total;
        }
    }

    private long UnspentExperience => AddExperience(excessExperience, Mathf.Max(0, currentExperience));

    private static long AddExperience(long total, long amount)
    {
        return amount > long.MaxValue - total ? long.MaxValue : total + amount;
    }

    private void SetUnspentExperience(long total)
    {
        currentExperience = (int)System.Math.Min(int.MaxValue, total);
        excessExperience = total - currentExperience;
    }

    // Use the entry hero's curve, including its level cap, without granting upgrade choices.
    private void SetTotalExperience(long total)
    {
        InitializeExperience();
        currentLevels = 1;
        while (currentLevels < MaximumLevel && total >= expLevels[currentLevels])
        {
            total -= expLevels[currentLevels];
            currentLevels++;
        }
        SetUnspentExperience(total);
        RefreshPresentation();
    }

    internal void BeginFusionExperience(ExperienceLevelController other)
    {
        if (hasFusionExperience || other == null || other == this)
            return;
        long ownTotal = TotalExperience;
        long combined = AddExperience(ownTotal, other.TotalExperience);
        fusionContribution = combined - ownTotal;
        hasFusionExperience = true;
        SetTotalExperience(combined);
    }

    internal void EndFusionExperience()
    {
        if (!hasFusionExperience)
            return;
        // Return the borrowed pool, keeping any experience earned by the entry hero while fused.
        long ownTotal = System.Math.Max(0, TotalExperience - fusionContribution);
        hasFusionExperience = false;
        fusionContribution = 0;
        SetTotalExperience(ownTotal);
    }

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
        World world = World.GetFor(this);
        if (!isActiveAndEnabled || (world != null && world.Manager != null
            && world.Manager.IsFused && world.InteractionPlayer != GetComponent<PlayerController>()))
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

        if (expLevels.Count == 1)
            expLevels.Add(5);
        for (int i = 1; i < expLevels.Count; i++)
            expLevels[i] = Mathf.Max(1, expLevels[i]);

        //Generate experience required for each level, increases by 10% each level
        while (expLevels.Count < levelCount)
        {
            double next = System.Math.Ceiling(expLevels[expLevels.Count - 1] * 1.1f);
            expLevels.Add((int)System.Math.Min(int.MaxValue, next));
        }

        currentLevels = Mathf.Clamp(currentLevels, 1, MaximumLevel);
        currentExperience = Mathf.Max(0, currentExperience);
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
        World world = World.GetFor(this);
        if (world != null && world.Manager != null && world.Manager.IsFused
            && world.InteractionPlayer != null && world.InteractionPlayer != GetComponent<PlayerController>())
        {
            world.InteractionPlayer.GetComponent<ExperienceLevelController>()?.GetExp(amountToGet);
            return;
        }

        if (!IsCurrentHero())
        {
            return;
        }

        if (amountToGet <= 0)
            return;
        SetUnspentExperience(AddExperience(UnspentExperience, amountToGet));
        if (!upgradePending)
            LevelUp();
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
        if (!IsCurrentHero() || upgradePending || currentLevels >= MaximumLevel
            || UnspentExperience < expLevels[currentLevels])
        {
            return;
        }

        //Reduce the current experience by the amount required to level up. Example, current xp 50, level up cost is 45, therefore 50-45, the remaining is 5
        SetUnspentExperience(UnspentExperience - expLevels[currentLevels]);
        //Increase the player level
        currentLevels++;

        //This is if in case the player somehow overlevels past the limit
        currentLevels = Mathf.Min(currentLevels, MaximumLevel);

        RefreshPresentation();
        if (ShowUpgradeSelection())
            return;

        // No selection UI is available; still consume every earned threshold up to the cap.
        while (currentLevels < MaximumLevel && UnspentExperience >= expLevels[currentLevels])
        {
            SetUnspentExperience(UnspentExperience - expLevels[currentLevels]);
            currentLevels++;
        }
        RefreshPresentation();
    }

    private bool ShowUpgradeSelection()
    {
        UIController ui = UIController.instance;
        var choices = new List<Weapon>();
        IReadOnlyList<Weapon> weapons = PlayerController.instance.EquippedWeapons;
        for (int i = 0; weapons != null && i < weapons.Count; i++)
        {
            Weapon weapon = weapons[i];
            if (weapon != null && weapon.availableUpgrades != null && weapon.availableUpgrades.Length > 0)
                choices.Add(weapon);
        }
        if (ui == null || ui.levelUpPanel == null || ui.levelUpButtons == null ||
            System.Array.TrueForAll(ui.levelUpButtons, button => button == null) || choices.Count == 0)
        {
            return false;
        }
        upgradePending = true;
        selectionAccepted = false;
        SelectionVersion++;

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
            int randomIndex = Random.Range(0, choices.Count);
            Weapon weapon = choices[randomIndex];
            Debug.Log("Button " + i + " got weapon: " + weapon);
            ui.levelUpButtons[i].UpdateButtonDisplay(weapon);
        }

        // UIController.instance.levelUpButtons[0].UpdateButtonDisplay(PlayerController.instance.activeWeapon);

        return true;
    }

    // A choice belongs to one visible panel and can only be consumed once.
    internal bool TryConsumeUpgradeSelection(int version)
    {
        UIController ui = UIController.instance;
        if (!IsCurrentHero() || !upgradePending || version != SelectionVersion
            || Time.unscaledTime < nextSelectionTime || ui == null || ui.levelUpPanel == null
            || !ui.levelUpPanel.activeInHierarchy)
            return false;
        upgradePending = false;
        selectionAccepted = true;
        SelectionVersion++;
        // Ignore the second click of a double-click on the newly opened choices.
        nextSelectionTime = Time.unscaledTime + 0.2f;
        return true;
    }

    internal void ReconcileUpgradeSelection(bool cancel)
    {
        if (!upgradePending && !selectionAccepted)
            return;
        upgradePending = selectionAccepted = false;
        SelectionVersion++;
        if (!cancel && IsCurrentHero() && ShowUpgradeSelection())
            return;
        UIController ui = UIController.instance;
        if (ui != null && ui.levelUpPanel != null)
            ui.levelUpPanel.SetActive(false);
        if (!cancel && IsCurrentHero())
            Time.timeScale = 1f;
    }

    // Process additional earned levels one selection at a time, never a burst of overwritten panels.
    public void CompleteUpgradeSelection()
    {
        if (!IsCurrentHero() || !selectionAccepted)
            return;
        selectionAccepted = false;
        UIController ui = UIController.instance;
        if (ui != null && ui.levelUpPanel != null)
            ui.levelUpPanel.SetActive(false);
        Time.timeScale = 1f;
        LevelUp();
    }
}
