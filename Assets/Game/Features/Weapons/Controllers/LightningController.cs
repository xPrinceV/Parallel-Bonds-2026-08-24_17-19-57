using UnityEngine;
using System.Collections.Generic;

public class LightningController : Weapon
{
    public float attackDamage;
    public float attackSpeed;
    public float attackRange;
    public float amount;
    public GameObject lightningPrefab;
    [SerializeField] private BuffController buffHolder;
    private float strikeDamage;
    private float attackCounter;
    private float strikeCounter;
    private float strikeInterval;
    private float strikes;
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
        if (attackCounter <= 0)
        {
            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
            // snapshot the burst so its hits only change later bursts
            strikeDamage = attackDamage * stats.damage;
            int count = Mathf.Max(0, Mathf.FloorToInt(amount * stats.amount));
            if (buffHolder != null)
            {
                strikeDamage = buffHolder.CalculateWeaponDamage(strikeDamage);
                count = buffHolder.CalculateProjectileCount(count);
            }
            strikes = count;
            float strikeDuration = attackCounter * 0.5f;

            if (strikes > 1)
            {
                strikeInterval = strikeDuration / (strikes - 1);
            }
            else
            {
                strikeInterval = 0;
            }
            strikeCounter = 0f;
        }
        if (strikes > 0)
        {
            strikeCounter -= Time.deltaTime;
            if(strikeCounter <= 0)
            {
                EnemyController targetEnemy = FindRandomEnemy();
                if (targetEnemy != null && World.GetFor(this) == World.GetFor(targetEnemy))
                {
                    Instantiate(lightningPrefab, targetEnemy.transform.position, Quaternion.identity, World.GetContentRoot(this));
                    float healthBefore = targetEnemy.health;
                    targetEnemy.TakeDamage(strikeDamage);
                    // report actual loss before deferred destruction, including lethal strikes
                    float damageDealt = Mathf.Clamp(healthBefore - targetEnemy.health, 0f, healthBefore);
                    if (buffHolder != null && damageDealt > 0f)
                        buffHolder.ReportHit(targetEnemy.gameObject, damageDealt);
                }
                strikes--;
                strikeCounter = strikeInterval;
            }
        }

    }

    private EnemyController FindRandomEnemy()
    {
        List<EnemyController> availableEnemies = new List<EnemyController>();

        //Find all collider hitboxes within the radius of attackRange
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, attackRange * stats.range);

        foreach (Collider2D enemy in enemiesInRange)
        {
            //Store all enemy controller within the range of the weapon into a list
            EnemyController enemyController = enemy.GetComponent<EnemyController>();

            if (enemyController != null && enemyController.gameObject.activeInHierarchy
                && enemyController.health > 0f && World.GetFor(this) == World.GetFor(enemyController)
                && !availableEnemies.Contains(enemyController))
            {
                availableEnemies.Add(enemyController);
            }
        }

        if(availableEnemies.Count == 0) return null;

        return availableEnemies[Random.Range(0, availableEnemies.Count)];
    }
}