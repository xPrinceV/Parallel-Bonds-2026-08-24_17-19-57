using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public Image[] weaponIcons;
    private int slotsPerRow;

    void OnEnable()
    {
        UpdateInventory();
    }

    void LateUpdate()
    {
        UpdateInventory();
    }

    // Extend the existing framed row upwards; never truncate the fused inventory.
    private void EnsureSlots(int count)
    {
        if (slotsPerRow == 0)
            slotsPerRow = weaponIcons.Length;
        if (count <= weaponIcons.Length || slotsPerRow == 0 || weaponIcons[0] == null)
            return;
        Image template = weaponIcons[0];
        RectTransform frame = template.transform.parent as RectTransform;
        if (frame == null)
            return;
        float spacing = frame.rect.width + 20f;
        if (slotsPerRow > 1 && weaponIcons[1] != null
            && weaponIcons[1].transform.parent is RectTransform second)
            spacing = second.anchoredPosition.x - frame.anchoredPosition.x;

        int previousCount = weaponIcons.Length;
        System.Array.Resize(ref weaponIcons, count);
        for (int i = previousCount; i < count; i++)
        {
            RectTransform slot = Instantiate(frame, frame.parent);
            slot.name = "Weapon Slot " + (i + 1);
            slot.anchoredPosition = frame.anchoredPosition
                + new Vector2((i % slotsPerRow) * spacing, (i / slotsPerRow) * (frame.rect.height + 20f));
            Image icon = slot.GetChild(template.transform.GetSiblingIndex()).GetComponent<Image>();
            weaponIcons[i] = icon;
            foreach (Graphic graphic in slot.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }
    }

    public void UpdateInventory()
    {
        if (weaponIcons == null)
            return;

        // The active alias stays with the entry hero during fusion.
        PlayerController player = PlayerController.instance;
        IReadOnlyList<Weapon> currentWeapons = player != null && player.isActiveAndEnabled
            ? player.EquippedWeapons : null;
        EnsureSlots(currentWeapons == null ? 0 : currentWeapons.Count);

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
