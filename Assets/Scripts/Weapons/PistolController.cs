using UnityEngine;

public class PistolController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float attackRange;
    [SerializeField] private float projectileSpeed;
    [SerializeField] private GameObject bullet;


    private float attackCounter;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        attackCounter = 0;
    }

    // Update is called once per frame
    void Update()
    {
        //Attack Timer
        attackCounter -= Time.deltaTime;

        //When the attack counter is 0, it means its ready to attack
        if (attackCounter <= 0)
        {
            //Call the FindClosestEnemy to determine the closest enemy, and assigned that gameObject to target
            EnemyController target = FindClosestEnemy();
            if (target != null)
            {
                GameObject newBullet = Instantiate(bullet, transform.position, transform.rotation);
                newBullet.GetComponent<BulletController>().SetTarget(target);
                newBullet.GetComponent<BulletController>().SetDamage(attackDamage * stats.damage);
                newBullet.GetComponent<BulletController>().SetSpeed(projectileSpeed * stats.speed);
                newBullet.GetComponent<BulletController>().SetKnockback(true);
            }
            attackCounter = 1f / (attackSpeed*stats.attackSpeed);
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
