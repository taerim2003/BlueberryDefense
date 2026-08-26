using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

// 진화 선택 창 — 2루트 × 2티어 (§EvolutionRoutes).
// 씬 오브젝트는 `Battle/Canvas/EvolutionPanel/Window` 밑의 `Node_R{루트}T{티어}` 4칸 + `Arrow_R{루트}` 2개.
//   nodes 인덱스 = 루트*2 + (티어-1),  arrows 인덱스 = 루트.
// 🔴 예전엔 씬에 3×3(9칸)이 깔려 있고 여기서 5칸을 런타임에 껐다 — 씬에서는 죽은 칸이 켜진 채
//    산 칸 위에 겹쳐 보여서 어느 칸을 고쳐야 할지 분간이 안 됐다. 2026-08-26에 죽은 칸을 지우고
//    배열을 4칸/2개로 줄였다. **씬에 보이는 것 = 화면에 나오는 것**이어야 인스펙터로 고칠 수 있다.
public class EvolutionTreeUI : MonoBehaviour
{
    [System.Serializable]
    public class NodeButton
    {
        public Button button;
        public Image frame;
        public Image icon;
        public TMP_Text title;
        public TMP_Text description;
    }

    // 노드 바탕은 이제 스킨 스프라이트(UI_LongRect_Unclean)라 아래 색이 거기 곱해진다.
    // 흰색을 쓰면 판이 하얗게 떠서 나머지 UI와 어긋난다 — 기본은 스킨 색이다.
    private static readonly Color BaseFrameColor = new Color(0.420f, 0.482f, 0.910f, 1f);
    private static readonly Color OwnedFrameColor = new Color(0.55f, 0.85f, 0.62f, 1f);
    private static readonly Color GoldFrameColor = new Color(1f, 0.878f, 0.302f, 1f); // 최종 티어 달성 시
    private static readonly Color LockedFrameColor = new Color(0.22f, 0.22f, 0.30f, 1f);
    private static readonly Color LockedTextColor = new Color(0.72f, 0.72f, 0.78f, 1f);
    private static readonly Color ActiveArrowColor = new Color(1f, 0.9f, 0.4f, 1f);
    private static readonly Color InactiveArrowColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    public static EvolutionTreeUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private UITransition panelTransition;
    [SerializeField] private TMP_Text skillNameText;
    [SerializeField] private TMP_Text subInfoText;
    [SerializeField] private Image skillIcon;
    [SerializeField] private Sprite[] activeIcons;
    [SerializeField] private Sprite[] passiveIcons;
    [SerializeField] private Sprite[] pathIconSprites; // 루트 0~1 색상 아이콘 (기존 path 아이콘 재사용)
    [SerializeField] private NodeButton[] nodes; // 길이 4, index = route*2 + (tier-1)
    [SerializeField] private TMP_Text[] arrows;  // 길이 2, index = route (T1→T2 화살표)

    private PlayerSkills skills;
    private PlayerPassives passives;
    private EquippedSkill currentSkill;
    private EquippedPassive currentPassive;
    private bool isPassiveMode;
    private System.Action onClosed; // 진화 완료(모달 닫힘) 후 1회 콜백 — 연쇄 보상이 대기
    private bool isOpen;            // 닫힘 연출 중에도 panel.activeSelf는 true라 ModalPause 짝을 이 플래그로 맞춘다

    // 진화 노드 카드에 붙는 juice 연출(등장 pop-in, 진화 가능 노드 강조 펄스)용 트윈 — 재오픈/닫기 시 정리
    private readonly List<Tween> nodeTweens = new List<Tween>();

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        // 🔴 배선이 비면 여기서 조용히 죽는 게 아니라 **무엇이 비었는지** 찍고 넘어간다.
        //    씬에서 노드를 다시 만들면 배열이 통째로 NULL이 되는데, 예전엔 그 상태로 예외가 나서
        //    진화창이 통째로 안 열렸고 원인이 화면에 안 드러났다.
        for (int route = 0; route < 2; route++)
        {
            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
            {
                int i = route * 2 + tierIdx;
                if (nodes == null || i >= nodes.Length || nodes[i] == null || nodes[i].button == null)
                {
                    Debug.LogWarning("[EvolutionTreeUI] nodes[" + i + "](루트" + route + " " + (tierIdx + 1) + "차)의 Button이 비었다 — 인스펙터를 확인할 것", this);
                    continue;
                }
                int capturedRoute = route;
                nodes[i].button.onClick.AddListener(() => OnRouteClicked(capturedRoute));
            }
        }
    }

    public void Show(PlayerSkills skillsRef, EquippedSkill skill, System.Action closed = null)
    {
        skills = skillsRef;
        currentSkill = skill;
        currentPassive = null;
        isPassiveMode = false;
        onClosed = closed;
        ShowInternal();
    }

    public void Show(PlayerPassives passivesRef, EquippedPassive passive, System.Action closed = null)
    {
        passives = passivesRef;
        currentPassive = passive;
        currentSkill = null;
        isPassiveMode = true;
        onClosed = closed;
        ShowInternal();
    }

    private void ShowInternal()
    {
        bool alreadyOpen = isOpen;
        KillNodeTweens();

        string name = isPassiveMode ? currentPassive.DisplayName : currentSkill.DisplayName;
        int level = isPassiveMode ? currentPassive.Level : currentSkill.Level;
        int stage = isPassiveMode ? currentPassive.EvolutionStage : currentSkill.EvolutionStage;
        int chosenRoute = isPassiveMode ? currentPassive.Route : currentSkill.Route;
        bool canEvolve = isPassiveMode ? passives.CanEvolve(currentPassive) : skills.CanEvolve(currentSkill);

        skillNameText.text = Loc.F("ui.evotree.header", name, level);

        if (subInfoText != null)
        {
            subInfoText.text = stage >= EvolutionRoutes.MaxStage
                ? Loc.T("ui.evotree.allDone")
                : stage == 0
                    ? Loc.T("ui.evotree.pickRoute")
                    : Loc.T("ui.evotree.continueRoute");
        }

        SetIcon(skillIcon, CurrentIcon());
        if (!alreadyOpen && skillIcon != null)
        {
            skillIcon.rectTransform.localScale = Vector3.one;
            nodeTweens.Add(skillIcon.rectTransform.DOPunchScale(Vector3.one * 0.4f, 0.26f, 8, 0.5f).SetUpdate(true));
        }

        for (int route = 0; route < 2; route++)
        {
            // 1차 진화에서 고르지 않은 루트는 영영 닫힌다.
            bool routeAbandoned = stage > 0 && chosenRoute != route;
            // 연계 스킬 미보유로 잠긴 루트 — 그 스킬을 얻으면 열린다.
            bool routeUnlocked = isPassiveMode
                ? passives.IsRouteUnlocked(currentPassive.Id, route)
                : skills.IsRouteUnlocked(currentSkill.Id, route);
            string prereqName = isPassiveMode
                ? EvolutionRoutes.RoutePrereqName(currentPassive.Id, route)
                : EvolutionRoutes.RoutePrereqName(currentSkill.Id, route);

            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
            {
                int tier = tierIdx + 1;
                NodeButton node = nodes[route * 2 + tierIdx];
                if (node == null) continue;   // 배선이 빈 칸 — Awake가 어느 칸인지 이미 찍었다

                bool owned = !routeAbandoned && stage >= tier;
                bool available = canEvolve && routeUnlocked && !routeAbandoned && stage == tier - 1;
                bool locked = !owned && !available;

                SetIcon(node.icon, RouteIcon(route) ?? GetIcon(pathIconSprites, route));
                if (node.frame != null)
                    node.frame.color = owned ? (tier == EvolutionRoutes.MaxStage ? GoldFrameColor : OwnedFrameColor)
                                             : (locked ? LockedFrameColor : BaseFrameColor);
                if (node.icon != null)
                    node.icon.color = locked ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;

                if (node.title != null)
                {
                    node.title.text = RouteTitle(route, tier);
                    node.title.color = locked ? LockedTextColor : Color.white;
                }

                string effect = RouteEffect(route, tier);
                if (node.description != null)
                {
                    // 자물쇠 이모지는 Galmuri11 폰트에 글리프가 없어 □로 깨진다 — 텍스트 표기로 대체
                    node.description.text = routeAbandoned ? Loc.T("ui.evotree.abandoned") + " " + effect
                                          : !routeUnlocked ? Loc.F("ui.evotree.lockedPrereq", prereqName) + " " + effect
                                          : locked ? Loc.T("ui.evotree.locked") + " " + effect
                                          : effect;
                    node.description.color = locked ? LockedTextColor : Color.white;
                }

                if (node.button == null) continue;
                node.button.interactable = available;
                AnimateNode((RectTransform)node.button.transform, route * 2 + tierIdx, available, alreadyOpen);
            }

            if (arrows != null && route < arrows.Length && arrows[route] != null)
                arrows[route].color = (!routeAbandoned && stage >= 1) ? ActiveArrowColor : InactiveArrowColor;
        }

        // 닫힘 연출 중에 다시 열리는 경우 SetActive(true)만으로는 연출이 되돌아오지 않는다 (LevelUpUI와 동일)
        if (panelTransition != null) panelTransition.Show();
        else panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
        isOpen = true;
    }

    // 노드 제목 = 진화 후 바뀔 이름. 옛 티어 제목("둔화 부여")보다 "대회오리"가 무엇이 되는지 훨씬 잘 보여준다.
    private string RouteTitle(int route, int tier) => isPassiveMode
        ? EvolutionRoutes.EvolvedName(currentPassive.Id, route, tier)
        : EvolutionRoutes.EvolvedName(currentSkill.Id, route, tier);

    // 새 티어의 설명: 한 번의 진화가 옛 티어 여러 개를 한꺼번에 주므로 설명도 이어 붙인다.
    private string RouteEffect(int route, int tier)
    {
        int path = isPassiveMode
            ? EvolutionRoutes.RoutePath(currentPassive.Id, route)
            : EvolutionRoutes.RoutePath(currentSkill.Id, route);

        List<string> parts = new List<string>();
        foreach (int legacyTier in EvolutionRoutes.LegacyTiersFor(tier))
        {
            string text = isPassiveMode
                ? PlayerPassives.DescribePathEffect(currentPassive.Id, path, legacyTier)
                : skills.GetPathEffectText(currentSkill.Id, path, legacyTier);
            if (!string.IsNullOrEmpty(text)) parts.Add(text);
        }
        return string.Join("\n", parts);
    }

    private void OnRouteClicked(int route)
    {
        if (isPassiveMode) passives.EvolvePassive(currentPassive.Id, route);
        else skills.EvolveSkill(currentSkill.Id, route);
        Close();
    }

    // 카드 등장 pop-in(스태거) + 진화 가능 노드 강조 펄스. Time.timeScale=0(모달 일시정지) 중에도 돌도록 SetUpdate(true).
    private void AnimateNode(RectTransform rt, int order, bool available, bool alreadyOpen)
    {
        if (rt == null) return;
        rt.localScale = Vector3.one;

        float pulseDelay = 0f;
        if (!alreadyOpen)
        {
            float delay = order * 0.03f;
            rt.localScale = Vector3.one * 0.5f;
            nodeTweens.Add(rt.DOScale(1f, 0.2f).SetDelay(delay).SetEase(Ease.OutBack).SetUpdate(true));
            pulseDelay = delay + 0.2f;
        }

        if (available)
            nodeTweens.Add(rt.DOScale(1.07f, 0.55f).SetDelay(pulseDelay).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true));
    }

    private void KillNodeTweens()
    {
        foreach (Tween t in nodeTweens) t?.Kill();
        nodeTweens.Clear();
    }

    private void Close()
    {
        isOpen = false;
        KillNodeTweens();
        ModalPause.Pop();
        if (panelTransition != null) panelTransition.Hide();
        else panel.SetActive(false);

        System.Action cb = onClosed;
        onClosed = null;
        cb?.Invoke();
    }

    // 헤더 아이콘 = 지금 상태(진화했으면 고른 루트의 그림).
    private Sprite CurrentIcon()
    {
        int stage = isPassiveMode ? currentPassive.EvolutionStage : currentSkill.EvolutionStage;
        int route = isPassiveMode ? currentPassive.Route : currentSkill.Route;
        Sprite evo = stage > 0 && route >= 0 ? RouteIcon(route) : null;
        return evo ?? (isPassiveMode ? GetIcon(passiveIcons, (int)currentPassive.Id) : GetIcon(activeIcons, (int)currentSkill.Id));
    }

    // 노드 아이콘 = 그 루트로 진화하면 되는 그림. 진화 아이콘 배열은 LevelUpUI가 단독 소유한다(HUD와 같은 이유).
    private Sprite RouteIcon(int route)
    {
        LevelUpUI ui = LevelUpUI.Instance;
        if (ui == null) return null;
        return isPassiveMode ? ui.GetPassiveEvoIcon(currentPassive.Id, route) : ui.GetActiveEvoIcon(currentSkill.Id, route);
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.enabled = sprite != null;
        image.sprite = sprite;
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        icons != null && index >= 0 && index < icons.Length ? icons[index] : null;
}
