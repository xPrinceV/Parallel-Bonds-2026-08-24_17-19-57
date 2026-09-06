using UnityEngine;

public class StateSwitchController : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField, Min(0.1f)] private float timer = 15f;

    private float timerCounter;

    // wait until manager initialization has completed before starting the timer
    private void Start()
    {
        if (worldManager == null || !worldManager.IsInitialized
            || !worldManager.IsSharedObject(transform)
            || timer <= 0f || float.IsNaN(timer) || float.IsInfinity(timer))
        {
            Debug.LogError("Assign an initialized WorldManager, use a positive timer and place this controller outside world content.", this);
            enabled = false;
            return;
        }

        timerCounter = timer;
    }

    private void Update()
    {
        // pause the countdown while the manager is unavailable or game time is paused
        if (worldManager == null || !worldManager.IsInitialized
            || !worldManager.isActiveAndEnabled || worldManager.IsSwitching
            || Time.deltaTime <= 0f)
            return;

        timerCounter -= Time.deltaTime;
        if (timerCounter > 0f)
            return;

        // use the manager state instead of maintaining a separate world flag
        WorldId targetWorldId = worldManager.CurrentWorldId == WorldId.Material
            ? WorldId.Echo
            : WorldId.Material;

        if (!worldManager.SwitchWorld(targetWorldId))
            Debug.LogWarning("Automatic world switch was rejected.", this);

        // wait a full interval after failure to avoid retrying every frame
        timerCounter = timer;
    }
}
