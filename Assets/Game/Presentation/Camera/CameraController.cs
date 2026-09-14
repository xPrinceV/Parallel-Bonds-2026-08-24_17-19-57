using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0f)] private float mapEdgePadding = 0.05f;

    private Camera viewCamera;
    private WorldMap framedMap;
    private Rect mapBounds;
    private bool hasMapBounds;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        viewCamera = GetComponent<Camera>();
    }

    // Update is called once per frame
    void Update()
    {
        //Make camera follow the player
        Transform followTarget = PlayerController.instance != null ? PlayerController.instance.transform : target;
        if (followTarget != null)
        {
            Vector3 position = new Vector3(followTarget.position.x, followTarget.position.y, transform.position.z);
            World world = World.GetFor(followTarget);
            WorldMap map = world == null ? null : world.Map;
            if (map != framedMap || !hasMapBounds || map == null)
            {
                // Cache valid static bounds, retry unavailable ones, and clear a destroyed map's bounds.
                framedMap = map;
                hasMapBounds = map != null && map.TryGetPlayableRect(out mapBounds);
            }
            if (hasMapBounds && viewCamera != null && viewCamera.orthographic)
            {
                // Keep the whole viewport inside the map, not only the camera's center.
                Vector3 right = transform.right * (viewCamera.orthographicSize * viewCamera.aspect);
                Vector3 up = transform.up * viewCamera.orthographicSize;
                float padding = float.IsNaN(mapEdgePadding) || float.IsInfinity(mapEdgePadding)
                    ? 0f : Mathf.Max(0f, mapEdgePadding);
                float halfWidth = Mathf.Abs(right.x) + Mathf.Abs(up.x) + padding;
                float halfHeight = Mathf.Abs(right.y) + Mathf.Abs(up.y) + padding;
                position.x = FitAxis(position.x, mapBounds.xMin, mapBounds.xMax, halfWidth);
                position.y = FitAxis(position.y, mapBounds.yMin, mapBounds.yMax, halfHeight);
            }
            transform.position = position;
        }
    }

    private static float FitAxis(float position, float minimum, float maximum, float extent)
    {
        // An oversized viewport stays centered; never zoom or change the player's position to make it fit.
        return maximum - minimum <= extent * 2f
            ? (minimum + maximum) * 0.5f
            : Mathf.Clamp(position, minimum + extent, maximum - extent);
    }
}
