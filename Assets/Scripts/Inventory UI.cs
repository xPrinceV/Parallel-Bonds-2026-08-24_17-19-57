using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public PlayerController player;
    public StateSwitchController stateSwitchController;

    public Image[] weaponIcons;

    public void UpdateInventory()
    {
        List<Weapon> currentWeapons;
        Debug.Log("Updating Inventory");

        if (stateSwitchController.isChar1)
        {
            currentWeapons = player.char1AssignedWeapons;
        }
        else
        {
            currentWeapons = player.char2AssignedWeapons;
        }

        for (int i = 0; i < weaponIcons.Length; i++)
        {
            if (i < currentWeapons.Count)
            {
                weaponIcons[i].sprite = currentWeapons[i].weaponIcon;
            }
            else
            {
                weaponIcons[i].sprite = null;
            }
        }
    }
}