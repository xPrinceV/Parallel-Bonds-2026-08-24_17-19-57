using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public Rigidbody2D RB;
    public float moveSpeed;
    protected Transform target;

    public float attack;
    public float health;
    
    public float hitWaitTime = 1f;
    public float hitCounter;
    public float knockbackTime = .25f;
    private float knockbackCounter;

    public int expDrop = 1;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //Sets target to the transform location of the player
        target = PlayerHealth.instance.transform;
    }

    // Update is called once per frame
    void Update()
    {
        /**
        Handle knockback. Knockback Time is a public field where you can customise the knockback time.
        .25s would mean enemy would be knockback for that duration. Knockback counter will countdown and
        during the duration of 0.25s, the enemy movement speed will be inversed, making them move backwards
        */
        if(knockbackCounter > 0)
        {
            knockbackCounter -= Time.deltaTime;
            if(moveSpeed > 0)
            {
                //This sets the force of knockback to be 2x the mobs moveSpeed
                moveSpeed = -moveSpeed * 2f;
            }

            if(knockbackCounter <= 0)
            {
                moveSpeed = Mathf.Abs(moveSpeed * .5f);
            }
        }
        //Sets the Rigidbody velocity to be moving towards the player
        RB.linearVelocity = (target.position - transform.position).normalized * moveSpeed;

        if(hitCounter > 0f)
        {
            hitCounter -= Time.deltaTime;
        }
    }

    //Method to detect collision
    private void OnCollisionEnter2D(Collision2D collision)
    {
        //Check if collision is done with a player
        if(collision.gameObject.tag == "Player" && hitCounter <= 0f)
        {
            PlayerHealth.instance.DamageHandler(attack);
            //A cooldown for the player taking damage
            hitCounter = hitWaitTime;
        }
    }

    public void TakeDamage(float damageTaken)
    {
        health -= damageTaken;
        //When the enemy dies
        if(health <= 0)
        {
            Destroy(gameObject);

            //Spawn Exp Orb
            ExperienceLevelController.instance.SpawnExp(transform.position, expDrop);
        }

        DamageNumberController.instance.SpawnDamage(damageTaken, transform.position);
    }

    public void TakeDamage(float damageTaken, bool Knockback)
    {
        TakeDamage(damageTaken);
        if(Knockback)
        {
            knockbackCounter = knockbackTime;
        }
    }

}
