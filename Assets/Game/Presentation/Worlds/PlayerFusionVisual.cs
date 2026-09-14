using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerFusionVisual : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private SpriteRenderer fusionRenderer;
    [SerializeField] private SpriteRenderer[] bodyRenderers;

    // entrance previews read sprites without enabling or replacing the gameplay renderers
    public Sprite NormalSprite
    {
        get
        {
            if (bodyRenderers != null)
                foreach (SpriteRenderer body in bodyRenderers)
                    if (body != null && body.gameObject.activeSelf && body.sprite != null)
                        return body.sprite;
            return null;
        }
    }
    public Sprite FusionSprite => fusionRenderer == null ? null : fusionRenderer.sprite;
    public float BodyWorldHeight => fusionRenderer == null ? 0f : fusionRenderer.bounds.size.y;
    public Vector3 BodyWorldCenter => fusionRenderer == null ? transform.position : fusionRenderer.bounds.center;

    private bool applied;
    private bool[] enabledStates;
    private WorldManager manager;

    private void OnEnable()
    {
        EnsureSubscription();
        RefreshVisual();
    }

    private void LateUpdate()
    {
        // Retry binding if this component enabled before its world's manager was assigned.
        EnsureSubscription();
        RefreshVisual();
    }

    private void EnsureSubscription()
    {
        World world = World.GetFor(player);
        WorldManager next = world == null ? null : world.Manager;
        if (manager == next)
            return;
        Unsubscribe();
        Restore();
        manager = next;
        if (manager != null)
            manager.FusionStateChanged += RefreshVisual;
    }

    private void Unsubscribe()
    {
        if (manager != null)
            manager.FusionStateChanged -= RefreshVisual;
        manager = null;
    }

    private void RefreshVisual()
    {
        bool show = player != null && player.isActiveAndEnabled && manager != null
            && manager.isActiveAndEnabled && manager.IsFused && manager.FusionPlayer == player;
        if (!show)
        {
            Restore();
            return;
        }
        if (fusionRenderer == null || bodyRenderers == null)
            return;

        if (!applied)
        {
            enabledStates = new bool[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                enabledStates[i] = bodyRenderers[i] != null && bodyRenderers[i].enabled;
            applied = true;
        }
        foreach (SpriteRenderer body in bodyRenderers)
            if (body != null)
                body.enabled = false;
        fusionRenderer.enabled = true;
    }

    private void OnDisable()
    {
        Unsubscribe();
        Restore();
    }

    private void Restore()
    {
        // A secondary hero never owns a snapshot; leave core suppression/restoration alone.
        // Also avoid replaying old flags after normal-state visual selection changes.
        if (!applied)
            return;
        applied = false;
        if (fusionRenderer != null)
            fusionRenderer.enabled = false;
        for (int i = 0; i < bodyRenderers.Length; i++)
            if (bodyRenderers[i] != null)
                bodyRenderers[i].enabled = enabledStates[i];
        enabledStates = null;
    }
}
