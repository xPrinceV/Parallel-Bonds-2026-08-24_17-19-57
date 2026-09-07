using UnityEngine;
using UnityEngine.UI;

// temporary world tint draws below the shared HUD and never receives input
public sealed class WorldFilter : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField] private Image overlay;

    private void LateUpdate()
    {
        if (overlay == null)
            return;

        World world = worldManager != null && worldManager.IsInitialized ? worldManager.CurrentWorld : null;
        overlay.color = world == null ? Color.clear : world.AmbientColor;
    }
}
