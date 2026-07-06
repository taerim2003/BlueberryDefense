using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class LevelUpUI : MonoBehaviour
{
    public static LevelUpUI Instance { get; private set; }

    private class Option
    {
        public string Label;
        public System.Action Apply;
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private Button optionButtonA;
    [SerializeField] private Button optionButtonB;
    [SerializeField] private Button optionButtonC;
    [SerializeField] private Text labelA;
    [SerializeField] private Text labelB;
    [SerializeField] private Text labelC;

    private Option[] currentOptions;

    private void Awake()
    {
        Instance = this;
        panel.SetActive(false);

        optionButtonA.onClick.AddListener(() => Choose(0));
        optionButtonB.onClick.AddListener(() => Choose(1));
        optionButtonC.onClick.AddListener(() => Choose(2));
    }

    public void Show()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        currentOptions = BuildOptions(skills, health, passives);

        labelA.text = currentOptions[0].Label;
        labelB.text = currentOptions[1].Label;
        labelC.text = currentOptions[2].Label;

        panel.SetActive(true);
        Time.timeScale = 0f;
    }

    private Option[] BuildOptions(PlayerSkills skills, PlayerHealth health, PlayerPassives passives)
    {
        List<Option> candidates = new List<Option>();

        if (!skills.HasMaxSkills)
        {
            foreach (ActiveSkillId id in new[] { ActiveSkillId.Whirlwind, ActiveSkillId.Orb, ActiveSkillId.Lightning, ActiveSkillId.EagleDrop })
            {
                if (skills.HasSkill(id)) continue;
                ActiveSkillId captured = id;
                candidates.Add(new Option { Label = GetActiveSkillName(captured) + " 획득", Apply = () => skills.AcquireSkill(captured) });
            }
        }

        if (!passives.HasMaxPassives)
        {
            foreach (PassiveSkillId id in new[] { PassiveSkillId.Strength, PassiveSkillId.Health, PassiveSkillId.Knowledge, PassiveSkillId.Assassinate, PassiveSkillId.Refresh })
            {
                if (passives.HasPassive(id)) continue;
                PassiveSkillId captured = id;
                candidates.Add(new Option { Label = GetPassiveSkillName(captured) + " 획득", Apply = () => passives.AcquirePassive(captured) });
            }
        }

        foreach (EquippedSkill equipped in skills.EquippedSkills)
        {
            if (!skills.CanUpgradeSkill(equipped)) continue;
            EquippedSkill captured = equipped;
            candidates.Add(new Option
            {
                Label = GetActiveSkillName(captured.Id) + " 강화 (Lv." + (captured.Level + 1) + ")",
                Apply = () => skills.UpgradeSkillLevel(captured.Id),
            });
        }

        Shuffle(candidates);
        List<Option> options = candidates.Take(3).ToList();

        string[] statLabels =
        {
            "기본 공격 피해량 +5",
            "기본 공격 쿨타임 10% 감소",
            "최대 체력 +20",
        };
        int statIndex = 0;
        while (options.Count < 3)
        {
            switch (statIndex % 3)
            {
                case 0:
                    options.Add(new Option { Label = statLabels[0], Apply = () => skills.UpgradeSkillDamage(ActiveSkillId.BasicAttack, 5f) });
                    break;
                case 1:
                    options.Add(new Option { Label = statLabels[1], Apply = () => skills.UpgradeSkillCooldown(ActiveSkillId.BasicAttack, 0.9f) });
                    break;
                case 2:
                    options.Add(new Option { Label = statLabels[2], Apply = () => health.IncreaseMaxHealth(20) });
                    break;
            }
            statIndex++;
        }

        return options.ToArray();
    }

    public void ShowTreasureReward()
    {
        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();

        EquippedSkill blocked = skills.EquippedSkills.FirstOrDefault(s =>
            s.Level >= 5 && s.EquippedGems.Count < s.Level / 5 && s.EquippedGems.Count < 3);

        if (blocked == null)
        {
            Show();
            return;
        }

        currentOptions = BuildGemOptions(skills, blocked);

        labelA.text = currentOptions[0].Label;
        labelB.text = currentOptions[1].Label;
        labelC.text = currentOptions[2].Label;

        panel.SetActive(true);
        Time.timeScale = 0f;
    }

    private Option[] BuildGemOptions(PlayerSkills skills, EquippedSkill skill)
    {
        List<GemType> pool = new List<GemType> { GemType.Emerald, GemType.Topaz, GemType.Amethyst, GemType.Garnet };
        pool.RemoveAll(g => skill.EquippedGems.Contains(g));
        Shuffle(pool);

        List<Option> options = new List<Option>();
        for (int i = 0; i < Mathf.Min(3, pool.Count); i++)
        {
            GemType gem = pool[i];
            options.Add(new Option { Label = GetGemName(gem) + " 장착 (" + GetActiveSkillName(skill.Id) + ")", Apply = () => skills.EquipGem(skill.Id, gem) });
        }

        while (options.Count < 3)
            options.Add(new Option { Label = "최대 체력 +20", Apply = () => FindAnyObjectByType<PlayerHealth>().IncreaseMaxHealth(20) });

        return options.ToArray();
    }

    private static string GetGemName(GemType gem) => gem switch
    {
        GemType.Emerald => "에메랄드",
        GemType.Topaz => "토파즈",
        GemType.Amethyst => "자수정",
        GemType.Garnet => "가넷",
        _ => gem.ToString(),
    };

    private void Choose(int index)
    {
        currentOptions[index].Apply?.Invoke();
        Close();
    }

    private void Close()
    {
        panel.SetActive(false);
        Time.timeScale = 1f;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static string GetActiveSkillName(ActiveSkillId id) => id switch
    {
        ActiveSkillId.Whirlwind => "회오리",
        ActiveSkillId.Orb => "오브",
        ActiveSkillId.Lightning => "낙뢰",
        ActiveSkillId.EagleDrop => "독수리 투하",
        _ => id.ToString(),
    };

    private static string GetPassiveSkillName(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => "힘",
        PassiveSkillId.Health => "건강",
        PassiveSkillId.Knowledge => "지식",
        PassiveSkillId.Assassinate => "암살",
        PassiveSkillId.Refresh => "리프레쉬",
        _ => id.ToString(),
    };
}
