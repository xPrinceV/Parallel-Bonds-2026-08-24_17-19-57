using System.Collections.Generic;
using UnityEngine;

public class ShieldOrbitController : MonoBehaviour, IWorldProjectile
{
    private float damage;
    private ShieldController source;
    private BuffController buffSource;
    private bool hasDespawned;
    private Collider2D hitbox;
    private readonly Dictionary<Collider2D, EnemyController> contacts = new Dictionary<Collider2D, EnemyController>();
    private readonly List<Collider2D> separatedContacts = new List<Collider2D>();

    void Awake()
    {
        hitbox = GetComponent<Collider2D>();
    }

    public void Initialize(ShieldController owner, BuffController buffs, float newDamage)
    {
        source = owner;
        buffSource = buffs;
        damage = newDamage;
        hasDespawned = false;
        contacts.Clear();
    }

    // The owner calls this only after repositioning, activating, and syncing the pivot.
    internal void ReconcileContacts()
    {
        separatedContacts.Clear();
        foreach (KeyValuePair<Collider2D, EnemyController> contact in contacts)
        {
            Collider2D collider = contact.Key;
            EnemyController enemy = contact.Value;
            bool valid = hitbox != null && hitbox.enabled && hitbox.gameObject.activeInHierarchy
                && collider != null && collider.enabled && collider.gameObject.activeInHierarchy
                && (collider.attachedRigidbody == null || collider.attachedRigidbody.simulated)
                && enemy != null && enemy.isActiveAndEnabled && enemy.health > 0f
                && World.CanInteract(this, collider) && World.CanInteract(this, enemy);
            if (valid)
            {
                ColliderDistance2D distance = hitbox.Distance(collider);
                valid = distance.isValid && distance.distance <= 0f;
            }
            if (!valid)
                separatedContacts.Add(collider);
        }
        foreach (Collider2D collider in separatedContacts)
            contacts.Remove(collider);
        separatedContacts.Clear();
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasDespawned || !isActiveAndEnabled || source == null || !source.CanAttack
            || !World.CanInteract(this, collision) || contacts.ContainsKey(collision))
            return;

        EnemyController enemy = collision.GetComponentInParent<EnemyController>();
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.health <= 0f
            || !World.CanInteract(this, enemy))
            return;

        // One hit per contact episode, not per collider. Resume reconciliation retains
        // continuing overlaps, so re-entry callbacks cannot award duplicate wake hits.
        bool alreadyTouching = contacts.ContainsValue(enemy);
        contacts.Add(collision, enemy);
        if (alreadyTouching)
            return;

        float healthBefore = enemy.health;
        enemy.TakeDamage(damage);
        float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
        if (buffSource != null && damageDealt > 0f)
            buffSource.ReportHit(enemy.gameObject, damageDealt);
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if (isActiveAndEnabled && source != null && source.CanAttack)
            contacts.Remove(collision);
    }

    public void Despawn()
    {
        if (hasDespawned)
            return;
        hasDespawned = true;
        gameObject.SetActive(false);
        // Detach before notifying the owner: a future pool return must survive pivot cleanup.
        transform.SetParent(null, true);
        if (source != null)
            source.ReleaseShield(this);
        source = null;
        contacts.Clear();
        // No projectile pool yet; replace destruction here with pool return when available.
        Destroy(gameObject);
    }
}
