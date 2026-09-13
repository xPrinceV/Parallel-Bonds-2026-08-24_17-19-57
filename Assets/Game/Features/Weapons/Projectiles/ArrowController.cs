using UnityEngine;
using System.Collections.Generic;

public class ArrowController : MonoBehaviour, IWorldProjectile
{
    private bool hasDespawned;

    void OnEnable()
    {
        hasDespawned = false;
    }

    public void Despawn()
    {
        if (hasDespawned)
            return;

        hasDespawned = true;
        gameObject.SetActive(false);
        // No projectile pool yet; replace destruction here with pool return when available.
        Destroy(gameObject);
    }
    public float projectileSpeed;
    public float damage;
    private Vector2 direction;
    private BuffController buffSource;
    private float lifetimeRemaining = 6f;
    private readonly HashSet<EnemyController> hitEnemies = new HashSet<EnemyController>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //Destroy the arrow after 6 seconds
        // Count only active time so world sleep preserves the projectile.
        lifetimeRemaining = 6f;
    }

    // Update is called once per frame
    void Update()
    {
        lifetimeRemaining -= Time.deltaTime;
        if (lifetimeRemaining <= 0f)
        {
            Despawn();
            return;
        }

        //Move the arrow every frame in the direction given from bow controller
        transform.position += (Vector3)direction * projectileSpeed * Time.deltaTime;
    }

    //Deal damage to the enemy when the arrow collides with it
    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!isActiveAndEnabled || lifetimeRemaining <= 0f || !collision.CompareTag("Enemy"))
            return;

        EnemyController enemy = collision.GetComponentInParent<EnemyController>();
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health <= 0f
            || !World.CanInteract(this, enemy) || !hitEnemies.Add(enemy))
            return;

        // Pierce distinct enemies, but do not count multiple colliders as extra hits.
        float healthBefore = enemy.health;
        enemy.TakeDamage(damage);
        float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
        if (buffSource != null && damageDealt > 0f)
            buffSource.ReportHit(enemy.gameObject, damageDealt);
    }
    
    public void SetBuffSource(BuffController source)
    {
        buffSource = source;
    }

    //functions to set the direction, damage, and speed of the arrow when it is created
    public void SetDirection(Vector2 newDirection)
    {
        direction = newDirection;
        transform.right = direction;
    }
    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }
    public void SetSpeed(float newSpeed)
    {
        projectileSpeed = newSpeed;
    }
}
