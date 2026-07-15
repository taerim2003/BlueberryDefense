using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// 트리 노드 하나의 입력 처리. 런타임에 SkillTreeUI가 각 노드에 붙인다(씬/프리팹에 저장되지 않아 같은 파일 OK).
// 좌클릭=구매, 우클릭=환불, 호버=툴팁.
public class SkillNodeButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public string NodeId;
    public System.Action<string, bool> OnClickNode; // (id, isRightClick)
    public System.Action<string> OnHoverEnter;
    public System.Action<string> OnHoverExit;

    public void OnPointerClick(PointerEventData e)
        => OnClickNode?.Invoke(NodeId, e.button == PointerEventData.InputButton.Right);

    public void OnPointerEnter(PointerEventData e) => OnHoverEnter?.Invoke(NodeId);
    public void OnPointerExit(PointerEventData e) => OnHoverExit?.Invoke(NodeId);
}

// 인게임(타이틀) 스킬트리 패널. MainSkillTree.asset을 읽어 노드/연결선을 런타임 생성.
// 팬(드래그)·줌(휠)·좌클릭 구매·우클릭 환불(자식 캐스케이드)·호버 툴팁·리셋·빌드셋.
// 인접(보유 노드 옆) 노드만 정보 노출, 나머지는 물음표로 마스킹.
public class SkillTreeUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SkillTreeData tree;

    [Header("Shell")]
    [SerializeField] private RectTransform panelRect;  // panelRoot의 RectTransform (툴팁 좌표계)
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private RectTransform content;    // 팬/줌 대상: 노드+연결선을 담음
    [SerializeField] private RectTransform nodeLayer;
    [SerializeField] private RectTransform lineLayer;

    [Header("Prefabs")]
    [SerializeField] private Button nodeButtonPrefab;
    [SerializeField] private Image linePrefab;
    [SerializeField] private BuildSlotUI buildSlotPrefab;
    [SerializeField] private Transform buildSlotContainer;

    [Header("Currency")]
    [SerializeField] private TMP_Text essenceText;
    [SerializeField] private TMP_Text crystalText;
    [SerializeField] private TMP_Text powderText;

    [Header("Tooltip")]
    [SerializeField] private GameObject tooltipRoot;
    [SerializeField] private RectTransform tooltipRect;
    [SerializeField] private TMP_Text tooltipName;
    [SerializeField] private TMP_Text tooltipDesc;
    [SerializeField] private TMP_Text tooltipCost;

    [Header("Actions")]
    [SerializeField] private Button respecButton;
    [SerializeField] private Button closeButton;

    [Header("Layout")]
    [SerializeField] private float posScale = 0.55f;
    [SerializeField] private float minZoom = 0.45f;
    [SerializeField] private float maxZoom = 1.6f;

    // 타입 기본색
    private static readonly Color ColNormal = new Color(0.42f, 0.68f, 1f);
    private static readonly Color ColGate = new Color(1f, 0.82f, 0.2f);
    private static readonly Color ColActive = new Color(1f, 0.35f, 0.85f);
    private static readonly Color ColMasked = new Color(0.22f, 0.22f, 0.26f);
    private static readonly Color ColLineDim = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color ColLineOn = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color RingUnlocked = new Color(0.4f, 1f, 0.5f, 1f);
    private static readonly Color RingBuyable = new Color(1f, 1f, 1f, 0.9f);

    private class NodeView { public SkillNode node; public RectTransform rt; public Image bg; public Image ring; public TMP_Text label; }
    private readonly Dictionary<string, NodeView> views = new();
    private readonly List<(string from, string to, Image img)> lines = new();

    private string hoveredId;
    private bool built;

    private void Awake()
    {
        if (respecButton != null)
        {
            respecButton.onClick.AddListener(OnRespec);
            var t = respecButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = "리셋";
        }
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) Build();
        if (panelRoot != null) panelRoot.SetActive(true);
        hoveredId = null;
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
        RefreshAll();
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ── 최초 1회: 노드·연결선·빌드슬롯 생성 ──
    private void Build()
    {
        if (tree == null || nodeButtonPrefab == null) return;

        foreach (SkillNode n in tree.nodes)
            foreach (string pre in n.prereqIds)
            {
                SkillNode p = tree.Find(pre);
                if (p == null || linePrefab == null) continue;
                Image line = Instantiate(linePrefab, lineLayer);
                line.gameObject.SetActive(true);
                PlaceLine(line.rectTransform, ToLocal(p.editorPos), ToLocal(n.editorPos));
                lines.Add((pre, n.id, line));
            }

        foreach (SkillNode n in tree.nodes)
        {
            Button btn = Instantiate(nodeButtonPrefab, nodeLayer);
            btn.gameObject.SetActive(true);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchoredPosition = ToLocal(n.editorPos);

            // 노드 프리팹 구조: 루트(Image raycast) > Ring > BG > Label
            var view = new NodeView
            {
                node = n,
                rt = rt,
                bg = btn.transform.Find("BG")?.GetComponent<Image>(),
                ring = btn.transform.Find("Ring")?.GetComponent<Image>(),
                label = btn.GetComponentInChildren<TMP_Text>(),
            };

            var input = btn.gameObject.AddComponent<SkillNodeButton>();
            input.NodeId = n.id;
            input.OnClickNode = OnNodeClick;
            input.OnHoverEnter = OnNodeHoverEnter;
            input.OnHoverExit = OnNodeHoverExit;

            views[n.id] = view;
        }

        if (buildSlotPrefab != null && buildSlotContainer != null)
            for (int i = 1; i <= SkillTreeSave.BuildSlots; i++)
            {
                BuildSlotUI slot = Instantiate(buildSlotPrefab, buildSlotContainer);
                slot.gameObject.SetActive(true);
                slot.Bind(i, OnBuildSave, OnBuildLoad);
            }

        built = true;
    }

    private Vector2 ToLocal(Vector2 editorPos) => new Vector2(editorPos.x, -editorPos.y) * posScale;

    private static void PlaceLine(RectTransform rt, Vector2 a, Vector2 b)
    {
        Vector2 dir = b - a;
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(dir.magnitude, rt.sizeDelta.y);
        rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    // ── 입력 ──
    private void OnNodeClick(string id, bool rightClick)
    {
        if (rightClick)
        {
            if (SkillTreeSave.RefundNode(tree, id)) { RefreshAll(); RefreshTooltip(); }
            return;
        }
        // 좌클릭 = 구매 (마스킹된 노드는 무시)
        if (!IsRevealed(id, SkillTreeSave.UnlockedIds())) return;
        if (SkillTreeSave.TryUnlock(tree, id)) { RefreshAll(); RefreshTooltip(); }
    }

    private void OnNodeHoverEnter(string id) { hoveredId = id; RefreshTooltip(); }

    private void OnNodeHoverExit(string id)
    {
        if (hoveredId == id) hoveredId = null;
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    private void OnRespec() { SkillTreeSave.Respec(); RefreshAll(); RefreshTooltip(); }
    private void OnBuildSave(int slot) { SkillTreeSave.SaveBuild(slot); RefreshAll(); }
    private void OnBuildLoad(int slot) { SkillTreeSave.LoadBuild(slot); RefreshAll(); RefreshTooltip(); }

    // ── 인접/공개 판정: 해금됨 or 선행 중 하나라도 해금됨 or 루트(선행 없음) ──
    private bool IsRevealed(string id, HashSet<string> unlocked)
    {
        if (unlocked.Contains(id)) return true;
        SkillNode n = tree.Find(id);
        if (n == null) return false;
        if (n.prereqIds.Count == 0) return true;
        foreach (string pre in n.prereqIds)
            if (unlocked.Contains(pre)) return true;
        return false;
    }

    // ── 갱신 ──
    private void RefreshAll()
    {
        if (essenceText != null) essenceText.text = SkillTreeSave.AvailableEssence(tree) + " 정수";
        if (crystalText != null) crystalText.text = SkillTreeSave.AvailableCrystal(tree) + " 결정";
        if (powderText != null) powderText.text = SkillTreeSave.AvailablePowder(tree) + " 가루";
        RefreshNodes();
        RefreshLines();
    }

    private void RefreshNodes()
    {
        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        foreach (NodeView v in views.Values)
        {
            bool isUnlocked = unlocked.Contains(v.node.id);
            bool revealed = IsRevealed(v.node.id, unlocked);
            bool buyable = revealed && !isUnlocked && SkillTreeSave.CanUnlock(tree, v.node.id);

            Color c;
            if (!revealed) c = ColMasked;                          // 물음표(잠김)
            else if (isUnlocked) c = BaseColor(v.node.type);       // 활성: 원색
            else if (buyable) c = BaseColor(v.node.type) * 0.82f;  // 구매 가능: 살짝 어둡게(선명)
            else c = BaseColor(v.node.type) * 0.32f;               // 구매 불가: 많이 어둡게
            c.a = 1f;
            if (v.bg != null) v.bg.color = c;

            if (v.label != null) v.label.text = revealed ? v.node.displayName : "?";

            if (v.ring != null)
            {
                bool show = isUnlocked || buyable;
                v.ring.enabled = show;
                v.ring.color = isUnlocked ? RingUnlocked : RingBuyable;
            }
        }
    }

    private void RefreshLines()
    {
        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        foreach (var l in lines)
            if (l.img != null)
                l.img.color = unlocked.Contains(l.from) && unlocked.Contains(l.to) ? ColLineOn : ColLineDim;
    }

    // ── 툴팁 ──
    private void RefreshTooltip()
    {
        if (tooltipRoot == null || hoveredId == null) { if (tooltipRoot != null) tooltipRoot.SetActive(false); return; }

        SkillNode n = tree.Find(hoveredId);
        if (n == null) { tooltipRoot.SetActive(false); return; }

        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        bool revealed = IsRevealed(hoveredId, unlocked);
        bool isUnlocked = unlocked.Contains(hoveredId);

        if (!revealed)
        {
            if (tooltipName != null) tooltipName.text = "???";
            if (tooltipDesc != null) tooltipDesc.text = "인접 노드를 먼저 열어야 정보를 볼 수 있습니다";
            if (tooltipCost != null) tooltipCost.text = "";
        }
        else
        {
            if (tooltipName != null) tooltipName.text = n.displayName;
            if (tooltipDesc != null) tooltipDesc.text = n.description;
            if (tooltipCost != null)
            {
                if (isUnlocked) tooltipCost.text = "해금됨 (우클릭 환불)";
                else tooltipCost.text = SkillTreeSave.CostOf(tree, n) + " " + ResLabel(SkillTreeSave.ResourceOf(n))
                        + (SkillTreeSave.CanUnlock(tree, hoveredId) ? "  ▸ 클릭하여 구매" : "");
            }
        }

        tooltipRoot.SetActive(true);
        PositionTooltip(views.TryGetValue(hoveredId, out var v) ? v.rt : null);
    }

    private void PositionTooltip(RectTransform nodeRt)
    {
        if (nodeRt == null || tooltipRect == null || panelRect == null) return;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, nodeRt.position);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, screen, null, out Vector2 local))
        {
            float zoom = content != null ? content.localScale.y : 1f;
            tooltipRect.anchoredPosition = local + new Vector2(0f, 34f * zoom + 14f);
        }
    }

    private static string ResLabel(SkillResource res) => res switch
    {
        SkillResource.Crystal => "결정",
        SkillResource.Powder => "가루",
        _ => "정수",
    };

    private static Color BaseColor(SkillNodeType type) => type switch
    {
        SkillNodeType.Gate => ColGate,
        SkillNodeType.ActiveSkill => ColActive,
        _ => ColNormal,
    };

    // TreePanDrag가 줌 클램프에 참조
    public float MinZoom => minZoom;
    public float MaxZoom => maxZoom;
}
