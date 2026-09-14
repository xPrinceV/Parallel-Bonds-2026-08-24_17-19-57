using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// configure the sample without putting test-specific rules inside the weapon
public static class BuffTestSetup
{
    private const string AssetPath = "Assets/Game/Features/Buffs/Configs/FiveHitPower.asset";

    [MenuItem("Parallel Bonds/Buffs/Configure Five Hit Sample")]
    public static void ConfigureOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Configure the sample outside Play Mode.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        PlayerController[] players = Object.FindObjectsOfType<PlayerController>(true)
            .Where(player => player.gameObject.scene == scene).ToArray();
        if (players.Length != 1)
        {
            Debug.LogError("The active scene must contain exactly one PlayerController.");
            return;
        }

        PistolController[] pistols = players[0].GetComponentsInChildren<PistolController>(true);
        if (pistols.Length == 0)
        {
            Debug.LogError("The sample requires a pistol below the player.");
            return;
        }

        BuffDefinition definition = GetOrCreateDefinition();
        if (definition == null || !definition.isValid)
        {
            Debug.LogError("The sample definition is invalid. Existing assets are not overwritten.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure Five Hit Sample");
        BuffController holder = players[0].GetComponent<BuffController>();
        if (holder == null)
            holder = Undo.AddComponent<BuffController>(players[0].gameObject);

        // append the sample once and preserve any other initial buffs
        var holderData = new SerializedObject(holder);
        SerializedProperty initial = holderData.FindProperty("initialBuffs");
        bool alreadyAssigned = false;
        for (int i = 0; i < initial.arraySize; i++)
            alreadyAssigned |= initial.GetArrayElementAtIndex(i).objectReferenceValue == definition;
        if (!alreadyAssigned)
        {
            int index = initial.arraySize;
            initial.InsertArrayElementAtIndex(index);
            initial.GetArrayElementAtIndex(index).objectReferenceValue = definition;
            holderData.ApplyModifiedProperties();
        }

        foreach (PistolController pistol in pistols)
        {
            var pistolData = new SerializedObject(pistol);
            pistolData.FindProperty("buffHolder").objectReferenceValue = holder;
            pistolData.ApplyModifiedProperties();
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeObject = definition;
        Debug.Log("Buff sample assigned to the player and pistol. Inspect the selected definition for its current parameters; existing assets are preserved.");
    }

    private static BuffDefinition GetOrCreateDefinition()
    {
        BuffDefinition existing = AssetDatabase.LoadAssetAtPath<BuffDefinition>(AssetPath);
        if (existing != null)
            return existing;
        if (!AssetDatabase.IsValidFolder("Assets/Game/Features/Buffs/Configs"))
        {
            Debug.LogError("The Buffs/Configs asset folder is missing.");
            return null;
        }

        var definition = ScriptableObject.CreateInstance<BuffDefinition>();
        var data = new SerializedObject(definition);
        data.FindProperty("isPermanent").boolValue = true;
        SerializedProperty atoms = data.FindProperty("atoms");
        atoms.arraySize = 2;
        SerializedProperty damage = atoms.GetArrayElementAtIndex(0);
        damage.managedReferenceValue = new DamageBuffAtom();
        damage.FindPropertyRelative("damageMultiplier").floatValue = 1.2f;

        SerializedProperty trigger = atoms.GetArrayElementAtIndex(1);
        trigger.managedReferenceValue = new StackToActivationBuffAtom();
        trigger.FindPropertyRelative("stackCount").intValue = 5;
        trigger.FindPropertyRelative("consumeMode").intValue = (int)StackConsumeMode.TriggerOnce;
        SerializedProperty activation = trigger.FindPropertyRelative("activation");
        activation.managedReferenceValue = new ActivateBuffEffects();
        SerializedProperty effects = activation.FindPropertyRelative("effects");
        effects.arraySize = 2;
        SerializedProperty bonus = effects.GetArrayElementAtIndex(0);
        bonus.managedReferenceValue = new DamageBuffAtom();
        bonus.FindPropertyRelative("damageMultiplier").floatValue = 1.1f;
        SerializedProperty projectile = effects.GetArrayElementAtIndex(1);
        projectile.managedReferenceValue = new ProjectileBuffAtom();
        projectile.FindPropertyRelative("projectileCount").intValue = 1;
        data.ApplyModifiedPropertiesWithoutUndo();

        if (!definition.isValid)
        {
            Object.DestroyImmediate(definition);
            return null;
        }
        AssetDatabase.CreateAsset(definition, AssetPath);
        AssetDatabase.SaveAssetIfDirty(definition);
        return definition;
    }
}
