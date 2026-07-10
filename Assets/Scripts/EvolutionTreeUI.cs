using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    [SerializeField] private Sprite[] pathIconSprites; // path 0~2 색상 아이콘
    [SerializeField] private NodeButton[] nodes; // 길이 9, index = path*3 + (tier-1)
    [SerializeField] private TMP_Text[] arrows; // 길이 6, index = path*2 + (0: T1->T2, 1: T2->T3)

    private PlayerSkills skills;
    private EquippedSkill currentSkill;

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        for (int path = 0; path < 3; path++)
        {
            for (int tierIdx = 0; tierIdx < 3; tierIdx++)
            {
                int capturedPath = path;
                int capturedTierIdx = tierIdx;
                nodes[path * 3 + tierIdx].button.onClick.AddListener(() => OnNodeClicked(capturedPath, capturedTierIdx));
            }
        }
    }

    public void Show(PlayerSkills skillsRef, EquippedSkill skill)
    {
        bool alreadyOpen = panel.activeSelf;

        skills = skillsRef;
        currentSkill = skill;

        skillNameText.text = PlayerSkills.GetActiveSkillName(skill.Id) + " 진화 — Lv." + skill.Level;

        if (subInfoText != null)
        {
            int nextLevel = (skill.TotalEvolutionTier + 1) * 5;
            subInfoText.text = skill.TotalEvolutionTier >= 4
                ? "모든 진화 완료"
                : $"Lv.{skill.Level} 달성으로 진화 가능 · 다음 진화: Lv.{nextLevel}";
        }

        SetIcon(skillIcon, GetIcon(activeIcons, (int)skill.Id));

        for (int path = 0; path < 3; path++)
        {
            for (int tierIdx = 0; tierIdx < 3; tierIdx++)
            {
                int tier = tierIdx + 1;
                NodeButton node = nodes[path * 3 + tierIdx];
                bool owned = currentSkill.PathTier[path] >= tier;
                bool available = currentSkill.PathTier[path] == tier - 1 && skills.CanEvolvePath(currentSkill, path);
                bool locked = !owned && !available;

                SetIcon(node.icon, GetIcon(pathIconSprites, path));
                node.frame.color = owned ? (tier == 3 ? GoldFrameColor : OwnedFrameColor) : (locked ? LockedFrameColor : Color.white);
                node.icon.color = locked ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;

                if (node.title != null)
                {
                    node.title.text = skills.GetPathTitleText(skill.Id, path, tier);
                    node.title.color = locked ? LockedTextColor : Color.black;
                }

                string effect = skills.GetPathEffectText(skill.Id, path, tier);
                if (locked)
                {
                    string reason = (tier == 2 && path != 0) ? GetPathName(skill.Id, path) + " 필요 · " : "";
                    node.description.text = "🔒 " + reason + effect;
                }
                else
                {
                    node.description.text = effect;
                }
                node.description.color = locked ? LockedTextColor : Color.black;
                node.button.interactable = available;
            }

            if (arrows != null && arrows.Length >= (path + 1) * 2)
            {
                arrows[path * 2].color = currentSkill.PathTier[path] >= 1 ? ActiveArrowColor : InactiveArrowColor;
                arrows[path * 2 + 1].color = currentSkill.PathTier[path] >= 2 ? ActiveArrowColor : InactiveArrowColor;
            }
        }

        panel.SetActive(true);
        if (!alreadyOpen) ModalPause.Push();
    }

    private void OnNodeClicked(int path, int tierIdx)
    {
        skills.EvolveSkill(currentSkill.Id, path);
        Close();
    }

    private void Close()
    {
        ModalPause.Pop();
        if (panelTransition != null) panelTransition.Hide();
        else panel.SetActive(false);
    }

    private string GetPathName(ActiveSkillId skillId, int path)
    {
        if (path == 0) return "기본";
        if (path == 1)
        {
            PassiveSkillId? req = PlayerSkills.GetPassivePrereq(skillId);
            return req.HasValue ? PlayerSkills.GetPassiveSkillName(req.Value) : "";
        }
        ActiveSkillId? activeReq = PlayerSkills.GetActivePrereq(skillId);
        return activeReq.HasValue ? PlayerSkills.GetActiveSkillName(activeReq.Value) : "";
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
