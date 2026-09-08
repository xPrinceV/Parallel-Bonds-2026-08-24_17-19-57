using UnityEngine;

public class TitanEnemyController : EnemyController
{
    public float range;
    private float distance;
    private bool startAttack;
    private Vector3 attackLocation;

    public float attackChannelTime = 0.5f;
    public float attackCooldown = 2f;
    private float cooldownCounter = 0;
    private float attackChannelCounter;    
    public GameObject titanAttack;


    // Update is called once per frame
    void Update()
    {
        cooldownCounter -= Time.deltaTime;
        
        // Calculate distance from target and the monster
        distance = Vector3.Distance(transform.position, target.position);

        // If within attack range, start attack
        if (!startAttack && distance <= range && cooldownCounter <= 0)
        {
            startAttack = true;
            attackChannelCounter = attackChannelTime;
        }

        // Attack
        if (startAttack)
        {
            // Stop moving
            RB.linearVelocity = Vector2.zero;

            // Countdown channel time
            attackChannelCounter -= Time.deltaTime;

            if (attackChannelCounter <= 0)
            {
                startAttack = false;
                ChannelAttack();
                cooldownCounter = attackCooldown;
            }
        }
        // Move toward player
        else
        {
            RB.linearVelocity = (target.position - transform.position).normalized * moveSpeed;
        }

        UpdatePoison();
    }

    public void ChannelAttack()
    {
        attackLocation = target.position;
        GameObject newAttack = Instantiate(titanAttack, target.position, Quaternion.identity);
        newAttack.GetComponent<TitanAttack>().SetDamage(attack);
    }
}
