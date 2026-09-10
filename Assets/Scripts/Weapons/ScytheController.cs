using System;
using UnityEngine;

public class ScytheController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float area;
    [SerializeField] private GameObject scythe;
    private PlayerController player;
    private float attackCounter;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        attackCounter = 0f;
        player = PlayerController.instance;
    }

    // Update is called once per frame
    void Update()
    {
        attackCounter -= Time.deltaTime;
        if(attackCounter <= 0)
        {
            Vector2 facingDirection = player.facingDirection;
            GameObject newScythe = Instantiate(scythe, transform.position, Quaternion.identity);

            newScythe.GetComponent<ScytheHitController>().SetDamage(attackDamage * stats.damage);
            newScythe.GetComponent<ScytheHitController>().SetArea(area * stats.area);
            newScythe.GetComponent<ScytheHitController>().SetDirection(facingDirection);

            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}
