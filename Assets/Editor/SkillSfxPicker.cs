using System.Reflection;
using UnityEditor;
using UnityEngine;

// 스킬 효과음을 **들어 보고 고르는** 창 (2026-09-27 사용자: "내가 후보군까지 다 듣고 최종적으로
// 쓰고 싶은 효과음에 체크를 치든 순위 1로 빼든 하는 방식으로 결정하고 싶어").
//
// 슬롯마다 후보 4개를 라디오로 놓고, ▶ 버튼으로 그 자리에서 재생한다. 고른 값은 SkillSfxLibrary 에셋에 저장된다.
//
// 🔴 미리듣기는 `UnityEditor.AudioUtil`의 내부 API다 — 버전마다 이름이 달라서 이름 후보를 순서대로 찾고,
//    하나도 없으면 버튼을 잠그고 안내를 띄운다(창이 통째로 죽지 않게).
public class SkillSfxPicker : EditorWindow
{
    private SkillSfxLibrary lib;
    private Vector2 scroll;
    private static MethodInfo playMethod, stopMethod;
    private static bool probed;

    [MenuItem("Window/Blueberry Defense/스킬 효과음 고르기")]
    private static void Open()
    {
        SkillSfxPicker w = GetWindow<SkillSfxPicker>("스킬 효과음");
        w.minSize = new Vector2(640f, 400f);
        w.Reload();
    }

    private void Reload()
    {
        lib = Resources.Load<SkillSfxLibrary>("SkillSfxLibrary");
    }

    // ── 미리듣기 (에디터 내부 API 리플렉션) ────────────────────────────────
    private static void Probe()
    {
        if (probed) return;
        probed = true;
        System.Type util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (util == null) return;
        string[] playNames = { "PlayPreviewClip", "PlayClip" };
        foreach (string n in playNames)
        {
            foreach (MethodInfo m in util.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (m.Name != n) continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length >= 1 && p[0].ParameterType == typeof(AudioClip)) { playMethod = m; break; }
            }
            if (playMethod != null) break;
        }
        string[] stopNames = { "StopAllPreviewClips", "StopAllClips" };
        foreach (string n in stopNames)
        {
            MethodInfo m = util.GetMethod(n, BindingFlags.Static | BindingFlags.Public);
            if (m != null) { stopMethod = m; break; }
        }
    }

    private static void Preview(AudioClip clip)
    {
        Probe();
        if (clip == null || playMethod == null) return;
        if (stopMethod != null) stopMethod.Invoke(null, null);
        ParameterInfo[] p = playMethod.GetParameters();
        object[] args = new object[p.Length];
        args[0] = clip;
        for (int i = 1; i < p.Length; i++)
            args[i] = p[i].ParameterType == typeof(bool) ? (object)false : (object)0;
        playMethod.Invoke(null, args);
    }

    private void OnGUI()
    {
        if (lib == null)
        {
            EditorGUILayout.HelpBox("Assets/Resources/SkillSfxLibrary.asset 이 없습니다.", MessageType.Warning);
            if (GUILayout.Button("다시 찾기")) Reload();
            return;
        }

        Probe();
        int slots = lib.slots != null ? lib.slots.Length : 0;
        int unfilled = 0, empty = 0;
        for (int i = 0; i < slots; i++)
        {
            SkillSfxLibrary.Slot s = lib.slots[i];
            if (s == null) continue;
            int have = 0;
            if (s.candidates != null)
                foreach (AudioClip c in s.candidates) if (c != null) have++;
            if (have < SkillSfxLibrary.CandidateCount) unfilled++;
            if (s.Clip == null) empty++;
        }

        EditorGUILayout.LabelField($"슬롯 {slots}개 · 후보가 덜 찬 슬롯 {unfilled}개 · 고른 칸이 빈 슬롯 {empty}개",
            EditorStyles.boldLabel);
        if (playMethod == null)
            EditorGUILayout.HelpBox("이 Unity 버전에서 미리듣기 API를 못 찾았습니다 — 클립을 인스펙터에서 눌러 들으세요.",
                MessageType.Info);
        EditorGUILayout.Space(4f);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUI.BeginChangeCheck();

        for (int i = 0; i < slots; i++)
        {
            SkillSfxLibrary.Slot s = lib.slots[i];
            if (s == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(s.label) ? s.id : s.label, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("볼륨", GUILayout.Width(32f));
            s.volume = EditorGUILayout.Slider(s.volume, 0f, SkillSfxLibrary.MaxVolume, GUILayout.Width(160f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(s.id, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(s.note))
                EditorGUILayout.LabelField(s.note, EditorStyles.wordWrappedMiniLabel);

            if (s.candidates == null || s.candidates.Length != SkillSfxLibrary.CandidateCount)
                System.Array.Resize(ref s.candidates, SkillSfxLibrary.CandidateCount);

            for (int c = 0; c < s.candidates.Length; c++)
            {
                EditorGUILayout.BeginHorizontal();
                bool on = s.chosen == c;
                bool now = EditorGUILayout.Toggle(on, EditorStyles.radioButton, GUILayout.Width(18f));
                if (now && !on) s.chosen = c;              // 라디오 — 하나만 켜진다
                s.candidates[c] = (AudioClip)EditorGUILayout.ObjectField(s.candidates[c], typeof(AudioClip), false);
                using (new EditorGUI.DisabledScope(s.candidates[c] == null || playMethod == null))
                    if (GUILayout.Button("▶", GUILayout.Width(28f))) Preview(s.candidates[c]);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        EditorGUILayout.EndScrollView();

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(lib);
            SkillSfx.ClearCache();
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("저장")) { EditorUtility.SetDirty(lib); AssetDatabase.SaveAssets(); }
        if (GUILayout.Button("재생 중지")) { Probe(); if (stopMethod != null) stopMethod.Invoke(null, null); }
        EditorGUILayout.EndHorizontal();
    }
}
