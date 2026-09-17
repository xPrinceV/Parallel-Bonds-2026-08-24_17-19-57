using UnityEngine;

public class BossAttack : MonoBehaviour
{
    //Orb projectile
    public GameObject orbProjectile;
    public float orbSpeed = 8f;
    public int orbCount = 8;

    //the interval between the firing of the orbs
    public float orbFireInterval = 0.15f;
    //damage of the orbs
    public float orbDamage = 20f;

    //Lightning Projectile
    public GameObject lightningPrefab;

    //number of lightning strikes per attack
    public int lightningCount = 5;

    //damage of each lightning strike
    public float lightningDamage = 20f;

    //spawn area of the lightning
    public float lightningSpawnRange = 8f;

    //Minimum distance between lightning strikes
    public float lightningMinSpacing = 2f;

    private BossController bossController;

    void Awake()
    {
        bossController = GetComponent<BossController>();
    }


    public void OrbAttack()
    {
        Debug.Log("Orb Attack");

        //5 orbs around the boss spawns
        for (int i = 0; i < orbCount; i++)
        {
            float startingAngle = i * (360f / orbCount);

            //Each orb has a delay before firing
            float releaseDelay = i * orbFireInterval;

            GameObject newOrb = Instantiate(
                orbProjectile,
                transform.position,
                Quaternion.identity
            );

            //Script from the BossProjectiles
            BossProjectiles orb = newOrb.GetComponent<BossProjectiles>();

            orb.Setup(
                transform,
                PlayerController.instance.transform,
                orbDamage,
                orbSpeed,
                startingAngle,
                releaseDelay
            );
        }
    }

    public void LightningAttack()
    {
        Debug.Log("Lightning Attack");

        for (int i = 0; i < lightningCount; i++)
        {
            Vector2 randomPosition = 
                (Vector2)PlayerController.instance.transform.position +
                Random.insideUnitCircle * lightningSpawnRange;

            GameObject newLightning = Instantiate(
                lightningPrefab,
                randomPosition,
                Quaternion.identity
            );

            BossLightning lightning = 
                newLightning.GetComponent<BossLightning>();

            lightning.SetDamage(lightningDamage);
        }
    }
  /*  // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    } */
}
