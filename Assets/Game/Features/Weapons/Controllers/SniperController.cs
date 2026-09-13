using System.Collections.Generic;
using UnityEngine;

public class SniperController : Weapon
{
    public float attackSpeed;
    public float attackDamage;
    public float attackRange;
    public GameObject bullet;
    [Min(0f)] public float projectileSpeed = 973f;
    [SerializeField] private BuffController buffHolder;
    [SerializeField, Min(0f)] private float projectileSpacing = 0.15f;

    private float attackCounter;
    private readonly List<Collider2D> enemiesInRange = new List<Collider2D>(32);
    private readonly List<Collider2D> queryScratch = new List<Collider2D>();

    void Start()
    {
        ResolveOwner();
        if (buffHolder == null && player != null)
            buffHolder = player.GetComponent<BuffController>();
        if (buffHolder == null)
            buffHolder = GetComponentInParent<BuffController>();
        if (bullet == null || bullet.GetComponent<PhysicsBullet>() == null)
        {
            Debug.LogError("Sniper requires a prefab with PhysicsBullet on its root.", this);
            enabled = false;
        }
    }

    void Update()
    {
        if (player == null || !player.isActiveAndEnabled || !World.CanInteract(this, player))
            return;

        attackCounter -= Time.deltaTime;
        if (attackCounter > 0f)
            return;

        EnemyController target = FindClosestEnemy();
        if (target == null)
            return;

        // Snapshot the whole volley, matching the local pistol's count/buff rules.
        float damage = attackDamage * stats.damage;
        int count = Mathf.Max(0, Mathf.FloorToInt(stats.amount));
        if (buffHolder != null)
        {
            damage = buffHolder.CalculateWeaponDamage(damage);
            count = buffHolder.CalculateProjectileCount(count);
        }

        Vector2 direction = (target.transform.position - transform.position).normalized;
        if (direction.sqrMagnitude == 0f)
            direction = Vector2.right;
        Vector3 side = Vector3.Cross(direction, Vector3.forward);
        float speed = projectileSpeed * stats.speed;
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = side * ((i - (count - 1) * 0.5f) * projectileSpacing);
            GameObject newBullet = Instantiate(bullet, transform.position + offset,
                Quaternion.identity, World.GetContentRoot(this));
            newBullet.GetComponent<PhysicsBullet>().Initialize(direction, damage, speed, buffHolder);
        }
        attackCounter = 1f / (attackSpeed * stats.attackSpeed);
    }

    private EnemyController FindClosestEnemy()
    {
        EnemyController nearestEnemy = null;
        float closestDistance = Mathf.Infinity;
        WeaponQuery.OverlapCircle(transform.position, attackRange * stats.range, enemiesInRange, queryScratch);
        foreach (Collider2D collider in enemiesInRange)
        {
            EnemyController enemy = collider.GetComponentInParent<EnemyController>();
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.health <= 0f
                || !World.CanInteract(this, collider) || !World.CanInteract(this, enemy))
                continue;

            float distance = (enemy.transform.position - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                nearestEnemy = enemy;
            }
        }
        return nearestEnemy;
    }
}
