using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(110)]
[DisallowMultipleComponent]
public sealed class FusionTransitionPresentation : MonoBehaviour
{
    [SerializeField] private FusionTransitionController controller;
    [SerializeField] private WorldFlipPresentation worldFlipPresentation;
    [SerializeField] private Shader whiteShader;

    private static readonly int WhiteAmountId = Shader.PropertyToID("_WhiteAmount");

    private readonly Dictionary<Renderer, bool> hidden = new Dictionary<Renderer, bool>();
    private readonly List<Renderer> renderers = new List<Renderer>();
    private Camera renderingCamera;
    private Canvas canvas;
    private RectTransform viewport;
    private Image tint;
    private Image portrait;
    private Material material;
    private bool reportedFailure;

    public void Configure(FusionTransitionController transition, WorldFlipPresentation flip, Shader shader)
    {
        ReleaseResources();
        controller = transition;
        worldFlipPresentation = flip;
        whiteShader = shader;
        reportedFailure = false;
    }

    private bool Playing => controller != null && controller.isActiveAndEnabled && controller.IsPlaying
        && worldFlipPresentation != null && worldFlipPresentation.isActiveAndEnabled
        && worldFlipPresentation.IsCapturing;

    private void OnEnable()
    {
        reportedFailure = false;
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
        RenderPipelineManager.endFrameRendering += EndFrameRendering;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }

    private void LateUpdate()
    {
        // Also recover if a pipeline aborted before its matching end callback.
        RestoreRenderers();
        if (!Playing)
        {
            if (canvas != null)
                canvas.enabled = false;
            return;
        }

        Camera camera = worldFlipPresentation.WorldCamera;
        if (camera == null || !camera.orthographic || whiteShader == null || !whiteShader.isSupported)
        {
            if (!reportedFailure)
                Debug.LogWarning("Fusion presentation requires an orthographic world camera and an explicitly assigned supported FusionPortrait shader.", this);
            reportedFailure = true;
            if (canvas != null)
                canvas.enabled = false;
            return;
        }

        bool fusionFace = controller.ShowFusionPortrait;
        PlayerController player = fusionFace ? controller.EntryPlayer
            : controller.DisplayWorld != null ? controller.DisplayWorld.Player : null;
        PlayerFusionVisual visual = player != null ? player.GetComponent<PlayerFusionVisual>() : null;
        Sprite sprite = visual == null ? null : fusionFace ? visual.FusionSprite : visual.NormalSprite;
        if (sprite == null)
        {
            if (canvas != null)
                canvas.enabled = false;
            return;
        }

        EnsureOverlay();
        canvas.targetDisplay = camera.targetDisplay;
        viewport.anchorMin = camera.rect.min;
        viewport.anchorMax = camera.rect.max;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;

        float reveal = Unit(controller.RevealProgress);
        Color ambient = controller.DisplayWorld != null ? controller.DisplayWorld.AmbientColor : Color.clear;
        ambient.a *= 1f - reveal;
        tint.color = ambient;
        portrait.sprite = sprite;
        // White is applied to sampled RGB, not multiplied over the sprite by Image.color.
        material.SetFloat(WhiteAmountId, Unit(controller.WhiteAmount));
        // keep one opaque body until handoff; fading before restoring it would leave a gap
        portrait.color = Color.white;

        int displayHeight = Screen.height;
        int display = camera.targetDisplay;
        if (display > 0 && display < Display.displays.Length)
            displayHeight = Display.displays[display].renderingHeight;
        float pixelHeight = displayHeight * camera.rect.height;
        float height = Mathf.Max(0f, visual.BodyWorldHeight) * pixelHeight
            / Mathf.Max(0.0001f, 2f * camera.orthographicSize);
        height *= Mathf.Lerp(1.4f, 1f, Mathf.SmoothStep(0f, 1f, reveal));
        portrait.rectTransform.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
        // Stay centered during flips, then align with the real sprite for a seamless handoff.
        Vector3 bodyCenter = camera.WorldToViewportPoint(visual.BodyWorldCenter);
        Vector2 finalOffset = new Vector2((bodyCenter.x - 0.5f) * pixelHeight * camera.aspect,
            (bodyCenter.y - 0.5f) * pixelHeight);
        portrait.rectTransform.anchoredPosition = Vector2.Lerp(Vector2.zero, finalOffset, Mathf.SmoothStep(0f, 1f, reveal));
        canvas.enabled = true;
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!Playing || canvas == null || !canvas.enabled || camera != worldFlipPresentation.WorldCamera)
            return;
        RestoreRenderers();
        renderingCamera = camera;
        try
        {
            if (controller.IsFlipping)
            {
                World display = controller.DisplayWorld;
                if (display == controller.EntryWorld)
                    HideWorld(controller.OtherWorld);
                else if (display == controller.OtherWorld)
                    HideWorld(controller.EntryWorld);
            }
            HideBody(controller.EntryWorld != null ? controller.EntryWorld.Player : controller.EntryPlayer);
            HideBody(controller.OtherWorld != null ? controller.OtherWorld.Player : null);
        }
        catch
        {
            RestoreRenderers();
            throw;
        }
    }

    private void HideWorld(World world)
    {
        if (world == null || world.ContentRoot == null)
            return;
        renderers.Clear();
        // Query each render so newly spawned enemies/projectiles are included, without caching stale roots.
        world.ContentRoot.GetComponentsInChildren<Renderer>(true, renderers);
        foreach (Renderer renderer in renderers)
            Hide(renderer);
    }

    private void HideBody(PlayerController player)
    {
        if (player == null)
            return;
        renderers.Clear();
        player.GetComponentsInChildren<Renderer>(true, renderers);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.GetComponentInParent<Weapon>(true) == null)
                Hide(renderer);
        }
    }

    private void Hide(Renderer renderer)
    {
        if (renderer == null || hidden.ContainsKey(renderer))
            return;
        hidden.Add(renderer, renderer.forceRenderingOff);
        renderer.forceRenderingOff = true;
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == renderingCamera)
            RestoreRenderers();
    }

    private void EndFrameRendering(ScriptableRenderContext context, Camera[] cameras)
    {
        RestoreRenderers();
    }

    private void SceneUnloaded(Scene scene)
    {
        RestoreRenderers();
        if (!Playing)
            ReleaseResources();
    }

    private void RestoreRenderers()
    {
        foreach (KeyValuePair<Renderer, bool> pair in hidden)
            if (pair.Key != null)
                pair.Key.forceRenderingOff = pair.Value;
        hidden.Clear();
        renderers.Clear();
        renderingCamera = null;
    }

    private void EnsureOverlay()
    {
        if (material == null)
            material = new Material(whiteShader)
            {
                name = "Fusion Portrait Material (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
        if (canvas != null)
            return;
        var root = new GameObject("Fusion Overlay (Runtime)", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.HideAndDontSave;
        canvas = root.GetComponent<Canvas>();
        canvas.enabled = false;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingLayerID = 0;
        canvas.sortingOrder = -50;
        canvas.pixelPerfect = false;
        var container = new GameObject("Camera Viewport", typeof(RectTransform));
        container.hideFlags = HideFlags.HideAndDontSave;
        viewport = container.GetComponent<RectTransform>();
        viewport.SetParent(root.transform, false);
        tint = CreateImage("World Tint", viewport);
        tint.rectTransform.anchorMin = Vector2.zero;
        tint.rectTransform.anchorMax = Vector2.one;
        tint.rectTransform.offsetMin = Vector2.zero;
        tint.rectTransform.offsetMax = Vector2.zero;
        portrait = CreateImage("Fusion Portrait", viewport);
        portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        portrait.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        portrait.rectTransform.anchoredPosition = Vector2.zero;
        portrait.preserveAspect = true;
        portrait.material = material;
    }

    private static Image CreateImage(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.hideFlags = HideFlags.HideAndDontSave;
        child.transform.SetParent(parent, false);
        Image image = child.GetComponent<Image>();
        image.raycastTarget = false;
        image.maskable = false;
        return image;
    }

    private void ReleaseResources()
    {
        RestoreRenderers();
        if (canvas != null)
        {
            canvas.enabled = false;
            DestroyRuntimeObject(canvas.gameObject);
        }
        if (material != null)
            DestroyRuntimeObject(material);
        canvas = null;
        viewport = null;
        tint = null;
        portrait = null;
        material = null;
    }

    private static float Unit(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }

    private static void DestroyRuntimeObject(Object value)
    {
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }

    private void OnDisable()
    {
        // ending presentation does not undo fusion, but must not leave an orphaned flip clock
        if (controller != null && controller.IsPlaying)
            controller.Cancel();
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        RenderPipelineManager.endFrameRendering -= EndFrameRendering;
        SceneManager.sceneUnloaded -= SceneUnloaded;
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }
}
