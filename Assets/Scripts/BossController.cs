using System.Globalization;
using System.Collections;
using UnityEngine;

public class BossController : EnemyController
{
    public BossHealthBar bossHealthBar;
    public float range = 8f;

    public float attackChannelTime = 1f;

    public float attackCooldown = 2f;

    //Teleportation settings
    public float teleportCooldown = 5f;

    //Minimum distance of teleportation from player
    public float teleportMinDistance = 4f;

    //Maximum distance of teleportation from player
    public float teleportMaxDistance = 10f;

    //Animation for the teleportaion
    public GameObject teleportAnimation;

    //Before the boss changes its position
    public float teleportDisappearTime = 0.5f;

    //Before the boss reappears 
    public float teleportReappearTime = 0.5f;

    //pause after teleportation
    public float teleportPause = 1f;

    //Offset of the teleport effect's position to the boss
    public Vector2 teleportEffectOffset;

    private float distance;

    private float cooldownCounter;

    private float attackChannelCounter;

    private float teleportCounter;

    private bool startAttack;

    private SpriteRenderer bossSprite;

    private bool isTeleporting = false;

    private BossAttack bossAttack;

    //Allows the Boss to do different attacks
    void Awake()
    {
        bossAttack = GetComponent<BossAttack>();

        bossSprite = GetComponent<SpriteRenderer>();

        //Boss's health bar
        if (bossHealthBar != null)
        {
            bossHealthBar.SetBoss(this);
        }

        teleportCounter = teleportCooldown;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    /* void Start()
    {

    } */

    // Update is called once per frame
    void Update()
    {
        cooldownCounter -= Time.deltaTime;

        //Interval before teleportation
        teleportCounter -= Time.deltaTime;

        if (teleportCounter <= 0)
        {
            Teleport();
        }

        //Calculate distance from target and the monster
        distance = Vector3.Distance(transform.position, target.position);

        //If within attack range, start attack
        if (!startAttack && distance <= range && cooldownCounter <= 0)
        {
            startAttack = true;
            attackChannelCounter = attackChannelTime;
        }

        //Attack
        if (startAttack)
        {
            //Stop moving
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

            //Countdown channel time
            attackChannelCounter -= Time.deltaTime;

            if (attackChannelCounter <= 0)
            {
                startAttack = false;
                chooseAttack();
                cooldownCounter = attackCooldown;
            }
        }
        //Move toward player
        else if (!isTeleporting)
        {
            GetComponent<Rigidbody2D>().linearVelocity = (target.position - transform.position).normalized * moveSpeed;
        }
        else
        {
            //Stop moving after teleporting
            GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        }

        //Poison effect
        UpdatePoison();
    }

    private void chooseAttack()
    {
       //Choose between 2 attacks
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
        } 
    }

    //teleportation of the boss
    private void Teleport()
    {   
        //Only one teleportation
        if (!isTeleporting)
        {
            teleportAnimation.SetActive(true);
            StartCoroutine(TeleportSequence());
        }
    }

    
    private IEnumerator TeleportSequence()
    {
        isTeleporting = true;

        //Teleport effect on boss's position
        if (teleportAnimation != null)
        {
            GameObject effect = Instantiate(teleportAnimation, (Vector2)transform.position + teleportEffectOffset, Quaternion.identity);
        
            //Destroy the teleport effect
            Destroy(effect, 1f);
        }

        //Boss is hidden
        bossSprite.enabled = false;

        //Wait for the teleportation animation - disappear
        yield return new WaitForSeconds(teleportDisappearTime);

        //Selection of the direction for the teleportation
        Vector2 randomDirection = Random.insideUnitCircle.normalized;

        //Selection of the distance 
        float randomDistance = Random.Range(teleportMinDistance, teleportMaxDistance);
    
        //Teleported position
        Vector2 teleportPosition = (Vector2)target.position + randomDirection * randomDistance;

        //Teleportation of the boss
        transform.position = teleportPosition;

        //Teleport effect on new position of boss
        if (teleportAnimation != null)
        {
            GameObject effect = Instantiate(teleportAnimation, (Vector2)transform.position + teleportEffectOffset, Quaternion.identity);
        
            //Destroy the teleport effect
            Destroy(effect, 1f);
        }

        //Boss is visible
        bossSprite.enabled = true;

        //Wait for the teleportation animation - appear
        yield return new WaitForSeconds(teleportPause);

        //Reset of the teleportation cooldown
        teleportCounter = teleportCooldown;
        isTeleporting = false;
    }
}
