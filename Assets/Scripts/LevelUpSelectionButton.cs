using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LevelUpSelectionButton : MonoBehaviour
{
    public TMP_Text upgradeDescText, nameLevelText;
    public Image weaponIcon;
    private Weapon assignedWeapon;
    private float selectedUpgrade;
    private UpgradeType selectedUpgradeType;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void UpdateButtonDisplay(Weapon theWeapon)
    {
        weaponIcon.sprite = theWeapon.icon;
        nameLevelText.text = theWeapon.weaponName;
        assignedWeapon = theWeapon;
        Upgrade();

        if (selectedUpgradeType == UpgradeType.Damage)
        {
            upgradeDescText.text = "+" + (selectedUpgrade * 100) + "% Damage";
        }
        else if (selectedUpgradeType == UpgradeType.AttackSpeed)
        {
            upgradeDescText.text = "+" + (selectedUpgrade * 100) + "% Attack Speed";
        }
        else if (selectedUpgradeType == UpgradeType.Range)
        {
            upgradeDescText.text = "+" + (selectedUpgrade * 100) + "% Range";
        }
        else if (selectedUpgradeType == UpgradeType.Duration)
        {
            upgradeDescText.text = "+" + (selectedUpgrade * 100) + "% Duration";
        }
        else if (selectedUpgradeType == UpgradeType.Amount)
        {
            upgradeDescText.text = "+" + selectedUpgrade + " Projectile Count";
        }
        else if (selectedUpgradeType == UpgradeType.Speed)
        {
            upgradeDescText.text = "+" + (selectedUpgrade * 100) + "% Projectile Speed";
        }

    }

    public void SelectUpgrade()
    {
        if (assignedWeapon != null)
        {
            Debug.Log("Weapon: " + assignedWeapon.weaponName + " | Upgrade: " + selectedUpgradeType + " | Value: " + selectedUpgrade);
            if (selectedUpgradeType == UpgradeType.Damage)
            {
                assignedWeapon.stats.damage += selectedUpgrade;
            }
            else if (selectedUpgradeType == UpgradeType.Speed)
            {
                assignedWeapon.stats.speed += selectedUpgrade;
            }
            else if (selectedUpgradeType == UpgradeType.Range)
            {
                assignedWeapon.stats.range += selectedUpgrade;
            }
            else if (selectedUpgradeType == UpgradeType.AttackSpeed)
            {
                assignedWeapon.stats.attackSpeed += selectedUpgrade;
            }
            else if (selectedUpgradeType == UpgradeType.Amount)
            {
                assignedWeapon.stats.amount += selectedUpgrade;
            }
            else if (selectedUpgradeType == UpgradeType.Duration)
            {
                assignedWeapon.stats.duration += selectedUpgrade;
            }

            //Close level up screen and unpause time
            UIController.instance.levelUpPanel.SetActive(false);
            Time.timeScale = 1f;
        }
    }

    public void Upgrade()
    {
        Debug.Log("Weapon: " + assignedWeapon);
        Debug.Log("Available upgrades: " + assignedWeapon.availableUpgrades);


        //Pick a random stat from the list of available upgrades the weapon accepts
        int randomStat = Random.Range(0, assignedWeapon.availableUpgrades.Length);
        selectedUpgradeType = assignedWeapon.availableUpgrades[randomStat];

        //Range of upgrades
        if (selectedUpgradeType == UpgradeType.Damage)
        {
            int randomChoice = Random.Range(0, assignedWeapon.stats.damageUpgrades.Length);
            selectedUpgrade = assignedWeapon.stats.damageUpgrades[randomChoice];
        }
        else if (selectedUpgradeType == UpgradeType.Speed)
        {
            int randomChoice = Random.Range(0, assignedWeapon.stats.speedUpgrades.Length);
            selectedUpgrade = assignedWeapon.stats.speedUpgrades[randomChoice];
        }
        else if (selectedUpgradeType == UpgradeType.Range)
        {
            int randomChoice = Random.Range(0, assignedWeapon.stats.rangeUpgrades.Length);
            selectedUpgrade = assignedWeapon.stats.rangeUpgrades[randomChoice];
        }
        else if (selectedUpgradeType == UpgradeType.AttackSpeed)
        {
            int randomChoice = Random.Range(0, assignedWeapon.stats.attackSpeedUpgrades.Length);
            selectedUpgrade = assignedWeapon.stats.attackSpeedUpgrades[randomChoice];
        }
        else if (selectedUpgradeType == UpgradeType.Amount)
        {
            int randomChoice = Random.Range(0, assignedWeapon.stats.amountUpgrades.Length);
            selectedUpgrade = assignedWeapon.stats.amountUpgrades[randomChoice];
        }
        else if (selectedUpgradeType == UpgradeType.Duration)
        {
            int randomChoice = Random.Range(0, assignedWeapon.stats.durationUpgrades.Length);
            selectedUpgrade = assignedWeapon.stats.durationUpgrades[randomChoice];
        }
    }

}
