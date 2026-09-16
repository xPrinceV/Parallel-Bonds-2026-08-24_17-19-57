using UnityEngine;

public class BossController : EnemyController
{
    public BossHealthBar bossHealthBar;
    public float range = 8f;

    public float attackChannelTime = 1f;

    public float attackCooldown = 2f;

    private float distance;

    private float cooldownCounter;

    private float attackChannelCounter;

    private bool startAttack;

    private BossAttack bossAttack;

    //Allows the Boss to do different attacks
    void Awake()
    {
        bossAttack = GetComponent<BossAttack>();

        //Boss's health bar
        if (bossHealthBar != null)
        {
            bossHealthBar.SetBoss(this);
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    /* void Start()
    {

    } */

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
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

            // Countdown channel time
            attackChannelCounter -= Time.deltaTime;

            if (attackChannelCounter <= 0)
            {
                startAttack = false;
                chooseAttack();
                cooldownCounter = attackCooldown;
            }
        }
        // Move toward player
        else
        {
            GetComponent<Rigidbody2D>().linearVelocity = (target.position - transform.position).normalized * moveSpeed;
        }

        //  Poison effect
        UpdatePoison();
    }

    private void chooseAttack()
    {
        bossAttack.OrbAttack();
       /* //Choose between 2 attacks
        int attackChoice = Random.Range(0, 2);

        //0 is orb attack
        //1 is lightning attack
        if (attackChoice == 0)
        {
            bossAttack.OrbAttack();
        }
        else
        {
            bossAttack.LightningAttack();
        } */
    }
}
