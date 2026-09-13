using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// keep the shortcut listener active while the developer canvas is hidden
public sealed class DeveloperDebugGui : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField] private Canvas canvas;
    [SerializeField] private GraphicRaycaster raycaster;
    [SerializeField] private TMP_Text worldStatus;
    [SerializeField] private Button materialButton;
    [SerializeField] private Button echoButton;
    [SerializeField] private Button fusionButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private KeyCode toggleKey = KeyCode.BackQuote;

    public bool IsOpen { get; private set; }
    public bool IsAvailable
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    private void Awake()
    {
        materialButton.onClick.AddListener(SelectMaterial);
        echoButton.onClick.AddListener(SelectEcho);
        fusionButton.onClick.AddListener(ToggleFusion);
        closeButton.onClick.AddListener(Close);
        SetOpen(false);
    }

    private void Update()
    {
        if (!IsAvailable)
            return;

        // BackQuote handles both the bare key and Shift + key used to type a tilde
        if (Input.GetKeyDown(toggleKey))
            Toggle();
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            Close();
        if (!IsOpen)
            return;

        bool ready = worldManager != null && worldManager.IsInitialized && worldManager.isActiveAndEnabled;
        World world = ready ? worldManager.CurrentWorld : null;
        PlayerHealth health = world != null && world.Player != null ? world.Player.GetComponent<PlayerHealth>() : null;
        bool canAct = ready && !worldManager.IsFinalFusion && !worldManager.IsSwitching
            && !worldManager.IsWorldTransitioning && !worldManager.IsFusionTransitioning && Time.timeScale > 0f
            && health != null && !health.IsDead && health.isActiveAndEnabled
            && (UIController.instance == null || UIController.instance.levelUpPanel == null
                || !UIController.instance.levelUpPanel.activeInHierarchy);
        materialButton.gameObject.SetActive(!ready || !worldManager.IsFused);
        echoButton.gameObject.SetActive(!ready || !worldManager.IsFused);
        materialButton.interactable = canAct && !worldManager.IsFused && worldManager.CurrentWorldId != WorldId.Material;
        echoButton.interactable = canAct && !worldManager.IsFused && worldManager.CurrentWorldId != WorldId.Echo;
        fusionButton.interactable = canAct;
        worldStatus.text = world == null ? "World unavailable" : $"World: {world.WorldId} | Fusion: {(worldManager.IsFused ? "On" : "Off")}"
            + (health == null ? "" : $"\nShared HP: {health.currentHealth:0.#} / {health.maxHealth:0.#}");
    }

    public void Toggle()
    {
        SetOpen(!IsOpen);
    }

    public void SetOpen(bool open)
    {
        IsOpen = open && IsAvailable && isActiveAndEnabled;
        if (!IsOpen && EventSystem.current != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        if (canvas != null)
            canvas.enabled = IsOpen;
        if (raycaster != null)
            raycaster.enabled = IsOpen;
    }

    // the same runtime entry points own validation for shortcuts, buttons and gameplay
    private void SelectMaterial()
    {
        if (IsOpen && worldManager != null)
            worldManager.SwitchWorld(WorldId.Material);
    }

    private void SelectEcho()
    {
        if (IsOpen && worldManager != null)
            worldManager.SwitchWorld(WorldId.Echo);
    }

    private void ToggleFusion()
    {
        if (!IsOpen || worldManager == null || worldManager.IsFinalFusion)
            return;
        if (worldManager.IsFused)
            worldManager.TryExitFusion();
        else
            worldManager.TryEnterFusion();
    }

    private void Close()
    {
        SetOpen(false);
    }

    private void OnDisable()
    {
        SetOpen(false);
    }

    private void OnDestroy()
    {
        if (materialButton != null) materialButton.onClick.RemoveListener(SelectMaterial);
        if (echoButton != null) echoButton.onClick.RemoveListener(SelectEcho);
        if (fusionButton != null) fusionButton.onClick.RemoveListener(ToggleFusion);
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
    }
}
