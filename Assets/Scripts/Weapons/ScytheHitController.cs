using UnityEngine;

public class ScytheHitController : MonoBehaviour
{
    [SerializeField] private Transform hitboxPivot;

    [SerializeField] private float offset;
    [SerializeField] private float swingAngle = -225f;
    [SerializeField] private float swingDuration = 0.4f;

    private float damage;
    private float swingTimer;

    private float startAngle;
    private float endAngle;

    private Animator animator;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        animator.SetTrigger("Swing");

        Destroy(gameObject, swingDuration);
    }

    void Update()
    {
        swingTimer += Time.deltaTime;

        float progress = swingTimer / swingDuration;

        float currentAngle = Mathf.LerpAngle(
            startAngle,
            endAngle,
            progress
        );

        hitboxPivot.localRotation =
            Quaternion.Euler(0f, 0f, currentAngle);
    }

    public void HitEnemy(Collider2D collision)
    {
        collision.GetComponent<EnemyController>().TakeDamage(damage, true);
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetDirection(Vector2 newDirection)
    {
        float directionAngle =
            Mathf.Atan2(newDirection.y, newDirection.x)
            * Mathf.Rad2Deg;

        startAngle = directionAngle + offset;
        endAngle = startAngle + swingAngle;

        hitboxPivot.localRotation =
            Quaternion.Euler(0f, 0f, startAngle);
    }
}