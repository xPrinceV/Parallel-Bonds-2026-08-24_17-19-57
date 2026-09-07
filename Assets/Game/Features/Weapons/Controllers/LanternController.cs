using UnityEngine;

public class LanternController : Weapon
{

    [SerializeField] private float attackDamage;
    [SerializeField] private float attackSpeed;
    [SerializeField] private float duration;
    [SerializeField] private float amount;
    [SerializeField] private BuffController buffHolder;
    public GameObject lantern;
    private float attackCounter;

    void Start()
    {
        attackCounter = 0;
        if (buffHolder == null)
            buffHolder = GetComponentInParent<BuffController>();
    }

    // Update is called once per frame
    void Update()
    {
        //Attack Timer
        attackCounter -= Time.deltaTime;

        if (attackCounter <= 0)
        {
            // Snapshot buffs once per volley.
            float damage = attackDamage * stats.damage;
            int count = Mathf.Max(0, Mathf.FloorToInt(amount + stats.amount));
            if (buffHolder != null)
            {
                damage = buffHolder.CalculateWeaponDamage(damage);
                count = buffHolder.CalculateProjectileCount(count);
            }

            //The for loop is to account for projectile count, it will loop based on the projectile count of the weapon
            for (var i = 0; i < count; i++)
            {
                GameObject newLantern = Instantiate(lantern, transform.position, Quaternion.identity, World.GetContentRoot(this));
                LanternProjController projectile = newLantern.GetComponent<LanternProjController>();
                projectile.SetDamage(damage);
                projectile.SetDuration(duration * stats.duration);
                projectile.SetBuffSource(buffHolder);
            }
            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}

