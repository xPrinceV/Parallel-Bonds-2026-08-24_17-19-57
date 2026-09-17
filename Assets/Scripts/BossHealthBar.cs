using UnityEngine;
using UnityEngine.UI;

public class BossHealthBar : MonoBehaviour
{
    public Slider healthSlider;

    private BossController boss;

   /* void Awake()
    {
        //Hides health bar at start of game
        gameObject.SetActive(false);
    }*/

    public void SetBoss(BossController newBoss)
    {
        boss = newBoss;
        //Set max health for the boss's starting health
        healthSlider.maxValue = boss.health;
        //starting health at max
        healthSlider.value = boss.health;

        //Show health bar
        gameObject.SetActive(true);
    }
    // Update is called once per frame
    void Update()
    {
        if (boss != null)
        {
            //Update health slider to match the boss's health
            healthSlider.value = boss.health;
        }
    }
}
