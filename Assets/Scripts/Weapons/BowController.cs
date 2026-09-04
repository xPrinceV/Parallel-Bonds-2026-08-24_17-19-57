using UnityEngine;

public class BowController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float amount;
    [SerializeField] private float projectileSpeed;
    [SerializeField] private GameObject arrow;
    private float attackCounter;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        attackCounter = 0;
    }

    // Update is called once per frame
    void Update()
    {
        attackCounter -= Time.deltaTime;

        if(attackCounter <= 0)
        {
            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}
