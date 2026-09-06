using UnityEngine;

public class PhysicsBullet : MonoBehaviour {

    [HideInInspector] public float damage;
    [HideInInspector] public Vector2 dir; // Set by the shooter into a normalized vector pointing in the direction of the target
    [HideInInspector] public PlayerController player;
    [HideInInspector] public float muzzleVelocity;

    private Vector3 lastPosition;
    private LineRenderer tracer;

    void Start() {
        Physics2D.IgnoreCollision(GetComponent<Collider2D>(), player.GetComponent<Collider2D>(), true);
        Rigidbody2D rigidbody = GetComponent<Rigidbody2D>();
        rigidbody.gravityScale = 0.0F;
        rigidbody.AddForce(dir * muzzleVelocity, ForceMode2D.Impulse);
        tracer = GetComponent<LineRenderer>();
        lastPosition = transform.position;
        Destroy(gameObject, 5F); // 973 * 5 = 4865 meters, far exceeding the length of the entire map
    }

    void LateUpdate() {
        tracer.SetPosition(1, transform.position);

        Vector3 travelDirection = (lastPosition - transform.position).normalized;
        float currentDistance = Vector3.Distance(transform.position, lastPosition);

        // Cap the length so it doesn't stretch infinitely if the bullet spawns far away
        if (currentDistance > 5F) {
            tracer.SetPosition(0, transform.position + (travelDirection * 5F));
        } else {
            tracer.SetPosition(0, lastPosition);
        }

        // Cache the position for the next frame
        lastPosition = transform.position;
    }

    private void OnCollisionEnter2D(Collision2D collision) {
        EnemyController enemy = collision.collider.GetComponent<EnemyController>();
        if (enemy != null) {
            enemy.TakeDamage(damage, true);
            Destroy(gameObject);
        }
    }
}
