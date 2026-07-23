using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

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
    private static readonly Color GoldFrameColor = new Color(1f, 0.82f, 0.2f, 1f); // 최종 티어(3) 달성 시
    private static readonly Color LockedFrameColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    private static readonly Color LockedTextColor = new Color(0.65f, 0.65f, 0.65f, 1f);
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
    [SerializeField] private Sprite[] pathIconSprites; // path 0~2 색상 아이콘
    [SerializeField] private NodeButton[] nodes; // 길이 9, index = path*3 + (tier-1)
    [SerializeField] private TMP_Text[] arrows; // 길이 6, index = path*2 + (0: T1->T2, 1: T2->T3)

    private PlayerSkills skills;
    private PlayerPassives passives;
    private EquippedSkill currentSkill;
    private EquippedPassive currentPassive;
    private bool isPassiveMode;
    private System.Action onClosed; // 진화 완료(모달 닫힘) 후 1회 콜백 — 보물상자 연쇄 레벨업이 대기

    // 진화 노드 카드에 붙는 juice 연출(등장 pop-in, 진화 가능 노드 강조 펄스)용 트윈 — 재오픈/닫기 시 정리
    private readonly List<Tween> nodeTweens = new List<Tween>();

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        for (int path = 0; path < 3; path++)
        {
            for (int tierIdx = 0; tierIdx < 3; tierIdx++)
            {
                int capturedPath = path;
                nodes[path * 3 + tierIdx].button.onClick.AddListener(() => OnNodeClicked(capturedPath));
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
        bool alreadyOpen = panel.activeSelf;
        KillNodeTweens();

        string name = isPassiveMode ? PlayerSkills.GetPassiveSkillName(currentPassive.Id) : PlayerSkills.GetActiveSkillName(currentSkill.Id);
        int level = isPassiveMode ? currentPassive.Level : currentSkill.Level;
        int totalTier = isPassiveMode ? currentPassive.TotalEvolutionTier : currentSkill.TotalEvolutionTier;
        int[] pathTier = isPassiveMode ? currentPassive.PathTier : currentSkill.PathTier;

        skillNameText.text = name + " 진화 — Lv." + level;

        if (subInfoText != null)
        {
            int nextLevel = (totalTier + 1) * 5;
            subInfoText.text = totalTier >= 4
                ? "모든 진화 완료"
                : $"Lv.{level} 달성으로 진화 가능 · 다음 진화: Lv.{nextLevel}";
        }

        SetIcon(skillIcon, isPassiveMode ? GetIcon(passiveIcons, (int)currentPassive.Id) : GetIcon(activeIcons, (int)currentSkill.Id));
        if (!alreadyOpen && skillIcon != null)
        {
            skillIcon.rectTransform.localScale = Vector3.one;
            nodeTweens.Add(skillIcon.rectTransform.DOPunchScale(Vector3.one * 0.35f, 0.4f, 6, 0.5f).SetUpdate(true));
        }

        for (int path = 0; path < 3; path++)
        {
            for (int tierIdx = 0; tierIdx < 3; tierIdx++)
            {
                int tier = tierIdx + 1;
                NodeButton node = nodes[path * 3 + tierIdx];
                bool owned = pathTier[path] >= tier;
                bool available = pathTier[path] == tier - 1 && CanEvolvePath(path);
                bool locked = !owned && !available;

                SetIcon(node.icon, GetIcon(pathIconSprites, path));
                node.frame.color = owned ? (tier == 3 ? GoldFrameColor : OwnedFrameColor) : (locked ? LockedFrameColor : Color.white);
                node.icon.color = locked ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;

                if (node.title != null)
                {
                    node.title.text = GetPathTitleText(path, tier);
                    node.title.color = locked ? LockedTextColor : Color.black;
                }

                string effect = GetPathEffectText(path, tier);
                if (locked)
                {
                    // 연계 대상 진화(path1/path2)는 대상 스킬/패시브가 Lv.5 이상이어야 함
                    string reason = (tier == 2 && path != 0) ? GetPathName(path) + " Lv.5 필요 · " : "";
                    node.description.text = "🔒 " + reason + effect;
                }
                else
                {
                    node.description.text = effect;
                }
                node.description.color = locked ? LockedTextColor : Color.black;
                node.button.interactable = available;

                AnimateNode((RectTransform)node.button.transform, path * 3 + tierIdx, available, alreadyOpen);
            }

            if (arrows != null && arrows.Length >= (path + 1) * 2)
            {
                arrows[path * 2].color = pathTier[path] >= 1 ? ActiveArrowColor : InactiveArrowColor;
                arrows[path * 2 + 1].color = pathTier[path] >= 2 ? ActiveArrowColor : InactiveArrowColor;
            }
        }

        panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
    }

    private bool CanEvolvePath(int path) =>
        isPassiveMode ? passives.CanEvolvePath(currentPassive, path) : skills.CanEvolvePath(currentSkill, path);

    private string GetPathTitleText(int path, int tier) =>
        isPassiveMode ? PlayerPassives.GetPathTierTitle(currentPassive.Id, path, tier) : skills.GetPathTitleText(currentSkill.Id, path, tier);

    private string GetPathEffectText(int path, int tier) =>
        isPassiveMode ? PlayerPassives.DescribePathEffect(currentPassive.Id, path, tier) : skills.GetPathEffectText(currentSkill.Id, path, tier);

    private void OnNodeClicked(int path)
    {
        if (isPassiveMode) passives.EvolvePassive(currentPassive.Id, path);
        else skills.EvolveSkill(currentSkill.Id, path);
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
        KillNodeTweens();
        ModalPause.Pop();
        if (panelTransition != null) panelTransition.Hide();
        else panel.SetActive(false);

        System.Action cb = onClosed;
        onClosed = null;
        cb?.Invoke();
    }

    private string GetPathName(int path)
    {
        if (path == 0) return "기본";

        if (isPassiveMode)
        {
            if (path == 1)
            {
                PassiveSkillId? req = PlayerPassives.GetPassivePrereq(currentPassive.Id);
                return req.HasValue ? PlayerSkills.GetPassiveSkillName(req.Value) : "";
            }
            ActiveSkillId? activeReq = PlayerPassives.GetActivePrereq(currentPassive.Id);
            return activeReq.HasValue ? PlayerSkills.GetActiveSkillName(activeReq.Value) : "";
        }

        if (path == 1)
        {
            PassiveSkillId? passiveReq = PlayerSkills.GetPassivePrereq(currentSkill.Id);
            return passiveReq.HasValue ? PlayerSkills.GetPassiveSkillName(passiveReq.Value) : "";
        }
        ActiveSkillId? req2 = PlayerSkills.GetActivePrereq(currentSkill.Id);
        return req2.HasValue ? PlayerSkills.GetActiveSkillName(req2.Value) : "";
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
