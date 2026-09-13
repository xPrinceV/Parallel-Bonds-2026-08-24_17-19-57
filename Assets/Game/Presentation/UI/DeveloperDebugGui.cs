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
    private RunStagePanel[] runStagePanels;
    private bool displayValid;
    private bool displayedFlow, displayedAutomatic;
    private float displayedInterval;
    private bool displayedWorld, displayedFusion, displayedHealth;
    private WorldId displayedWorldId;
    private float displayedCurrentHealth, displayedMaxHealth;

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
        runStagePanels = GetComponentsInChildren<RunStagePanel>(true);
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

        RefreshDisplay();
    }

    private void RefreshDisplay(bool force = false)
    {
        if (!IsAvailable || !IsOpen || canvas == null || !canvas.isActiveAndEnabled)
        {
            displayValid = false;
            return;
        }

        bool ready = worldManager != null && worldManager.IsInitialized && worldManager.isActiveAndEnabled;
        World world = ready ? worldManager.CurrentWorld : null;
        PlayerHealth health = world != null && world.Player != null ? world.Player.GetComponent<PlayerHealth>() : null;
        bool canAct = ready && !worldManager.IsFinalFusion && !worldManager.IsSwitching && !worldManager.IsWorldTransitioning && !worldManager.IsFusionTransitioning && Time.timeScale > 0f
            && health != null && !health.IsDead && health.isActiveAndEnabled
            && (UIController.instance == null || UIController.instance.levelUpPanel == null
                || !UIController.instance.levelUpPanel.activeInHierarchy);
        var switchFlow = worldManager != null ? worldManager.SwitchFlow : null;
        bool canSwitch = canAct && !worldManager.IsFused && switchFlow != null;
        if (materialButton.interactable != canSwitch)
            materialButton.interactable = canSwitch;
        // scheduling can be toggled while paused; this never resumes or commits a transition
        bool canToggleAutomatic = ready && switchFlow != null && switchFlow.isActiveAndEnabled
            && !worldManager.IsFused && !worldManager.IsFinalFusion;
        if (echoButton.interactable != canToggleAutomatic)
            echoButton.interactable = canToggleAutomatic;

        bool hasFlow = switchFlow != null;
        bool automatic = hasFlow && switchFlow.AutomaticSwitchingEnabled;
        float interval = hasFlow ? switchFlow.SwitchInterval : 0f;
        if (force || !displayValid || displayedFlow != hasFlow || displayedAutomatic != automatic)
        {
            if (automaticSwitchButtonLabel != null)
                automaticSwitchButtonLabel.text = !hasFlow ? "Auto switch: Unavailable"
                    : automatic ? "Auto switch: On" : "Auto switch: Off";
        }
        if (force || !displayValid || displayedFlow != hasFlow || displayedInterval != interval)
        {
            if (switchButtonLabel != null)
                switchButtonLabel.text = hasFlow ? $"{interval:0.#}s Switch" : "Switch";
        }
        displayedFlow = hasFlow;
        displayedAutomatic = automatic;
        displayedInterval = interval;

        bool hasWorld = world != null;
        WorldId worldId = hasWorld ? world.WorldId : default(WorldId);
        bool fused = hasWorld && worldManager.IsFused;
        bool hasHealth = health != null;
        float currentHealth = hasHealth ? health.currentHealth : 0f;
        float maxHealth = hasHealth ? health.maxHealth : 0f;
        if (force || !displayValid || displayedWorld != hasWorld || displayedWorldId != worldId
            || displayedFusion != fused || displayedHealth != hasHealth
            || displayedCurrentHealth != currentHealth || displayedMaxHealth != maxHealth)
        {
            worldStatus.text = !hasWorld ? "World unavailable" : $"World: {worldId} | Fusion: {(fused ? "On" : "Off")}"
                + (!hasHealth ? "" : $"\nShared HP: {currentHealth:0.#} / {maxHealth:0.#}");
            displayedWorld = hasWorld;
            displayedWorldId = worldId;
            displayedFusion = fused;
            displayedHealth = hasHealth;
            displayedCurrentHealth = currentHealth;
            displayedMaxHealth = maxHealth;
        }
        displayValid = true;
    }

    public void Toggle()
    {
        SetOpen(!IsOpen);
    }

    public void SetOpen(bool open)
    {
        bool wasOpen = IsOpen;
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
        if (IsOpen && !wasOpen)
        {
            RefreshDisplay(true);
            if (runStagePanels != null)
                foreach (var panel in runStagePanels)
                    if (panel != null) panel.RefreshDisplay(true);
        }
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
