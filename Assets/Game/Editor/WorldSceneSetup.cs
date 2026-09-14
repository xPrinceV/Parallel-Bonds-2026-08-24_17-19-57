using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// configure the two world roots without moving shared gameplay objects
public static class WorldSceneSetup
{
    private const string MenuPath = "Tools/Parallel Bonds/Set Up Two Worlds";
    private const string MainScenePath = "Assets/Scenes/Main.unity";

    [MenuItem(MenuPath)]
    private static void SetUpWorlds()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != MainScenePath)
        {
            Debug.LogError("Open Main.unity and exit Play Mode before setting up worlds.");
            return;
        }

        GameObject grid = null;
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            // do not overwrite an existing or partially configured world system
            if (root.GetComponentsInChildren<World>(true).Length > 0
                || root.GetComponentsInChildren<WorldManager>(true).Length > 0
                || root.name == "MaterialWorld" || root.name == "EchoWorld"
                || root.name == "WorldSystem")
            {
                Debug.LogError("World setup already exists. Undo the previous setup or configure it manually.", root);
                return;
            }

            if (root.name == "Grid" && root.GetComponent<Grid>() != null)
            {
                if (grid != null)
                {
                    Debug.LogError("Multiple root Grids found; choose the map hierarchy manually.");
                    return;
                }
                grid = root;
            }
        }

        // only duplicate map content, never player scripts or gameplay managers
        if (grid == null || !grid.activeSelf
            || grid.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
        {
            Debug.LogError("Expected an active root Grid containing map content without gameplay scripts.");
            return;
        }

        if (!EditorUtility.DisplayDialog("Set Up Two Worlds",
            "Move Grid into MaterialWorld/Content, copy it into EchoWorld/Content and replace automatic switching with a shared WorldSystem? Shared player, camera and UI objects will stay in place. The scene will not be saved automatically.",
            "Set Up", "Cancel"))
            return;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Up Two Worlds");
        try
        {
            World material = CreateWorld(scene, "MaterialWorld", WorldId.Material, out GameObject materialContent);
            World echo = CreateWorld(scene, "EchoWorld", WorldId.Echo, out GameObject echoContent);

            // keep the original map coordinates and use the same layout as a safe starting point
            Undo.SetTransformParent(grid.transform, materialContent.transform, "Move Material Map");
            GameObject echoGrid = UnityEngine.Object.Instantiate(grid, echoContent.transform, true);
            Undo.RegisterCreatedObjectUndo(echoGrid, "Create Echo Map");
            echoGrid.name = "Grid";
            Undo.RecordObject(echoContent, "Set Initial World Visibility");
            echoContent.SetActive(false);

            // disable old timers but preserve their objects and existing serialized data
            foreach (GameObject root in roots)
            {
                foreach (StateSwitchController controller in root.GetComponentsInChildren<StateSwitchController>(true))
                {
                    Undo.RecordObject(controller, "Disable Old World Timer");
                    controller.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
                }
            }

            GameObject system = CreateObject(scene, "WorldSystem", null);
            WorldManager manager = Undo.AddComponent<WorldManager>(system);
            SerializedObject managerData = new SerializedObject(manager);
            managerData.FindProperty("initialWorldId").intValue = (int)WorldId.Material;
            SerializedProperty worlds = managerData.FindProperty("worlds");
            worlds.arraySize = 2;
            worlds.GetArrayElementAtIndex(0).objectReferenceValue = material;
            worlds.GetArrayElementAtIndex(1).objectReferenceValue = echo;
            managerData.ApplyModifiedProperties();

            StateSwitchController timer = Undo.AddComponent<StateSwitchController>(system);
            SerializedObject timerData = new SerializedObject(timer);
            timerData.FindProperty("worldManager").objectReferenceValue = manager;
            timerData.FindProperty("timer").floatValue = 15f;
            timerData.ApplyModifiedProperties();

            // leave scene saving to the user so the hierarchy can be reviewed first
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = system;
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("World setup complete. Review and save Main.unity. Both maps initially share the same layout; customize EchoWorld/Content separately. Automatic switching runs every 15 seconds.", system);
        }
        catch (Exception exception)
        {
            // restore the scene if any setup step fails
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogException(exception);
        }
    }

    private static World CreateWorld(Scene scene, string name, WorldId id, out GameObject content)
    {
        GameObject root = CreateObject(scene, name, null);
        content = CreateObject(scene, "Content", root.transform);
        World world = Undo.AddComponent<World>(root);
        SerializedObject data = new SerializedObject(world);
        data.FindProperty("worldId").intValue = (int)id;
        data.FindProperty("contentRoot").objectReferenceValue = content;
        data.ApplyModifiedProperties();
        return world;
    }

    private static GameObject CreateObject(Scene scene, string name, Transform parent)
    {
        GameObject created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Create World Object");
        SceneManager.MoveGameObjectToScene(created, scene);
        if (parent != null)
            Undo.SetTransformParent(created.transform, parent, "Parent World Object");
        return created;
    }
}
