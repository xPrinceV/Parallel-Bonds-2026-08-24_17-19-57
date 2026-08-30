using UnityEngine;

public class HollowProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float damage;
    private float speed;
    public float channelTime = 1f;
    private float channelCountdown;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        channelCountdown = channelTime;
        Destroy(gameObject, 10f);
    }

    // Update is called once per frame
    void Update()
    {
        channelCountdown -= Time.deltaTime;
        if(channelCountdown <= 0)
        {
            transform.position += direction * speed * Time.deltaTime;
        }
        
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            collision.GetComponent<PlayerHealth>().DamageHandler(damage);
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
