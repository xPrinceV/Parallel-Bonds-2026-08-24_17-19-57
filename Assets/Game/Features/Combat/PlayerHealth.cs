using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

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
    private bool isDead;
    public bool IsDead => HealthOwner.isDead;
    private PlayerHealth sharedHealth;
    private PlayerHealth HealthOwner => sharedHealth != null ? sharedHealth : this;
    public bool HasInitialized => HealthOwner.healthInitialized;

    // keep existing scene values while routing both heroes to one runtime health pool
    [SerializeField, FormerlySerializedAs("currentHealth")]
    private float storedCurrentHealth;
    [SerializeField, FormerlySerializedAs("maxHealth")]
    private float storedMaxHealth;

    public float currentHealth
    {
        get => HealthOwner.storedCurrentHealth;
        set
        {
            if (IsDead)
                return;
            HealthOwner.storedCurrentHealth = Mathf.Max(0f, value);
            RefreshSharedPresentation();
            if (HealthOwner.healthInitialized && currentHealth <= 0f)
            {
                HealthOwner.isDead = true;
                World world = World.GetFor(this);
                if (world != null && world.Manager != null && world.Manager.IsInitialized)
                    world.Manager.HandlePlayerDeath();
                else
                    gameObject.SetActive(false);
            }
        }
    }

    public float maxHealth
    {
        get => HealthOwner.storedMaxHealth;
        set
        {
            HealthOwner.storedMaxHealth = value;
            RefreshSharedPresentation();
        }
    }

    // the world manager binds health before any world activation callbacks run
    internal void ShareHealthWith(PlayerHealth source)
    {
        sharedHealth = source == this ? null : source.HealthOwner;
        InitializeHealth();
    }

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
        World world = World.GetFor(this);
        if (!isActiveAndEnabled || IsDead || (world != null && world.Manager != null
            && world.Manager.IsFused && world.InteractionPlayer != GetComponent<PlayerController>()))
        {
            return;
        }

        InitializeHealth();
        instance = this;
        RefreshPresentation();
    }

    private void InitializeHealth()
    {
        PlayerHealth owner = HealthOwner;
        if (owner.healthInitialized)
        {
            return;
        }

        owner.storedCurrentHealth = owner.storedMaxHealth;
        owner.healthInitialized = true;
    }

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
        if (!isActiveAndEnabled || IsDead || currentHealth <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damageTaken);


    }

    // updates from either hero refresh the active hero's shared HUD
    private void RefreshSharedPresentation()
    {
        if (instance != null && instance.HealthOwner == HealthOwner)
            instance.RefreshPresentation();
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
