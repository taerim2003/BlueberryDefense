using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

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

    [Header("Outgame Level (선택 — 패널 하단 경험치 바)")]
    [SerializeField] private TMP_Text levelText;      // "Lv N"
    [SerializeField] private Image levelXpFill;       // filled 이미지(fillAmount)
    [SerializeField] private TMP_Text levelXpText;    // "현재/다음" 경험치

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
    private int prevEssence = -1, prevCrystal = -1, prevPowder = -1;

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
        prevEssence = prevCrystal = prevPowder = -1; // 재오픈 시 자원 펀치 생략
        RefreshAll();
        PlayOpenStagger();
    }

    public void Close()
    {
        KillAllTweens();
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // 패널 열 때 노드 스태거 pop-in
    private void PlayOpenStagger()
    {
        int i = 0;
        foreach (NodeView v in views.Values)
        {
            if (v.rt == null) continue;
            v.scaleTween?.Kill();
            v.rt.localScale = Vector3.one * 0.4f;
            v.scaleTween = v.rt.DOScale(1f, 0.35f).SetDelay(i * 0.015f).SetEase(Ease.OutBack).SetUpdate(true);
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
            if (SkillTreeSave.RefundOneLevel(tree, id)) { PlayNodePunch(id, -0.22f); RefreshAll(); RefreshTooltip(); }
            return;
        }
        // 좌클릭 = 레벨업/구매 (마스킹된 노드는 무시)
        if (!IsRevealed(id, SkillTreeSave.UnlockedIds())) return;
        if (SkillTreeSave.TryUpgrade(tree, id)) { PlayNodePunch(id, 0.4f); RefreshAll(); RefreshTooltip(); }
    }

    // 구매(+)/환불(-) 시 노드 펀치. 펀치는 스케일 채널이라 진행 중 pop-in/호버 트윈을 교체한다.
    private void PlayNodePunch(string id, float strength)
    {
        if (!views.TryGetValue(id, out NodeView v) || v.rt == null) return;
        v.scaleTween?.Kill();
        v.rt.localScale = Vector3.one;
        v.scaleTween = v.rt.DOPunchScale(Vector3.one * strength, 0.45f, 8, 0.6f).SetUpdate(true);
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
        v.scaleTween = v.rt.DOScale(entering ? 1.12f : 1f, 0.18f).SetEase(Ease.OutBack).SetUpdate(true);
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
        int ess = SkillTreeSave.AvailableEssence(tree);
        int cry = SkillTreeSave.AvailableCrystal(tree);
        int pow = SkillTreeSave.AvailablePowder(tree);
        if (essenceText != null) { essenceText.text = ess + " 정수"; if (prevEssence >= 0 && ess != prevEssence) PunchCurrency(essenceText); }
        if (crystalText != null) { crystalText.text = cry + " 결정"; if (prevCrystal >= 0 && cry != prevCrystal) PunchCurrency(crystalText); }
        if (powderText != null) { powderText.text = pow + " 가루"; if (prevPowder >= 0 && pow != prevPowder) PunchCurrency(powderText); }
        prevEssence = ess; prevCrystal = cry; prevPowder = pow;
        UpdateLevelBar();
        RefreshNodes();
        RefreshLines();
    }

    // 아웃게임 레벨 바(선택): 총정수 기반 레벨/진행도 표시. 레벨은 판 종료 때만 오르므로 패널 열 때 갱신으로 충분.
    private void UpdateLevelBar()
    {
        if (levelText != null) levelText.text = "Lv " + SkillTreeSave.OutgameLevel;
        if (levelXpFill != null) levelXpFill.fillAmount = SkillTreeSave.LevelProgress;
        if (levelXpText != null) levelXpText.text = SkillTreeSave.XpIntoCurrentLevel + " / " + SkillTreeSave.XpForNextLevel;
    }

    private static void PunchCurrency(TMP_Text t)
    {
        t.rectTransform.DOKill();
        t.rectTransform.localScale = Vector3.one;
        t.rectTransform.DOPunchScale(Vector3.one * 0.3f, 0.4f, 6, 0.6f).SetUpdate(true);
    }

    private void RefreshNodes()
    {
        HashSet<string> unlocked = SkillTreeSave.UnlockedIds();
        foreach (NodeView v in views.Values)
        {
            bool isUnlocked = unlocked.Contains(v.node.id);
            bool revealed = IsRevealed(v.node.id, unlocked);
            bool buyable = revealed && SkillTreeSave.CanUpgrade(tree, v.node.id); // 미보유 구매 + 보유 레벨업 모두 포함

            Color c;
            if (!revealed) c = ColMasked;                          // 물음표(잠김)
            else if (isUnlocked) c = BaseColor(v.node.type);       // 활성: 원색
            else if (buyable) c = BaseColor(v.node.type) * 0.82f;  // 구매 가능: 살짝 어둡게(선명)
            else c = BaseColor(v.node.type) * 0.32f;               // 구매 불가: 많이 어둡게
            c.a = 1f;
            if (v.bg != null) v.bg.color = c;

            if (v.label != null)
            {
                if (!revealed) v.label.text = "?";
                else
                {
                    int lv = SkillTreeSave.LevelOf(v.node.id);
                    int max = SkillTreeSave.MaxLevelOf(v.node);
                    // 레벨제 노드(만렙>1)이고 보유 중이면 Lv 표기
                    v.label.text = (lv >= 1 && max > 1)
                        ? v.node.displayName + "\n<size=65%>Lv " + lv + "/" + max + "</size>"
                        : v.node.displayName;
                }
            }

            if (v.ring != null)
            {
                bool show = isUnlocked || buyable;
                v.ring.enabled = show;
                v.ring.color = isUnlocked ? RingUnlocked : RingBuyable;

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
                int lv = SkillTreeSave.LevelOf(hoveredId);
                int max = SkillTreeSave.MaxLevelOf(n);
                string resLabel = ResLabel(SkillTreeSave.ResourceOf(n));
                bool canUp = SkillTreeSave.CanUpgrade(tree, hoveredId);
                int nextCost = SkillTreeSave.NextLevelCost(tree, n);

                if (!isUnlocked)
                    tooltipCost.text = nextCost + " " + resLabel + (canUp ? "  ▸ 클릭하여 구매" : "");
                else if (lv >= max)
                    tooltipCost.text = (max > 1 ? "Lv " + lv + "/" + max + " (최대)" : "해금됨") + "  · 우클릭 환불";
                else
                    tooltipCost.text = "Lv " + lv + "/" + max + "  · 다음 " + nextCost + " " + resLabel
                        + (canUp ? "  ▸ 클릭" : "") + "  · 우클릭 환불";
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
