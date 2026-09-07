using UnityEngine;

public class PistolController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float attackRange;
    [SerializeField] private float projectileSpeed;
    [SerializeField] private GameObject bullet;
    [SerializeField] private BuffController buffHolder;
    [SerializeField, Min(0f)] private float projectileSpacing = 0.15f;


    private float attackCounter;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
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

        //When the attack counter is 0, it means its ready to attack
        if (attackCounter <= 0)
        {
            //Call the FindClosestEnemy to determine the closest enemy, and assigned that gameObject to target
            EnemyController target = FindClosestEnemy();
            if (target != null)
            {
                // snapshot this attack so a hit only changes later volleys
                float damage = attackDamage * stats.damage;
                int count = Mathf.Max(0, Mathf.FloorToInt(stats.amount));
                if (buffHolder != null)
                {
                    damage = buffHolder.CalculateWeaponDamage(damage);
                    count = buffHolder.CalculateProjectileCount(count);
                }

                Vector3 direction = (target.transform.position - transform.position).normalized;
                Vector3 side = Vector3.Cross(direction, Vector3.forward);
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = side * ((i - (count - 1) * 0.5f) * projectileSpacing);
                    GameObject newBullet = Instantiate(bullet, transform.position + offset, transform.rotation);
                    BulletController projectile = newBullet.GetComponent<BulletController>();
                    projectile.SetTarget(target);
                    projectile.SetDamage(damage);
                    projectile.SetSpeed(projectileSpeed * stats.speed);
                    projectile.SetKnockback(true);
                    projectile.SetBuffSource(buffHolder);
                }
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
