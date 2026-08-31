using UnityEngine;

public class LanternController : Weapon
{

    [SerializeField] private float attackDamage;
    [SerializeField] private float attackSpeed;
    [SerializeField] private float duration;
    [SerializeField] private float amount;
    public GameObject lantern;
    private float attackCounter;

    void Start()
    {
        attackCounter = 0;
    }

    // Update is called once per frame
    void Update()
    {
        //Attack Timer
        attackCounter -= Time.deltaTime;

        if (attackCounter <= 0)
        {
            //The for loop is to account for projectile count, it will loop based on the projectile count of the weapon
            for (var i = 0; i < Mathf.FloorToInt(amount + stats.amount); i++)
            {
                GameObject newLantern = Instantiate(lantern, transform.position, Quaternion.identity);
                newLantern.GetComponent<LanternProjController>().SetDamage(attackDamage * stats.damage);
                newLantern.GetComponent<LanternProjController>().SetDuration(duration * stats.duration);
            }
            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}

