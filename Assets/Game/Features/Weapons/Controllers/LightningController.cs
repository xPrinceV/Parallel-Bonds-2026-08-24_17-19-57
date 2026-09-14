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
    private bool strikeSoundRequested;
    private readonly List<Collider2D> enemiesInRange = new List<Collider2D>(32);
    private readonly List<Collider2D> queryScratch = new List<Collider2D>();
    private readonly List<EnemyController> availableEnemies = new List<EnemyController>(32);
    private readonly HashSet<EnemyController> seenEnemies = new HashSet<EnemyController>();
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
            strikeSoundRequested = false;
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
                if (targetEnemy != null && World.CanInteract(this, targetEnemy))
                {
                    Instantiate(lightningPrefab, targetEnemy.transform.position, Quaternion.identity, World.GetContentRoot(this));
                    float healthBefore = targetEnemy.health;
                    targetEnemy.TakeDamage(strikeDamage);
                    // Bound audio to the first valid strike, even if playback is declined.
                    if (!strikeSoundRequested)
                    {
                        strikeSoundRequested = true;
                        AudioService.Instance?.Play(SoundId.LightningStrike);
                    }
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
        availableEnemies.Clear();
        seenEnemies.Clear();

        //Find all collider hitboxes within the radius of attackRange
        WeaponQuery.OverlapCircle(transform.position, attackRange * stats.range, enemiesInRange, queryScratch);

        foreach (Collider2D enemy in enemiesInRange)
        {
            //Store all enemy controller within the range of the weapon into a list
            EnemyController enemyController = enemy.GetComponent<EnemyController>();

            if (enemyController != null && enemyController.gameObject.activeInHierarchy
                && enemyController.health > 0f && World.CanInteract(this, enemyController)
                && seenEnemies.Add(enemyController))
            {
                availableEnemies.Add(enemyController);
            }
        }

        if(availableEnemies.Count == 0) return null;

        return availableEnemies[Random.Range(0, availableEnemies.Count)];
    }
}