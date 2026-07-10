using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JuicyButton))]
[CanEditMultipleObjects]
public class JuicyButtonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("visualRoot"), new GUIContent("Visual Root"));

        GUILayout.Space(4);

        DrawSection("Scale", () =>
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hoverScale"), new GUIContent("Hover"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pressScale"), new GUIContent("Press"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("squashX"), new GUIContent("Squash X"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("squashDuration"), new GUIContent("Squash Duration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hoverDuration"), new GUIContent("Hover Duration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pressDuration"), new GUIContent("Press Duration"));
        });

        GUILayout.Space(4);

        DrawSection("Hover Shake", () =>
        {
            var shakeOnHover = serializedObject.FindProperty("shakeOnHover");
            EditorGUILayout.PropertyField(shakeOnHover, new GUIContent("Enabled"));

            if (shakeOnHover.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shakeStrength"), new GUIContent("Angle"));
                EditorGUI.indentLevel--;
            }
        });

        GUILayout.Space(4);

        DrawSection("Color", () =>
        {
            var colorOnHover = serializedObject.FindProperty("colorOnHover");
            EditorGUILayout.PropertyField(colorOnHover, new GUIContent("Enabled"));

            if (colorOnHover.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("targetGraphic"), new GUIContent("Target"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("colorDuration"), new GUIContent("Duration"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("hoverColor"), new GUIContent("Hover"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("pressColor"), new GUIContent("Press"));
                EditorGUI.indentLevel--;
            }
        });

        GUILayout.Space(4);

        EditorGUILayout.PropertyField(serializedObject.FindProperty("onClick"), new GUIContent("On Click"));

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSection(string title, System.Action draw)
    {
        if (EditorStyles.helpBox == null) { draw(); return; }
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        draw();
        EditorGUILayout.EndVertical();
    }
}
