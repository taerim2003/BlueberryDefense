using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

// 진화 선택 창 — 2루트 × 2티어 (§EvolutionRoutes).
// 씬의 노드는 여전히 3×3(Node_P{path}T{tier}) 9칸이지만, 여기서 왼쪽 위 2×2만 쓰고 나머지는 끈다.
//   표시 행 0 = 루트 0 → nodes[0](T1), nodes[1](T2)
//   표시 행 1 = 루트 1 → nodes[3](T1), nodes[4](T2)
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

    private static readonly Color OwnedFrameColor = new Color(0.75f, 1f, 0.75f, 1f);
    private static readonly Color GoldFrameColor = new Color(1f, 0.82f, 0.2f, 1f); // 최종 티어 달성 시
    private static readonly Color LockedFrameColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color LockedTextColor = new Color(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Color ActiveArrowColor = new Color(1f, 0.9f, 0.4f, 1f);
    private static readonly Color InactiveArrowColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    // 2×2로 쓰는 노드 인덱스 — [루트][티어-1]
    private static readonly int[,] NodeIndex = { { 0, 1 }, { 3, 4 } };
    private static readonly int[] ArrowIndex = { 0, 2 };            // 루트별 T1→T2 화살표
    private static readonly int[] HiddenNodes = { 2, 5, 6, 7, 8 };  // 3×3 중 안 쓰는 칸
    private static readonly int[] HiddenArrows = { 1, 3, 4, 5 };

    public static EvolutionTreeUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private UITransition panelTransition;
    [SerializeField] private TMP_Text skillNameText;
    [SerializeField] private TMP_Text subInfoText;
    [SerializeField] private Image skillIcon;
    [SerializeField] private Sprite[] activeIcons;
    [SerializeField] private Sprite[] passiveIcons;
    [SerializeField] private Sprite[] pathIconSprites; // 루트 0~1 색상 아이콘 (기존 path 아이콘 재사용)
    [SerializeField] private NodeButton[] nodes; // 길이 9, index = path*3 + (tier-1)
    [SerializeField] private TMP_Text[] arrows;  // 길이 6, index = path*2 + (0: T1->T2, 1: T2->T3)

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

        for (int route = 0; route < 2; route++)
        {
            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
            {
                int capturedRoute = route;
                nodes[NodeIndex[route, tierIdx]].button.onClick.AddListener(() => OnRouteClicked(capturedRoute));
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

        skillNameText.text = name + " 진화 — Lv." + level;

        if (subInfoText != null)
        {
            subInfoText.text = stage >= EvolutionRoutes.MaxStage
                ? "모든 진화 완료"
                : stage == 0
                    ? "루트를 하나 고르세요 — 고른 뒤에는 바꿀 수 없습니다"
                    : "1차에서 고른 루트를 이어갑니다";
        }

        SetIcon(skillIcon, isPassiveMode ? GetIcon(passiveIcons, (int)currentPassive.Id) : GetIcon(activeIcons, (int)currentSkill.Id));
        if (!alreadyOpen && skillIcon != null)
        {
            skillIcon.rectTransform.localScale = Vector3.one;
            nodeTweens.Add(skillIcon.rectTransform.DOPunchScale(Vector3.one * 0.35f, 0.4f, 6, 0.5f).SetUpdate(true));
        }

        foreach (int i in HiddenNodes)
            if (nodes[i].button != null) nodes[i].button.gameObject.SetActive(false);
        if (arrows != null)
            foreach (int i in HiddenArrows)
                if (i < arrows.Length && arrows[i] != null) arrows[i].gameObject.SetActive(false);

        for (int route = 0; route < 2; route++)
        {
            // 1차 진화에서 고르지 않은 루트는 영영 닫힌다.
            bool routeAbandoned = stage > 0 && chosenRoute != route;

            for (int tierIdx = 0; tierIdx < 2; tierIdx++)
            {
                int tier = tierIdx + 1;
                NodeButton node = nodes[NodeIndex[route, tierIdx]];
                node.button.gameObject.SetActive(true);

                bool owned = !routeAbandoned && stage >= tier;
                bool available = canEvolve && !routeAbandoned && stage == tier - 1;
                bool locked = !owned && !available;

                SetIcon(node.icon, GetIcon(pathIconSprites, route));
                node.frame.color = owned ? (tier == EvolutionRoutes.MaxStage ? GoldFrameColor : OwnedFrameColor)
                                         : (locked ? LockedFrameColor : Color.white);
                node.icon.color = locked ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;

                if (node.title != null)
                {
                    node.title.text = RouteTitle(route, tier);
                    node.title.color = locked ? LockedTextColor : Color.black;
                }

                string effect = RouteEffect(route, tier);
                // 자물쇠 이모지는 Galmuri11 폰트에 글리프가 없어 □로 깨진다 — 텍스트 표기로 대체
                node.description.text = routeAbandoned ? "[포기한 루트] " + effect
                                      : locked ? "[잠김] " + effect
                                      : effect;
                node.description.color = locked ? LockedTextColor : Color.black;
                node.button.interactable = available;

                AnimateNode((RectTransform)node.button.transform, route * 2 + tierIdx, available, alreadyOpen);
            }

            if (arrows != null && ArrowIndex[route] < arrows.Length && arrows[ArrowIndex[route]] != null)
            {
                arrows[ArrowIndex[route]].gameObject.SetActive(true);
                arrows[ArrowIndex[route]].color = (!routeAbandoned && stage >= 1) ? ActiveArrowColor : InactiveArrowColor;
            }
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
        if (tier == 1)
            parts.Add($"<color=#FF8A3C>피해량 {Mathf.RoundToInt((EvolutionRoutes.EvolveDamageMult - 1f) * 100f)}% 증가 · 레벨 1부터 다시 성장</color>");

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
            float delay = order * 0.05f;
            rt.localScale = Vector3.one * 0.55f;
            nodeTweens.Add(rt.DOScale(1f, 0.35f).SetDelay(delay).SetEase(Ease.OutBack).SetUpdate(true));
            pulseDelay = delay + 0.35f;
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

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.enabled = sprite != null;
        image.sprite = sprite;
    }

    private static Sprite GetIcon(Sprite[] icons, int index) =>
        icons != null && index >= 0 && index < icons.Length ? icons[index] : null;
}
