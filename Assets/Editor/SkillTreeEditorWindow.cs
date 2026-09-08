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
    private const float NodeW = 210f;
    private const float TitleH = 20f;

    private SkillTreeData data;
    private Vector2 panOffset = new Vector2(60, 80);
    private string linkingFrom;

    private bool isPanning;
    private int draggingNode = -1;

    private bool pendingAdd;
    private int pendingDelete = -1;

    private GUIStyle titleStyle;

    [MenuItem("Blueberry Defense/Skill Tree Editor")]
    public static void Open() => GetWindow<SkillTreeEditorWindow>("Skill Tree");

    // 창을 닫거나 스크립트 리컴파일로 사라질 때 배치를 잃지 않도록 확정 저장
    private void OnDisable()
    {
        if (data == null) return;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    private void OnGUI()
    {
        titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

        DrawToolbar();
        if (data == null)
        {
            EditorGUILayout.HelpBox("편집할 SkillTreeData 에셋을 상단에 지정하세요.\n(Project > 우클릭 > Create > Blueberry Defense > Skill Tree Data)", MessageType.Info);
            return;
        }

        DrawEdges();

        for (int i = 0; i < data.nodes.Count; i++)
            DrawNode(i);

        // 노드가 자기 위의 클릭을 먼저 소비한 뒤, 남은(빈 공간) 이벤트만 팬으로 처리
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
                : $"노드 {data.nodes.Count}개 · 제목바 드래그=노드 이동 · 빈 공간 드래그=화면 이동");
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
                if (e.button != 1 && e.mousePosition.y > ToolbarH && !IsOverAnyNode(e.mousePosition)) // 우클릭 제외, 빈 공간
                {
                    isPanning = true;
                    GUIUtility.hotControl = panId; // MouseDrag가 이 창으로 확실히 전달되도록
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (draggingNode >= 0 && draggingNode < data.nodes.Count)
                {
                    data.nodes[draggingNode].editorPos += e.delta;
                    EditorUtility.SetDirty(data); // 배치 변경도 에셋 저장 대상으로 표시
                    e.Use();
                    Repaint();
                }
                else if (isPanning)
                {
                    panOffset += e.delta;
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (draggingNode >= 0)
                {
                    draggingNode = -1;
                    e.Use();
                    Repaint();
                }
                else if (isPanning)
                {
                    isPanning = false;
                    if (GUIUtility.hotControl == panId) GUIUtility.hotControl = 0;
                    e.Use();
                    Repaint();
                }
                break;
        }
    }

    private bool IsOverAnyNode(Vector2 mouse)
    {
        foreach (SkillNode n in data.nodes)
            if (NodeRect(n).Contains(mouse)) return true;
        return false;
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

    // 노드는 고정 크기 박스로 직접 그린다. (예전엔 GUILayout.Window를 썼는데, Unity가 창 밖으로 나간
    //  window를 뷰 안으로 강제로 끌어당기고 그 좌표를 editorPos에 되써서 배치가 통째로 망가졌다.)
    private Rect NodeRect(SkillNode n) => new Rect(n.editorPos + panOffset, new Vector2(NodeW, NodeHeight(n)));

    private static float NodeHeight(SkillNode n)
    {
        float h = TitleH + 4 * 20f + 18f + 46f + 22f + 6f; // 제목 + 기본4필드 + 메모라벨 + 텍스트영역 + 버튼줄 + 여백
        if (n.type == SkillNodeType.SkillUnlock || n.type == SkillNodeType.SkillEnhance) h += 20f; // 스킬 선택 줄
        return h + n.prereqIds.Count * 20f;
    }

    private void DrawNode(int index)
    {
        SkillNode n = data.nodes[index];
        Rect r = NodeRect(n);

        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = TypeColor(n.type);
        GUI.Box(r, GUIContent.none, GUI.skin.window);
        GUI.backgroundColor = prevBg;

        Rect title = new Rect(r.x, r.y, r.width, TitleH);
        GUI.Label(title, string.IsNullOrEmpty(n.displayName) ? n.id : n.displayName, titleStyle);
        EditorGUIUtility.AddCursorRect(title, MouseCursor.Pan);

        // 제목바 드래그 = 노드 이동 (실제 이동은 HandlePan의 MouseDrag에서)
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && title.Contains(e.mousePosition))
        {
            draggingNode = index;
            e.Use();
        }

        GUILayout.BeginArea(new Rect(r.x + 6f, r.y + TitleH, r.width - 12f, r.height - TitleH - 2f));
        DrawNodeBody(n, index);
        GUILayout.EndArea();
    }

    private void DrawNodeBody(SkillNode n, int id)
    {
        // 노드 폭(210)이 좁아서 라벨 폭을 고정하지 않으면 EditorGUILayout이 창 너비 기준으로 라벨을 잡아
        // 오른쪽 컨트롤(연결 끊기 x 버튼 등)이 노드 밖으로 밀려 잘려 나간다.
        float prevLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 100f;

        n.id = EditorGUILayout.TextField("id", n.id);
        n.displayName = EditorGUILayout.TextField("이름", n.displayName);
        n.type = (SkillNodeType)EditorGUILayout.EnumPopup("타입", n.type);

        // 비용 등급(0,1,2…). 0=1정수 고정. 인게임 정수 비용은 SkillTreeSave.TierCost가 등급→비용으로 계산.
        n.tier = Mathf.Max(0, EditorGUILayout.IntField($"등급 (={SkillTreeSave.TierCost(Mathf.Max(0, n.tier))} 정수)", n.tier));

        // 스킬 해금/강화 노드는 대상 스킬을 지정(해금 노드는 이 스킬이 인게임 카드 풀에 등장).
        // ⚠️ 강화 노드에선 **표시용**이다 — 실제 효과는 id로 정해진다(SkillEffects의 스위치).
        //    패시브(건강·힘·암살·방어·가속·지식) 강화 노드는 여기 고를 게 없어 값이 의미 없다.
        if (n.type == SkillNodeType.SkillUnlock || n.type == SkillNodeType.SkillEnhance)
            n.skill = (ActiveSkillId)EditorGUILayout.EnumPopup("스킬", n.skill);

        // 일반 노드는 **여기 값이 곧 효과다**(SkillEffects가 축×레벨당×레벨로 읽는다).
        // 코드를 안 고치고 노드를 얼마든지 늘릴 수 있는 자리 — 축과 크기를 반드시 채울 것.
        if (n.type == SkillNodeType.Normal)
        {
            n.effect = (MetaUpgradeId)EditorGUILayout.EnumPopup("효과 축", n.effect);
            n.perLevel = EditorGUILayout.FloatField("레벨당", n.perLevel);
            n.maxLevel = Mathf.Max(1, EditorGUILayout.IntField("단계(만렙)", n.maxLevel));
        }

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

        // 선행조건 목록 — 각 줄의 x가 그 연결을 끊는다.
        // (EditorGUILayout.LabelField는 라벨 폭을 통째로 예약해 x 버튼을 밀어내므로 GUILayout.Label을 쓴다)
        for (int k = n.prereqIds.Count - 1; k >= 0; k--)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("← " + n.prereqIds[k], EditorStyles.miniLabel);
            if (GUILayout.Button("x", GUILayout.Width(22))) { n.prereqIds.RemoveAt(k); EditorUtility.SetDirty(data); }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUIUtility.labelWidth = prevLabelWidth;
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
