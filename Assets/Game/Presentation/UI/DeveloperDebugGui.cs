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

    private TMP_Text switchButtonLabel;
    private TMP_Text automaticSwitchButtonLabel;

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
        switchButtonLabel = materialButton.GetComponentInChildren<TMP_Text>(true);
        materialButton.onClick.AddListener(RequestWorldSwitch);
        // reuse the serialized button reference without replacing the scene object
        automaticSwitchButtonLabel = echoButton.GetComponentInChildren<TMP_Text>(true);
        echoButton.onClick.AddListener(ToggleAutomaticSwitching);
        // child visibility and layout are authored in the scene, not changed on entry
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
        bool canAct = ready && !worldManager.IsFinalFusion && !worldManager.IsSwitching && !worldManager.IsWorldTransitioning && !worldManager.IsFusionTransitioning && Time.timeScale > 0f
            && health != null && !health.IsDead && health.isActiveAndEnabled
            && (UIController.instance == null || UIController.instance.levelUpPanel == null
                || !UIController.instance.levelUpPanel.activeInHierarchy);
        var switchFlow = worldManager != null ? worldManager.SwitchFlow : null;
        materialButton.interactable = canAct && !worldManager.IsFused && switchFlow != null;
        // scheduling can be toggled while paused; this never resumes or commits a transition
        echoButton.interactable = ready && switchFlow != null && switchFlow.isActiveAndEnabled
            && !worldManager.IsFused && !worldManager.IsFinalFusion;
        if (automaticSwitchButtonLabel != null)
            automaticSwitchButtonLabel.text = switchFlow == null ? "Auto switch: Unavailable"
                : switchFlow.AutomaticSwitchingEnabled ? "Auto switch: On" : "Auto switch: Off";
        if (switchButtonLabel != null)
            switchButtonLabel.text = switchFlow != null ? $"{switchFlow.SwitchInterval:0.#}s Switch" : "Switch";
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
    private void RequestWorldSwitch()
    {
        if (IsOpen && worldManager != null && worldManager.SwitchFlow != null)
            worldManager.SwitchFlow.RequestNextWorldSwitch();
    }

    private void ToggleAutomaticSwitching()
    {
        if (!IsOpen || worldManager == null || worldManager.IsFused || worldManager.IsFinalFusion)
            return;
        var flow = worldManager.SwitchFlow;
        if (flow != null && flow.isActiveAndEnabled)
            flow.AutomaticSwitchingEnabled = !flow.AutomaticSwitchingEnabled;
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
        if (materialButton != null) materialButton.onClick.RemoveListener(RequestWorldSwitch);
        if (echoButton != null) echoButton.onClick.RemoveListener(ToggleAutomaticSwitching);
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
    }
}
