using UnityEngine;

public class AuraItemController : MonoBehaviour {

    [HideInInspector] public float damage;
    [HideInInspector] public float speed;
    [HideInInspector] public PlayerController player;

    private Vector3 off;

    void Start() {
        off = transform.position - player.transform.position;
        Physics2D.IgnoreCollision(GetComponent<Collider2D>(), player.GetComponent<Collider2D>(), true);
        Rigidbody2D rigidbody = GetComponent<Rigidbody2D>();
        rigidbody.gravityScale = 0F;
    }

    void LateUpdate() {
        Quaternion rotation = Quaternion.AngleAxis(speed * Time.deltaTime, Vector3.forward);
        off = rotation * off;

        transform.position = player.transform.position + off;
    }

    private void OnCollisionEnter2D(Collision2D collision) {
        EnemyController enemy = collision.collider.GetComponent<EnemyController>();
        if (enemy != null) {
            enemy.TakeDamage(damage);
        }
    }
}
