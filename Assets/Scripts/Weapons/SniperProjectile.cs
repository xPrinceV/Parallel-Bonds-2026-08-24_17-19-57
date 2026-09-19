using UnityEngine;

public class SniperProjectile : MonoBehaviour
{
    public float damage;
    public float projectileSpeed;

    private Vector2 direction;


    //Set the direction the projectile will travel
    public void SetDirection(Vector2 newDirection)
    {
        direction = newDirection;

        //Get the angle of the direction so the sprite faces where it is travelling
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }


    void Update()
    {
        //Move projectile forward in the set direction
        transform.position += (Vector3)direction * projectileSpeed * Time.deltaTime;
    }


    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.CompareTag("Enemy"))
        {
            EnemyController enemy =
                collision.GetComponent<EnemyController>();

            if(enemy != null)
            {
                //Deal damage to enemy and knockback
                enemy.TakeDamage(damage,true);
            }
        }
    }
}