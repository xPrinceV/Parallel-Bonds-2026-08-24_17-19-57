using UnityEngine;
using UnityEngine.UI;

// temporary world tint draws below the shared HUD and never receives input
public sealed class WorldFilter : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField] private Image overlay;

    [SerializeField, Min(0f)] private float worldTransitionDuration = 0.45f;
    [SerializeField, Min(0f)] private float fusionTransitionDuration = 0.6f;
    [SerializeField] private Color fusionTint = new Color(0.32f, 0.46f, 0.52f, 1f);
    [SerializeField, Range(0f, 0.1f)] private float fusionTintStrength = 0.045f;

    public Color TargetColor { get; private set; }
    public bool IsTransitioning => elapsed < duration;

    private World displayedWorld;
    private bool displayedFusion;
    private bool initialized;
    private bool fusionTransition;
    private Color startColor;
    private float elapsed;
    private float duration;

    private void LateUpdate()
    {
        if (overlay == null)
            return;

        World world = worldManager != null && worldManager.IsInitialized ? worldManager.CurrentWorld : null;
        bool fused = world != null && worldManager.IsFused;
        Color target = world == null || fused ? Color.clear : world.AmbientColor;
        if (!initialized || world == null)
        {
            // loading a scene establishes its tint without an entry flash
            initialized = world != null;
            displayedWorld = world;
            displayedFusion = fused;
            TargetColor = target;
            elapsed = duration = 0f;
            overlay.color = target;
            return;
        }

        if (world != displayedWorld || fused != displayedFusion || target != TargetColor)
        {
            // retarget from the visible color so rapid toggles never stack animations
            startColor = overlay.color;
            fusionTransition = fused != displayedFusion;
            duration = Mathf.Max(0f, fusionTransition ? fusionTransitionDuration : worldTransitionDuration);
            elapsed = 0f;
            TargetColor = target;
            displayedWorld = world;
            displayedFusion = fused;
        }

        // presentation follows game time; pausing does not change combat or transition state
        elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
        float progress = duration > 0f ? elapsed / duration : 1f;
        Color tint = Color.Lerp(startColor, TargetColor, Mathf.SmoothStep(0f, 1f, progress));
        if (fusionTransition && progress < 1f)
        {
            // a soft envelope starts and ends at zero, without a white flash or blackout
            float envelope = 16f * progress * progress * (1f - progress) * (1f - progress);
            float pulseAlpha = Mathf.Clamp(fusionTintStrength, 0f, 0.1f) * envelope;
            float baseAlpha = tint.a * (1f - pulseAlpha);
            float alpha = baseAlpha + pulseAlpha;
            if (alpha > 0f)
            {
                tint = new Color(
                    (tint.r * baseAlpha + fusionTint.r * pulseAlpha) / alpha,
                    (tint.g * baseAlpha + fusionTint.g * pulseAlpha) / alpha,
                    (tint.b * baseAlpha + fusionTint.b * pulseAlpha) / alpha,
                    alpha);
            }
        }
        overlay.color = progress >= 1f ? TargetColor : tint;
    }

    private void OnDisable()
    {
        initialized = false;
        elapsed = duration = 0f;
        if (overlay != null)
            overlay.color = Color.clear;
    }
}
