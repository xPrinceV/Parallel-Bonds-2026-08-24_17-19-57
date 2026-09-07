using UnityEngine;

[DefaultExecutionOrder(-100)]
public class WorldManager : MonoBehaviour
{
    [SerializeField] private WorldId initialWorldId;
    [SerializeField] private World[] worlds;

    // ensure the world id only can be set internally
    public WorldId CurrentWorldId { get; private set; }

    // security check for world id validity and switching state
    public bool IsSwitching { get; private set; }
    public bool IsInitialized => isInitialized;
    public World CurrentWorld => FindWorld(CurrentWorldId);

    private bool isInitialized;

    // validate all worlds before changing the initial content state
    private void Awake()
    {
        if (!IsWorldValid(initialWorldId) || !ValidateWorlds()
            || FindWorld(initialWorldId) == null)
        {
            Debug.LogError("Invalid world configuration or missing initial world.", this);
            enabled = false;
            return;
        }

        IsSwitching = true;
        try
        {
            // disable other worlds before enabling the initial world
            foreach (World world in worlds)
            {
                if (world.WorldId != initialWorldId && !world.SetWorldActive(false))
                {
                    enabled = false;
                    return;
                }
            }

            // publish the id before activation callbacks read the current world
            CurrentWorldId = initialWorldId;
            if (!FindWorld(initialWorldId).SetWorldActive(true))
            {
                Debug.LogError("Failed to activate the initial world.", this);
                enabled = false;
                return;
            }

            isInitialized = true;
        }
        finally
        {
            IsSwitching = false;
        }
    }

    private static bool IsWorldValid(WorldId worldId)
    {
        return System.Enum.IsDefined(typeof(WorldId), worldId);
    }

    // security check for missing references, duplicate ids and overlapping content
    private bool ValidateWorlds()
    {
        if (worlds == null || worlds.Length == 0)
            return false;

        for (int i = 0; i < worlds.Length; i++)
        {
            World world = worlds[i];
            if (world == null || !world.IsConfigured || !world.gameObject.activeInHierarchy
                || world.ContainsContent(transform)
                || (world.Player == null) != (worlds[0].Player == null))
                return false;

            for (int j = 0; j < i; j++)
            {
                World other = worlds[j];
                if (world.WorldId == other.WorldId
                    || world.ContainsContent(other.transform)
                    || other.ContainsContent(world.transform))
                    return false;
            }
        }

        return true;
    }

    // find the configured world with the given id
    private World FindWorld(WorldId worldId)
    {
        if (worlds == null)
            return null;

        foreach (World world in worlds)
        {
            if (world != null && world.WorldId == worldId)
                return world;
        }

        return null;
    }

    // keep automatic switching outside content that this manager can disable
    public bool IsSharedObject(Transform target)
    {
        if (target == null || worlds == null)
            return false;

        foreach (World world in worlds)
        {
            if (world == null || world.ContainsContent(target))
                return false;
        }

        return true;
    }

    // use this function to switch to a new world
    public bool SwitchWorld(WorldId targetWorldId)
    {
        if (!isInitialized || !isActiveAndEnabled || IsSwitching)
            return false;
        if (!IsWorldValid(targetWorldId) || targetWorldId == CurrentWorldId)
            return false;
        // do not transfer control while an upgrade selection belongs to the current hero
        if (Time.timeScale <= 0f || (UIController.instance != null
            && UIController.instance.levelUpPanel != null && UIController.instance.levelUpPanel.activeSelf))
            return false;
        if (!ValidateWorlds())
        {
            Debug.LogError("World configuration is no longer valid.", this);
            return false;
        }

        World currentWorld = FindWorld(CurrentWorldId);
        World targetWorld = FindWorld(targetWorldId);
        if (currentWorld == null || targetWorld == null)
        {
            Debug.LogError("The current or target world is not configured.", this);
            return false;
        }

        Vector3 previousTargetPosition = targetWorld.Player == null ? Vector3.zero : targetWorld.Player.transform.position;
        if (currentWorld.Player != null && targetWorld.Player != null)
        {
            PlayerHealth health = currentWorld.Player.GetComponent<PlayerHealth>();
            PlayerHealth targetHealth = targetWorld.Player.GetComponent<PlayerHealth>();
            if (!currentWorld.Player.gameObject.activeInHierarchy
                || (health != null && health.currentHealth <= 0f)
                || !targetWorld.Player.gameObject.activeSelf
                || (targetHealth != null && targetHealth.HasInitialized && targetHealth.currentHealth <= 0f))
                return false;

            // transfer position only; health, experience, upgrades and buffs stay with each hero
            targetWorld.Player.transform.position = currentWorld.Player.transform.position;
            Rigidbody2D body = targetWorld.Player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
        }

        WorldId previousWorldId = CurrentWorldId;
        bool succeeded = false;
        IsSwitching = true;
        try
        {
            if (!currentWorld.SetWorldActive(false))
                return false;

            // target activation callbacks should observe the target world id
            CurrentWorldId = targetWorldId;
            succeeded = targetWorld.SetWorldActive(true) && !currentWorld.IsActive;
            return succeeded;
        }
        finally
        {
            try
            {
                // restore the previous world if activation did not complete
                if (!succeeded)
                {
                    bool targetDisabled = targetWorld.SetWorldActive(false);
                    CurrentWorldId = previousWorldId;
                    if (targetWorld.Player != null)
                        targetWorld.Player.transform.position = previousTargetPosition;
                    bool previousRestored = currentWorld.SetWorldActive(true);
                    Debug.LogError("World switch failed; restoring the previous world.", this);
                    if (!targetDisabled || !previousRestored)
                    {
                        isInitialized = false;
                        enabled = false;
                        Debug.LogError("World recovery failed; the manager has been disabled.", this);
                    }
                }
            }
            finally
            {
                IsSwitching = false;
            }
        }
    }
}
