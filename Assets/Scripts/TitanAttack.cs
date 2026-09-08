using UnityEngine;

public class TitanAttack : MonoBehaviour
{
    private bool channelFinished = false;
    public float damage;
    public float attackDelay = 2.5f;
    public GameObject effectAnimation;
    [SerializeField] private CircleCollider2D attackCollider;

    void Start()
    {
        attackCollider.enabled = false;
        Destroy(gameObject, 3f);
    }


    void Update()
    {
        attackDelay -= Time.deltaTime;
        if (attackDelay <= 0 && !channelFinished)
        {
            channelFinished = true;
            effectAnimation.SetActive(true);
            attackCollider.enabled = true;
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.tag == "Player")
        {
            collision.GetComponent<PlayerHealth>().DamageHandler(damage);
        }
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }
}
