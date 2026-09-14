using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PhysicsBullet : MonoBehaviour, IWorldProjectile
{
    [SerializeField, Min(0f)] private float lifetime = 5f;

    private Rigidbody2D body;
    private Collider2D hitbox;
    private Vector2 direction;
    private float damage;
    private float speed;
    private float lifetimeRemaining;
    private BuffController buffSource;
    private bool initialized;
    private bool hasDespawned;
    private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(32);

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        hitbox = GetComponent<Collider2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        hitbox.isTrigger = true;
    }

    // Call on every spawn/pool checkout, not on world wake (which must preserve flight state).
    public void Initialize(Vector2 newDirection, float newDamage, float newSpeed, BuffController source)
    {
        direction = newDirection.sqrMagnitude > 0f ? newDirection.normalized : Vector2.right;
        damage = newDamage;
        speed = Mathf.Max(0f, newSpeed);
        buffSource = source;
        lifetimeRemaining = lifetime;
        hasDespawned = false;
        initialized = true;
        body.rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
    }

    void FixedUpdate()
    {
        if (!initialized || hasDespawned)
            return;
        if (lifetimeRemaining <= 0f)
        {
            Despawn();
            return;
        }

        float step = Mathf.Min(Time.fixedDeltaTime, lifetimeRemaining);
        float distance = speed * step;
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(Physics2D.DefaultRaycastLayers);
        hitbox.Cast(direction, filter, hits, distance);

        EnemyController nearestEnemy = null;
        float nearestDistance = Mathf.Infinity;
        foreach (RaycastHit2D hit in hits)
        {
            EnemyController enemy = GetTarget(hit.collider);
            if (enemy != null && hit.distance < nearestDistance)
            {
                nearestEnemy = enemy;
                nearestDistance = hit.distance;
            }
        }

        if (nearestEnemy != null)
        {
            body.position += direction * nearestDistance;
            HitEnemy(nearestEnemy);
            return;
        }

        // Sweep first, then place the kinematic body. MovePosition/impulses are capped by
        // Physics2D's 100-unit/s translation limit, below the sniper's 973-unit/s speed.
        body.position += direction * distance;
        lifetimeRemaining -= step;
        if (lifetimeRemaining <= 0f)
            Despawn();
    }

    private EnemyController GetTarget(Collider2D collision)
    {
        if (collision == null || !World.CanInteract(this, collision))
            return null;
        EnemyController enemy = collision.GetComponentInParent<EnemyController>();
        return enemy != null && enemy.isActiveAndEnabled && enemy.health > 0f
            && World.CanInteract(this, enemy) ? enemy : null;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!initialized || hasDespawned || !isActiveAndEnabled || lifetimeRemaining <= 0f)
            return;
        EnemyController enemy = GetTarget(collision);
        if (enemy != null)
            HitEnemy(enemy);
    }

    private void HitEnemy(EnemyController enemy)
    {
        // Block reentrant/multi-collider hits before damage can dispatch buff events.
        initialized = false;
        try
        {
            float healthBefore = enemy.health;
            enemy.TakeDamage(damage, true);
            float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
            // Swept and trigger impacts share this audio origin.
            if (damageDealt > 0f && !float.IsNaN(damageDealt) && !float.IsInfinity(damageDealt))
                AudioService.Instance?.Play(SoundId.ProjectileHit);
            if (buffSource != null && damageDealt > 0f)
                buffSource.ReportHit(enemy.gameObject, damageDealt);
        }
        finally
        {
            Despawn();
        }
    }

    public void Despawn()
    {
        if (hasDespawned)
            return;
        hasDespawned = true;
        initialized = false;
        gameObject.SetActive(false);
        // No projectile pool yet; replace destruction here with pool return when available.
        Destroy(gameObject);
    }
}
