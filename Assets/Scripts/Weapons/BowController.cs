using UnityEngine;

public class BowController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float amount;
    [SerializeField] private float projectileSpeed;
    [SerializeField] private GameObject arrow;
    private PlayerController player;
    private Vector2 facingDirection;
    private float attackCounter;
    private float spreadAngle = 45f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        attackCounter = 0;
        player = PlayerController.instance;
    }

    // Update is called once per frame
    void Update()
    {
        attackCounter -= Time.deltaTime;

        if (attackCounter <= 0)
        {
            //Sets facingDirection to the player's facing direction, so that the arrow will shoot in the direction the player is facing
            facingDirection = player.facingDirection;

            //The loop is account for projectile count
            int arrowCount = Mathf.FloorToInt(amount * stats.amount);
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
                GameObject newArrow = Instantiate(arrow, transform.position, Quaternion.identity);
                newArrow.GetComponent<ArrowController>().SetDamage(attackDamage * stats.damage);
                newArrow.GetComponent<ArrowController>().SetSpeed(projectileSpeed * stats.speed);
                newArrow.GetComponent<ArrowController>().SetDirection(arrowDirection);
            }

            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}
