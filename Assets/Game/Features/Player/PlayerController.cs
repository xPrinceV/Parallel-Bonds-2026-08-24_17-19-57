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
    [SerializeField] private List<Weapon> startingWeapons = new List<Weapon>();

    // Keep source ownership; fusion exposes both inventories without cloning weapons.
    public IReadOnlyList<Weapon> EquippedWeapons
    {
        get
        {
            World world = World.GetFor(this);
            WorldManager manager = world == null ? null : world.Manager;
            if (manager != null && manager.IsFused && manager.FusionPlayer == this)
                return manager.FusionWeapons;
            return assignedWeapons;
        }
    }

    public bool HasEquippedWeapon(Weapon weapon)
    {
        if (weapon == null)
            return false;
        IReadOnlyList<Weapon> weapons = EquippedWeapons;
        for (int i = 0; weapons != null && i < weapons.Count; i++)
            if (weapons[i] == weapon)
                return true;
        return false;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private bool starterWeaponsInitialized;

    void Start()
    {
        InitializeStartingWeapons();
    }

    // Fusion may start before the sleeping hero receives its first Start callback.
    internal void InitializeStartingWeapons()
    {
        if (starterWeaponsInitialized)
            return;
        starterWeaponsInitialized = true;
        if (assignedWeapons != null && assignedWeapons.Count > 0)
            return;

        assignedWeapons ??= new List<Weapon>();
        unassignedWeapons ??= new List<Weapon>();
        if (startingWeapons != null && startingWeapons.Count > 0)
        {
            foreach (Weapon weapon in startingWeapons)
                AddWeapon(unassignedWeapons.IndexOf(weapon));
            return;
        }

        //Temporary for now until weapon chest implemented
        // Preserve unconfigured legacy scenes; game scenes supply explicit starters.
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
            Weapon newWeapon = unassignedWeapons[weaponNumber];

            if (newWeapon == null || assignedWeapons.Contains(newWeapon)
                || newWeapon.GetComponentInParent<PlayerController>() != this)
                return;

            assignedWeapons.Add(newWeapon);
            newWeapon.gameObject.SetActive(true);
            unassignedWeapons.RemoveAt(weaponNumber);
            World.GetFor(this)?.Manager?.RefreshFusionWeapons();
        }
    }
}
