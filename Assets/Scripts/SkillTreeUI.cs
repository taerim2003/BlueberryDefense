using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

// 트리 노드 하나의 입력 처리. 런타임에 SkillTreeUI가 각 노드에 붙인다(씬/프리팹에 저장되지 않아 같은 파일 OK).
// 좌클릭=구매(해금), 호버=툴팁. (되돌리기 불가 — 우클릭 환불 없음)
public class SkillNodeButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public string NodeId;
    public System.Action<string> OnClickNode;
    public System.Action<string> OnHoverEnter;
    public System.Action<string> OnHoverExit;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left) OnClickNode?.Invoke(NodeId);
    }

    public void OnPointerEnter(PointerEventData e) => OnHoverEnter?.Invoke(NodeId);
    public void OnPointerExit(PointerEventData e) => OnHoverExit?.Invoke(NodeId);
}

// 인게임(타이틀) 스킬트리 패널. MainSkillTree.asset을 읽어 노드/연결선을 런타임 생성.
// 팬(드래그)·줌(휠)·좌클릭 구매·호버 툴팁. **자원 1개(정수) · 되돌리기 불가**.
// 인접(보유 노드 옆) 노드만 정보 노출, 나머지는 물음표로 마스킹.
public class SkillTreeUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private SkillTreeData tree;

    [Header("Shell")]
    [SerializeField] private RectTransform panelRect;  // panelRoot의 RectTransform (툴팁 좌표계)
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private UITransition panelTransition; // 있으면 열고 닫을 때 팝 연출을 대신 태운다
    [SerializeField] private RectTransform content;    // 팬/줌 대상: 노드+연결선을 담음
    [SerializeField] private RectTransform nodeLayer;
    [SerializeField] private RectTransform lineLayer;

    [Header("Prefabs")]
    [SerializeField] private Button nodeButtonPrefab;
    [SerializeField] private Image linePrefab;

    [Header("Currency")]
    [SerializeField] private TMP_Text essenceText;

    [Header("Tooltip")]
    [SerializeField] private GameObject tooltipRoot;
    [SerializeField] private RectTransform tooltipRect;
    [SerializeField] private TMP_Text tooltipName;
    [SerializeField] private TMP_Text tooltipDesc;
    [SerializeField] private TMP_Text tooltipCost;

    [Header("Actions")]
    [SerializeField] private Button closeButton;

    [Header("Layout")]
    [SerializeField] private float posScale = 0.55f;
    [SerializeField] private float minZoom = 0.45f;
    [SerializeField] private float maxZoom = 1.6f;

    // 타입 기본색
    private static readonly Color ColNormal = new Color(0.42f, 0.68f, 1f);
    private static readonly Color ColUnlock = new Color(1f, 0.82f, 0.2f);   // 스킬 해금 = 금색
    private static readonly Color ColEnhance = new Color(1f, 0.35f, 0.85f); // 스킬 강화 = 마젠타
    private static readonly Color ColSpecial = new Color(0.95f, 0.95f, 0.15f); // 특수 해금 = 노랑
    private static readonly Color ColLineDim = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color ColLineOn = new Color(1f, 1f, 1f, 0.6f);
    // 노드 테두리 4상태(칸반 "폴리싱 할일 리스트업"): 만렙=파랑 / 지금 찍을 수 있음=초록 /
    // 선행은 됐는데 정수가 모자람=빨강 / 선행이 안 됨=회색.
    private static readonly Color RingMaxed = new Color(0.35f, 0.62f, 1f, 1f);
    private static readonly Color RingBuyable = new Color(0.4f, 1f, 0.5f, 1f);
    private static readonly Color RingUnaffordable = new Color(1f, 0.35f, 0.35f, 1f);
    private static readonly Color RingLocked = new Color(0.55f, 0.55f, 0.55f, 0.9f);

    private class NodeView
    {
        public SkillNode node; public RectTransform rt; public Image bg; public Image ring; public TMP_Text label;
        public Tween scaleTween;  // 호버/구매/pop-in — 한 번에 하나(교체 전 Kill)
        public Tween ringPulse;   // 구매 가능 대기 강조(링 알파 yoyo) — 스케일과 별도 채널
    }
    private class LineView { public string from; public string to; public Image img; public bool on; }
    private readonly Dictionary<string, NodeView> views = new();
    private readonly List<LineView> lines = new();

    private string hoveredId;
    private bool built;

    // 자원 텍스트 펀치용 직전값(첫 갱신엔 펀치 생략)
    private int prevEssence = -1;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void Open()
    {
        if (!built) Build();
        if (panelTransition != null) panelTransition.Show();
        else if (panelRoot != null) panelRoot.SetActive(true);
        hoveredId = null;
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
        prevEssence = -1; // 재오픈 시 자원 펀치 생략
        RefreshAll();
        PlayOpenStagger();
    }

    public void Close()
    {
        KillAllTweens();
        if (panelTransition != null) panelTransition.Hide();
        else if (panelRoot != null) panelRoot.SetActive(false);
    }

    // 패널 열 때 노드 스태거 pop-in
    private void PlayOpenStagger()
    {
        int i = 0;
        foreach (NodeView v in views.Values)
        {
            if (v.rt == null || !v.rt.gameObject.activeSelf) continue; // 안개에 가려진 노드는 건너뜀
            v.scaleTween?.Kill();
            v.rt.localScale = Vector3.one * 0.4f;
            v.scaleTween = v.rt.DOScale(1f, 0.2f).SetDelay(i * 0.01f).SetEase(Ease.OutBack).SetUpdate(true);
            i++;
        }
    }

    private void KillAllTweens()
    {
        foreach (NodeView v in views.Values)
        {
            v.scaleTween?.Kill(); v.scaleTween = null;
            v.ringPulse?.Kill(); v.ringPulse = null;
            if (v.rt != null) v.rt.localScale = Vector3.one;
            if (v.ring != null) { Color rc = v.ring.color; rc.a = 1f; v.ring.color = rc; }
        }
    }

    // ── 최초 1회: 노드·연결선 생성 ──
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
                line.color = ColLineDim; // off 초기색 명시(RefreshLines가 변화 없으면 스킵하므로)
                PlaceLine(line.rectTransform, ToLocal(p.editorPos), ToLocal(n.editorPos));
                lines.Add(new LineView { from = pre, to = n.id, img = line, on = false });
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

    // ── 입력 ── 좌클릭 = 해금(마스킹된 노드는 무시). 되돌리기 없음.
    private void OnNodeClick(string id)
    {
        if (FogOf(id, SkillTreeSave.UnlockedIds()) == Fog.Hidden) return; // 숨겨진(2링크 이상) 노드는 구매 불가
        if (SkillTreeSave.TryUpgrade(tree, id)) { SfxPlayer.Play(SfxId.SkillTreeNode); PlayNodePunch(id, 0.4f); RefreshAll(); RefreshTooltip(); }
    }

    // 구매 시 노드 펀치. 펀치는 스케일 채널이라 진행 중 pop-in/호버 트윈을 교체한다.
    private void PlayNodePunch(string id, float strength)
    {
        if (!views.TryGetValue(id, out NodeView v) || v.rt == null) return;
        v.scaleTween?.Kill();
        v.rt.localScale = Vector3.one;
        v.scaleTween = v.rt.DOPunchScale(Vector3.one * strength, 0.28f, 10, 0.6f).SetUpdate(true);
    }

    private void OnNodeHoverEnter(string id) { hoveredId = id; RefreshTooltip(); AnimateHover(id, true); }

    private void OnNodeHoverExit(string id)
    {
        if (hoveredId == id) hoveredId = null;
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
        AnimateHover(id, false);
    }

    private void AnimateHover(string id, bool entering)
    {
        if (!views.TryGetValue(id, out NodeView v) || v.rt == null) return;
        v.scaleTween?.Kill();
        v.scaleTween = v.rt.DOScale(entering ? JuicyTuning.HoverScale : 1f, JuicyTuning.HoverDuration).SetEase(Ease.OutBack).SetUpdate(true);
    }

    // ── 안개(공개 범위) ──
    // 트리 전체 규모가 처음부터 보이면 재미가 없으므로, 내가 연 노드 바로 옆(1링크)까지만 보여주고
    // 그보다 먼 노드(2링크 이상)는 존재 자체를 감춘다(노드·연결선 모두 비활성).
    private enum Fog
    {
        Hidden,   // 아예 안 보임(2링크 이상)
        Hinted,   // 해금 노드 바로 옆 — 이름·효과·비용을 모두 공개(아직 미보유). 구매 가능
        Revealed, // 해금됨 — 전부 표시
    }

    private Fog FogOf(string id, HashSet<string> unlocked)
    {
        if (unlocked.Contains(id)) return Fog.Revealed;
        SkillNode n = tree.Find(id);
        if (n == null) return Fog.Hidden;
        if (n.prereqIds.Count == 0) return Fog.Hinted; // 루트는 항상 시작점으로 보인다
        foreach (string pre in n.prereqIds)
        {
            if (unlocked.Contains(pre)) return Fog.Hinted; // 해금 노드와 인접
            // 🔴 루트의 자식은 루트를 사기 **전에도** 보여준다.
            //    이 예외가 없으면 세이브 초기화 직후(해금 0개) 34개 중 루트 1개만, 그것도 미보유라 어둡게 뜬다
            //    — 화면이 통째로 비어 보이고, 정수 0이라 그 하나도 못 산다(8/24 플레이스루 "초기화 후 스킬트리 고장").
            //    신규 플레이어의 첫 화면이 정확히 이 상태다. 후반 안개 규칙은 이 예외의 영향을 받지 않는다.
            SkillNode p = tree.Find(pre);
            if (p != null && p.prereqIds.Count == 0) return Fog.Hinted;
        }
        return Fog.Hidden;
    }

    // ── 갱신 ──
    private void RefreshAll()
    {
        int ess = SkillTreeSave.AvailableEssence(tree);
        if (essenceText != null) { essenceText.text = Loc.F("ui.essence", ess); if (prevEssence >= 0 && ess != prevEssence) PunchCurrency(essenceText); }
        prevEssence = ess;
        RefreshNodes();
        RefreshLines();
    }

    private static void PunchCurrency(TMP_Text t)
    {
        t.rectTransform.DOKill();
        t.rectTransform.localScale = Vector3.one;
        t.rectTransform.DOPunchScale(Vector3.one * 0.34f, 0.26f, 8, 0.6f).SetUpdate(true);
    }

    private void RefreshNodes()
    {
        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        foreach (NodeView v in views.Values)
        {
            Fog fog = FogOf(v.node.id, unlocked);

            // 안개 밖 노드는 통째로 감춘다(연결선도 RefreshLines에서 함께 숨김)
            if (v.rt != null && v.rt.gameObject.activeSelf != (fog != Fog.Hidden))
            {
                if (fog == Fog.Hidden) { v.scaleTween?.Kill(); v.scaleTween = null; v.ringPulse?.Kill(); v.ringPulse = null; }
                v.rt.gameObject.SetActive(fog != Fog.Hidden);
            }
            if (fog == Fog.Hidden) continue;

            bool isUnlocked = fog == Fog.Revealed;
            bool buyable = SkillTreeSave.CanUpgrade(tree, v.node.id); // 미보유 구매 + 보유 레벨업 모두 포함
            int lv = SkillTreeSave.LevelOf(v.node.id);
            int max = SkillTreeSave.MaxLevelOf(v.node);

            // 미보유(힌트) 노드도 타입 색으로 내용을 공개하되, 아직 안 산 상태임을 어둡게 구분(구매 가능하면 살짝 밝게).
            Color c = isUnlocked ? BaseColor(v.node.type) : BaseColor(v.node.type) * (buyable ? 0.7f : 0.5f);
            c.a = 1f;
            if (v.bg != null) v.bg.color = c;

            if (v.label != null)
            {
                // 이름은 인접 노드부터 공개. 레벨제 노드(만렙>1)이고 보유 중이면 Lv 표기.
                v.label.text = (isUnlocked && lv >= 1 && max > 1)
                    ? v.node.Name + "\n<size=65%>Lv " + lv + "/" + max + "</size>"
                    : v.node.Name;
            }

            if (v.ring != null)
            {
                // 보이는 노드는 전부 테두리를 켠다 — 테두리 색 자체가 "지금 이 노드를 어떻게 할 수 있는가"의 표시다.
                bool maxed = lv >= max;
                bool prereqOk = lv > 0 || SkillTreeSave.PrereqMet(tree, v.node);
                v.ring.enabled = true;
                v.ring.color = maxed ? RingMaxed
                    : buyable ? RingBuyable
                    : prereqOk ? RingUnaffordable
                    : RingLocked;

                // 구매 가능 노드만 링 알파 펄스(스케일과 별도 채널). 상태 바뀔 때만 재구성.
                v.ringPulse?.Kill(); v.ringPulse = null;
                if (buyable)
                    v.ringPulse = v.ring.DOFade(0.3f, 0.7f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
            }
        }
    }

    private void RefreshLines()
    {
        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        foreach (LineView l in lines)
        {
            if (l.img == null) continue;

            // 양쪽 끝이 모두 보이는 선만 그린다 — 안 보이는 노드로 이어지는 선이 존재를 알려주면 안 되므로
            bool visible = FogOf(l.from, unlocked) != Fog.Hidden && FogOf(l.to, unlocked) != Fog.Hidden;
            if (l.img.gameObject.activeSelf != visible) l.img.gameObject.SetActive(visible);
            if (!visible) continue;

            bool on = unlocked.Contains(l.from) && unlocked.Contains(l.to);
            if (on == l.on) continue;               // 변화 없으면 건드리지 않음
            l.img.DOKill();
            l.img.DOColor(on ? ColLineOn : ColLineDim, 0.3f).SetUpdate(true);
            l.on = on;
        }
    }

    // ── 툴팁 ──
    private void RefreshTooltip()
    {
        if (tooltipRoot == null || hoveredId == null) { if (tooltipRoot != null) tooltipRoot.SetActive(false); return; }

        SkillNode n = tree.Find(hoveredId);
        if (n == null) { tooltipRoot.SetActive(false); return; }

        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        Fog fog = FogOf(hoveredId, unlocked);
        bool isUnlocked = fog == Fog.Revealed;

        if (fog == Fog.Hidden) { tooltipRoot.SetActive(false); return; }

        if (!isUnlocked)
        {
            // 해금 노드 바로 옆(힌트) 노드는 이름·효과·비용을 모두 공개한다 — 살지 말지 미리 판단 가능하게
            if (tooltipName != null) tooltipName.text = n.Name;
            if (tooltipDesc != null) tooltipDesc.text = n.Desc;
            if (tooltipCost != null)
            {
                int cost = SkillTreeSave.NextLevelCost(tree, n);
                bool can = SkillTreeSave.CanUpgrade(tree, hoveredId);
                tooltipCost.text = Loc.F("ui.tree.cost", cost) + (can ? Loc.T("ui.tree.clickUnlock") : "");
            }
        }
        else
        {
            if (tooltipName != null) tooltipName.text = n.Name;
            if (tooltipDesc != null) tooltipDesc.text = n.Desc;
            if (tooltipCost != null)
            {
                int lv = SkillTreeSave.LevelOf(hoveredId);
                int max = SkillTreeSave.MaxLevelOf(n);
                bool canUp = SkillTreeSave.CanUpgrade(tree, hoveredId);
                int nextCost = SkillTreeSave.NextLevelCost(tree, n);

                if (lv >= max)
                    tooltipCost.text = max > 1 ? "Lv " + lv + "/" + max + " " + Loc.T("ui.tree.maxed") : Loc.T("ui.tree.unlocked");
                else
                    tooltipCost.text = "Lv " + lv + "/" + max + Loc.F("ui.tree.next", nextCost) + (canUp ? Loc.T("ui.tree.click") : "");
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

    private static Color BaseColor(SkillNodeType type) => type switch
    {
        SkillNodeType.SkillUnlock => ColUnlock,
        SkillNodeType.SkillEnhance => ColEnhance,
        SkillNodeType.SpecialUnlock => ColSpecial,
        _ => ColNormal,
    };

    // TreePanDrag가 줌 클램프에 참조
    public float MinZoom => minZoom;
    public float MaxZoom => maxZoom;
}
