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
    public event System.Action<EnemyController> Died;
    private bool isDead;
    protected bool IsDead => isDead;
    private PlayerHealth playerHealth;

    //These are for handling status effects like poison
    private float poisonDamage;
    private float poisonDuration;
    private float poisonCounter;
    [SerializeField] private GameObject poisonEffect;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshTarget();
    }

    protected void RefreshTarget()
    {
        //Sets target to the transform location of the player
        World world = World.GetFor(this);
        PlayerController player = world != null ? world.InteractionPlayer : null;
        playerHealth = world != null
            ? (player != null ? player.GetComponent<PlayerHealth>() : null)
            : PlayerHealth.instance;
        target = playerHealth != null ? playerHealth.transform : null;
    }

    // Update is called once per frame
    void Update()
    {
        UpdatePoison();
        if (isDead)
            return;

        RefreshTarget();

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
                //This is to reset the mobs moveSpeed back to normal
                moveSpeed = Mathf.Abs(moveSpeed * .5f);
            }
        }

        //Sets the Rigidbody velocity to be moving towards the player
        RB.linearVelocity = target != null && target.gameObject.activeInHierarchy
            ? (Vector2)(target.position - transform.position).normalized * moveSpeed
            : Vector2.zero;

        if(hitCounter > 0f)
        {
            hitCounter -= Time.deltaTime;
        }
    }

    protected void UpdatePoison()
    {
        if (isDead || !isActiveAndEnabled || poisonDamage <= 0f || Time.deltaTime <= 0f)
            return;

        // Only active-world time counts; do not tick beyond the remaining duration on a long frame.
        float elapsed = Mathf.Min(Time.deltaTime, poisonDuration);
        poisonDuration -= elapsed;
        poisonCounter -= elapsed;
        while (poisonCounter <= 0f && !isDead)
        {
            TakeDamage(poisonDamage);
            poisonCounter += 1f;
        }

        if (poisonDuration <= 0f || isDead)
        {
            poisonDamage = 0f;
            poisonDuration = 0f;
            poisonCounter = 0f;
            if (poisonEffect != null)
                poisonEffect.SetActive(false);
        }
    }

    public void ApplyPoison(float damage, float duration)
    {
        if (isDead || !isActiveAndEnabled || damage <= 0f || duration <= 0f)
            return;

        //This is so poison will stack damage, but not stack duration
        // Refresh duration without postponing the next tick of an existing effect.
        if (poisonDamage <= 0f)
            poisonCounter = 1f;
        poisonDamage += damage;
        poisonDuration = duration;
        if (poisonEffect != null)
            poisonEffect.SetActive(true);
    }

    //Method to detect collision
    private void OnCollisionEnter2D(Collision2D collision)
    {
        //Check if collision is done with a player
        if(collision.gameObject.tag == "Player" && hitCounter <= 0f)
        {
            RefreshTarget();
            if (!isActiveAndEnabled || !World.CanInteract(this, collision.transform)
                || playerHealth == null || collision.gameObject.GetComponentInParent<PlayerHealth>() != playerHealth)
                return;

            playerHealth.DamageHandler(attack);
            //A cooldown for the player taking damage
            hitCounter = hitWaitTime;
        }
    }

    public void TakeDamage(float damageTaken)
    {
        if (isDead || !gameObject.activeInHierarchy)
            return;

        //Reduce health by damage taken
        health -= damageTaken;

        //When the enemy dies
        if(health <= 0)
        {
            isDead = true;
            Destroy(gameObject);

            //Spawn Exp Orb at the position of the enemy
            // SpawnExp owns parenting through the owning hero, never the active-world singleton.
            World world = World.GetFor(this);
            ExperienceLevelController experience = world != null
                ? (world.Player != null ? world.Player.GetComponent<ExperienceLevelController>() : null)
                : ExperienceLevelController.instance;
            if (experience != null)
                experience.SpawnExp(transform.position, expDrop);
        }

        //Spawn the damage number
        if (DamageNumberController.instance != null)
            DamageNumberController.instance.SpawnDamage(damageTaken, transform.position, World.GetFor(this));

        // Publish only an actual lethal hit, after source-world XP and presentation.
        // Subscribers must defer encounter cleanup until the damage call stack has returned.
        if (isDead)
            Died?.Invoke(this);
    }

    //This function is the same as the above, but it takes in an extra argument to account for knockback
    //Call this function when you want the damage dealt to knockback the enemies too
    public void TakeDamage(float damageTaken, bool shouldKnockback)
    {
        TakeDamage(damageTaken);
        if(shouldKnockback)
        {
            knockbackCounter = knockbackTime;
        }
    }

}
