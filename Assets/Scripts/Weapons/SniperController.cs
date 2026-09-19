using UnityEngine;

public class SniperController : Weapon
{
    public float attackSpeed;
    public float attackDamage;
    public float attackRange;
    public float projectileSpeed;
    public GameObject bullet;

    private float attackCounter = 0;

    void Update()
    {
        if (attackCounter <= 0)
        {
            //Find the closest enemy within attack range
            EnemyController target = FindClosestEnemy();

            if (target != null)
            {
                //Get the direction from the player towards the target
                Vector2 direction =
                    (target.transform.position - transform.position).normalized;

                //Create a new sniper projectile
                GameObject newBullet = Instantiate(
                    bullet,
                    transform.position,
                    Quaternion.identity
                );

                //Get the projectile script and set its stats
                SniperProjectile bulletHandle = newBullet.GetComponent<SniperProjectile>();

                bulletHandle.damage = attackDamage * stats.damage;
                bulletHandle.projectileSpeed = projectileSpeed * stats.speed;

                //Set the projectile direction towards the target
                bulletHandle.SetDirection(direction);

                //Play sniper sound effect
                AudioManager.instance.PlaySFXPitch(
                    AudioManager.instance.sniper,
                    0.3f
                );

                //Reset attack cooldown
                attackCounter =
                    1f / (attackSpeed * stats.attackSpeed);
            }
        }
        else
        {
            //Count down attack cooldown
            attackCounter -= Time.deltaTime;
        }
    }


    private EnemyController FindClosestEnemy()
    {
        //Nearest enemy starts as null, closest distance starts as infinity
        EnemyController nearestEnemy = null;
        float closestDistance = Mathf.Infinity;

        //Find all enemy colliders within attack range
        Collider2D[] enemiesInRange =
            Physics2D.OverlapCircleAll(
                transform.position,
                attackRange * stats.range
            );

        foreach (Collider2D enemy in enemiesInRange)
        {
            EnemyController foundEnemy =
                enemy.GetComponent<EnemyController>();

            if (foundEnemy != null)
            {
                //Get distance between player and this enemy
                float distance = Vector3.Distance(
                    transform.position,
                    foundEnemy.transform.position
                );

                //If this enemy is closer, set it as the new target
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    nearestEnemy = foundEnemy;
                }
            }
        }

        return nearestEnemy;
    }
}
