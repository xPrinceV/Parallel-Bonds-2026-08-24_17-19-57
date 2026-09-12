using UnityEngine;

public class StateSwitchController : MonoBehaviour
{
    public bool isActive = true;
    public GameObject char1;
    public GameObject char2;

    // Inventories
    public InventorySystem inventorySystem;
    public InventoryUI inventoryUI;

    private float timer = 15f;
    private float timerCounter;
    void Start()
    {
        timerCounter = timer;
        char1.SetActive(isActive);
        char2.SetActive(!isActive);

        inventorySystem.bobActive = isActive;
        inventoryUI.RefreshInventory();
    }

    void Update()
    {
        timerCounter -= Time.deltaTime;
        if(timerCounter <= 0)
        {
            isActive = !isActive;
            char1.SetActive(isActive);
            char2.SetActive(!isActive);

            inventorySystem.bobActive = isActive;
            inventoryUI.RefreshInventory();

            timerCounter = timer;
        }
    }
}
