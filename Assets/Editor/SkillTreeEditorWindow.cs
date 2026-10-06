using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// 비주얼 노드 스킬트리 편집기. SkillTreeData 에셋을 편집한다.
// 노드는 id·이름·타입 + 효과 축(MetaUpgradeId enum)·레벨당 상승값·최대 레벨을 입력받는다.
// 메뉴: Blueberry Defense > Skill Tree Editor
//   · 캔버스 이동: 빈 공간 드래그(좌/가운데버튼)
//   · 노드 이동: 노드 제목바 드래그
//   · 연결: 부모 "연결→" → 자식 "여기로"
public class SkillTreeEditorWindow : EditorWindow
{
    private const float ToolbarH = 20f;
    private const float NodeW = 210f;
    private const float TitleH = 20f;

    // 🔴 세로 간격은 **편집기에서 그릴 때만** 벌린다. editorPos는 인게임 트리(SkillTreeUI.ToLocal)가
    //    그대로 좌표로 쓰기 때문에, 저장값을 벌리면 게임 화면의 트리도 같이 늘어난다.
    //    에셋 격자 270 × 1.2 = 324 > 가장 큰 노드(274) — 노드 사이에 50px이 남는다.
    private const float YSpread = 1.2f;

    private SkillTreeData data;
    private Vector2 panOffset = new Vector2(60, 80);
    private string linkingFrom;

    private bool isPanning;
    private int draggingNode = -1;

    private bool pendingAdd;
    private int pendingDelete = -1;

    private GUIStyle titleStyle;

    // 노드 인덱스 → 마지막 Repaint에서 잰 본문 높이
    private readonly Dictionary<int, float> measuredHeights = new Dictionary<int, float>();

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
            measuredHeights.Clear(); // 인덱스가 밀리므로 잰 높이를 버리고 다시 잰다
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
                    // 화면에서 끈 거리를 저장 좌표로 되돌린다(세로는 YSpread로 벌려 그리므로 나눠 준다)
                    data.nodes[draggingNode].editorPos += new Vector2(e.delta.x, e.delta.y / YSpread);
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
        for (int i = 0; i < data.nodes.Count; i++)
            if (NodeRect(data.nodes[i], i).Contains(mouse)) return true;
        return false;
    }

    private void AddNode()
    {
        Vector2 screen = new Vector2(position.width, position.height) * 0.4f - panOffset;
        SkillNode n = new SkillNode
        {
            id = "node_" + data.nodes.Count,
            displayName = "새 노드",
            editorPos = new Vector2(screen.x, screen.y / YSpread), // 화면 좌표 → 저장 좌표
        };
        data.nodes.Add(n);
        EditorUtility.SetDirty(data);
    }

    // 노드는 고정 크기 박스로 직접 그린다. (예전엔 GUILayout.Window를 썼는데, Unity가 창 밖으로 나간
    //  window를 뷰 안으로 강제로 끌어당기고 그 좌표를 editorPos에 되써서 배치가 통째로 망가졌다.)
    // 저장 좌표 → 화면 좌표. 세로만 YSpread배로 벌린다(저장값은 그대로).
    private Vector2 CanvasPos(SkillNode n) => new Vector2(n.editorPos.x, n.editorPos.y * YSpread) + panOffset;

    private Rect NodeRect(SkillNode n, int index) => new Rect(CanvasPos(n), new Vector2(NodeW, NodeHeight(n, index)));

    // 박스 높이는 **실제로 그려진 본문**을 재서 쓴다(DrawNodeBody 끝에서 갱신).
    // 상수로 계산하던 때는 칸을 하나 늘릴 때마다 이 식을 같이 고쳐야 했고, 잊으면 본문이 박스 밖으로
    // 잘려 '여기로'·'삭제' 버튼이 통째로 안 보였다(가격·대역 칸이 늘었을 때 실제로 그랬다).
    private float NodeHeight(SkillNode n, int index)
        => measuredHeights.TryGetValue(index, out float measured) ? measured : EstimateHeight(n);

    // 측정 전(그 노드를 아직 한 번도 안 그린 프레임) 임시값
    private static float EstimateHeight(SkillNode n)
    {
        float h = TitleH + 5 * 20f + 18f + 46f + 22f + 8f; // 제목 + 기본5필드 + 메모라벨 + 텍스트영역 + 버튼줄 + 여백
        if (n.type == SkillNodeType.SkillUnlock || n.type == SkillNodeType.SkillEnhance) h += 20f; // 스킬 선택 줄
        if (n.type == SkillNodeType.Normal) h += 40f; // 효과 축 + 효과량
        return h + n.prereqIds.Count * 20f;
    }

    private void DrawNode(int index)
    {
        SkillNode n = data.nodes[index];
        Rect r = NodeRect(n, index);

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

        // 영역 높이는 잰 본문 높이와 **정확히** 같게 둔다(아래 여백 8f는 박스에만 준다) —
        // 남는 공간이 있으면 늘어나는 컨트롤이 그걸 먹고 다시 재면서 높이가 매 프레임 커진다.
        GUILayout.BeginArea(new Rect(r.x + 6f, r.y + TitleH, r.width - 12f, r.height - TitleH - 8f));
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

        // 가격 = 이 노드의 1레벨 정수 비용. 에셋에 직접 저작한다(2026-09-19 — 대역 등비 공식 폐기).
        n.cost = Mathf.Max(1, EditorGUILayout.IntField("가격 (정수)", n.cost));
        // 대역은 이제 **표시·그룹용**일 뿐 비용과 무관하다(일반 노드 이름의 I~VI와 같은 뜻).
        n.tier = Mathf.Max(0, EditorGUILayout.IntField("대역 (표시용)", n.tier));

        // 🔴 가격을 노드마다 손으로 치면 "부모보다 싼 자식"이 반드시 생긴다 — 그러면 선행을 안 찍고도
        //    낼 수 있어서 해금 순서가 무너진다. 해금류는 싸도 되는 것이 규칙이라 예외.
        if (n.type != SkillNodeType.SkillUnlock && n.type != SkillNodeType.SpecialUnlock)
            foreach (string pid in n.prereqIds)
            {
                SkillNode parent = data != null ? data.nodes.Find(x => x.id == pid) : null;
                if (parent != null && n.cost < SkillTreeSave.CostOf(parent))
                    EditorGUILayout.HelpBox($"선행 {pid}({SkillTreeSave.CostOf(parent)}정수)보다 쌉니다", MessageType.Error);
            }

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
            // 레벨제가 폐지돼 노드는 한 번만 산다 — 이 값이 그 노드가 주는 전부다(단계 칸은 없앴다).
            n.perLevel = EditorGUILayout.FloatField("효과량", n.perLevel);
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

        // 그려진 본문의 바닥을 재서 다음 프레임 박스 높이로 쓴다(레이아웃 좌표는 Repaint에서만 유효).
        if (Event.current.type == EventType.Repaint)
        {
            float h = TitleH + GUILayoutUtility.GetLastRect().yMax + 8f;
            if (!measuredHeights.TryGetValue(id, out float prev) || Mathf.Abs(prev - h) > 0.5f)
            {
                measuredHeights[id] = h;
                Repaint();
            }
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
            Vector2 to = CanvasPos(n) + new Vector2(105, 8);
            foreach (string pid in n.prereqIds)
            {
                SkillNode p = data.Find(pid);
                if (p == null) continue;
                Vector2 from = CanvasPos(p) + new Vector2(105, 8);
                Handles.DrawBezier(from, to, from + Vector2.up * 45, to - Vector2.up * 45, TypeColor(n.type), null, 3f);
            }
        }
        Handles.EndGUI();
    }
}
