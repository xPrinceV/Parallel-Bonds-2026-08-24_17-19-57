using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{

    public static PlayerHealth instance;

    public Slider healthSlider;
    public TMP_Text healthText;

    //Called before start
    private void Awake()
    {
        InitializeHealth();
    }

    private bool healthInitialized;
    public bool HasInitialized => healthInitialized;

    private void OnEnable()
    {
        BindAsCurrent();
    }

    private void OnDisable()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void BindAsCurrent()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        InitializeHealth();
        instance = this;
        RefreshPresentation();
    }

    private void InitializeHealth()
    {
        if (healthInitialized)
        {
            return;
        }

        currentHealth = maxHealth;
        healthInitialized = true;
    }

    public float currentHealth, maxHealth;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshPresentation();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void DamageHandler(float damageTaken)
    {
        currentHealth -= damageTaken;
        RefreshPresentation();
        if(currentHealth <= 0)
        {
            //Trigger Lost Condition (SetActive to false is temporary)
            gameObject.SetActive(false);
        }

    }

    private void RefreshPresentation()
    {
        if (instance != this)
        {
            return;
        }

        //Update health slider
        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHealth;
            healthSlider.value = currentHealth;
        }

        //Update health text
        if (healthText != null)
        {
            healthText.text = currentHealth + " / " + maxHealth;
        }
    }
}
