using UnityEngine;

//Called when the Hollow enemy uses their projectile
public class HollowProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float damage;
    private float speed;
    public float channelTime = 1f;
    private float channelCountdown;
    private float lifetimeRemaining = 10f;
    private bool hasHit;

    void Start()
    {
        channelCountdown = channelTime;
        //Destroy the projectile after 10 seconds
        // Count only active time so sleeping worlds retain their projectiles.
        lifetimeRemaining = 10f;
    }

    // Update is called once per frame
    void Update()
    {
        lifetimeRemaining -= Time.deltaTime;
        if (lifetimeRemaining <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        channelCountdown -= Time.deltaTime;
        //Once the channel countdown has finished, make the projectile move in the direction of the player
        if(channelCountdown <= 0)
        {
            transform.position += direction * speed * Time.deltaTime;
        }
        
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            if (hasHit || !isActiveAndEnabled || World.GetFor(this) != World.GetFor(collision))
                return;

            PlayerHealth playerHealth = collision.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null)
                return;

            hasHit = true;
            playerHealth.DamageHandler(damage);
            Destroy(gameObject);
        }
    }

    public void SetTarget(Vector3 newTarget)
    {
        direction = (newTarget - transform.position).normalized;
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }
}
