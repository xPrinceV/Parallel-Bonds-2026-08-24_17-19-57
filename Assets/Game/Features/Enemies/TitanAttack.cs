using System.Collections.Generic;
using UnityEngine;

public class TitanAttack : MonoBehaviour
{
    private bool channelFinished = false;
    public float damage;
    public float attackDelay = 2.5f;
    public GameObject effectAnimation;
    [SerializeField] private CircleCollider2D attackCollider;

    private float lifetimeRemaining = 3f;
    // Keep hit history through content sleep and fusion/switch trigger re-entry.
    private readonly HashSet<PlayerHealth> hitPlayers = new HashSet<PlayerHealth>();

    void Awake()
    {
        attackCollider.enabled = false;
    }


    void Update()
    {
        if (!isActiveAndEnabled)
            return;

        // Parenting under the source content pauses both telegraph and lifetime during sleep.
        lifetimeRemaining -= Time.deltaTime;
        if (lifetimeRemaining <= 0f)
        {
            attackCollider.enabled = false;
            Destroy(gameObject);
            return;
        }

        attackDelay -= Time.deltaTime;
        if (attackDelay <= 0 && !channelFinished)
        {
            channelFinished = true;
            effectAnimation.SetActive(true);
            attackCollider.enabled = true;
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!isActiveAndEnabled || !channelFinished || lifetimeRemaining <= 0f
            || !attackCollider.enabled || !collision.gameObject.activeInHierarchy
            || !World.CanInteract(this, collision))
            return;

        PlayerHealth playerHealth = collision.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null || !playerHealth.CompareTag("Player")
            || !playerHealth.isActiveAndEnabled || playerHealth.IsDead)
            return;

        World world = World.GetFor(this);
        if (world != null && (world.InteractionPlayer == null
            || world.InteractionPlayer.GetComponent<PlayerHealth>() != playerHealth))
            return;

        // Record before damage, which can synchronously disable content on player death.
        if (hitPlayers.Add(playerHealth))
            playerHealth.DamageHandler(damage);
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }
}
