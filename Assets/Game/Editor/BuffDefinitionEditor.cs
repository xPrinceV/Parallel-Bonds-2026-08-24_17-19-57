using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BuffDefinition))]
public class BuffDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "atoms");

        SerializedProperty atoms = serializedObject.FindProperty("atoms");
        if (atoms == null || !atoms.isArray)
        {
            EditorGUILayout.HelpBox("BuffDefinition requires a serialized atoms list.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.LabelField("Atoms", EditorStyles.boldLabel);
        for (int i = 0; i < atoms.arraySize; i++)
        {
            SerializedProperty atom = atoms.GetArrayElementAtIndex(i);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string label = atom.managedReferenceValue == null
                ? "Missing Atom"
                : ObjectNames.NicifyVariableName(atom.managedReferenceValue.GetType().Name);
            EditorGUILayout.PropertyField(atom, new GUIContent(label), true);
            if (atom.managedReferenceValue is StackToActivationBuffAtom)
            {
                // repair missing nested templates without discarding the stack settings
                SerializedProperty condition = atom.FindPropertyRelative("condition");
                SerializedProperty activation = atom.FindPropertyRelative("activation");
                if (condition.managedReferenceValue == null && GUILayout.Button("Restore Hit Condition"))
                    condition.managedReferenceValue = new HitStackCondition();
                if (activation.managedReferenceValue == null && GUILayout.Button("Restore Grant Activation"))
                    activation.managedReferenceValue = new GrantBuffActivation();

                // switching action types is explicit because each action owns different parameters
                if (!(activation.managedReferenceValue is ActivateBuffEffects)
                    && GUILayout.Button("Use Internal Effects"))
                    activation.managedReferenceValue = new ActivateBuffEffects();
                if (!(activation.managedReferenceValue is GrantBuffActivation)
                    && GUILayout.Button("Use Independent Buff Grant"))
                    activation.managedReferenceValue = new GrantBuffActivation();
                if (activation.managedReferenceValue is ActivateBuffEffects
                    && GUILayout.Button("Add Internal Effect"))
                    ShowAddAtomMenu(activation.FindPropertyRelative("effects").propertyPath, true);
            }
            bool remove = GUILayout.Button("Remove Atom");
            EditorGUILayout.EndVertical();

            if (remove)
            {
                atom.managedReferenceValue = null;
                atoms.DeleteArrayElementAtIndex(i);
                break;
            }
        }

        if (GUILayout.Button("Add Atom"))
            ShowAddAtomMenu();

        // serialized edits provide undo and dirty tracking without running effects
        serializedObject.ApplyModifiedProperties();
        if (!((BuffDefinition)target).isValid)
            EditorGUILayout.HelpBox("Check duration, atom parameters and the target buff reference. Empty recipes are invalid.", MessageType.Warning);
    }

    private void ShowAddAtomMenu()
    {
        ShowAddAtomMenu("atoms", false);
    }

    private void ShowAddAtomMenu(string propertyPath, bool numericOnly)
    {
        var menu = new GenericMenu();
        var types = TypeCache.GetTypesDerivedFrom<BuffAtom>()
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters
                && (!numericOnly || typeof(StatBuffAtom).IsAssignableFrom(type)))
            .OrderBy(type => type.FullName);

        foreach (Type type in types)
        {
            var label = new GUIContent(type.FullName);
            if (!type.IsSerializable || type.GetConstructor(Type.EmptyTypes) == null)
            {
                menu.AddDisabledItem(label);
                continue;
            }

            menu.AddItem(label, false, () => AddAtom(type, propertyPath));
        }

        if (menu.GetItemCount() == 0)
            menu.AddDisabledItem(new GUIContent("No BuffAtom types found"));
        menu.ShowAsContext();
    }

    private void AddAtom(Type type, string propertyPath)
    {
        if (target == null)
            return;

        // construct only after a menu choice so nested defaults stay intact
        var instance = (BuffAtom)Activator.CreateInstance(type);
        serializedObject.Update();
        SerializedProperty atoms = serializedObject.FindProperty(propertyPath);
        if (atoms == null || !atoms.isArray)
            return;

        int index = atoms.arraySize;
        atoms.InsertArrayElementAtIndex(index);
        SerializedProperty atom = atoms.GetArrayElementAtIndex(index);
        atom.managedReferenceValue = instance;
        atom.isExpanded = true;
        serializedObject.ApplyModifiedProperties();
        Repaint();
    }
}
