using UnityEngine;

public class DaggerController : Weapon
{
    [SerializeField] private float attackDamage;
    [SerializeField] private float attackSpeed;
    [SerializeField] private float duration;
    [SerializeField] private float amount;
    [SerializeField] private float attackRange;
    [SerializeField] private float projectileSpeed;
    [SerializeField] private float bounces;
    [SerializeField] private GameObject dagger;
    [SerializeField] private BuffController buffHolder;
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
        
        if(attackCounter <= 0)
        {
            EnemyController target = FindClosestEnemy();
            if (target != null)
            {
                // Snapshot buffs once per volley.
                float damage = attackDamage * stats.damage;
                int count = Mathf.Max(0, Mathf.FloorToInt(amount + stats.amount));
                if (buffHolder != null)
                {
                    damage = buffHolder.CalculateWeaponDamage(damage);
                    count = buffHolder.CalculateProjectileCount(count);
                }

                for (int i = 0; i < count; i++)
                {
                    //Create a new dagger projectile and set its damage, duration, range, bounces, speed, and target
                    GameObject newDagger = Instantiate(dagger, transform.position, Quaternion.identity, World.GetContentRoot(this));
                    DaggerProjectile projectile = newDagger.GetComponent<DaggerProjectile>();
                    projectile.SetDamage(damage);
                    projectile.SetDuration(duration * stats.duration);
                    projectile.SetRange(attackRange * stats.range);
                    projectile.SetBounces(Mathf.FloorToInt(bounces + stats.bounces));
                    projectile.SetSpeed(projectileSpeed * stats.speed);
                    projectile.SetTarget(target);
                    projectile.SetBuffSource(buffHolder);
                }
            }
            //Reset attack counter based on the attack speed
            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }

    private EnemyController FindClosestEnemy()
    {
        //Nearest enemy = null, closest distance = infinity
        EnemyController nearestEnemy = null;
        float closestDistance = Mathf.Infinity;

        //Find all collider hitboxes within the radius of attackRange
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, attackRange*stats.range);

        foreach (Collider2D enemy in enemiesInRange)
        {
            EnemyController foundEnemy = enemy.GetComponentInParent<EnemyController>();
            if (foundEnemy != null && foundEnemy.gameObject.activeInHierarchy && foundEnemy.health > 0f
                && World.GetFor(this) == World.GetFor(foundEnemy))
            {
                //Get the distance from that particular enemy
                float distance = Vector3.Distance(transform.position, foundEnemy.transform.position);

                //If the distance from that enemy, is lower than the closest distance
                if (distance < closestDistance)
                {
                    //Assign it as the new closest distance
                    closestDistance = distance;

                    //Assign that game object as the nearestEnemy
                    nearestEnemy = foundEnemy;
                }
            }
        }

        return nearestEnemy;
    }
}
