using UnityEngine;

public class SniperController : Weapon {
    public float attackSpeed;
    public float attackDamage;
    public float attackRange;
    public GameObject bullet;

    private float attackCounter = 0;

    void Update() {
        if (attackCounter <= 0) {
            //Call the FindClosestEnemy to determine the closest enemy, and assigned that gameObject to target
            EnemyController target = FindClosestEnemy();
            if (target != null) {
                GameObject newBullet = Instantiate(bullet, transform.position, transform.rotation);
                PhysicsBullet bulletHandle = newBullet.GetComponent<PhysicsBullet>();
                bulletHandle.dir = (target.transform.position - transform.position).normalized;
                bulletHandle.damage = attackDamage * stats.damage;
                bulletHandle.player = player;
                bulletHandle.muzzleVelocity = 973F;
                attackCounter = 1F / (attackSpeed * stats.attackSpeed);
            }
        } else {
            attackCounter -= Time.deltaTime;
        }
    }

    private EnemyController FindClosestEnemy() {
        //Nearest enemy = null, closest distance = infinity
        EnemyController nearestEnemy = null;
        float closestDistance = Mathf.Infinity;

        //Find all collider hitboxes within the radius of attackRange
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, attackRange * stats.range);

        foreach (Collider2D enemy in enemiesInRange) {
            EnemyController foundEnemy = enemy.GetComponent<EnemyController>();
            if (foundEnemy != null) {
                //Get the distance from that particular enemy
                float distance = Vector3.Distance(transform.position, foundEnemy.transform.position);

                //If the distance from that enemy, is lower than the closest distance
                if (distance < closestDistance) {
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
