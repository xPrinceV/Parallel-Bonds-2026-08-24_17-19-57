using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FusionVisualSetup
{
    public const string SpritePath = "Assets/Vampire Survival Assets/Art/fusion.png";
    private const string Operation = "Configure Fusion Visuals";

    [MenuItem("Tools/Parallel Bonds/Configure Fusion Visuals")]
    public static void ConfigureOpenScene()
    {
        ConfigureScene(SceneManager.GetActiveScene());
    }

    public static void ConfigureScene(Scene scene)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,
            "Exit Play Mode and wait for compilation first.");
        Require(scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene)
            && PrefabStageUtility.GetCurrentPrefabStage() == null
            && (scene.path == "Assets/Scenes/Main.unity" || scene.path == "Assets/Scenes/DebugRun.unity"),
            "Open Main or DebugRun in edit mode.");
        PlayerController[] players = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<World>(true)).Select(w => w.Player).ToArray();
        Require(players.Length == 2 && players.All(p => p != null) && players.Distinct().Count() == 2,
            "Expected two configured world heroes.");
        foreach (PlayerController player in players)
        {
            Transform characters = player.transform.Find("Characters");
            Require(characters != null, "Missing Characters on " + player.name);
            SpriteRenderer[] bodies = Bodies(characters);
            Require(bodies.All(b => b != null && b.sprite != null && b.GetComponentInParent<Weapon>() == null)
                && bodies.Count(b => b.gameObject.activeSelf) == 1, "Expected one selected Jeff body.");
            Require(characters.Cast<Transform>().Count(t => t.name == "Fusion Visual") <= 1,
                "Duplicate fusion children; repair explicitly.");
            Transform existing = characters.Find("Fusion Visual");
            Require(existing == null || existing.GetComponent<SpriteRenderer>() != null,
                "Existing Fusion Visual has no SpriteRenderer.");
        }
        var importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        Require(importer != null, "Missing fusion texture importer.");
        if (importer.filterMode != FilterMode.Point || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(SpritePath).OfType<Sprite>().Single();
        Require(sprite.bounds.size.y > 0f, "Fusion sprite has empty bounds.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(Operation);
        try
        {
            foreach (PlayerController player in players)
            {
                Transform characters = player.transform.Find("Characters");
                SpriteRenderer[] bodies = Bodies(characters);
                SpriteRenderer reference = bodies.Single(b => b.gameObject.activeSelf);
                Transform child = characters.Find("Fusion Visual");
                if (child == null)
                {
                    GameObject go = new GameObject("Fusion Visual", typeof(SpriteRenderer));
                    Undo.RegisterCreatedObjectUndo(go, Operation);
                    child = go.transform;
                    Undo.SetTransformParent(child, characters, Operation);
                }
                SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
                Undo.RecordObjects(new UnityEngine.Object[] { child, renderer }, Operation);
                Vector3 scale = reference.transform.localScale * (reference.sprite.bounds.size.y / sprite.bounds.size.y);
                child.localRotation = reference.transform.localRotation;
                child.localScale = scale;
                Vector3 feet = new Vector3(reference.sprite.bounds.center.x, reference.sprite.bounds.min.y, 0f);
                Vector3 fusionFeet = new Vector3(sprite.bounds.center.x, sprite.bounds.min.y, 0f);
                child.localPosition = reference.transform.localPosition + reference.transform.localRotation
                    * (Vector3.Scale(feet, reference.transform.localScale) - Vector3.Scale(fusionFeet, scale));
                renderer.sprite = sprite;
                renderer.sharedMaterial = reference.sharedMaterial;
                renderer.sortingLayerID = reference.sortingLayerID;
                renderer.sortingOrder = reference.sortingOrder;
                renderer.color = reference.color;
                renderer.flipX = reference.flipX;
                renderer.flipY = reference.flipY;
                renderer.enabled = false;
                PlayerFusionVisual visual = player.GetComponent<PlayerFusionVisual>();
                if (visual == null)
                    visual = Undo.AddComponent<PlayerFusionVisual>(player.gameObject);
                var data = new SerializedObject(visual);
                data.FindProperty("player").objectReferenceValue = player;
                data.FindProperty("fusionRenderer").objectReferenceValue = renderer;
                SerializedProperty entries = data.FindProperty("bodyRenderers");
                entries.arraySize = bodies.Length;
                for (int i = 0; i < bodies.Length; i++)
                    entries.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
                data.ApplyModifiedProperties();
                Debug.Log($"FUSION_VISUAL {scene.name}/{player.name}: sprite={AssetDatabase.GetAssetPath(renderer.sprite)}:{sprite.name}; spriteBounds={sprite.bounds.size}; reference={reference.name} bounds={reference.sprite.bounds.size} scale={reference.transform.localScale}; fusionScale={scale}; worldHeight={renderer.bounds.size.y:F6}/{reference.bounds.size.y:F6}; feet={renderer.bounds.min.y:F6}/{reference.bounds.min.y:F6}; animators={characters.GetComponentsInChildren<Animator>(true).Length}");
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
        }
        catch
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static SpriteRenderer[] Bodies(Transform characters)
    {
        return new[] { characters.Find("Jeff_0")?.GetComponent<SpriteRenderer>(),
            characters.Find("Jeff_1")?.GetComponent<SpriteRenderer>() };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
