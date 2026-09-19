using UnityEngine;
using System.Collections;

public class BossLightning : MonoBehaviour
{
    //warning time before strike of lightning
    public float warningTime = 0.75f;

    //length of attack
    public float strikeTime = 0.75f;

    private float damage;

    public GameObject warningObject;
    public GameObject strikeObject;

    private Collider2D damageCollider;

    void Awake()
    {
        damageCollider = GetComponent<Collider2D>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(Strike());
    }

    private IEnumerator Strike()
    {
        //warning 
        warningObject.SetActive(true);
        strikeObject.SetActive(false);

        damageCollider.enabled = false;
        
        yield return new WaitForSeconds(warningTime);

        //strike
        warningObject.SetActive(false);
        strikeObject.SetActive(true);

        damageCollider.enabled = true;

        yield return new WaitForSeconds(strikeTime);

        //End of strike
        damageCollider.enabled = false;
        Destroy(gameObject);
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }


    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PlayerHealth playerHealth = collision.GetComponent<PlayerHealth>();

            if (playerHealth != null)
            {
                playerHealth.DamageHandler(damage);

            }
        }
    }
}
