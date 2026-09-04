using UnityEngine;

public class ArrowController : MonoBehaviour
{
    public float projectileSpeed;
    public float damage;
    private Vector2 direction;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //Destroy the arrow after 6 seconds
        Destroy(gameObject, 6f);
    }

    // Update is called once per frame
    void Update()
    {
        //Move the arrow every frame in the direction given from bow controller
        transform.position += (Vector3)direction * projectileSpeed * Time.deltaTime;
    }

    //Deal damage to the enemy when the arrow collides with it
    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Enemy")
        {
            collision.GetComponent<EnemyController>().TakeDamage(damage);
        }
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
