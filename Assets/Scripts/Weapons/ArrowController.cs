using UnityEngine;

public class ArrowController : MonoBehaviour {
    [HideInInspector] public float projectileSpeed;
    [HideInInspector] public float damage;
    [HideInInspector] private Vector2 direction;
    [HideInInspector] public Collider2D playerCollider;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        Physics2D.IgnoreCollision(GetComponent<Collider2D>(), playerCollider, true);
        Rigidbody2D rigidbody = GetComponent<Rigidbody2D>();
        rigidbody.gravityScale = 0F;
        rigidbody.AddForce(direction * projectileSpeed, ForceMode2D.Impulse);
        //Destroy the arrow after 6 seconds
        Destroy(gameObject, 6f);
    }

    // Update is called once per frame
    void Update() {
        //Move the arrow every frame in the direction given from bow controller
     // transform.position += (Vector3)direction * projectileSpeed * Time.deltaTime;
    }

    //Deal damage to the enemy when the arrow collides with it
    private void OnCollisionEnter2D(Collision2D collision) {
        EnemyController enemy = collision.collider.GetComponent<EnemyController>();
        if (enemy != null) {
            enemy.TakeDamage(damage);
            Physics2D.IgnoreCollision(GetComponent<Collider2D>(), collision.collider, true);
        }
    }
    
    //functions to set the direction, damage, and speed of the arrow when it is created
    public void SetDirection(Vector2 newDirection) {
        direction = newDirection;
        transform.right = direction;
    }
}
