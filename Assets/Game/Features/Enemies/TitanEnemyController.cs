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
        if (!isActiveAndEnabled || IsDead)
            return;

        // This Update replaces the base loop, so poison must tick exactly once here.
        UpdatePoison();
        if (IsDead)
            return;

        if (!RefreshLiveTarget())
        {
            RB.linearVelocity = Vector2.zero;
            return;
        }

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
    }

    private bool RefreshLiveTarget()
    {
        RefreshTarget();
        PlayerHealth playerHealth = target != null ? target.GetComponent<PlayerHealth>() : null;
        return playerHealth != null && playerHealth.isActiveAndEnabled && !playerHealth.IsDead;
    }

    public void ChannelAttack()
    {
        if (!isActiveAndEnabled || IsDead || !RefreshLiveTarget())
            return;

        attackLocation = target.position;
        GameObject newAttack = Instantiate(
            titanAttack, attackLocation, Quaternion.identity, World.GetContentRoot(this));
        newAttack.GetComponent<TitanAttack>().SetDamage(attack);
    }
}
