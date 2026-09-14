using UnityEngine;
using UnityEngine.UI;

// temporary world tint draws below the shared HUD and never receives input
public sealed class WorldFilter : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField] private Image overlay;

    [SerializeField, Min(0f)] private float worldTransitionDuration = 0.45f;
    [SerializeField, Min(0f)] private float fusionTransitionDuration = 0.6f;
        [SerializeField, Range(0f, 0.1f)] private float worldTintStrength = 0.06f;
            [SerializeField] private Color materialTransitionTint = new Color(0.65f, 0.52f, 0.32f, 1f);
    [SerializeField] private Color fusionTint = new Color(0.32f, 0.46f, 0.52f, 1f);
    [SerializeField, Range(0f, 0.1f)] private float fusionTintStrength = 0.045f;

    public Color TargetColor { get; private set; }
    public bool IsTransitioning => elapsed < duration;

    private World displayedWorld;
    private bool displayedFusion;
    private bool initialized;
    private bool fusionTransition;
        private Color pulseColor;
        private WorldManager subscribedManager;
    private Color startColor;
    private float elapsed;
    private float duration;

    private void OnEnable()
    {
        Subscribe();
    }

    // both gameplay and debug controls publish the same committed-state events
    private void Subscribe()
    {
        if (subscribedManager == worldManager)
            return;
        Unsubscribe();
        subscribedManager = worldManager;
        if (subscribedManager == null)
            return;
        subscribedManager.WorldChanged += PlayWorldTransition;
        subscribedManager.FusionStateChanged += PlayFusionTransition;
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
        {
            subscribedManager.WorldChanged -= PlayWorldTransition;
            subscribedManager.FusionStateChanged -= PlayFusionTransition;
        }
        subscribedManager = null;
    }

    private void PlayWorldTransition()
    {
        PlayTransition(false);
    }

    private void PlayFusionTransition()
    {
        PlayTransition(true);
    }

    // the animation observes state; it never delays or changes the gameplay transition
    private void PlayTransition(bool fusion)
    {
        if (overlay == null || worldManager == null || !worldManager.IsInitialized)
            return;
        World world = worldManager.CurrentWorld;
        if (world == null)
            return;

        startColor = overlay.color;
        fusionTransition = fusion;
        duration = Mathf.Max(0f, fusion ? fusionTransitionDuration : worldTransitionDuration);
        elapsed = 0f;
        displayedWorld = world;
        displayedFusion = worldManager.IsFused;
        TargetColor = displayedFusion ? Color.clear : world.AmbientColor;
        // the untinted material world still gets a warm, temporary transition cue
                pulseColor = displayedFusion ? fusionTint
                    : world.WorldId == WorldId.Material ? materialTransitionTint : world.AmbientColor;
        initialized = true;
    }

    private void LateUpdate()
    {
        Subscribe();
        if (overlay == null)
            return;

        // the entrance owns alternating tints; do not stack the older fusion pulse on it
        if (worldManager != null && worldManager.IsFusionTransitioning)
        {
            initialized = false;
            elapsed = duration = 0f;
            TargetColor = overlay.color = Color.clear;
            return;
        }
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
            PlayTransition(fused != displayedFusion);
        }

        // presentation follows game time; pausing does not change combat or transition state
        elapsed = Mathf.Min(duration, elapsed + Time.deltaTime);
        float progress = duration > 0f ? elapsed / duration : 1f;
        Color tint = Color.Lerp(startColor, TargetColor, Mathf.SmoothStep(0f, 1f, progress));
        if (progress < 1f)
        {
            // a soft envelope starts and ends at zero, without a white flash or blackout
            float envelope = 16f * progress * progress * (1f - progress) * (1f - progress);
            float strength = fusionTransition ? fusionTintStrength : worldTintStrength;
                        float pulseAlpha = Mathf.Clamp(strength, 0f, 0.1f) * envelope;
            float baseAlpha = tint.a * (1f - pulseAlpha);
            float alpha = baseAlpha + pulseAlpha;
            if (alpha > 0f)
            {
                tint = new Color(
                    (tint.r * baseAlpha + pulseColor.r * pulseAlpha) / alpha,
                    (tint.g * baseAlpha + pulseColor.g * pulseAlpha) / alpha,
                    (tint.b * baseAlpha + pulseColor.b * pulseAlpha) / alpha,
                    alpha);
            }
        }
        overlay.color = progress >= 1f ? TargetColor : tint;
    }

    private void OnDisable()
    {
        Unsubscribe();
        initialized = false;
        elapsed = duration = 0f;
        if (overlay != null)
            overlay.color = Color.clear;
    }
}
