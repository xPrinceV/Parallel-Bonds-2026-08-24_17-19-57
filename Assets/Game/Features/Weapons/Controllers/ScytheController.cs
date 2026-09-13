using UnityEngine;

public class ScytheController : Weapon
{
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float area;
    [SerializeField] private GameObject scythe;

    private BuffController buffHolder;
    private float attackCounter;

    void Start()
    {
        attackCounter = 0f;
        ResolveOwner();
        if (player != null)
            buffHolder = player.GetComponent<BuffController>();
    }

    void Update()
    {
        if (player == null || !player.isActiveAndEnabled || !World.CanInteract(this, player))
            return;

        attackCounter -= Time.deltaTime;
        if (attackCounter <= 0)
        {
            if (scythe == null || scythe.GetComponent<ScytheHitController>() == null)
            {
                Debug.LogError("Scythe requires a prefab with ScytheHitController on its root.", this);
                enabled = false;
                return;
            }

            // Preserve the upstream single swing; only count buffs add extra swings.
            float damage = attackDamage * stats.damage;
            int count = 1;
            if (buffHolder != null)
            {
                damage = buffHolder.CalculateWeaponDamage(damage);
                count = buffHolder.CalculateProjectileCount(count);
            }

            Vector2 facingDirection = player.facingDirection;
            if (facingDirection.sqrMagnitude == 0f)
                facingDirection = Vector2.right;

            for (int i = 0; i < count; i++)
            {
                // Fan additional swings evenly around the source, retaining its facing for the first.
                Vector2 direction = Quaternion.Euler(0f, 0f, 360f * i / count) * facingDirection;
                GameObject newScythe = Instantiate(scythe, player.transform.position,
                    Quaternion.identity, World.GetContentRoot(this));
                ScytheHitController swing = newScythe.GetComponent<ScytheHitController>();
                swing.SetSource(player, buffHolder);
                swing.SetDamage(damage);
                swing.SetArea(area * stats.area);
                swing.SetDirection(direction);
            }

            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}
