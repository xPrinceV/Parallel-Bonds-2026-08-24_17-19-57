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
        
        if(attackCounter <= 0)
        {
            EnemyController target = FindClosestEnemy();
            if (target != null)
            {
                //Create a new dagger projectile and set its damage, duration, range, bounces, speed, and target
                GameObject newDagger = Instantiate(dagger, transform.position, Quaternion.identity);
                newDagger.GetComponent<DaggerProjectile>().SetDamage(attackDamage * stats.damage);
                newDagger.GetComponent<DaggerProjectile>().SetDuration(duration * stats.duration);
                newDagger.GetComponent<DaggerProjectile>().SetRange(attackRange * stats.range);
                newDagger.GetComponent<DaggerProjectile>().SetBounces(Mathf.FloorToInt(bounces + stats.bounces));
                newDagger.GetComponent<DaggerProjectile>().SetSpeed(projectileSpeed * stats.speed);
                newDagger.GetComponent<DaggerProjectile>().SetTarget(target);
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
            EnemyController foundEnemy = enemy.GetComponent<EnemyController>();
            if (foundEnemy != null)
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
