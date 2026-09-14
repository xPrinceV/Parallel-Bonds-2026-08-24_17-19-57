using UnityEngine;
using System.Collections.Generic;

public class DaggerProjectile : MonoBehaviour, IWorldProjectile
{
    private bool hasDespawned;

    void OnEnable()
    {
        hasDespawned = false;
    }

    public void Despawn()
    {
        if (hasDespawned)
            return;

        hasDespawned = true;
        gameObject.SetActive(false);
        // No projectile pool yet; replace destruction here with pool return when available.
        Destroy(gameObject);
    }
    private EnemyController target;
    public float speed;
    public float damage;
    public float duration;
    public float bounces;
    public float range;
    public bool shouldKnockback;
    private bool isFirstHit = true;
    private bool isFinished;
    private float lifetimeRemaining = 7f;
    private BuffController buffSource;
    private EnemyController lastHit;
    private readonly List<Collider2D> enemiesInRange = new List<Collider2D>(32);
    private readonly List<Collider2D> queryScratch = new List<Collider2D>();
    private readonly HashSet<EnemyController> hitList = new HashSet<EnemyController>();
    private readonly Dictionary<Collider2D, EnemyController> contacts = new Dictionary<Collider2D, EnemyController>();

    void Start()
    {
        //Destroy projectile after 7 seconds
        // Count only active time so world sleep preserves the projectile.
        lifetimeRemaining = 7f;
        isFirstHit = true;

    }

    // Update is called once per frame
    void Update()
    {
        lifetimeRemaining -= Time.deltaTime;
        if (isFinished || lifetimeRemaining <= 0f)
        {
            isFinished = true;
            Despawn();
            return;
        }
        if (!IsValidEnemy(target))
        {
            target = FindClosestEnemy(lastHit);
            if (target == null)
            {
                isFinished = true;
                Despawn();
                return;
            }
        }
            //Move the projectile towards the target
            transform.position = Vector2.MoveTowards(transform.position, target.transform.position, speed * Time.deltaTime);

            //Spin the dagger
            transform.Rotate(0, 0, 1800 * Time.deltaTime);

    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (isFinished || !isActiveAndEnabled || lifetimeRemaining <= 0f || !collision.CompareTag("Enemy"))
            return;

        //Get the enemy controller of the enemy that was hit and add it to the hitList
        EnemyController currentEnemy = collision.GetComponentInParent<EnemyController>();
        if (!IsValidEnemy(currentEnemy))
            return;

        // A revisit is allowed only after leaving all of that enemy's colliders.
        bool alreadyTouching = contacts.ContainsValue(currentEnemy);
        contacts[collision] = currentEnemy;
        if (alreadyTouching || currentEnemy == lastHit)
            return;

        hitList.Add(currentEnemy);
        lastHit = currentEnemy;
        if (!isFirstHit)
            bounces--;
        isFirstHit = false;
        isFinished = bounces <= 0f;

        //Deal damage to the enemy and apply poison effect
        float healthBefore = currentEnemy.health;
        currentEnemy.TakeDamage(damage, shouldKnockback);
        float damageDealt = Mathf.Clamp(healthBefore - currentEnemy.health, 0f, healthBefore);
        // Only the dagger impact requests audio, not subsequent poison ticks.
        if (damageDealt > 0f && !float.IsNaN(damageDealt) && !float.IsInfinity(damageDealt))
            AudioService.Instance?.Play(SoundId.DaggerImpact);
        // Report the impact once, including lethal hits; poison ticks belong to the enemy.
        if (buffSource != null && damageDealt > 0f)
            buffSource.ReportHit(currentEnemy.gameObject, damageDealt);
        if (IsValidEnemy(currentEnemy) && damageDealt > 0f)
            currentEnemy.ApplyPoison(damage * 0.2f, duration);

        EnemyController newTarget = isFinished ? null : FindClosestEnemy(currentEnemy);
        if (newTarget != null)
        {
            target = newTarget;
        }
        else
        {
            isFinished = true;
            Despawn();
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        contacts.Remove(collision);
    }

    private bool IsValidEnemy(EnemyController enemy)
    {
        return enemy != null && enemy.gameObject.activeInHierarchy && enemy.health > 0f
            && World.CanInteract(this, enemy);
    }

    private EnemyController FindClosestEnemy(EnemyController targetToIgnore)
    {
        //Nearest enemy = null, closest distance = infinity
        EnemyController nearestEnemy = null;
        float closestDistance = Mathf.Infinity;
        
        //Look for all colliders within the range of the projectile
        WeaponQuery.OverlapCircle(transform.position, range, enemiesInRange, queryScratch);

        //Prioritize finding the closest enemy that is not the targetToIgnore and has not been hit yet
        foreach (Collider2D enemy in enemiesInRange)
        {
            //Retrieve the enemy controller
            EnemyController foundEnemy = enemy.GetComponentInParent<EnemyController>();
            //If the found enemy is not null, is not the targetToIgnore, and has not been hit yet, check its distance
            if (IsValidEnemy(foundEnemy) && foundEnemy != targetToIgnore && !hitList.Contains(foundEnemy)
                && !contacts.ContainsValue(foundEnemy))
            {
                float distance = Vector3.Distance(transform.position, foundEnemy.transform.position);
                if(distance < closestDistance)
                {
                    closestDistance = distance;
                    nearestEnemy = foundEnemy;
                }
            }
        }

        //If a nearest enemy was found, return it
        if(nearestEnemy != null)
        {
            return nearestEnemy;
        }
        
        //Second Priority: If no nearest enemy was found, look for the closest enemy that is not the targetToIgnore, even if it has been hit before
        closestDistance = Mathf.Infinity;
        foreach (Collider2D enemy in enemiesInRange)
        {
            EnemyController foundEnemy = enemy.GetComponentInParent<EnemyController>();
            //If the found enemy is not null and is not the targetToIgnore, check its distance
            if (IsValidEnemy(foundEnemy) && foundEnemy != targetToIgnore && !contacts.ContainsValue(foundEnemy))
            {
                float distance = Vector3.Distance(transform.position, foundEnemy.transform.position);
                if(distance < closestDistance)
                {
                    closestDistance = distance;
                    nearestEnemy = foundEnemy;
                }
            }
        }

        return nearestEnemy;
    }

    //The following functions are used to set the values of the projectile when it is instantiated
    public void SetTarget(EnemyController newTarget)
    {
        target = IsValidEnemy(newTarget) ? newTarget : null;
    }

    public void SetBuffSource(BuffController source)
    {
        buffSource = source;
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }
    public void SetDuration(float newDuration)
    {
        duration = newDuration;
    }
    public void SetBounces(float newBounces)
    {
        bounces = Mathf.Max(0, Mathf.FloorToInt(newBounces));
    }
    public void SetSpeed(float newSpeed)
    {
        speed = newSpeed;
    }
    public void SetRange(float newRange)
    {
        range = newRange;
    }
}
