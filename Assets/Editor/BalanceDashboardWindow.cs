using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 흩어진 밸런스 SO(스킬·적·맵·캐릭터·스케일링)를 한 창의 탭으로 묶는 편의 도구.
// Window > Blueberry Defense > Balance Dashboard
// 원칙: 필드별 UI를 손으로 짜지 않고, 타입별 에셋을 자동 발견해 기본 인스펙터로 그린다.
//       → SO에 필드가 늘거나 새 맵/캐릭터/적 에셋을 만들면 여기 손대지 않아도 자동 반영.
public class BalanceDashboardWindow : EditorWindow
{
    private enum Tab { Skills, Enemies, Maps, Characters, Scaling, Constants }
    private static readonly string[] TabLabels = { "스킬", "적", "맵", "캐릭터", "스케일링·XP", "전역상수" };

    private Tab tab;
    private Vector2 scroll;

    // 에셋별 기본 인스펙터를 캐시(매 프레임 재생성 방지 + 폴드아웃 상태 유지). OnDisable에서 파기.
    private readonly Dictionary<Object, Editor> editorCache = new();

    [MenuItem("Window/Blueberry Defense/Balance Dashboard")]
    private static void Open() => GetWindow<BalanceDashboardWindow>("밸런스");

    private void OnDisable()
    {
        foreach (var ed in editorCache.Values)
            if (ed != null) DestroyImmediate(ed);
        editorCache.Clear();
    }

    private void OnGUI()
    {
        tab = (Tab)GUILayout.Toolbar((int)tab, TabLabels);
        EditorGUILayout.Space();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        switch (tab)
        {
            case Tab.Skills:
                DrawAllOfType<SkillProgression>("스킬 (시작값 + 레벨업 커브)");
                DrawAllOfType<PassiveProgression>("패시브 (기본값 + 레벨업당 상승값)");
                break;
            case Tab.Enemies:
                DrawAllOfType<EnemyDefinition>("적 스탯 (이속·피해·체력·XP·정수드랍)");
                break;
            case Tab.Maps:
                DrawAllOfType<MapDefinition>("맵 (배경·BGM·적 로스터·스폰)");
                DrawAllOfType<StageTable>("스테이지 구성 (물량·확률·배율)");
                break;
            case Tab.Characters:
                DrawAllOfType<CharacterDefinition>("캐릭터 (시작스킬·허용풀·기본체력·외형)");
                break;
            case Tab.Scaling:
                DrawAllOfType<ScalingTable>("후반 스케일링 + XP 커브");
                break;
            case Tab.Constants:
                DrawConstants();
                break;
        }
        EditorGUILayout.EndScrollView();
    }

    // 프로젝트 내 T 타입 SO 에셋을 전부 찾아 각각 헤더 + 기본 인스펙터로 그린다.
    private void DrawAllOfType<T>(string caption) where T : ScriptableObject
    {
        EditorGUILayout.LabelField(caption, EditorStyles.boldLabel);

        string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
        if (guids.Length == 0)
        {
            EditorGUILayout.HelpBox(typeof(T).Name + " 에셋이 없습니다.", MessageType.Info);
            EditorGUILayout.Space();
            return;
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) continue;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(asset.name, EditorStyles.miniBoldLabel);
            if (GUILayout.Button("선택", GUILayout.Width(50)))
                Selection.activeObject = asset;
            EditorGUILayout.EndHorizontal();

            if (!editorCache.TryGetValue(asset, out Editor editor) || editor == null)
            {
                editor = Editor.CreateEditor(asset);
                editorCache[asset] = editor;
            }
            using (new EditorGUI.IndentLevelScope())
                editor.OnInspectorGUI();

            EditorGUILayout.Space();
        }
    }

    // BalanceConstants는 const(코드) — 인스펙터로 못 고친다. 값만 모아 보여주고 편집은 파일로 안내.
    private void DrawConstants()
    {
        EditorGUILayout.LabelField("전역 상수 (BalanceConstants.cs — 읽기 전용)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("이 값들은 코드 const라 여기서 편집할 수 없습니다. 값을 바꾸려면 파일을 여세요 (Tier B).", MessageType.Info);

        var fields = typeof(BalanceConstants).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (var f in fields)
        {
            if (!f.IsLiteral) continue; // const만
            EditorGUILayout.LabelField(f.Name, f.GetValue(null).ToString());
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("BalanceConstants.cs 열기"))
        {
            string[] guids = AssetDatabase.FindAssets("BalanceConstants t:MonoScript");
            if (guids.Length > 0)
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guids[0])));
        }
    }
}
