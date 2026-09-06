using UnityEngine;

public class BulletController : MonoBehaviour {
    [HideInInspector] public EnemyController target;
    [HideInInspector] public float speed;
    [HideInInspector] public float damage;
    [HideInInspector] public bool shouldKnockback;
    [HideInInspector] public Collider2D playerCollider;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        Physics2D.IgnoreCollision(GetComponent<Collider2D>(), playerCollider, true);
        Rigidbody2D rigidbody = GetComponent<Rigidbody2D>();
        rigidbody.gravityScale = 0.0F;
        rigidbody.AddForce((target.transform.position - transform.position).normalized * speed, ForceMode2D.Impulse);
    }

    // Update is called once per frame
    void Update() {
        //If the target gets lost, destroy the gameObject (Might change behaviour soon)
        if (target == null) {
            Destroy(gameObject);
         // return;
        }

        //Make bullet move towards the target
     // transform.position = Vector2.MoveTowards(transform.position, target.transform.position, speed * Time.deltaTime);
    }

    private void OnCollisionEnter2D(Collision2D collision) {
        EnemyController enemy = collision.collider.GetComponent<EnemyController>();
        if (enemy != null) {
            enemy.TakeDamage(damage, shouldKnockback);
            Destroy(gameObject);
        }
    }
}
