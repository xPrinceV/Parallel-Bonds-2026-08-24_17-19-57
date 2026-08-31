using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed;
    public float pickupRange = 2f;
    public static PlayerController instance;

    void Awake()
    {
        instance = this;
    }

    // public Weapon activeWeapon;
    public List<Weapon> unassignedWeapons, assignedWeapons;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //Temporary for now until weapon chest implemented
        AddWeapon(0);
        AddWeapon(0);
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 moveInput = new Vector3(0f, 0f, 0f);
        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");

        //Normalizes vector so diagonal movement isn't faster than horizontal and vertical movement
        moveInput.Normalize();

        transform.position += moveInput * moveSpeed * Time.deltaTime;
    }

    public void AddWeapon(int weaponNumber)
    {
        if(weaponNumber < unassignedWeapons.Count)
        {
            assignedWeapons.Add(unassignedWeapons[weaponNumber]);
            unassignedWeapons[weaponNumber].gameObject.SetActive(true);
            unassignedWeapons.RemoveAt(weaponNumber);
        }
    }
}
