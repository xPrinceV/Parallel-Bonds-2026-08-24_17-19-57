using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class ShieldController : Weapon
{
    public GameObject shieldPrefab;
    public GameObject shieldPivot;
    public float orbitDistance;
    public float orbitSpeed;
    public float damage;
    public float duration;
    public float cooldown;
    private float timer;
    private bool isShieldActive;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //Spawn Immediately when weapon first becomes active
        SpawnShields();
    }

    // Update is called once per frame
    void Update()
    {
        timer -= Time.deltaTime;
        if (isShieldActive && shieldPivot != null)
        {
            shieldPivot.transform.position = PlayerController.instance.transform.position;
            shieldPivot.transform.rotation = Quaternion.Euler(0f, 0f, shieldPivot.transform.rotation.eulerAngles.z + (orbitSpeed * stats.speed * Time.deltaTime));
        }
        if (timer <= 0)
        {
            if (isShieldActive)
            {
                //This means the shield is active, so we destroy the shield and start a fixed cooldown
                DestroyShields();
            }
            else
            {
                //Cooldown has finished, so we spawn a new set of shield
                SpawnShields();
            }
        }
    }

    void SpawnShields()
    {
        isShieldActive = true;

        //Set projectile count to stats.amount (default value is 1)
        int shieldCount = Mathf.FloorToInt(stats.amount);

        //This is so shield spawns equally spaced between each other
        float angleSpread = 360f / shieldCount;

        //Create a empty game object, this will act as a pivot and hold all shields
        shieldPivot = new GameObject("Shield Pivot");

        shieldPivot.transform.position = PlayerController.instance.transform.position;



        for (int i = 0; i < shieldCount; i++)
        {
            float angle = angleSpread * i;
            float radians = angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * orbitDistance;

            GameObject newShield = Instantiate(shieldPrefab, shieldPivot.transform.position + offset, Quaternion.identity, shieldPivot.transform);
            newShield.GetComponent<ShieldOrbitController>().damage = damage * stats.damage;
        }

        //Start timer for active duration
        timer = duration * stats.duration;

    }

    void DestroyShields()
    {
        isShieldActive = false;

        //Destroying the pivot destroys all children
        if (shieldPivot != null)
        {
            Destroy(shieldPivot);
        }

        //Start a fixed cooldown timer
        timer = cooldown;
    }

    //Switching characters will disable weapons, therefore we need to have this
    void OnDisable()
    {
        //Destroying the pivot destroys all children
        if (shieldPivot != null)
        {
            Destroy(shieldPivot);
        }

        isShieldActive = false;
    }
}
