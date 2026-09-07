using UnityEngine;

public class BulletController : MonoBehaviour
{
    private EnemyController target;
    private BuffController buffSource;
    private bool hasHit;
    public float speed;
    public float damage;
    public bool shouldKnockback;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //If the target gets lost, destroy the gameObject (Might change behaviour soon)
        if(target == null || !target.gameObject.activeInHierarchy || World.GetFor(this) != World.GetFor(target))
        {
            Destroy(gameObject);
            return;
        }

        //Make bullet move towards the target
        transform.position = Vector2.MoveTowards(transform.position, target.transform.position, speed * Time.deltaTime);
    }

    public void SetTarget(EnemyController newTarget)
    {
        target = newTarget != null && World.GetFor(this) == World.GetFor(newTarget) ? newTarget : null;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasHit || !isActiveAndEnabled || !collision.CompareTag("Enemy"))
            return;

        EnemyController enemy = collision.GetComponent<EnemyController>();
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health <= 0f
            || World.GetFor(this) != World.GetFor(enemy))
            return;

        // one projectile reports one confirmed hit, even with multiple colliders
        hasHit = true;
        try
        {
            float healthBefore = enemy.health;
            enemy.TakeDamage(damage, shouldKnockback);
            float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
            // Destroy is deferred; report lethal hits before the target leaves this frame
            if (buffSource != null && damageDealt > 0f)
                buffSource.ReportHit(enemy.gameObject, damageDealt);
        }
        finally
        {
            Destroy(gameObject);
        }
    }

    // keep the firing holder separate from the projectile object
    public void SetBuffSource(BuffController source)
    {
        buffSource = source;
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }

    public void SetKnockback(bool knockback)
    {
        shouldKnockback = knockback;
    }
}
