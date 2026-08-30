using UnityEngine;

public class RangedEnemyController : EnemyController
{
    public float range;
    public float projectileSpeed;

    private float distance;
    private bool startAttack;
    private Vector3 attackLocation;

    public float attackChannelTime = 1f;
    private float attackChannelCounter;

    public GameObject projectile;


    // Update is called once per frame
    void Update()
    {
        // Calculate distance from target and the monster
        distance = Vector3.Distance(transform.position, target.position);

        // If within attack range, start attack
        if (!startAttack && distance <= range)
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
                ShootProjectile();
                startAttack = false;
            }
        }
        // Move toward player
        else
        {
            RB.linearVelocity =
                (target.position - transform.position).normalized * moveSpeed;
        }
    }


    public void ShootProjectile()
    {
        // Remember where the player is when the attack finishes
        attackLocation = target.position;

        GameObject newProjectile = Instantiate(
            projectile,
            transform.position + Vector3.up * 0.5f,
            Quaternion.identity
        );

        newProjectile.GetComponent<HollowProjectile>().SetDamage(attack);
        newProjectile.GetComponent<HollowProjectile>().SetTarget(attackLocation);
        newProjectile.GetComponent<HollowProjectile>().SetSpeed(projectileSpeed);
    }
}
