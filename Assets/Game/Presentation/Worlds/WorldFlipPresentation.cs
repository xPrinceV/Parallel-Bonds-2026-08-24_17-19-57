    using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// the world capture draws below WorldFilter (-100) and the screen-space HUD (0)
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class WorldFlipPresentation : MonoBehaviour
{
    [SerializeField] private StateSwitchController stateSwitchController;
    [SerializeField] private FusionTransitionController fusionTransition;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Shader worldFlipShader;
    [SerializeField, Range(0f, 8f)] private float blurMaxPixels = 2f;
    [SerializeField, Range(0f, 16f)] private float shakeAmplitudePixels = 0f;
    [SerializeField, Min(0f)] private float shakeFrequency = 12f;
    [SerializeField, Range(0.01f, 0.1f)] private float minimumHorizontalScale = 0.025f;
    [SerializeField, Range(0f, 8f)] private float backgroundBlurPixels = 4f;
    [SerializeField, Range(0.1f, 1f)] private float backgroundBrightness = 0.65f;

    public Camera WorldCamera => worldCamera;
    public bool IsCapturing => capturing && runtimeCanvas != null && runtimeCanvas.enabled;
    public float BlurPixels { get; private set; }
    public float HorizontalScale { get; private set; } = 1f;

    private static readonly int BlurPixelsId = Shader.PropertyToID("_BlurPixels");
    private static readonly int HorizontalScaleId = Shader.PropertyToID("_HorizontalScale");
    private static readonly int ShakeUvId = Shader.PropertyToID("_ShakeUV");
    private static readonly int BackgroundBlurId = Shader.PropertyToID("_BackgroundBlurPixels");
    private static readonly int BackgroundBrightnessId = Shader.PropertyToID("_BackgroundBrightness");

    private Camera capturedCamera;
    private RenderTexture originalTarget;
    private RenderTexture capture;
    private Material runtimeMaterial;
    private Canvas runtimeCanvas;
    private RawImage worldImage;
    private bool capturing;
    private bool reportedFailure;
    private Camera displayCamera;

    // the shader must also be serialized by the scene/bootstrap; no name-only build lookup
    public void Configure(StateSwitchController controller, Camera camera, Shader shader)
    {
        ReleaseResources();
        stateSwitchController = controller;
        worldCamera = camera;
        worldFlipShader = shader;
        reportedFailure = false;
    }

    public void ConfigureFusion(FusionTransitionController controller)
    {
        fusionTransition = controller;
    }

    private void OnEnable()
    {
        reportedFailure = false;
    }

    private void LateUpdate()
    {
        if (capturing && capturedCamera != worldCamera)
            StopCapture();

        bool fusionPlaying = fusionTransition != null && fusionTransition.isActiveAndEnabled
            && fusionTransition.IsPlaying;
        if ((!fusionPlaying && (stateSwitchController == null || !stateSwitchController.isActiveAndEnabled))
            || worldCamera == null || !worldCamera.isActiveAndEnabled || reportedFailure)
        {
            StopCapture();
            return;
        }

        float warning = fusionPlaying ? 0f : FiniteClamp(stateSwitchController.WarningProgress, 0f, 1f);
        float progress = fusionPlaying ? FiniteClamp(fusionTransition.Turns, 0f, 6f)
            : FiniteClamp(stateSwitchController.FlipProgress, 0f, 1f);
        bool flipping = fusionPlaying ? fusionTransition.IsFlipping : stateSwitchController.IsFlipping;
        float envelope = flipping && progress >= 0.5f
            ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.5f) * 2f))
            : warning;
        BlurPixels = FiniteClamp(blurMaxPixels, 0f, 8f) * envelope;
        // smooth the absolute cosine, not the signed cosine: keep the midpoint edge-on
        HorizontalScale = flipping
            ? Mathf.Lerp(FiniteClamp(minimumHorizontalScale, 0.01f, 0.1f), 1f,
                Mathf.SmoothStep(0f, 1f, Mathf.Abs(Mathf.Cos(Mathf.PI * progress))))
            : 1f;
        float shakePixels = FiniteClamp(shakeAmplitudePixels, 0f, 16f) * envelope;
        if (fusionPlaying)
        {
            float reveal = FiniteClamp(fusionTransition.RevealProgress, 0f, 1f);
            HorizontalScale = flipping ? Mathf.Max(0.025f, Mathf.Abs(Mathf.Cos(Mathf.PI * progress))) : 1f;
            BlurPixels = Mathf.Min(1f, FiniteClamp(blurMaxPixels, 0f, 8f))
                * (1f - HorizontalScale) * (1f - reveal);
            shakePixels = 0f;
        }
        // Fusion needs a capture even face-on: its render callback selects worlds and hides bodies.
        if (!fusionPlaying && BlurPixels <= 0f && HorizontalScale >= 1f && shakePixels <= 0f)
        {
            StopCapture();
            return;
        }

        if (worldFlipShader == null || !worldFlipShader.isSupported)
        {
            Fail("Assign a supported WorldFlip shader explicitly in the scene/bootstrap.");
            return;
        }

        EnsureOverlay();
        // preserve a non-null pre-existing target as well as the ordinary backbuffer target
        if (!capturing)
        {
            capturedCamera = worldCamera;
            originalTarget = worldCamera.targetTexture;
            capturing = true;
        }

        Rect viewport = originalTarget != null ? new Rect(0f, 0f, 1f, 1f) : worldCamera.rect;
        int displayWidth = Screen.width;
        int displayHeight = Screen.height;
        int display = worldCamera.targetDisplay;
        if (display > 0 && display < Display.displays.Length)
        {
            displayWidth = Display.displays[display].renderingWidth;
            displayHeight = Display.displays[display].renderingHeight;
        }
        int width = originalTarget != null ? originalTarget.width : Mathf.RoundToInt(displayWidth * viewport.width);
        int height = originalTarget != null ? originalTarget.height : Mathf.RoundToInt(displayHeight * viewport.height);
        if (width <= 0 || height <= 0)
        {
            StopCapture();
            return;
        }
        if (!EnsureCapture(width, height))
            return;

        runtimeCanvas.targetDisplay = display;
        RectTransform imageRect = worldImage.rectTransform;
        imageRect.anchorMin = viewport.min;
        imageRect.anchorMax = viewport.max;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        float phase = Time.time * FiniteClamp(shakeFrequency, 0f, 60f) * (2f * Mathf.PI);
        // offsets stay in source pixels and never touch the follow camera or its projection
        Vector4 shakeUv = new Vector4(Mathf.Sin(phase) * shakePixels / width,
            Mathf.Sin(phase * 1.37f) * shakePixels / height, 0f, 0f);
        runtimeMaterial.SetFloat(BlurPixelsId, BlurPixels);
        runtimeMaterial.SetFloat(HorizontalScaleId, HorizontalScale);
        runtimeMaterial.SetVector(ShakeUvId, shakeUv);
        runtimeMaterial.SetFloat(BackgroundBlurId, fusionPlaying ? BlurPixels : FiniteClamp(backgroundBlurPixels, 0f, 8f));
        runtimeMaterial.SetFloat(BackgroundBrightnessId, fusionPlaying ? 1f : FiniteClamp(backgroundBrightness, 0.1f, 1f));
        capturedCamera.targetTexture = capture;
        worldImage.texture = capture;
        runtimeCanvas.enabled = true;
        // keep a real backbuffer camera so Game View and URP can present overlay UI
        displayCamera.targetDisplay = display;
        displayCamera.rect = viewport;
        displayCamera.depth = capturedCamera.depth + 1f;
        displayCamera.enabled = true;
    }

    private void EnsureOverlay()
    {
        if (runtimeMaterial != null && runtimeMaterial.shader != worldFlipShader)
        {
            worldImage.material = null;
            DestroyRuntimeObject(runtimeMaterial);
            runtimeMaterial = null;
        }
        if (runtimeMaterial == null)
        {
            runtimeMaterial = new Material(worldFlipShader)
            {
                name = "World Flip Material (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        if (runtimeCanvas == null)
        {
            var canvasObject = new GameObject("World Flip Overlay (Runtime)", typeof(RectTransform), typeof(Canvas));
            canvasObject.hideFlags = HideFlags.HideAndDontSave;
            runtimeCanvas = canvasObject.GetComponent<Canvas>();
            runtimeCanvas.enabled = false;
            runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeCanvas.sortingLayerID = 0;
            runtimeCanvas.sortingOrder = -200;
            runtimeCanvas.pixelPerfect = false;

            var imageObject = new GameObject("World Capture", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.hideFlags = HideFlags.HideAndDontSave;
            imageObject.transform.SetParent(canvasObject.transform, false);
            worldImage = imageObject.GetComponent<RawImage>();
            worldImage.raycastTarget = false;
            worldImage.maskable = false;
            worldImage.color = Color.white;
            // no GraphicRaycaster or CanvasScaler: the capture uses display pixels

            var outputObject = new GameObject("World Flip Display Camera (Runtime)", typeof(Camera));
            outputObject.hideFlags = HideFlags.HideAndDontSave;
            outputObject.transform.SetParent(canvasObject.transform, false);
            displayCamera = outputObject.GetComponent<Camera>();
            displayCamera.enabled = false;
            displayCamera.cullingMask = 0;
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = Color.black;
            displayCamera.orthographic = true;
            displayCamera.allowHDR = false;
            displayCamera.allowMSAA = false;
            displayCamera.useOcclusionCulling = false;
            // no world geometry, post-processing or AudioListener is rendered twice
            var cameraData = displayCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
        }
        worldImage.material = runtimeMaterial;
    }

    private bool EnsureCapture(int width, int height)
    {
        if (capture != null && capture.width == width && capture.height == height && capture.IsCreated())
            return true;

        // detach before releasing the old size; the replacement is rendered later this frame
        capturedCamera.targetTexture = originalTarget;
        ReleaseCaptureTexture();
        capture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = "World Flip Capture (Runtime)",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            useDynamicScale = false
        };
        if (capture.Create())
            return true;
        Fail("Unable to create the WorldFlip render texture; the camera target has been restored.");
        return false;
    }

    private void Fail(string message)
    {
        if (!reportedFailure)
            Debug.LogWarning(message, this);
        reportedFailure = true;
        StopCapture();
    }

    private void StopCapture()
    {
        if (displayCamera != null)
            displayCamera.enabled = false;
        if (runtimeCanvas != null)
            runtimeCanvas.enabled = false;
        if (capturing && capturedCamera != null)
            capturedCamera.targetTexture = originalTarget;
        capturing = false;
        capturedCamera = null;
        originalTarget = null;
        ReleaseCaptureTexture();
        BlurPixels = 0f;
        HorizontalScale = 1f;
        if (runtimeMaterial != null)
            runtimeMaterial.SetVector(ShakeUvId, Vector4.zero);
    }

    private void ReleaseCaptureTexture()
    {
        if (worldImage != null)
            worldImage.texture = null;
        if (capture == null)
            return;
        capture.Release();
        DestroyRuntimeObject(capture);
        capture = null;
    }

    private void ReleaseResources()
    {
        StopCapture();
        if (runtimeCanvas != null)
            DestroyRuntimeObject(runtimeCanvas.gameObject);
        if (runtimeMaterial != null)
            DestroyRuntimeObject(runtimeMaterial);
        runtimeCanvas = null;
        displayCamera = null;
        worldImage = null;
        runtimeMaterial = null;
    }

    private static float FiniteClamp(float value, float min, float max)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? min : Mathf.Clamp(value, min, max);
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
        if (fusionTransition != null && fusionTransition.IsPlaying)
            fusionTransition.Cancel();
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }
}
