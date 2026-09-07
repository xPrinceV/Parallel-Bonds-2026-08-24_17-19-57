using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DualWorldSceneSetup
{
    private const string Operation = "Configure Dual World Run";
    private const string FilterPath = "Assets/Game/Presentation/Worlds/WorldFilter.cs";
    private const string OverlayName = "World Ambient Overlay";
    private static readonly Color EchoColor = new Color(.12f, .25f, .6f, .22f);

    [MenuItem("Tools/Parallel Bonds/Configure Dual World Run")]
    public static void ConfigureOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,
            "Exit Play Mode and wait for compilation before configuring the scene.");
        Require(scene.IsValid() && scene.isLoaded && scene.path == "Assets/Scenes/Main.unity"
            && !EditorSceneManager.IsPreviewScene(scene), "Open the Main scene in edit mode.");
        Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "Exit Prefab Mode first.");

        World[] worlds = Find<World>(scene);
        Require(worlds.Length == 2 && worlds.Count(w => w.WorldId == WorldId.Material) == 1
            && worlds.Count(w => w.WorldId == WorldId.Echo) == 1,
            "Expected exactly one Material World and one Echo World.");
        World material = worlds.Single(w => w.WorldId == WorldId.Material);
        World echo = worlds.Single(w => w.WorldId == WorldId.Echo);
        ValidateWorld(material, "MaterialWorld");
        ValidateWorld(echo, "EchoWorld");
        WorldManager manager = Single(Find<WorldManager>(scene), "shared WorldManager");
        Require(manager.enabled && manager.gameObject.activeInHierarchy
            && !worlds.Any(w => w.ContainsContent(manager.transform)), "WorldManager must remain shared and active.");
        var managerData = new SerializedObject(manager);
        SerializedProperty entries = Property(managerData, "worlds");
        Require(entries.arraySize == 2
            && worlds.All(world => Enumerable.Range(0, 2)
                .Count(i => entries.GetArrayElementAtIndex(i).objectReferenceValue == world) == 1),
            "WorldManager must reference exactly the two scene Worlds.");
        int initialId = Property(managerData, "initialWorldId").intValue;
        Require(initialId == (int)WorldId.Material || initialId == (int)WorldId.Echo,
            "WorldManager has an invalid initial world ID.");
        ValidateNoRuntimeObjects(scene);

        // Resolve the companion presentation script without requiring it to exist at editor compilation time.
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(FilterPath);
        Type filterType = script == null ? null : script.GetClass();
        Require(filterType != null && typeof(MonoBehaviour).IsAssignableFrom(filterType)
            && !filterType.IsAbstract, "A compiled WorldFilter MonoBehaviour is required at " + FilterPath + ".");

        Require((material.Player == null) == (echo.Player == null),
            "Partial dual-world setup: only one World has a player. Undo or repair it before continuing.");
        if (material.Player != null)
        {
            ValidateCompleted(scene, material, echo, manager, filterType, initialId);
            return;
        }

        Require(Find(scene, filterType).Length == 0
            && !Find<Transform>(scene).Any(t => t.name == OverlayName),
            "An overlay already exists without configured players; refusing a partial setup.");
        ValidateHud(scene, null);
        PlayerController player = Single(Find<PlayerController>(scene), "shared PlayerController");
        Require(player.name == "Player" && player.transform.parent == null && player.gameObject.activeSelf,
            "Expected the original active root Player.");
        ValidateHero(player, false, false);
        ExperienceLevelController experience = Single(Find<ExperienceLevelController>(scene), "shared experience controller");
        Require(experience.transform.parent == null && experience.gameObject != player.gameObject,
            "Expected a separate root experience controller; local XP indicates a partial setup.");
        EnemySpawner spawner = Single(Find<EnemySpawner>(scene), "shared EnemySpawner");
        Require(spawner.transform.parent == null && spawner.gameObject != experience.gameObject,
            "Expected a separate root EnemySpawner GameObject.");
        ValidateSpawner(spawner);
        CameraController[] cameras = Find<CameraController>(scene);
        Object[] cameraTargets = cameras.Select(c => Property(new SerializedObject(c), "target").objectReferenceValue).ToArray();

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(Operation);
        try
        {
            ExperienceLevelController localExperience = Undo.AddComponent<ExperienceLevelController>(player.gameObject);
            // CopySerialized is an EditorUtility API; recording the destination makes the full copy undoable.
            Undo.RegisterCompleteObjectUndo(localExperience, Operation);
            EditorUtility.CopySerialized(experience, localExperience);
            EditorUtility.SetDirty(localExperience);
            PrefabUtility.RecordPrefabInstancePropertyModifications(localExperience);
            Undo.DestroyObjectImmediate(experience);

            Undo.SetTransformParent(player.transform, material.ContentRoot, Operation);
            Rename(player.gameObject, "MaterialPlayer");
            GameObject echoObject = Clone(player.gameObject, echo.ContentRoot, "EchoPlayer");
            PlayerController echoPlayer = echoObject.GetComponent<PlayerController>();
            ConfigureHero(player, false);
            ConfigureHero(echoPlayer, true);
            ConfigureWorld(material, player, Color.clear);
            ConfigureWorld(echo, echoPlayer, EchoColor);

            Undo.SetTransformParent(spawner.transform, material.ContentRoot, Operation);
            Clone(spawner.gameObject, echo.ContentRoot, spawner.name);
            SetActive(material.ContentRoot.gameObject, initialId == (int)WorldId.Material);
            SetActive(echo.ContentRoot.gameObject, initialId == (int)WorldId.Echo);
            CreateOverlay(scene, manager, filterType, initialId == (int)WorldId.Material ? Color.clear : EchoColor);

            ValidateCompleted(scene, material, echo, manager, filterType, initialId);
            for (int i = 0; i < cameras.Length; i++)
                Require(Property(new SerializedObject(cameras[i]), "target").objectReferenceValue == cameraTargets[i],
                    "Camera target changed unexpectedly.");
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        catch (Exception exception)
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
            throw new InvalidOperationException("Dual-world setup failed and was rolled back: " + exception.Message, exception);
        }
    }

    private static void ValidateWorld(World world, string expectedName)
    {
        Require(world.name == expectedName && world.transform.parent == null && world.enabled
            && world.gameObject.activeInHierarchy && world.IsConfigured && world.ContentRoot != null
            && world.ContentRoot.name == "Content", "Invalid world root or Content: " + expectedName + ".");
    }

    private static void ValidateHero(PlayerController player, bool hasExperience, bool echo)
    {
        Require(player.enabled && player.gameObject.activeSelf, "Hero root must remain active and enabled.");
        Single(player.GetComponentsInChildren<PlayerController>(true), "PlayerController per hero");
        PlayerHealth health = Single(player.GetComponentsInChildren<PlayerHealth>(true), "PlayerHealth per hero");
        BuffController holder = Single(player.GetComponentsInChildren<BuffController>(true), "BuffController per hero");
        Require(health.gameObject == player.gameObject && health.enabled
            && holder.gameObject == player.gameObject && holder.enabled,
            "Health and buffs must be enabled on the hero root.");
        ExperienceLevelController[] xp = player.GetComponentsInChildren<ExperienceLevelController>(true);
        Require(hasExperience ? xp.Length == 1 && xp[0].gameObject == player.gameObject && xp[0].enabled : xp.Length == 0,
            "Unexpected hero experience ownership.");
        Transform characters = player.transform.Find("Characters");
        Require(characters != null && characters.gameObject.activeSelf, "Missing active Characters hierarchy.");
        Transform jeff0 = characters.Find("Jeff_0");
        Transform jeff1 = characters.Find("Jeff_1");
        Require(jeff0 != null && jeff1 != null, "Expected Characters/Jeff_0 and Characters/Jeff_1.");
        Require(jeff0.gameObject.activeSelf == !echo && jeff1.gameObject.activeSelf == echo,
            "Unexpected hero visual selection.");
        Require(player.GetComponentsInChildren<StateSwitchController>(true).All(c => !c.enabled),
            "Legacy hero StateSwitchController must remain disabled.");
        Weapon[] weapons = player.GetComponentsInChildren<Weapon>(true);
        Require(weapons.Length == 3 && weapons.Count(w => w is PistolController) == 1
            && weapons.Count(w => w is LanternController) == 1 && weapons.Count(w => w is LightningController) == 1,
            "Expected one child pistol, lantern and lightning controller per hero.");
        foreach (Weapon weapon in weapons)
        {
            Require(weapon.transform != player.transform, "Weapon controllers must be children of the hero.");
            SerializedProperty reference = Property(new SerializedObject(weapon), "buffHolder");
            Require(hasExperience ? reference.objectReferenceValue == holder
                : reference.objectReferenceValue == null || reference.objectReferenceValue == holder,
                "Weapon buffHolder must reference its local hero.");
        }
    }

    private static void ValidateSpawner(EnemySpawner spawner)
    {
        Require(spawner.enabled && spawner.gameObject.activeSelf, "Spawner must remain active and enabled.");
        foreach (Component component in spawner.GetComponentsInChildren<Component>(true))
            Require(component != null && (component is Transform || component == spawner),
                "EnemySpawner hierarchy contains an unexpected component; refusing to move or clone shared systems.");
        Require(spawner.minSpawn != null && spawner.maxSpawn != null
            && spawner.minSpawn != spawner.maxSpawn
            && spawner.minSpawn.IsChildOf(spawner.transform) && spawner.maxSpawn.IsChildOf(spawner.transform),
            "Spawner bounds must belong to its own hierarchy so cloning remaps them locally.");
    }

    private static void ValidateCompleted(Scene scene, World material, World echo, WorldManager manager,
        Type filterType, int initialId)
    {
        Require(Find<PlayerController>(scene).Length == 2 && Find<PlayerHealth>(scene).Length == 2
            && Find<BuffController>(scene).Length == 2 && Find<ExperienceLevelController>(scene).Length == 2
            && Find<Weapon>(scene).Length == 6 && Find<EnemySpawner>(scene).Length == 2,
            "Configured scene must contain exactly two local heroes, health/buff/XP sets, weapon sets and spawners.");
        foreach (World world in new[] { material, echo })
        {
            bool isEcho = world == echo;
            Require(world.Player != null && world.Player.transform.parent == world.ContentRoot
                && world.Player.name == (isEcho ? "EchoPlayer" : "MaterialPlayer"), "Invalid local World.player reference.");
            ValidateHero(world.Player, true, isEcho);
            EnemySpawner spawner = Single(world.ContentRoot.GetComponentsInChildren<EnemySpawner>(true), "world-local spawner");
            Require(spawner.transform.parent == world.ContentRoot, "Spawner must be directly under its world's Content.");
            ValidateSpawner(spawner);
            Require(world.AmbientColor == (isEcho ? EchoColor : Color.clear), "Unexpected world ambient color.");
            Require(world.ContentRoot.gameObject.activeSelf == ((int)world.WorldId == initialId),
                "Only the initial world's Content should be active.");
        }
        Component filter = Single(Find(scene, filterType), "shared WorldFilter");
        var data = new SerializedObject(filter);
        Image image = Property(data, "overlay").objectReferenceValue as Image;
        Require(Property(data, "worldManager").objectReferenceValue == manager && image != null,
            "WorldFilter must reference the shared manager and overlay Image.");
        Canvas canvas = filter.GetComponent<Canvas>();
        Require(canvas != null && canvas.transform.parent == null && canvas.name == OverlayName
            && canvas.enabled && canvas.gameObject.activeInHierarchy && ((Behaviour)filter).enabled
            && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder == -100
            && canvas.sortingLayerID == 0 && canvas.targetDisplay == 0
            && image.transform.parent == canvas.transform && image.enabled && image.gameObject.activeSelf
            && !image.raycastTarget && image.sprite == null && image.material == image.defaultMaterial
            && canvas.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0,
            "Invalid shared ambient overlay hierarchy or rendering settings.");
        RectTransform rect = image.rectTransform;
        Require(rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one
            && rect.offsetMin == Vector2.zero && rect.offsetMax == Vector2.zero,
            "Ambient overlay Image must stretch over the whole screen.");
        ValidateHud(scene, canvas);
    }

    private static void ValidateHud(Scene scene, Canvas overlay)
    {
        foreach (Canvas canvas in Find<Canvas>(scene))
            Require(canvas == overlay || canvas.sortingOrder >= 0,
                "Existing HUD Canvas must have sortingOrder >= 0: " + canvas.name + ".");
    }

    private static void ValidateNoRuntimeObjects(Scene scene)
    {
        Require(Find<EnemyController>(scene).Length == 0 && Find<ExpPickup>(scene).Length == 0
            && Find<BulletController>(scene).Length == 0 && Find<LanternProjController>(scene).Length == 0
            && Find<HollowProjectile>(scene).Length == 0 && Find<LanternFire>(scene).Length == 0
            && Find<DamageNumber>(scene).All(number => Find<DamageNumberController>(scene)
                .Any(controller => controller.numberToSpawn == number && !number.gameObject.activeSelf)),
            "Remove runtime enemies, pickups, projectiles and damage numbers before setup.");
        Require(Find<Transform>(scene).All(t => (t.gameObject.hideFlags & HideFlags.DontSave) == 0),
            "Unsaved runtime or temporary objects are present in Main.");
    }

    private static void ConfigureHero(PlayerController player, bool echo)
    {
        SetActive(player.transform.Find("Characters/Jeff_0").gameObject, !echo);
        SetActive(player.transform.Find("Characters/Jeff_1").gameObject, echo);
        BuffController holder = player.GetComponent<BuffController>();
        foreach (Weapon weapon in player.GetComponentsInChildren<Weapon>(true))
        {
            var data = new SerializedObject(weapon);
            Property(data, "buffHolder").objectReferenceValue = holder;
            data.ApplyModifiedProperties();
        }
    }

    private static void ConfigureWorld(World world, PlayerController player, Color color)
    {
        var data = new SerializedObject(world);
        Property(data, "player").objectReferenceValue = player;
        Property(data, "ambientColor").colorValue = color;
        data.ApplyModifiedProperties();
    }

    private static void CreateOverlay(Scene scene, WorldManager manager, Type filterType, Color color)
    {
        GameObject root = new GameObject(OverlayName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, Operation);
        SceneManager.MoveGameObjectToScene(root, scene);
        Canvas canvas = Undo.AddComponent<Canvas>(root);
        Undo.RecordObject(canvas, Operation);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -100;
        GameObject child = new GameObject("Tint", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(child, Operation);
        SceneManager.MoveGameObjectToScene(child, scene);
        Undo.SetTransformParent(child.transform, root.transform, Operation);
        RectTransform rect = (RectTransform)child.transform;
        Undo.RecordObject(rect, Operation);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        Image image = Undo.AddComponent<Image>(child);
        Undo.RecordObject(image, Operation);
        image.raycastTarget = false;
        image.color = color;
        Component filter = Undo.AddComponent(root, filterType);
        var data = new SerializedObject(filter);
        Property(data, "worldManager").objectReferenceValue = manager;
        Property(data, "overlay").objectReferenceValue = image;
        data.ApplyModifiedProperties();
    }

    private static GameObject Clone(GameObject source, Transform parent, string name)
    {
        GameObject clone = Object.Instantiate(source, parent, true);
        Undo.RegisterCreatedObjectUndo(clone, Operation);
        Rename(clone, name);
        return clone;
    }

    private static void Rename(GameObject target, string name)
    {
        Undo.RecordObject(target, Operation);
        target.name = name;
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static void SetActive(GameObject target, bool active)
    {
        Undo.RecordObject(target, Operation);
        target.SetActive(active);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static T[] Find<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    }

    private static Component[] Find(Scene scene, Type type)
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(type, true)).ToArray();
    }

    private static T Single<T>(T[] items, string description)
    {
        Require(items.Length == 1, "Expected exactly one " + description + "; found " + items.Length + ".");
        return items[0];
    }

    private static SerializedProperty Property(SerializedObject data, string name)
    {
        SerializedProperty property = data.FindProperty(name);
        Require(property != null, data.targetObject.GetType().Name + " is missing serialized field '" + name + "'.");
        return property;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
