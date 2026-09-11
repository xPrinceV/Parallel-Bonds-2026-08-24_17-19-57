using UnityEngine;

[DefaultExecutionOrder(-100)]
public class WorldManager : MonoBehaviour
{
    [SerializeField] private WorldId initialWorldId;
    [SerializeField] private World[] worlds;
    [SerializeField] private KeyCode fusionKey = KeyCode.F;

    // ensure the world id only can be set internally
    public WorldId CurrentWorldId { get; private set; }

    // security check for world id validity and switching state
    public bool IsSwitching { get; private set; }
    public bool IsInitialized => isInitialized;
    public World CurrentWorld => FindWorld(CurrentWorldId);

    public bool IsFused { get; private set; }
    public PlayerController FusionPlayer { get; private set; }

    private bool isInitialized;
    private bool hasDied;
    private World secondaryWorld;
    private GameObject secondaryContentRoot;
    private PlayerController secondaryPlayer;
    private bool isEndingFusion;
    private Vector3 secondaryPosition;
    private Quaternion secondaryRotation;
    private Vector3 secondaryScale;
    private Vector2 secondaryFacing;
    private Renderer[] secondaryRenderers;
    private bool[] rendererStates;
    private Collider2D[] secondaryColliders;
    private bool[] colliderStates;
    private Rigidbody2D secondaryBody;
    private RigidbodyConstraints2D secondaryConstraints;
    private Vector2 secondaryVelocity;
    private float secondaryAngularVelocity;

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

        foreach (World world in worlds)
            world.Manager = this;

        IsSwitching = true;
        try
        {
            // use the initial hero's health settings for the whole run
            World initialWorld = FindWorld(initialWorldId);
            if (initialWorld.Player != null)
            {
                PlayerHealth sharedHealth = initialWorld.Player.GetComponent<PlayerHealth>();
                foreach (World world in worlds)
                    world.Player.GetComponent<PlayerHealth>().ShareHealthWith(sharedHealth);
            }

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
                || (world.Manager != null && world.Manager != this)
                || (world.Player == null) != (worlds[0].Player == null)
                || (world.Player != null && world.Player.GetComponent<PlayerHealth>() == null))
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

    private bool CanToggleFusion()
    {
        return isInitialized && isActiveAndEnabled && !IsSwitching && !hasDied
            && Time.timeScale > 0f && (UIController.instance == null
                || UIController.instance.levelUpPanel == null
                || !UIController.instance.levelUpPanel.activeSelf);
    }

    private void Update()
    {
        if (IsFused && (FusionPlayer == null || !FusionPlayer.isActiveAndEnabled
            || FusionPlayer.GetComponent<PlayerHealth>().currentHealth <= 0f
            || secondaryWorld == null || !secondaryWorld.IsActive
            || secondaryWorld.Player == null || !secondaryWorld.Player.isActiveAndEnabled
            || CurrentWorld == null || !CurrentWorld.IsActive))
        {
            if (FusionPlayer != null && FusionPlayer.GetComponent<PlayerHealth>().currentHealth <= 0f)
                HandlePlayerDeath();
            else
                EndFusion();
        }

        if (Input.GetKeyDown(fusionKey))
        {
            if (IsFused)
                TryExitFusion();
            else
                TryEnterFusion();
        }
    }

    private void FixedUpdate()
    {
        SyncFusionPlayer();
    }

    private void LateUpdate()
    {
        SyncFusionPlayer();
    }

    // weapons stay on their original hero; only the secondary body follows the entry hero
    internal void SyncFusionPlayer()
    {
        if (!IsFused || FusionPlayer == null || secondaryWorld == null || secondaryWorld.Player == null)
            return;

        Transform secondary = secondaryWorld.Player.transform;
        secondary.SetPositionAndRotation(FusionPlayer.transform.position, FusionPlayer.transform.rotation);
        if (secondaryBody != null)
        {
            secondaryBody.position = FusionPlayer.transform.position;
            secondaryBody.rotation = FusionPlayer.transform.eulerAngles.z;
        }
        secondaryWorld.Player.facingDirection = FusionPlayer.facingDirection;
    }

    public bool TryEnterFusion()
    {
        if (!CanToggleFusion() || IsFused || !ValidateWorlds() || worlds.Length != 2)
            return false;

        World entry = CurrentWorld;
        World secondary = worlds[0] == entry ? worlds[1] : worlds[0];
        if (entry == null || !entry.IsActive || secondary.IsActive
            || entry.Player == null || secondary.Player == null
            || !entry.Player.isActiveAndEnabled || !secondary.Player.enabled
            || !secondary.Player.gameObject.activeSelf
            || entry.Player.GetComponent<PlayerHealth>().currentHealth <= 0f
            || secondary.Player.GetComponent<PlayerHealth>().currentHealth <= 0f)
            return false;

        IsSwitching = true;
        bool succeeded = false;
        try
        {
            secondaryWorld = secondary;
            secondaryContentRoot = secondary.ContentRoot.gameObject;
            secondaryPlayer = secondary.Player;
            Transform secondaryTransform = secondary.Player.transform;
            secondaryPosition = secondaryTransform.position;
            secondaryRotation = secondaryTransform.rotation;
            secondaryScale = secondaryTransform.localScale;
            secondaryFacing = secondary.Player.facingDirection;
            secondaryRenderers = secondary.Player.GetComponentsInChildren<Renderer>(true);
            rendererStates = new bool[secondaryRenderers.Length];
            secondaryColliders = secondary.Player.GetComponentsInChildren<Collider2D>(true);
            colliderStates = new bool[secondaryColliders.Length];
            secondaryBody = secondary.Player.GetComponent<Rigidbody2D>();
            if (secondaryBody != null)
            {
                secondaryConstraints = secondaryBody.constraints;
                secondaryVelocity = secondaryBody.linearVelocity;
                secondaryAngularVelocity = secondaryBody.angularVelocity;
            }
            for (int i = 0; i < secondaryRenderers.Length; i++)
                rendererStates[i] = secondaryRenderers[i].enabled;
            for (int i = 0; i < secondaryColliders.Length; i++)
                colliderStates[i] = secondaryColliders[i].enabled;

            // publish ownership before OnEnable can bind the secondary hero's aliases
            FusionPlayer = entry.Player;
            IsFused = true;
            SyncFusionPlayer();
            for (int i = 0; i < secondaryRenderers.Length; i++)
            {
                if (secondaryRenderers[i].GetComponentInParent<Weapon>() == null)
                    secondaryRenderers[i].enabled = false;
            }
            for (int i = 0; i < secondaryColliders.Length; i++)
            {
                if (secondaryColliders[i].GetComponentInParent<Weapon>() == null)
                    secondaryColliders[i].enabled = false;
            }
            if (secondaryBody != null)
            {
                // keep weapon child colliders simulated while preventing secondary movement
                secondaryBody.linearVelocity = Vector2.zero;
                secondaryBody.angularVelocity = 0f;
                secondaryBody.constraints = RigidbodyConstraints2D.FreezeAll;
            }

            succeeded = secondary.SetWorldActive(true) && isInitialized && isActiveAndEnabled
                && IsFused && FusionPlayer == entry.Player && secondary.Player.isActiveAndEnabled && entry.IsActive;
            if (succeeded)
                FusionPlayer.BindAsCurrent();
            return succeeded;
        }
        finally
        {
            if (!succeeded)
                EndFusion();
            IsSwitching = false;
        }
    }

    public bool TryExitFusion()
    {
        if (!CanToggleFusion() || !IsFused)
            return false;

        IsSwitching = true;
        try
        {
            return EndFusion();
        }
        finally
        {
            IsSwitching = false;
        }
    }

    // forced cleanup also runs during pause, death and manager disable
    private bool EndFusion()
    {
        if (isEndingFusion)
            return false;
        if (!IsFused)
            return true;

        isEndingFusion = true;
        try
        {
            PlayerController entry = FusionPlayer;
            bool restored = secondaryWorld != null && secondaryContentRoot != null
                && secondaryWorld.ContentRoot == secondaryContentRoot.transform
                && secondaryWorld.SetWorldActive(false);
            bool secondaryAsleep = (secondaryContentRoot == null || !secondaryContentRoot.activeSelf)
                && (secondaryPlayer == null || !secondaryPlayer.gameObject.activeInHierarchy);
            if (!restored || !secondaryAsleep)
            {
                restored = false;
                isInitialized = false;
                // use the captured root even if the world configuration changed during fusion
                if (secondaryContentRoot != null)
                {
                    foreach (BuffController holder in secondaryContentRoot.GetComponentsInChildren<BuffController>(true))
                        holder.SetWorldSuspended(true);
                    secondaryContentRoot.SetActive(false);
                }
                secondaryAsleep = (secondaryContentRoot == null || !secondaryContentRoot.activeSelf)
                    && (secondaryPlayer == null || !secondaryPlayer.gameObject.activeInHierarchy);
                // OnDisable must not reenter cleanup while the fallback is in progress
                enabled = false;
                if (!secondaryAsleep)
                {
                    if (secondaryPlayer != null)
                        secondaryPlayer.gameObject.SetActive(false);
                    Debug.LogError("Fusion cleanup failed to sleep the secondary content; suppression retained and manager disabled.", this);
                    return false;
                }
                Debug.LogError("Fusion cleanup required the captured content root fallback; manager disabled.", this);
            }

            // restore the secondary body only after its content is safely asleep
            if (secondaryPlayer != null)
            {
                Transform secondary = secondaryPlayer.transform;
                secondary.SetPositionAndRotation(secondaryPosition, secondaryRotation);
                secondary.localScale = secondaryScale;
                secondaryPlayer.facingDirection = secondaryFacing;
            }
            for (int i = 0; secondaryRenderers != null && i < secondaryRenderers.Length; i++)
            {
                if (secondaryRenderers[i] != null && secondaryRenderers[i].GetComponentInParent<Weapon>() == null)
                    secondaryRenderers[i].enabled = rendererStates[i];
            }
            for (int i = 0; secondaryColliders != null && i < secondaryColliders.Length; i++)
            {
                if (secondaryColliders[i] != null && secondaryColliders[i].GetComponentInParent<Weapon>() == null)
                    secondaryColliders[i].enabled = colliderStates[i];
            }
            if (secondaryBody != null)
            {
                secondaryBody.position = secondaryPosition;
                secondaryBody.rotation = secondaryRotation.eulerAngles.z;
                secondaryBody.constraints = secondaryConstraints;
                secondaryBody.linearVelocity = secondaryVelocity;
                secondaryBody.angularVelocity = secondaryAngularVelocity;
            }
            IsFused = false;
            FusionPlayer = null;
            secondaryWorld = null;
            secondaryContentRoot = null;
            secondaryPlayer = null;
            secondaryRenderers = null;
            rendererStates = null;
            secondaryColliders = null;
            colliderStates = null;
            secondaryBody = null;
            if (!hasDied && entry != null)
                entry.BindAsCurrent();
            return restored;
        }
        finally
        {
            isEndingFusion = false;
        }
    }

    internal void HandlePlayerDeath()
    {
        PlayerController player = IsFused ? FusionPlayer : CurrentWorld == null ? null : CurrentWorld.Player;
        hasDied = true;
        EndFusion();
        //Trigger Lost Condition (SetActive to false is temporary)
        if (player != null)
            player.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (!isEndingFusion)
            EndFusion();
    }

    // use this function to switch to a new world
    public bool SwitchWorld(WorldId targetWorldId)
    {
        if (!isInitialized || !isActiveAndEnabled || IsSwitching || IsFused || hasDied)
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

            // transfer position only; health is shared, experience, upgrades and buffs stay with each hero
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
