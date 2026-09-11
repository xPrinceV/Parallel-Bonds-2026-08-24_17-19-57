using UnityEngine;

public class ScytheHitbox : MonoBehaviour
{
    private ScytheHitController scytheController;

    void Awake()
    {
        scytheController = GetComponentInParent<ScytheHitController>();
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (isActiveAndEnabled && scytheController != null)
            scytheController.HitEnemy(collision);
    }
}
