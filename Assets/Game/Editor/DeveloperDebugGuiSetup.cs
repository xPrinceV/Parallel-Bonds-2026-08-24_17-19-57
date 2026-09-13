using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class DeveloperDebugGuiSetup
{
    [MenuItem("Tools/Parallel Bonds/Configure Developer Debug GUI")]
    public static void ConfigureOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Exit Play/Prefab Mode and wait for compilation first.");
        ConfigureScene(SceneManager.GetActiveScene());
    }

    public static void ConfigureScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("A loaded scene is required.", nameof(scene));
        GameObject[] roots = scene.GetRootGameObjects();
        WorldManager manager = roots.SelectMany(r => r.GetComponentsInChildren<WorldManager>(true)).Single();
        UIController ui = roots.SelectMany(r => r.GetComponentsInChildren<UIController>(true)).Single();
        TMP_FontAsset font = ui.timeText != null ? ui.timeText.font : TMP_Settings.defaultFontAsset;
        if (font == null)
            throw new InvalidOperationException("An existing TextMesh Pro UI font is required.");
        GameObject[] candidates = roots.Where(r => r.name == "Debug Run UI"
            || r.GetComponent<DeveloperDebugGui>() != null || r.name == "Developer Debug UI").ToArray();
        if (candidates.Length > 1)
            throw new InvalidOperationException("Multiple developer GUI roots found; refusing to duplicate or delete UI.");
        GameObject root = candidates.SingleOrDefault();
        if (root == null)
        {
            root = new GameObject("Developer Debug UI", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        root.SetActive(true);
        Canvas canvas = GetOrAdd<Canvas>(root);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        // Keep authoring visible; DeveloperDebugGui.Awake hides the entire shared canvas in play.
        canvas.enabled = true;
        CanvasScaler scaler = GetOrAdd<CanvasScaler>(root);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // keep developer text legible in a docked Game view as well as a full-size window
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        GraphicRaycaster raycaster = GetOrAdd<GraphicRaycaster>(root);
        raycaster.enabled = true;

        RectTransform panel = Rect("Developer World Panel", root.transform, new Vector2(440, 232), new Vector2(-16, -16));
        GetOrAdd<Image>(panel.gameObject).color = new Color(.04f, .05f, .08f, .94f);
        Label("Title", panel, font, "` / ~ Developer | Esc close", new Vector2(416, 30), new Vector2(-12, -10), 24);
        TMP_Text status = Label("World Status", panel, font, "World: -- | Fusion: --\nShared HP: -- / --",
            new Vector2(416, 64), new Vector2(-12, -48), 22);
        Button material = Button("Material", panel, font, new Vector2(-336, -114));
        Button echo = Button("Echo", panel, font, new Vector2(-228, -114));
        Button fusion = Button("Fusion", panel, font, new Vector2(-120, -114));
        Button close = Button("Close", panel, font, new Vector2(-12, -114));
        // Store the same event controls in the scene that players see in the developer window.
        StateSwitchController flow = manager.GetComponent<StateSwitchController>();
        ConfigureButton(material, flow != null ? $"{flow.SwitchInterval:0.#}s Switch" : "Switch",
            new Vector2(-224, -132));
        ConfigureButton(close, "Close", new Vector2(-12, -132));
        ConfigureButton(echo, flow == null ? "Auto switch: Unavailable"
            : $"Auto switch: {(flow.AutomaticSwitchingEnabled ? "On" : "Off")}", new Vector2(-12, -180));
        ((RectTransform)echo.transform).sizeDelta = new Vector2(416, 40);
        echo.GetComponentInChildren<TMP_Text>(true).rectTransform.sizeDelta = new Vector2(416, 40);
        fusion.gameObject.SetActive(false);
        Transform stage = root.transform.Find("Run Stage Panel");
        if (stage != null)
        {
            ((RectTransform)stage).anchoredPosition = new Vector2(-16, -260);
            ConfigureEventPanel(stage.GetComponent<RunStagePanel>());
        }

        DeveloperDebugGui gui = GetOrAdd<DeveloperDebugGui>(root);
        gui.enabled = true;
        var data = new SerializedObject(gui);
        Reference(data, "worldManager", manager);
        Reference(data, "canvas", canvas);
        Reference(data, "raycaster", raycaster);
        Reference(data, "worldStatus", status);
        Reference(data, "materialButton", material);
        Reference(data, "echoButton", echo);
        Reference(data, "fusionButton", fusion);
        Reference(data, "closeButton", close);
        Property(data, "toggleKey").intValue = (int)KeyCode.BackQuote;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void ConfigureEventPanel(RunStagePanel panel)
    {
        if (panel == null)
            return;
        var data = new SerializedObject(panel);
        var run = Property(data, "runController").objectReferenceValue as RunStageController;
        SerializedProperty stages = Property(data, "stageButtons");
        for (int i = 0; i < stages.arraySize; i++)
        {
            var button = stages.GetArrayElementAtIndex(i).objectReferenceValue as Button;
            if (button == null)
                continue;
            button.gameObject.SetActive(i == 3);
            if (i == 3)
                ConfigureButton(button, run != null ? $"{run.FinaleStartTime:0.#}s Finale" : "Finale",
                    new Vector2(-224, -132));
        }
        var next = Property(data, "nextButton").objectReferenceValue as Button;
        if (next != null)
            next.gameObject.SetActive(false);
        var restart = Property(data, "restartButton").objectReferenceValue as Button;
        if (restart != null)
            ConfigureButton(restart, "Restart", new Vector2(-12, -132));
        ((RectTransform)panel.transform).sizeDelta = new Vector2(440, 184);
        var status = Property(data, "statusText").objectReferenceValue as TMP_Text;
        if (status != null)
        {
            status.text = "Run: -- | State: --\nFusion in: --s | Bosses: --";
            status.rectTransform.sizeDelta = new Vector2(416, 64);
            status.rectTransform.anchoredPosition = new Vector2(-12, -12);
            ConfigureText(status, 22);
        }
        Transform hint = panel.transform.Find("Hint");
        if (hint != null && hint.TryGetComponent(out TMP_Text text))
        {
            text.text = "Trigger only; no clock jump";
            text.rectTransform.sizeDelta = new Vector2(416, 28);
            text.rectTransform.anchoredPosition = new Vector2(-12, -84);
            ConfigureText(text, 20);
        }
    }

    private static void ConfigureButton(Button button, string label, Vector2 position)
    {
        button.gameObject.SetActive(true);
        var rect = (RectTransform)button.transform;
        rect.sizeDelta = new Vector2(204, 40);
        rect.anchoredPosition = position;
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.text = label;
            text.rectTransform.sizeDelta = rect.sizeDelta;
            ConfigureText(text, 22);
            text.alignment = TextAlignmentOptions.Center;
        }
    }

    private static void ConfigureText(TMP_Text text, float size)
    {
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.margin = Vector4.zero;
        text.raycastTarget = false;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException("Missing serialized contract field: " + name);

    private static void Reference(SerializedObject data, string name, UnityEngine.Object value) =>
        Property(data, name).objectReferenceValue = value;

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        Transform child = parent.Find(name);
        var rect = child != null ? child.GetComponent<RectTransform>()
            : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (rect == null)
            throw new InvalidOperationException("Expected a RectTransform for " + name);
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value,
        Vector2 size, Vector2 position, float fontSize)
    {
        TMP_Text text = GetOrAdd<TextMeshProUGUI>(Rect(name, parent, size, position).gameObject);
        text.font = font;
        ConfigureText(text, fontSize);
        text.text = value;
        text.color = Color.white;
        return text;
    }

    private static Button Button(string name, Transform parent, TMP_FontAsset font, Vector2 position)
    {
        Vector2 size = new Vector2(100, 32);
        RectTransform rect = Rect(name, parent, size, position);
        Image image = GetOrAdd<Image>(rect.gameObject);
        image.color = new Color(.19f, .24f, .32f);
        Button button = GetOrAdd<Button>(rect.gameObject);
        button.targetGraphic = image;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        Label("Label", rect, font, name, size, Vector2.zero, 20).alignment = TextAlignmentOptions.Center;
        return button;
    }
}
