using UnityEngine;
using System.Collections.Generic;
public class LanternFire : MonoBehaviour
{
    public float damage;
    //Burning list will be used to store enemies that are "burning"
    public List<EnemyController> burningList;
    //How frequent in seconds, the enemy will take damage from the fire
    public float tickRate = 0.5f;
    public float tickCounter = 0;
    public float duration = 10f;
    public float durationCounter;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        durationCounter = duration;
    }

    // Update is called once per frame
    void Update()
    {
        tickCounter -= Time.deltaTime;
        durationCounter -= Time.deltaTime;
        if (tickCounter <= 0 && burningList.Count > 0)
        {
            //Iterate through the list of burning enemies
            for (int i = burningList.Count - 1; i >= 0; i--)
            {
                EnemyController enemy = burningList[i];
                //If the enemy is null (they died), remove them from the list
                if (enemy == null)
                {
                    burningList.RemoveAt(i);
                    continue;
                }
                //Make the enemy take damage
                enemy.TakeDamage(damage);
            }
            //Set tick counter to the tick rate 
            tickCounter = tickRate;
        }

        if (durationCounter <= 0)
        {
            Destroy(gameObject);
        }
    }

    //When the enemy collides with the fire, make them take damage and add them to the list of burning enemies
    void OnTriggerEnter2D(Collider2D collision)
    {

        if (collision.tag == "Enemy")
        {
            EnemyController enemy = collision.GetComponent<EnemyController>();
            enemy.TakeDamage(damage);
            if (!burningList.Contains(enemy))
            {
                burningList.Add(enemy);
            }

        }
    }

    //When the enemy exits the collision box of the fire, get the enemy object and remove them from the burning list
    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.tag == "Enemy")
        {
            EnemyController enemy = collision.GetComponent<EnemyController>();
            burningList.Remove(enemy);
        }
    }
    public void SetDamage(float newDamage)
    {
        damage = newDamage / 2;
    }

    public void SetDuration(float newDuration)
    {
        duration = newDuration;
    }
}
