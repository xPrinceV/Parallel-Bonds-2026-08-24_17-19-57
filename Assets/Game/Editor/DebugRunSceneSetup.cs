using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DebugRunSceneSetup
{
    public const string ScenePath = "Assets/Scenes/DebugRun.unity";
    private const string SourcePath = "Assets/Scenes/Main.unity";

    [MenuItem("Tools/Parallel Bonds/Create Debug Run Scene")]
    public static void CreateDebugRunScene()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,
            "Exit Play Mode and wait for compilation before creating DebugRun.");
        Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "Exit Prefab Mode first.");
        Require(!File.Exists(ScenePath) && !File.Exists(ScenePath + ".meta")
            && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null,
            "DebugRun already exists; refusing to overwrite it. Validate or remove it explicitly first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            Require(open.path != SourcePath || !open.isDirty,
                "Main has unsaved changes. Save it explicitly or run setup in an isolated project.");
        }

        byte[] original = File.ReadAllBytes(SourcePath);
        Scene previous = SceneManager.GetActiveScene();
        Scene debug = default;
        Require(AssetDatabase.CopyAsset(SourcePath, ScenePath), "Could not copy Main to DebugRun.");
        try
        {
            // Additive loading never closes or saves any of the user's original scenes.
            debug = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(debug);
            WorldManager manager = Single<WorldManager>(debug);
            World[] worlds = Find<World>(debug);
            Require(worlds.Length == 2 && worlds.Select(w => w.WorldId).Distinct().Count() == 2,
                "Main must have two configured worlds.");
            foreach (World world in worlds)
            {
                Require(world.IsConfigured && world.Player != null, "Each world needs its local hero and content.");
                PlayerController player = world.Player;
                Require(player.unassignedWeapons.Count == 6 && player.unassignedWeapons.All(w => w != null)
                    && player.unassignedWeapons.Distinct().Count() == 6 && player.assignedWeapons.Count == 0,
                    "Preserve six configured weapons with the existing three-starter runtime selection.");
            }
            EnemySpawner[] spawners = Find<EnemySpawner>(debug);
            Require(spawners.Length == 2 && worlds.All(w =>
                spawners.Count(s => s.transform.IsChildOf(w.ContentRoot)) == 1),
                "Expected one configured spawner per world.");
            Require(Find<RunStageController>(debug).Length == 0 && Find<RunStagePanel>(debug).Length == 0,
                "Main must not already contain debug-run components.");
            Single<EventSystem>(debug);
            UIController ui = Single<UIController>(debug);
            EnemyController boss = EnsureBossPrefab();

            RunStageController controller = new GameObject("Run Stage Controller").AddComponent<RunStageController>();
            var data = new SerializedObject(controller);
            Property(data, "worldManager").objectReferenceValue = manager;
            SerializedProperty entries = Property(data, "spawners");
            entries.arraySize = spawners.Length;
            for (int i = 0; i < spawners.Length; i++)
                entries.GetArrayElementAtIndex(i).objectReferenceValue = spawners[i];
            SerializedProperty durations = Property(data, "stageDurations");
            durations.arraySize = 3;
            for (int i = 0; i < 3; i++)
                durations.GetArrayElementAtIndex(i).floatValue = 20f;
            Property(data, "bossPrefab").objectReferenceValue = boss;
            Property(data, "bossHealth").floatValue = 600f;
            Property(data, "bossDistance").floatValue = 6f;
            data.ApplyModifiedPropertiesWithoutUndo();
            CreatePanel(debug, controller, ui);
            DeveloperDebugGuiSetup.ConfigureScene(debug);
            FusionVisualSetup.ConfigureScene(debug);
            Require(EditorSceneManager.SaveScene(debug), "Failed to save DebugRun.");
            Require(original.SequenceEqual(File.ReadAllBytes(SourcePath)), "Main changed unexpectedly during setup.");
            // Preserve every existing scene entry and config object, including disabled entries.
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes
                    .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            Debug.Log("DEBUG_RUN_SETUP PASS: " + ScenePath + "; stages=20,20,20; RiftLordBoss=600; distance=6");
        }
        catch
        {
            if (debug.IsValid() && debug.isLoaded)
                EditorSceneManager.CloseScene(debug, true);
            AssetDatabase.DeleteAsset(ScenePath);
            throw;
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
        }
    }

    private static EnemyController EnsureBossPrefab()
    {
        const string path = "Assets/Prefabs/RiftLordBoss.prefab";
        GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RiftLordProjectile.prefab");
        Require(projectile != null && projectile.GetComponent<RiftLordProjectile>() != null,
            "RiftLordBoss requires the RiftLordProjectile prefab.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Require(!File.Exists(path) && !File.Exists(path + ".meta"),
                "RiftLordBoss exists but cannot be loaded; refusing to overwrite it.");
            GameObject titan = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Titan.prefab");
            Require(titan != null && titan.GetComponent<TitanEnemyController>() != null,
                "RiftLordBoss requires the source Titan prefab and its TitanEnemyController.");
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Vampire Survival Assets/Art/Rift Lord.png")
                .OfType<Sprite>().SingleOrDefault(s => s.name == "Rift Lord_0");
            Require(sprite != null, "RiftLordBoss requires the Rift Lord_0 sprite.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                // Saving a connected instance creates a variant; physics, attacks and poison stay inherited.
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(titan, preview);
                instance.name = "RiftLordBoss";
                SpriteRenderer body = instance.transform.Find("Monster_3_0").GetComponent<SpriteRenderer>();
                Bounds reference = body.bounds;
                body.sprite = sprite;
                Require(body.bounds.size.y > 0f, "Rift Lord_0 must have nonzero rendered height.");
                float scale = reference.size.y / body.bounds.size.y;
                Vector3 visualScale = body.transform.localScale;
                body.transform.localScale = new Vector3(visualScale.x * scale, visualScale.y * scale, visualScale.z);
                body.transform.position += Vector3.up * (reference.min.y - body.bounds.min.y);
                // Enlarge the variant after fitting its sprite; the inherited collider scales with it.
                instance.transform.localScale *= 2f;
                TitanEnemyController enemy = instance.GetComponent<TitanEnemyController>();
                enemy.health = 600f;
                RiftLordBarrage barrage = instance.AddComponent<RiftLordBarrage>();
                var attack = new SerializedObject(barrage);
                Property(attack, "projectilePrefab").objectReferenceValue = projectile.GetComponent<RiftLordProjectile>();
                Property(attack, "bodyRenderer").objectReferenceValue = body;
                attack.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(enemy);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                PrefabUtility.RecordPrefabInstancePropertyModifications(body);
                PrefabUtility.RecordPrefabInstancePropertyModifications(body.transform);
                prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
                Require(prefab != null, "Could not save RiftLordBoss prefab variant.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        EnemyController boss = prefab.GetComponent<EnemyController>();
        Require(boss is TitanEnemyController, "RiftLordBoss prefab must retain a TitanEnemyController.");
        RiftLordBarrage configuredBarrage = prefab.GetComponent<RiftLordBarrage>();
        Require(configuredBarrage != null && configuredBarrage.enabled,
            "RiftLordBoss requires an enabled RiftLordBarrage; existing assets are not overwritten.");
        var configured = new SerializedObject(configuredBarrage);
        Require(Property(configured, "projectilePrefab").objectReferenceValue == projectile.GetComponent<RiftLordProjectile>()
            && Property(configured, "bodyRenderer").objectReferenceValue != null,
            "RiftLordBarrage requires its projectile and body renderer references.");
        return boss;
    }

    private static void CreatePanel(Scene scene, RunStageController controller, UIController ui)
    {
        // Main may already carry the developer controls; keep one shared canvas for all debug UI.
        GameObject root = scene.GetRootGameObjects()
            .SingleOrDefault(r => r.GetComponent<DeveloperDebugGui>() != null);
        if (root == null)
        {
            root = new GameObject("Debug Run UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        root.name = "Debug Run UI";
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        RectTransform panel = Rect("Run Stage Panel", root.transform, new Vector2(440, 180), new Vector2(-16, -16));
        panel.gameObject.AddComponent<Image>().color = new Color(.04f, .05f, .08f, .94f);
        RunStagePanel view = panel.gameObject.AddComponent<RunStagePanel>();
        TMP_FontAsset font = ui.timeText != null ? ui.timeText.font : TMP_Settings.defaultFontAsset;
        Require(font != null, "A TextMesh Pro font is required for the debug panel.");
        TMP_Text status = Label("Status", panel, font, "Wave 1 | Running\nTime: 20s | Bosses: 0",
            new Vector2(416, 58), new Vector2(-12, -10), 22);
        Label("Hint", panel, font, "F Fusion | Bosses in both worlds",
            new Vector2(416, 28), new Vector2(-12, -68), 18);
        Button[] buttons = new Button[4];
        for (int i = 0; i < 4; i++)
            buttons[i] = CreateButton(i == 3 ? "Boss" : "Wave " + (i + 1), panel, font,
                new Vector2(-336 + i * 108, -102), new Vector2(100, 30));
        Button next = CreateButton("Next", panel, font, new Vector2(-228, -140), new Vector2(208, 30));
        Button restart = CreateButton("Restart", panel, font, new Vector2(-12, -140), new Vector2(208, 30));
        var data = new SerializedObject(view);
        Property(data, "runController").objectReferenceValue = controller;
        Property(data, "uiController").objectReferenceValue = ui;
        Property(data, "statusText").objectReferenceValue = status;
        SerializedProperty stages = Property(data, "stageButtons");
        stages.arraySize = buttons.Length;
        for (int i = 0; i < buttons.Length; i++)
            stages.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        Property(data, "nextButton").objectReferenceValue = next;
        Property(data, "restartButton").objectReferenceValue = restart;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Button CreateButton(string name, Transform parent, TMP_FontAsset font, Vector2 position, Vector2 size)
    {
        RectTransform rect = Rect(name, parent, size, position);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.19f, .24f, .32f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        TMP_Text text = Label("Label", rect, font, name, size, Vector2.zero, 20);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value,
        Vector2 size, Vector2 position, float fontSize)
    {
        TMP_Text text = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.text = value;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static T Single<T>(Scene scene) where T : Component
    {
        T[] items = Find<T>(scene);
        Require(items.Length == 1, "Expected exactly one " + typeof(T).Name + ".");
        return items[0];
    }

    private static SerializedProperty Property(SerializedObject data, string name) => data.FindProperty(name)
        ?? throw new InvalidOperationException("Missing serialized contract field: " + name);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
