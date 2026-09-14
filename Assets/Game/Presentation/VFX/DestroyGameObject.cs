using UnityEngine;

public class DestroyGameObject : MonoBehaviour
{
    public float duration;
    private float remainingDuration;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        remainingDuration = duration;
    }

    // Update is called once per frame
    void Update()
    {
        // Inactive world content keeps its remaining VFX lifetime.
        remainingDuration -= Time.deltaTime;
        if (remainingDuration <= 0f)
            Destroy(gameObject);
    }
}
