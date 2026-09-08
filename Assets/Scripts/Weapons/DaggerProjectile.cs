using UnityEngine;
using System.Collections.Generic;

public class DaggerProjectile : MonoBehaviour
{
    private EnemyController target;
    public float speed;
    public float damage;
    public float duration;
    public float bounces;
    public float range;
    public bool shouldKnockback;
    private bool isFirstHit;
    private List<EnemyController> hitList= new List<EnemyController>();

    void Start()
    {
        //Destroy projectile after 7 seconds
        Destroy(gameObject, 7f);
        isFirstHit = true;

    }

    // Update is called once per frame
    void Update()
    {
        if (bounces <= 0)
        {
            Destroy(gameObject);
            return;
        }
        if (target == null)
        {
            target = FindClosestEnemy(null);
            if (target == null)
            {
                Destroy(gameObject);
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
        if (collision.tag == "Enemy")
        {
            //Get the enemy controller of the enemy that was hit and add it to the hitList
            EnemyController currentEnemy = collision.GetComponent<EnemyController>();
            hitList.Add(currentEnemy);

            //Deal damage to the enemy and apply poison effect
            currentEnemy.TakeDamage(damage, shouldKnockback);
            currentEnemy.ApplyPoison(damage*0.2f, duration);

            if (!isFirstHit)
            {
                bounces--;
            }
            else
            {
                isFirstHit = false;
            }

            EnemyController newTarget = FindClosestEnemy(currentEnemy);
            if (newTarget != null)
            {
                target = newTarget;
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    private EnemyController FindClosestEnemy(EnemyController targetToIgnore)
    {
        //Nearest enemy = null, closest distance = infinity
        EnemyController nearestEnemy = null;
        float closestDistance = Mathf.Infinity;
        
        //Look for all colliders within the range of the projectile
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, range);

        //Prioritize finding the closest enemy that is not the targetToIgnore and has not been hit yet
        foreach (Collider2D enemy in enemiesInRange)
        {
            //Retrieve the enemy controller
            EnemyController foundEnemy = enemy.GetComponent<EnemyController>();
            //If the found enemy is not null, is not the targetToIgnore, and has not been hit yet, check its distance
            if(foundEnemy != null && foundEnemy != targetToIgnore && !hitList.Contains(foundEnemy))
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
            EnemyController foundEnemy = enemy.GetComponent<EnemyController>();
            //If the found enemy is not null and is not the targetToIgnore, check its distance
            if(foundEnemy != null && foundEnemy != targetToIgnore)
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
        target = newTarget;
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
        bounces = newBounces;
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
