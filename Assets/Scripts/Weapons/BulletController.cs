using UnityEngine;

public class BulletController : MonoBehaviour
{
    private EnemyController target;
    public float speed;
    public float damage;
    public bool shouldKnockback;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(target == null)
        {
            Destroy(gameObject);
            return;
        }
        transform.position = Vector2.MoveTowards(transform.position, target.transform.position, speed * Time.deltaTime);
    }

    public void SetTarget(EnemyController newTarget)
    {
        target = newTarget;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Enemy")
        {
            collision.GetComponent<EnemyController>().TakeDamage(damage, shouldKnockback);
            Destroy(gameObject);
        }
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }

    public void SetKnockback(bool knockback)
    {
        shouldKnockback = knockback;
    }
}
