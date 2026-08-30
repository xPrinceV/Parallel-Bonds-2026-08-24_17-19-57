using UnityEngine;
using System.Collections.Generic;
public class LanternFire : MonoBehaviour
{
    public float damage;
    public List<EnemyController> burningList;
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
        if(tickCounter <= 0 && burningList.Count > 0)
        {
            for(int i = burningList.Count - 1; i >=0; i--)
            {
                EnemyController enemy = burningList[i];
                if(enemy == null)
                {
                    burningList.RemoveAt(i);
                    continue;
                }
                enemy.TakeDamage(damage);
            }
            tickCounter = tickRate;
        }

        if(durationCounter <= 0)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Enemy")
        {
            EnemyController enemy = collision.GetComponent<EnemyController>();
            enemy.TakeDamage(damage);
            if(!burningList.Contains(enemy))
            {
                burningList.Add(enemy);
            }
            
        }
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if(collision.tag == "Enemy")
        {
            EnemyController enemy = collision.GetComponent<EnemyController>();
            burningList.Remove(enemy);
        }
    }
    public void SetDamage(float newDamage)
    {
        damage = newDamage/2;
    }

    public void SetDuration(float newDuration)
    {
        duration = newDuration;
    }
}
