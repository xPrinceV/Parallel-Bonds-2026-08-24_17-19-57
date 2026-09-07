using UnityEngine;
using System.Collections.Generic;
public class LanternFire : MonoBehaviour
{
    public float damage;
    //Burning list will be used to store enemies that are "burning"
    public List<EnemyController> burningList = new List<EnemyController>();
    private BuffController buffSource;
    private readonly Dictionary<EnemyController, HashSet<Collider2D>> burningColliders =
        new Dictionary<EnemyController, HashSet<Collider2D>>();
    //How frequent in seconds, the enemy will take damage from the fire
    public float tickRate = 0.5f;
    public float tickCounter = 0;
    public float duration = 10f;
    public float durationCounter;
    void Awake()
    {
        if (burningList == null)
            burningList = new List<EnemyController>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        durationCounter = duration;
    }

    // Update is called once per frame
    void Update()
    {
        tickCounter -= Time.deltaTime;
        durationCounter -= Time.deltaTime;
        RemoveStaleEnemies();
        if (tickCounter <= 0 && burningList.Count > 0)
        {
            //Iterate through the list of burning enemies
            for (int i = burningList.Count - 1; i >= 0; i--)
            {
                EnemyController enemy = burningList[i];
                //If the enemy is null (they died), remove them from the list
                if (!IsValidEnemy(enemy))
                {
                    RemoveEnemyAt(i);
                    continue;
                }
                //Make the enemy take damage
                ApplyDamage(enemy);
            }
            //Set tick counter to the tick rate 
            tickCounter = tickRate;
            RemoveStaleEnemies();
        }

        if (durationCounter <= 0)
        {
            Destroy(gameObject);
        }
    }

    //When the enemy collides with the fire, make them take damage and add them to the list of burning enemies
    void OnTriggerEnter2D(Collider2D collision)
    {

        if (collision == null || !collision.CompareTag("Enemy"))
            return;

        RemoveStaleEnemies();
        EnemyController enemy = collision.GetComponentInParent<EnemyController>();
        if (!IsValidEnemy(enemy))
            return;

        if (!burningColliders.TryGetValue(enemy, out HashSet<Collider2D> colliders))
        {
            colliders = new HashSet<Collider2D>();
            burningColliders.Add(enemy, colliders);
        }

        // Only the first overlapping collider applies entry damage.
        if (!colliders.Add(collision) || colliders.Count > 1)
            return;

        if (!burningList.Contains(enemy))
            burningList.Add(enemy);
        ApplyDamage(enemy);
        RemoveStaleEnemies();
    }

    //When the enemy exits the collision box of the fire, get the enemy object and remove them from the burning list
    void OnTriggerExit2D(Collider2D collision)
    {
        // Use the stored collider even if its tag or parent changed.
        foreach (HashSet<Collider2D> colliders in burningColliders.Values)
            colliders.Remove(collision);
        RemoveStaleEnemies();
    }
    private static bool IsValidEnemy(EnemyController enemy)
    {
        return enemy != null && enemy.gameObject.activeInHierarchy && enemy.health > 0f;
    }

    private void ApplyDamage(EnemyController enemy)
    {
        if (!IsValidEnemy(enemy))
            return;

        float healthBefore = enemy.health;
        enemy.TakeDamage(damage);
        float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
        // Report lethal hits before deferred destruction.
        if (buffSource != null && damageDealt > 0f)
            buffSource.ReportHit(enemy.gameObject, damageDealt);
    }

    private void RemoveStaleEnemies()
    {
        for (int i = burningList.Count - 1; i >= 0; i--)
        {
            EnemyController enemy = burningList[i];
            if (!IsValidEnemy(enemy) || !burningColliders.TryGetValue(enemy, out HashSet<Collider2D> colliders))
            {
                RemoveEnemyAt(i);
                continue;
            }

            // Disabled or destroyed colliders may never send an exit.
            colliders.RemoveWhere(collider => collider == null || !collider.enabled ||
                !collider.gameObject.activeInHierarchy || collider.GetComponentInParent<EnemyController>() != enemy);
            if (colliders.Count == 0)
                RemoveEnemyAt(i);
        }
    }

    private void RemoveEnemyAt(int index)
    {
        EnemyController enemy = burningList[index];
        // Destroyed Unity objects still serve as dictionary keys.
        if (!ReferenceEquals(enemy, null))
            burningColliders.Remove(enemy);
        burningList.RemoveAt(index);
    }

    public void SetBuffSource(BuffController source)
    {
        buffSource = source;
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage / 2;
    }

    public void SetDuration(float newDuration)
    {
        duration = newDuration;
    }
}
