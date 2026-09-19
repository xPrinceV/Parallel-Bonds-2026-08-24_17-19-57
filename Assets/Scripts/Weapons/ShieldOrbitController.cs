using UnityEngine;

public class ShieldOrbitController : MonoBehaviour
{
    public float damage;

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Enemy")
        {
            EnemyController enemy = collision.GetComponent<EnemyController>();
            if(enemy != null)
            {
                enemy.TakeDamage(damage, true);
                AudioManager.instance.PlaySFXPitch(AudioManager.instance.shield, 0.2f);
            }
        }       
    }

}
