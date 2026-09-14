using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{

    public static PlayerHealth instance;

    public Slider healthSlider;
    public TMP_Text healthText;

    //Calls gameover function only once
    private bool isDead;
    public GameOverManager gameOverManager;

    //Called before start
    private void Awake()
    {
        instance = this;
    }
    public float currentHealth, maxHealth;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHealth = maxHealth;

        healthSlider.maxValue = maxHealth;
        healthSlider.value = currentHealth;

        healthText.text = currentHealth + " / " + maxHealth;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void DamageHandler(float damageTaken)
    {
        currentHealth -= damageTaken;
        if(currentHealth <= 0 && !isDead)
        {
            isDead = true;  
            //Call GamgeOver function
            gameOverManager.GameOver();

            //Trigger Lost Condition (SetActive to false is temporary)
            gameObject.SetActive(false);
        }

        //Update health slider
        healthSlider.value = currentHealth;

        //Update health text
        healthText.text = currentHealth + " / " + maxHealth;
    }
}
