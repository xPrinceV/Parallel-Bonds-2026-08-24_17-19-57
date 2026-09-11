using UnityEngine;
using System.Collections.Generic;

public class InventorySystem : MonoBehaviour
{    
    // Bob's Main Weapon - gun
    public Weapon bobMainWeapon;

    // Jeff's Main weapon - gloves
    public Weapon jeffMainWeapon;

    // Bob's inventory
    public List<Weapon> bobWeapons = new List<Weapon>();

    // Jeff's inventory
    public List<Weapon> jeffWeapons = new List<Weapon>();

    public bool bobActive = true;
    

    // Obtained weapons for Bob 
    public void ObtainedWeaponBob(Weapon newWeapon)
    {
        if (bobWeapons.Count < 3)
        {
            bobWeapons.Add(newWeapon);
        }
        else
        {
            Debug.Log("Bob's inventory is full.");
        }
    }

    public void ObtainedWeaponJeff(Weapon newWeapon)
    {
        if (jeffWeapons.Count < 3)
        {
            jeffWeapons.Add(newWeapon);
        }
        else
        {
            Debug.Log("Jeff's inventory is full.");
        }
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
/* for the weapon - selection 
 * ref public Inventory System inventorySystem;
 * when players choose Bob
 * include inventorySystem.ObtainedWeaponBob(selectedWeapon);
 * or Jeff
 * include inventorySystem.ObtainedWeaponJeff(selectedWeapon);
 * then drag inventory_Manager into the Inventory System component */