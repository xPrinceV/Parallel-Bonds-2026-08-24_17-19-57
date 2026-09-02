using UnityEngine;
using System.Collections.Generic;

public class LightningController : Weapon
{
    public float attackDamage;
    public float attackSpeed;
    public float attackRange;
    public float amount;
    public GameObject lightningPrefab;
    private float attackCounter;
    private float strikeCounter;
    private float strikeInterval;
    private float strikes;
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
        if (attackCounter <= 0)
        {
            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
            strikes = Mathf.FloorToInt(amount * stats.amount);
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
                if (targetEnemy != null)
                {
                    Instantiate(lightningPrefab, targetEnemy.transform.position, Quaternion.identity);
                    targetEnemy.TakeDamage(attackDamage * stats.damage);
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

            if (enemyController != null)
            {
                availableEnemies.Add(enemyController);
            }
        }

        if(availableEnemies.Count == 0) return null;

        return availableEnemies[Random.Range(0, availableEnemies.Count)];
    }
}