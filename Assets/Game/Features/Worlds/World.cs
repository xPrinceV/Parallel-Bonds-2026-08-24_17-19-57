using UnityEngine;

// declare world class
public class World : MonoBehaviour
{
    [SerializeField] private WorldId worldId;
    // this declares the child object that the script will use to enable or disable world content
    [SerializeField] private GameObject contentRoot;
    // store the world color for the presentation system to apply
    [SerializeField] private Color ambientColor = Color.white;


    // allow other classes to read the world settings without changing them
    public WorldId WorldId => worldId;
    public Color AmbientColor => ambientColor;
    // check the actual active state including the parent hierarchy
    public bool IsActive => contentRoot != null && contentRoot.activeInHierarchy;

    // security check for world id validity and content ownership
    // require a direct child so inactive intermediate parents cannot block activation
    public bool IsConfigured =>
        System.Enum.IsDefined(typeof(WorldId), worldId)
        && contentRoot != null
        && contentRoot != gameObject
        && contentRoot.transform.parent == transform;

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

        contentRoot.SetActive(active);
        return IsActive == active;
    }
}
