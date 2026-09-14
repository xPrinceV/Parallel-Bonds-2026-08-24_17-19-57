using UnityEngine;

[DisallowMultipleComponent]
public sealed class RiftLordProjectile : MonoBehaviour, IWorldProjectile
{
    private readonly RaycastHit2D[] sweepHits = new RaycastHit2D[32];
    private readonly Collider2D[] overlaps = new Collider2D[32];
    private ContactFilter2D playerFilter;
    private RiftLordBarrage owner;
    private Vector2 direction;
    private float speed;
    private float damage;
    private float lifetimeRemaining = 8f;
    private float radius = 0.25f;
    private bool initialized;
    private bool renderOnly;
    private bool hasDespawned;

    private void Awake()
    {
        // Collision is query-only, including the spawn overlap; warnings cannot hit anything.
        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        if (circle != null)
        {
            Vector3 scale = transform.lossyScale;
            radius = RiftLordBarrage.Bounded(circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
                0.25f, 0.02f, 1f);
        }
        foreach (Collider2D collider in GetComponentsInChildren<Collider2D>(true))
            collider.enabled = false;
        playerFilter = new ContactFilter2D();
        playerFilter.SetLayerMask(LayerMask.GetMask("Player"));
        playerFilter.useTriggers = true;
    }

    internal void Initialize(RiftLordBarrage source, Vector2 heading, float shotSpeed, float shotDamage, float lifetime)
    {
        owner = source;
        if (owner == null || !RiftLordBarrage.IsFinite(heading.x) || !RiftLordBarrage.IsFinite(heading.y)
            || heading.sqrMagnitude < 0.0001f)
        {
            Despawn();
            return;
        }
        direction = heading.normalized;
        speed = RiftLordBarrage.Bounded(shotSpeed, 3f, 0.1f, 20f);
        damage = RiftLordBarrage.Bounded(shotDamage, 10f, 0f, 100f);
        lifetimeRemaining = RiftLordBarrage.Bounded(lifetime, 8f, 0.1f, 15f);
        initialized = true;
    }

    internal void InitializePreview(RiftLordBarrage source, float lifetime)
    {
        owner = source;
        renderOnly = true;
        lifetimeRemaining = RiftLordBarrage.Bounded(lifetime, 1.6f, 0.1f, 15f);
        initialized = true;
    }

    private void Update()
    {
        if (hasDespawned)
            return;
        if (!initialized || owner == null || !owner.CanKeepProjectiles)
        {
            Despawn();
            return;
        }
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;
        float elapsed = Mathf.Min(dt, lifetimeRemaining);
        if (!renderOnly)
        {
            Vector2 start = transform.position;
            int count = Physics2D.OverlapCircle(start, radius, playerFilter, overlaps);
            for (int i = 0; i < count; i++)
                if (TryHit(overlaps[i]))
                    return;

            float distance = speed * elapsed;
            count = Physics2D.CircleCast(start, radius, direction, playerFilter, sweepHits, distance);
            for (int i = 0; i < count; i++)
                if (TryHit(sweepHits[i].collider))
                    return;
            transform.position += (Vector3)(direction * distance);
        }
        lifetimeRemaining -= elapsed;
        if (lifetimeRemaining <= 0f)
            Despawn();
    }

    private bool TryHit(Collider2D collider)
    {
        if (hasDespawned || renderOnly || collider == null || !World.CanInteract(this, collider))
            return false;
        PlayerHealth health = collider.GetComponentInParent<PlayerHealth>();
        if (health == null || !health.isActiveAndEnabled || health.IsDead || !World.CanInteract(this, health))
            return false;

        // Latch before shared health can end the run and re-enter owner cleanup.
        Despawn();
        health.DamageHandler(damage);
        return true;
    }

    public void Despawn()
    {
        if (hasDespawned)
            return;
        hasDespawned = true;
        ReleaseOwner();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private void ReleaseOwner()
    {
        if (owner != null)
            owner.ReleaseProjectile(this);
        owner = null;
    }

    private void OnDisable() => Despawn();
    private void OnDestroy() => ReleaseOwner();
}
