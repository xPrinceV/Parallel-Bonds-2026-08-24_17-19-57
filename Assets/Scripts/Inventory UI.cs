using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public InventorySystem inventory;

    // Main Weapon Icon
    public Image mainWeaponIcon;
    // Slot 2 Icon
    public Image slot2Icon;
    // Slot 3 Icon
    public Image slot3Icon;
    // Slot 4 Icon
    public Image slot4Icon;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshInventory();
    }

    public void RefreshInventory()
    {
        Weapon mainWeapon;
        List<Weapon> currentWeapons;

        if (inventory.bobActive)
        {
            mainWeapon = inventory.bobMainWeapon;
            currentWeapons = inventory.bobWeapons;
        }
        else
        {
            mainWeapon = inventory.jeffMainWeapon;
            currentWeapons = inventory.jeffWeapons;
        }

        // Main Weapon Icon
        SetWeaponIcon(mainWeaponIcon, mainWeapon);

        // Obtained Weapons
        SetWeaponIcon(
            slot2Icon,
            currentWeapons.Count > 0 ? currentWeapons[0] : null);

        SetWeaponIcon(
            slot3Icon,
            currentWeapons.Count > 1 ? currentWeapons[1] : null);

        SetWeaponIcon(
            slot4Icon,
            currentWeapons.Count > 2 ? currentWeapons[2] : null);
    }

    private void SetWeaponIcon(Image image, Weapon weapon)
    {
        if (weapon != null && weapon.weaponIcon != null)
        {
            image.sprite = weapon.weaponIcon;
            image.enabled = true;
        }
        else
        {
            image.sprite = null;
            image.enabled = false;
        }
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
