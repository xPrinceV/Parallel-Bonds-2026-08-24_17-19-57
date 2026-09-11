using UnityEngine;

public class ScytheHitController : MonoBehaviour
{
    [SerializeField] private Transform hitboxPivot;

    [SerializeField] private float offset = 0f;
    [SerializeField] private float swingDuration = 0.4f;
    [SerializeField] private float swingAngle = -225f;
    [SerializeField] private Transform animationTransform;
    [HideInInspector] public PlayerController player;

    [HideInInspector] public float damage;
    [HideInInspector] public float area;
    private float swingTimer;

    private float startAngle;
    private float endAngle;

    private Animator animator;

    void Awake() {
        animator = GetComponentInChildren<Animator>();
    }

    void Start() {
        animator.SetTrigger("Swing");

        //Set size
        transform.localScale = new Vector3(area, area, area);
        
        // Destroy after swing finishes
        Destroy(gameObject, swingDuration);
    }

    void Update() {
        swingTimer += Time.deltaTime;

        float progress = Mathf.Clamp01(swingTimer / swingDuration);

        float currentAngle = Mathf.Lerp(startAngle, endAngle, progress);

        hitboxPivot.localRotation = Quaternion.Euler(0f, 0f, currentAngle);

        transform.position = player.transform.position;
    }

    public void SetDirection(Vector2 newDirection)
    {
        float directionAngle =
            Mathf.Atan2(newDirection.y, newDirection.x) * Mathf.Rad2Deg;

        // Rotate the visual animation to face the player's direction
        animationTransform.localRotation = Quaternion.Euler(0f, 0f, directionAngle);

        // Hitbox starts from its own offset
        startAngle = directionAngle + offset;
        endAngle = startAngle + swingAngle;

        hitboxPivot.localRotation = Quaternion.Euler(0f, 0f, startAngle);
    }

    public void HitEnemy(Collider2D collision)
    {
        if (collision.tag == "Enemy")
        {
            EnemyController enemy = collision.GetComponent<EnemyController>();

            if (enemy != null)
            {
                enemy.TakeDamage(damage, true);
            }
        }
    }
}