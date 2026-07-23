using UnityEngine;
using UnityEditor;

// 비주얼 노드 스킬트리 편집기. SkillTreeData 에셋을 편집한다.
// 노드는 id·이름·타입 + 효과(자유 텍스트)만 입력받는다(효과가 노드마다 제각각이라 enum이 아닌 자유 텍스트).
// 메뉴: Blueberry Defense > Skill Tree Editor
//   · 캔버스 이동: 빈 공간 드래그(좌/가운데버튼)
//   · 노드 이동: 노드 제목바 드래그
//   · 연결: 부모 "연결→" → 자식 "여기로"
public class SkillTreeEditorWindow : EditorWindow
{
    private const float ToolbarH = 20f;

    private SkillTreeData data;
    private Vector2 panOffset = new Vector2(60, 80);
    private string linkingFrom;

    private bool isPanning;

    private bool pendingAdd;
    private int pendingDelete = -1;

    [MenuItem("Blueberry Defense/Skill Tree Editor")]
    public static void Open() => GetWindow<SkillTreeEditorWindow>("Skill Tree");

    private void OnGUI()
    {
        DrawToolbar();
        if (data == null)
        {
            EditorGUILayout.HelpBox("편집할 SkillTreeData 에셋을 상단에 지정하세요.\n(Project > 우클릭 > Create > Blueberry Defense > Skill Tree Data)", MessageType.Info);
            return;
        }

        DrawEdges();

        Color prevBg = GUI.backgroundColor;
        BeginWindows();
        for (int i = 0; i < data.nodes.Count; i++)
        {
            SkillNode n = data.nodes[i];
            GUI.backgroundColor = TypeColor(n.type);
            Rect r = new Rect(n.editorPos + panOffset, new Vector2(210, 10));
            Rect moved = GUILayout.Window(i, r, DrawNode, string.IsNullOrEmpty(n.displayName) ? n.id : n.displayName);
            n.editorPos = moved.position - panOffset;
        }
        EndWindows();
        GUI.backgroundColor = prevBg;

        // 노드(window)가 자기 위의 클릭을 먼저 소비한 뒤, 남은(빈 공간) 이벤트만 팬으로 처리
        HandlePan();

        if (pendingAdd) { AddNode(); pendingAdd = false; }
        if (pendingDelete >= 0 && pendingDelete < data.nodes.Count)
        {
            string removedId = data.nodes[pendingDelete].id;
            data.nodes.RemoveAt(pendingDelete);
            foreach (SkillNode n in data.nodes) n.prereqIds.Remove(removedId);
            pendingDelete = -1;
            EditorUtility.SetDirty(data);
        }

        if (GUI.changed) EditorUtility.SetDirty(data);
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        SkillTreeData newData = (SkillTreeData)EditorGUILayout.ObjectField(data, typeof(SkillTreeData), false, GUILayout.Width(200));
        if (newData != data) { data = newData; linkingFrom = null; }
        if (data != null)
        {
            if (GUILayout.Button("노드 추가", EditorStyles.toolbarButton, GUILayout.Width(64))) pendingAdd = true;
            if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.Width(42)))
            { EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); }
            if (GUILayout.Button("뷰 리셋", EditorStyles.toolbarButton, GUILayout.Width(52)))
            { panOffset = new Vector2(60, 80); Repaint(); }
            GUILayout.Space(8);
            GUILayout.Label(linkingFrom != null
                ? $"연결 중: [{linkingFrom}] → 대상 노드의 '여기로' 클릭 (Esc 취소)"
                : $"노드 {data.nodes.Count}개 · 빈 공간 드래그=이동");
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void HandlePan()
    {
        Event e = Event.current;
        int panId = GUIUtility.GetControlID(FocusType.Passive);

        switch (e.type)
        {
            case EventType.KeyDown:
                if (e.keyCode == KeyCode.Escape) { linkingFrom = null; Repaint(); }
                break;

            case EventType.MouseDown:
                if (e.button != 1 && e.mousePosition.y > ToolbarH) // 우클릭 제외, 빈 공간
                {
                    isPanning = true;
                    GUIUtility.hotControl = panId; // MouseDrag가 이 창으로 확실히 전달되도록
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (isPanning)
                {
                    panOffset += e.delta;
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (isPanning)
                {
                    isPanning = false;
                    if (GUIUtility.hotControl == panId) GUIUtility.hotControl = 0;
                    e.Use();
                    Repaint();
                }
                break;
        }
    }

    private void AddNode()
    {
        SkillNode n = new SkillNode
        {
            id = "node_" + data.nodes.Count,
            displayName = "새 노드",
            hasEffect = false,
            editorPos = new Vector2(position.width, position.height) * 0.4f - panOffset,
        };
        data.nodes.Add(n);
        EditorUtility.SetDirty(data);
    }

    private void DrawNode(int id)
    {
        SkillNode n = data.nodes[id];

        n.id = EditorGUILayout.TextField("id", n.id);
        n.displayName = EditorGUILayout.TextField("이름", n.displayName);
        n.type = (SkillNodeType)EditorGUILayout.EnumPopup("타입", n.type);

        // 비용 등급(0,1,2…). 0=1정수 고정. 인게임 정수 비용은 SkillTreeSave.TierCost가 등급→비용으로 계산.
        n.tier = Mathf.Max(0, EditorGUILayout.IntField($"등급 (={SkillTreeSave.TierCost(Mathf.Max(0, n.tier))} 정수)", n.tier));

        // 스킬 해금/강화 노드는 대상 스킬을 지정(해금 노드는 이 스킬이 인게임 카드 풀에 등장).
        if (n.type == SkillNodeType.SkillUnlock || n.type == SkillNodeType.SkillEnhance)
            n.skill = (ActiveSkillId)EditorGUILayout.EnumPopup("스킬", n.skill);

        EditorGUILayout.LabelField("효과 / 메모", EditorStyles.miniBoldLabel);
        n.description = EditorGUILayout.TextArea(n.description, GUILayout.Height(42));

        EditorGUILayout.BeginHorizontal();
        if (linkingFrom == null)
        {
            if (GUILayout.Button("연결→")) linkingFrom = n.id;
        }
        else if (linkingFrom != n.id)
        {
            if (GUILayout.Button("여기로"))
            {
                if (!n.prereqIds.Contains(linkingFrom)) n.prereqIds.Add(linkingFrom);
                linkingFrom = null;
                EditorUtility.SetDirty(data);
            }
        }
        else if (GUILayout.Button("연결취소")) linkingFrom = null;

        if (GUILayout.Button("삭제")) pendingDelete = id;
        EditorGUILayout.EndHorizontal();

        for (int k = n.prereqIds.Count - 1; k >= 0; k--)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("← " + n.prereqIds[k]);
            if (GUILayout.Button("x", GUILayout.Width(22))) { n.prereqIds.RemoveAt(k); EditorUtility.SetDirty(data); }
            EditorGUILayout.EndHorizontal();
        }

        GUI.DragWindow();
    }

    private static Color TypeColor(SkillNodeType type) => type switch
    {
        SkillNodeType.SkillUnlock => new Color(1f, 0.8f, 0.2f),     // 금색 = 스킬 해금
        SkillNodeType.SkillEnhance => new Color(1f, 0.45f, 0.9f),   // 마젠타 = 스킬 강화
        SkillNodeType.SpecialUnlock => new Color(0.95f, 0.95f, 0.2f), // 노랑 = 특수 해금
        _ => Color.cyan,                                            // 일반
    };

    private void DrawEdges()
    {
        Handles.BeginGUI();
        foreach (SkillNode n in data.nodes)
        {
            Vector2 to = n.editorPos + panOffset + new Vector2(105, 8);
            foreach (string pid in n.prereqIds)
            {
                SkillNode p = data.Find(pid);
                if (p == null) continue;
                Vector2 from = p.editorPos + panOffset + new Vector2(105, 8);
                Handles.DrawBezier(from, to, from + Vector2.up * 45, to - Vector2.up * 45, TypeColor(n.type), null, 3f);
            }
        }
        Handles.EndGUI();
    }
}
