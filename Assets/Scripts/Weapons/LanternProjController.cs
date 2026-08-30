using UnityEngine;

public class LanternProjController : MonoBehaviour
{
    public Rigidbody2D RB;
    public float lifetime = 3f;
    public float minForce = 5f;
    public float maxForce = 10f;
    public float vertForce = 5f;
    public float damage = 10f;
    public float duration;
    public GameObject explosion;
    public GameObject fire;
    void Start()
    {
        //50% chance to choose -1 or 1 (throw left, throw right)
        float throwDirection = Random.value < 0.5f ? -1f: 1f;
        float throwForce = Random.Range(minForce, maxForce);

        Vector2 force = new Vector2(throwForce*throwDirection, vertForce);
        RB.AddForce(force, ForceMode2D.Impulse);
        //Destroy Itself after 10 seconds
        Destroy(gameObject, 10f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Enemy")
        {
            collision.GetComponent<EnemyController>().TakeDamage(damage);
            GameObject newFire = Instantiate(fire, transform.position, Quaternion.identity);
            newFire.GetComponent<LanternFire>().SetDamage(damage);
            newFire.GetComponent<LanternFire>().SetDuration(duration);
            Instantiate(explosion, transform.position, Quaternion.identity);
            Destroy(gameObject);
        }
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetDuration(float newDuration)
    {
        duration = newDuration;
    }
}
