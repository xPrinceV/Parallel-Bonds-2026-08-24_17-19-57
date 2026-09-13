using UnityEngine;

public class StateSwitchController : MonoBehaviour
{
    public bool isActive = true;
    public GameObject char1;
    public GameObject char2;
    public GameObject world1;
    public GameObject world2;

    public bool isChar1;

    private float timer = 15f;
    private float timerCounter;
    public PlayerController player;
    public InventoryUI inventoryUI;
    void Start()
    {
        timerCounter = timer;
        char1.SetActive(isActive);
        char2.SetActive(!isActive);
        isChar1 = isActive;

    }

    void Update()
    {
        timerCounter -= Time.deltaTime;
        if(timerCounter <= 0)
        {
            isActive = !isActive;
            char1.SetActive(isActive);
            char2.SetActive(!isActive);
            world1.SetActive(isActive);
            world2.SetActive(!isActive);
            isChar1 = isActive;

            //Trigger switch weapon
            SwitchWeapon();
            //Update UI to show character's weapon
            inventoryUI.UpdateInventory();


            timerCounter = timer;
        }
    }

    public void SwitchWeapon()
    {
        foreach(Weapon weapon in player.char1AssignedWeapons)
        {
            weapon.gameObject.SetActive(isChar1);
        }

        foreach(Weapon weapon in player.char2AssignedWeapons)
        {
            weapon.gameObject.SetActive(!isChar1);
        }
    }
}
