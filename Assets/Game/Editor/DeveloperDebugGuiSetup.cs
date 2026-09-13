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
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        GraphicRaycaster raycaster = GetOrAdd<GraphicRaycaster>(root);
        raycaster.enabled = true;

        RectTransform panel = Rect("Developer World Panel", root.transform, new Vector2(440, 160), new Vector2(-16, -16));
        GetOrAdd<Image>(panel.gameObject).color = new Color(.04f, .05f, .08f, .94f);
        Label("Title", panel, font, "` / ~ Developer | Esc close", new Vector2(416, 30), new Vector2(-12, -10), 22);
        TMP_Text status = Label("World Status", panel, font, "World: Material | Fusion: Off",
            new Vector2(416, 52), new Vector2(-12, -48), 20);
        Button material = Button("Material", panel, font, new Vector2(-336, -114));
        Button echo = Button("Echo", panel, font, new Vector2(-228, -114));
        Button fusion = Button("Fusion", panel, font, new Vector2(-120, -114));
        Button close = Button("Close", panel, font, new Vector2(-12, -114));
        Transform stage = root.transform.Find("Run Stage Panel");
        if (stage != null)
            ((RectTransform)stage).anchoredPosition = new Vector2(-16, -188);

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
        text.fontSize = fontSize;
        text.text = value;
        text.color = Color.white;
        text.raycastTarget = false;
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
