using System.Collections.Generic;
using UnityEngine;

public class ScytheHitController : MonoBehaviour
{
    [SerializeField] private Transform hitboxPivot;
    [SerializeField] private float offset = 0f;
    [SerializeField] private float swingDuration = 0.4f;
    [SerializeField] private float swingAngle = -225f;
    [SerializeField] private Transform animationTransform;
    private PlayerController player;
    private BuffController buffSource;
    private readonly HashSet<EnemyController> hitEnemies = new HashSet<EnemyController>();
    private float damage;
    private float area = 1f;
    private float swingTimer;
    private float startAngle;
    private float endAngle;
    private Quaternion visualBaseRotation = Quaternion.identity;
    private bool finished;

    void Awake()
    {
        if (animationTransform != null)
            visualBaseRotation = animationTransform.localRotation;
    }

    void Start()
    {
        if (player == null || hitboxPivot == null || animationTransform == null || swingDuration <= 0f)
        {
            Debug.LogError("Scythe swing requires a source, pivot, visual transform, and positive duration.", this);
            finished = true;
            Destroy(gameObject);
            return;
        }


        //Set size
        transform.localScale = new Vector3(area, area, area);
    }

    void Update()
    {
        if (finished)
            return;
        if (player == null)
        {
            finished = true;
            Destroy(gameObject);
            return;
        }
        if (!player.isActiveAndEnabled || !World.CanInteract(this, player))
            return;

        // Destroy after swing finishes, counting only active time (not suspended-world time).
        swingTimer += Time.deltaTime;
        if (swingTimer >= swingDuration)
        {
            finished = true;
            Destroy(gameObject);
            return;
        }

        float progress = Mathf.Clamp01(swingTimer / swingDuration);
        float currentAngle = Mathf.Lerp(startAngle, endAngle, progress);
        ApplySwingRotation(currentAngle);
        transform.position = player.transform.position;
    }

    public void SetDirection(Vector2 newDirection)
    {
        float directionAngle = Mathf.Atan2(newDirection.y, newDirection.x) * Mathf.Rad2Deg;

        // Hitbox starts from its own offset
        startAngle = directionAngle + offset;
        endAngle = startAngle + swingAngle;
        ApplySwingRotation(startAngle);
    }

    private void ApplySwingRotation(float angle)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        if (hitboxPivot != null)
            hitboxPivot.localRotation = rotation;
        // Both siblings sweep together while retaining the sprite's authored alignment and flip.
        if (animationTransform != null)
            animationTransform.localRotation = rotation * visualBaseRotation;
    }

    public void HitEnemy(Collider2D collision)
    {
        if (finished || !isActiveAndEnabled || collision == null || player == null ||
            !player.isActiveAndEnabled || !World.CanInteract(this, player) || !World.CanInteract(this, collision))
            return;

        EnemyController enemy = collision.GetComponentInParent<EnemyController>();
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.health <= 0f ||
            !World.CanInteract(this, enemy) || !hitEnemies.Add(enemy))
            return;

        // One hit per enemy per swing, including child colliders and re-entry after suspension.
        float healthBefore = enemy.health;
        enemy.TakeDamage(damage, true);
        float damageDealt = Mathf.Clamp(healthBefore - enemy.health, 0f, healthBefore);
        // Report lethal hits before deferred destruction, using actual health lost.
        if (buffSource != null && damageDealt > 0f)
            buffSource.ReportHit(enemy.gameObject, damageDealt);
    }

    public void SetSource(PlayerController source, BuffController buffs)
    {
        player = source;
        buffSource = buffs;
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetArea(float newArea)
    {
        area = newArea;
    }
}
