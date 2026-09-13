using UnityEngine;

public class Player_Collision : MonoBehaviour
{
    // Set to "Building" layer in the Inspector
    public LayerMask collisionLayer;
    
    private Collider2D playerCollider;

    // Stores the last position in which the Player was in the safe position
    private Vector3 lastSafePosition;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Look for the Collider2D component attached to the player
        playerCollider = GetComponent<Collider2D>();
        // The starting position is considered a safe position
        lastSafePosition = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        Physics2D.SyncTransforms();
        // Only detects the collider assets
        ContactFilter2D filter = new ContactFilter2D();

        // View the selected layer in the collisionLayer
        filter.SetLayerMask(collisionLayer);
        
        //Layer filtering
        filter.useLayerMask = true;

        // Ignores the Trigger colliders
        filter.useTriggers = false;

        Collider2D[] results = new Collider2D[10];

        int hits = playerCollider.Overlap(filter, results);

        // The detection of the building will depend on the hits
        if (hits > 0)
        {
            transform.position = lastSafePosition;

            Physics2D.SyncTransforms();
        }
        else
        {
            // If the Player is not touching the building, the current positon = safe position
            lastSafePosition = transform.position;
        }
    }
}
