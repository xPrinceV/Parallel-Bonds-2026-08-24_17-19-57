using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(-50)]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed;
    public float pickupRange = 2f;
    public Vector2 facingDirection = Vector2.right;
    public static PlayerController instance;

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
            && world.Manager.IsFused && world.Manager.FusionPlayer != this))
        {
            return;
        }

        instance = this;
        GetComponent<PlayerHealth>()?.BindAsCurrent();
        GetComponent<ExperienceLevelController>()?.BindAsCurrent();
    }

    // public Weapon activeWeapon;
    public List<Weapon> unassignedWeapons, assignedWeapons;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private bool starterWeaponsInitialized;

    void Start()
    {
        if (starterWeaponsInitialized)
            return;
        starterWeaponsInitialized = true;
        if (assignedWeapons != null && assignedWeapons.Count > 0)
            return;

        //Temporary for now until weapon chest implemented
        AddWeapon(0);
        AddWeapon(0);
        AddWeapon(0);
    }

    // Update is called once per frame
    void Update()
    {
        World world = World.GetFor(this);
        WorldManager manager = world == null ? null : world.Manager;
        if (manager != null && manager.IsFused && manager.FusionPlayer != this)
            return;

        Vector3 moveInput = new Vector3(0f, 0f, 0f);
        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");

        //Condition to check if the player is moving, if so, update the facing direction to the direction of movement
        if (moveInput != Vector3.zero)
        {
            facingDirection = moveInput.normalized;
        }

        //Normalizes vector so diagonal movement isn't faster than horizontal and vertical movement
        moveInput.Normalize();

        transform.position += moveInput * moveSpeed * Time.deltaTime;
        if (manager != null && manager.IsFused)
            manager.SyncFusionPlayer();
    }

    public void AddWeapon(int weaponNumber)
    {
        if(unassignedWeapons != null && assignedWeapons != null
            && weaponNumber >= 0 && weaponNumber < unassignedWeapons.Count)
        {
            assignedWeapons.Add(unassignedWeapons[weaponNumber]);
            unassignedWeapons[weaponNumber].gameObject.SetActive(true);
            unassignedWeapons.RemoveAt(weaponNumber);
        }
    }
}
