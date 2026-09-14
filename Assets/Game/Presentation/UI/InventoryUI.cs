using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public Image[] weaponIcons;

    void OnEnable()
    {
        UpdateInventory();
    }

    void LateUpdate()
    {
        UpdateInventory();
    }

    public void UpdateInventory()
    {
        if (weaponIcons == null)
            return;

        // The active alias stays with the entry hero during fusion.
        PlayerController player = PlayerController.instance;
        List<Weapon> currentWeapons = player != null && player.isActiveAndEnabled
            ? player.assignedWeapons : null;

        for (int i = 0; i < weaponIcons.Length; i++)
        {
            Image image = weaponIcons[i];
            if (image == null)
                continue;

            Weapon weapon = currentWeapons != null && i < currentWeapons.Count ? currentWeapons[i] : null;
            Sprite icon = weapon == null ? null : (weapon.weaponIcon != null ? weapon.weaponIcon : weapon.icon);
            // Avoid dirtying the canvas when equipment has not changed.
            if (image.sprite != icon)
                image.sprite = icon;
            if (image.enabled != (icon != null))
                image.enabled = icon != null;
        }
    }
}
