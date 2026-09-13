using System.Collections.Generic;
using UnityEngine;

public class ShieldController : Weapon
{
    public GameObject shieldPrefab;
    [HideInInspector] public GameObject shieldPivot;
    public float orbitDistance;
    public float orbitSpeed;
    public float damage;
    public float duration;
    public float cooldown;
    [SerializeField] private BuffController buffHolder;

    private float timer;
    private bool isShieldActive;
    private readonly List<ShieldOrbitController> shields = new List<ShieldOrbitController>();

    internal bool CanAttack => isActiveAndEnabled && player != null && player.isActiveAndEnabled
        && World.CanInteract(this, player);

    void Start()
    {
        ResolveOwner();
        if (buffHolder == null && player != null)
            buffHolder = player.GetComponent<BuffController>();
        if (buffHolder == null)
            buffHolder = GetComponentInParent<BuffController>();
        if (shieldPrefab == null || shieldPrefab.GetComponent<ShieldOrbitController>() == null)
        {
            Debug.LogError("Shield requires a prefab with ShieldOrbitController on its root.", this);
            enabled = false;
            return;
        }
        if (CanAttack)
            SpawnShields();
    }

    void Update()
    {
        if (!CanAttack)
        {
            if (shieldPivot != null)
                shieldPivot.SetActive(false);
            return;
        }

        timer -= Time.deltaTime;
        if (isShieldActive && shieldPivot != null)
        {
            shieldPivot.transform.position = player.transform.position;
            if (!shieldPivot.activeSelf)
            {
                // OnEnable can precede world/player projection setup. Resume here, at the
                // owner's current position, before physics can deliver new contact callbacks.
                shieldPivot.SetActive(true);
                Physics2D.SyncTransforms();
                foreach (ShieldOrbitController shield in shields)
                    if (shield != null)
                        shield.ReconcileContacts();
            }
            shieldPivot.transform.Rotate(0f, 0f, orbitSpeed * stats.speed * Time.deltaTime);
        }
        if (timer <= 0f)
        {
            if (isShieldActive)
                DestroyShields();
            else
                SpawnShields();
        }
    }

    private void SpawnShields()
    {
        int count = Mathf.Max(0, Mathf.FloorToInt(stats.amount));
        float attackDamage = damage * stats.damage;
        if (buffHolder != null)
        {
            count = buffHolder.CalculateProjectileCount(count);
            attackDamage = buffHolder.CalculateWeaponDamage(attackDamage);
        }
        if (count <= 0)
        {
            timer = cooldown;
            return;
        }

        isShieldActive = true;
        shieldPivot = new GameObject("Shield Pivot");
        shieldPivot.transform.SetParent(World.GetContentRoot(this), false);
        shieldPivot.transform.position = player.transform.position;
        for (int i = 0; i < count; i++)
        {
            float radians = 2f * Mathf.PI * i / count;
            Vector3 offset = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * orbitDistance;
            GameObject newShield = Instantiate(shieldPrefab, shieldPivot.transform.position + offset,
                Quaternion.identity, shieldPivot.transform);
            ShieldOrbitController shield = newShield.GetComponent<ShieldOrbitController>();
            shield.Initialize(this, buffHolder, attackDamage);
            shields.Add(shield);
        }
        timer = duration * stats.duration;
    }

    private void DestroyShields()
    {
        // Despawn owns teardown (and may become a pool return); never bypass it for effects.
        for (int i = shields.Count - 1; i >= 0; i--)
        {
            ShieldOrbitController shield = shields[i];
            if (shield != null)
                shield.Despawn();
        }
        shields.Clear();
        FinishCycle();
    }

    internal void ReleaseShield(ShieldOrbitController shield)
    {
        shields.Remove(shield);
        if (shields.Count == 0)
            FinishCycle();
    }

    private void FinishCycle()
    {
        isShieldActive = false;
        if (shieldPivot != null)
        {
            shieldPivot.SetActive(false);
            Destroy(shieldPivot);
            shieldPivot = null;
        }
        timer = cooldown;
    }

    void OnDisable()
    {
        // Weapon disable/world sleep pauses this cycle; it must not recreate or reset it.
        if (shieldPivot != null)
            shieldPivot.SetActive(false);
    }


    void OnDestroy()
    {
        DestroyShields();
    }
}
