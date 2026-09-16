using UnityEngine;

public class BossProjectiles : MonoBehaviour
{
    private Transform boss;
    private Transform target;

    private float damage;
    private float speed;

    private float orbitAngle;

    private float releaseDelay;

    public float orbitRadius = 1.5f;
    public float orbitSpeed = 120f;

    public float orbitTime = 2f;
    private float orbitCounter;

    public float trackingTime = 2f;
    private float trackingCounter;

    private Vector3 direction;

    private bool isOrbiting = true;
    private bool isTracking = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        orbitCounter = orbitTime + releaseDelay;

        //Orb is destroyed if in the scene for a long time
        Destroy(gameObject, 10f);
    }

    // Update is called once per frame
    void Update()
    {
        //Circles around the boss initially
        if (isOrbiting)
        {
            OrbitBoss();

            orbitCounter -= Time.deltaTime;

            if (orbitCounter <= 0)
            {
                isOrbiting = false;
                isTracking = true;
                trackingCounter = trackingTime;
            }
        }

        //Tracks player's movement for a short time 
        else if (isTracking)
        {
            trackingCounter -= Time.deltaTime;

            if (target != null)
            {
                direction = (target.position - transform.position).normalized;
            }

            transform.position += direction * Time.deltaTime * speed;

            //Tracking ends after the stated time
            if (trackingCounter <= 0)
            {
                isTracking = false;
            }
        }

        //The orb continues in the direction it was moving before
        else
        {
            transform.position += direction * Time.deltaTime * speed;
        }
    }

    private void OrbitBoss()
    {
        if (boss ==  null)
        {
            isOrbiting = false;
            isTracking = true;
            trackingCounter = trackingTime;
            return;
        }

        // Orb circles around the boss before attacking
        orbitAngle -= Time.deltaTime * orbitSpeed;
        float angleInRAD = orbitAngle * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Cos(angleInRAD),
            Mathf.Sin(angleInRAD),
            0f
        ) * orbitRadius;

        transform.position = boss.position + offset;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            collision.GetComponent<PlayerHealth>().DamageHandler(damage);
            Destroy(gameObject);
        }
    }

    public void Setup(
        Transform newBoss,
        Transform newTarget,
        float newDamage,
        float newSpeed,
        float startingAngle,
        float newReleaseDelay
    ) {
        boss = newBoss;
        target = newTarget;
        damage = newDamage;
        speed = newSpeed;
        orbitAngle = startingAngle;

        //Release time of each orb is different
        releaseDelay = newReleaseDelay;
    }
}
