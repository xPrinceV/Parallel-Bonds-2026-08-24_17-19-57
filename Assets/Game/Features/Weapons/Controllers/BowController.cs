using UnityEngine;

public class BowController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float amount;
    [SerializeField] private float projectileSpeed;
    [SerializeField] private GameObject arrow;

    [SerializeField] private BuffController buffHolder;
    private Vector2 facingDirection = Vector2.right;
    private float attackCounter;
    private float spreadAngle = 45f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        attackCounter = 0;
        ResolveOwner();
        if (buffHolder == null && player != null)
            buffHolder = player.GetComponent<BuffController>();
        if (buffHolder == null)
            buffHolder = GetComponentInParent<BuffController>();
    }

    // Update is called once per frame
    void Update()
    {
        if (IsFiringSuspended)
            return;

        if (player == null || !player.isActiveAndEnabled || !World.CanInteract(this, player))
            return;

        //Sets facingDirection to the player's facing direction, so that the arrow will shoot in the direction the player is facing
        facingDirection = player.facingDirection;

        attackCounter -= Time.deltaTime;

        if (attackCounter <= 0)
        {

            //The loop is account for projectile count
            int arrowCount = Mathf.Max(0, Mathf.FloorToInt(amount + stats.amount));
            // Snapshot buffs once per volley; hits affect later attacks.
            float damage = attackDamage * stats.damage;
            if (buffHolder != null)
            {
                damage = buffHolder.CalculateWeaponDamage(damage);
                arrowCount = buffHolder.CalculateProjectileCount(arrowCount);
            }
            for (int i = 0; i < arrowCount; i++)
            {
                float angle;
                if (arrowCount == 1)
                {
                    angle = 0f;
                }
                //If there is more than 1 arrow, the arrows will be spread evenly across the spreadAngle
                else
                {
                    angle = -spreadAngle / 2f + (spreadAngle / (arrowCount - 1)) * i;
                }
                //Rotate the arrow's direction by the angle
                Vector2 arrowDirection = Quaternion.Euler(0, 0, angle) * facingDirection;

                //Create a new arrow and set its damage, speed, and direction
                GameObject newArrow = Instantiate(arrow, transform.position, Quaternion.identity, World.GetContentRoot(this));
                ArrowController projectile = newArrow.GetComponent<ArrowController>();
                projectile.SetDamage(damage);
                projectile.SetSpeed(projectileSpeed * stats.speed);
                projectile.SetDirection(arrowDirection);
                projectile.SetBuffSource(buffHolder);
            }
            if (arrowCount > 0)
                AudioService.Instance?.Play(SoundId.BowFire);

            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}
