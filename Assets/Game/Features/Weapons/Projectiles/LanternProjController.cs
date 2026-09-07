using UnityEngine;

public class LanternProjController : MonoBehaviour
{
    public Rigidbody2D RB;
    public float lifetime = 3f;
    public float minForce = 5f;
    public float maxForce = 10f;
    public float vertForce = 5f;
    public float damage = 10f;
    public float duration;
    public GameObject explosion;
    public GameObject fire;
    private BuffController buffSource;
    private bool hasHit;
    private float lifetimeRemaining = 10f;
    void Start()
    {
        //50% chance to choose -1 or 1 (throw left, throw right)
        float throwDirection = Random.value < 0.5f ? -1f: 1f;

        //Use random range to add variance to the force the lantern is thrown with
        float throwForce = Random.Range(minForce, maxForce);

        //Add the force to the projectile instantly (Impulse)
        Vector2 force = new Vector2(throwForce*throwDirection, vertForce);
        RB.AddForce(force, ForceMode2D.Impulse);

        //Destroy Itself after 10 seconds
        // Preserve the existing lifetime, but count only awake time.
        lifetimeRemaining = 10f;
    }

    void Update()
    {
        lifetimeRemaining -= Time.deltaTime;
        if (lifetimeRemaining <= 0f)
            Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasHit || !isActiveAndEnabled || collision == null || !collision.CompareTag("Enemy"))
            return;

        EnemyController enemy = collision.GetComponentInParent<EnemyController>();
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemy.health <= 0f
            || World.GetFor(this) != World.GetFor(enemy))
            return;

        // Consume the impact before other colliders can trigger it.
        hasHit = true;
        try
        {
            float healthBefore = enemy.health;
            enemy.TakeDamage(damage);
            float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
            // Report lethal hits before deferred destruction.
            if (buffSource != null && damageDealt > 0f)
                buffSource.ReportHit(enemy.gameObject, damageDealt);

            GameObject newFire = Instantiate(fire, transform.position, Quaternion.identity, World.GetContentRoot(this));
            LanternFire lanternFire = newFire.GetComponent<LanternFire>();
            lanternFire.SetDamage(damage);
            lanternFire.SetDuration(duration);
            lanternFire.SetBuffSource(buffSource);
            Instantiate(explosion, transform.position, Quaternion.identity, World.GetContentRoot(this));
        }
        finally
        {
            Destroy(gameObject);
        }
    }

    public void SetBuffSource(BuffController source)
    {
        buffSource = source;
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetDuration(float newDuration)
    {
        duration = newDuration;
    }
}
