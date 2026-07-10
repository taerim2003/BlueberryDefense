using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UITransition))]
[CanEditMultipleObjects]
public class UITransitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("visualRoot"), new GUIContent("Visual Root"));
        GUILayout.Space(4);

        var transitionType = serializedObject.FindProperty("transitionType");

        DrawSection("Transition", () =>
        {
            EditorGUILayout.PropertyField(transitionType, new GUIContent("Type"));
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("delay"), new GUIContent("Delay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("duration"), new GUIContent("Duration"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ease"), new GUIContent("Ease"));
        });

        if (transitionType.enumValueIndex == (int)UITransition.TransitionType.Pop)
        {
            GUILayout.Space(4);
            DrawSection("Pop", () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("popStartScale"), new GUIContent("Start Scale"));
            });
        }

        if (transitionType.enumValueIndex == (int)UITransition.TransitionType.Slide)
        {
            GUILayout.Space(4);
            DrawSection("Slide", () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("slideFrom"), new GUIContent("Direction"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("slideDistance"), new GUIContent("Distance"));
            });
        }

        GUILayout.Space(4);
        DrawSection("Wobble", () =>
        {
            var wobbleOnShow = serializedObject.FindProperty("wobbleOnShow");
            EditorGUILayout.PropertyField(wobbleOnShow, new GUIContent("On Show"));
            if (wobbleOnShow.boolValue)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("wobbleAngle"), new GUIContent("Angle"));
        });

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
