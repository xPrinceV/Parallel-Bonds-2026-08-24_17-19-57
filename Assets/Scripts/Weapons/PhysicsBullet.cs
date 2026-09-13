using UnityEngine;

public class PhysicsBullet : MonoBehaviour {

    [HideInInspector] public float damage;
    [HideInInspector] public Vector2 dir; // Set by the shooter into a normalized vector pointing in the direction of the target
    [HideInInspector] public PlayerController player;
    [HideInInspector] public float muzzleVelocity;

    void Start() {
        Physics2D.IgnoreCollision(GetComponent<Collider2D>(), player.GetComponent<Collider2D>(), true);
        Rigidbody2D rigidbody = GetComponent<Rigidbody2D>();
        rigidbody.gravityScale = 0.0F;
        rigidbody.AddForce(dir * muzzleVelocity, ForceMode2D.Impulse);
        Destroy(gameObject, 5F); // 973 * 5 = 4865 meters, far exceeding the length of the entire map
    }

    private void OnCollisionEnter2D(Collision2D collision) {
        EnemyController enemy = collision.collider.GetComponent<EnemyController>();
        if (enemy != null) {
            enemy.TakeDamage(damage, true);
            Destroy(gameObject);
        }
    }
}
