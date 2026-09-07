using UnityEngine;

// declare world class
public class World : MonoBehaviour
{
    [SerializeField] private WorldId worldId;
    // this declares the child object that the script will use to enable or disable world content
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private PlayerController player;
    // store the world color for the presentation system to apply
    [SerializeField] private Color ambientColor = Color.white;


    // allow other classes to read the world settings without changing them
    public WorldId WorldId => worldId;
    public PlayerController Player => player;
    public bool IsSuspended { get; private set; }
    public Transform ContentRoot => contentRoot == null ? null : contentRoot.transform;

    // runtime objects inherit their source world, never whichever world happens to be current
    public static World GetFor(Component source)
    {
        return source == null ? null : source.GetComponentInParent<World>();
    }

    public static Transform GetContentRoot(Component source)
    {
        World world = GetFor(source);
        return world == null ? null : world.ContentRoot;
    }
    public Color AmbientColor => ambientColor;
    // check the actual active state including the parent hierarchy
    public bool IsActive => contentRoot != null && contentRoot.activeInHierarchy;

    // security check for world id validity and content ownership
    // require a direct child so inactive intermediate parents cannot block activation
    public bool IsConfigured =>
        System.Enum.IsDefined(typeof(WorldId), worldId)
        && contentRoot != null
        && contentRoot != gameObject
        && contentRoot.transform.parent == transform
        && (player == null || player.transform.IsChildOf(contentRoot.transform));

    // prevent shared managers or other worlds from being disabled with this content
    public bool ContainsContent(Transform target)
    {
        return contentRoot != null && target != null
            && target.IsChildOf(contentRoot.transform);
    }

    // use this function to switch world content without disabling the controller
    // return false if the configuration or parent state prevents the request
    public bool SetWorldActive(bool active)
    {
        if (!IsConfigured)
        {
            Debug.LogError(
                "Invalid world configuration: use a defined WorldId and assign a direct child object as contentRoot.",
                this
            );
            return false;
        }

        // enabling content cannot activate an inactive parent hierarchy
        if (active && !gameObject.activeInHierarchy)
        {
            Debug.LogError("Cannot activate world content while its parent hierarchy is inactive.", this);
            return false;
        }

        IsSuspended = !active;
        // world sleep preserves buff instances; ordinary disable still ends them
        foreach (BuffController holder in contentRoot.GetComponentsInChildren<BuffController>(true))
            holder.SetWorldSuspended(!active);
        contentRoot.SetActive(active);
        if (active && player != null)
            player.BindAsCurrent();
        return IsActive == active;
    }
}
