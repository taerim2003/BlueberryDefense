using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Play 모드에서 레벨업/진화/스킬 획득 등을 즉시 적용해볼 수 있는 치트 창.
// Window > Blueberry Defense > Cheat Window
public class CheatWindow : EditorWindow
{
    private Vector2 scroll;

    [MenuItem("Window/Blueberry Defense/Cheat Window")]
    private static void Open() => GetWindow<CheatWindow>("치트");

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play 모드에서만 사용할 수 있습니다.", MessageType.Info);
            return;
        }

        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        if (skills == null || health == null || passives == null)
        {
            EditorGUILayout.HelpBox("Player를 찾을 수 없습니다.", MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawExperienceSection();
        DrawHealthSection(health);
        DrawSkillAcquireSection(skills);
        DrawEquippedSkillsSection(skills);
        DrawPassiveSection(passives);
        DrawStageSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawExperienceSection()
    {
        PlayerExperience xp = PlayerExperience.Instance;
        if (xp == null) return;

        EditorGUILayout.LabelField("경험치 / 레벨 (Lv." + xp.Level + ")", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("레벨업 1회")) xp.AddXP(xp.XPToNextLevel - xp.CurrentXP);
        if (GUILayout.Button("레벨업 5회"))
            for (int i = 0; i < 5; i++) xp.AddXP(xp.XPToNextLevel - xp.CurrentXP);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
    }

    private void DrawHealthSection(PlayerHealth health)
    {
        EditorGUILayout.LabelField("체력 (" + health.CurrentHealth + "/" + health.MaxHealth + ")", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("풀피 회복")) health.FullHeal();
        if (GUILayout.Button("최대체력 +50")) health.IncreaseMaxHealth(50);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
    }

    private void DrawSkillAcquireSection(PlayerSkills skills)
    {
        EditorGUILayout.LabelField("스킬 획득", EditorStyles.boldLabel);
        foreach (ActiveSkillId id in Enum.GetValues(typeof(ActiveSkillId)))
        {
            if (skills.HasSkill(id)) continue;
            if (GUILayout.Button("획득: " + PlayerSkills.GetActiveSkillName(id)))
                skills.AcquireSkill(id);
        }
        EditorGUILayout.Space();
    }

    private void DrawEquippedSkillsSection(PlayerSkills skills)
    {
        EditorGUILayout.LabelField("장착 스킬 / 진화", EditorStyles.boldLabel);
        foreach (EquippedSkill eq in skills.EquippedSkills.ToList())
        {
            EditorGUILayout.LabelField(PlayerSkills.GetActiveSkillName(eq.Id) + " Lv." + eq.Level
                + "  [" + eq.PathTier[0] + "/" + eq.PathTier[1] + "/" + eq.PathTier[2] + "]");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("레벨 +1")) skills.UpgradeSkillLevel(eq.Id);
            for (int path = 0; path < 3; path++)
            {
                int capturedPath = path;
                bool canEvolve = skills.CanEvolvePath(eq, capturedPath);
                using (new EditorGUI.DisabledScope(!canEvolve))
                {
                    if (GUILayout.Button("진화 path" + (capturedPath + 1)))
                        skills.EvolveSkill(eq.Id, capturedPath);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
    }

    private void DrawPassiveSection(PlayerPassives passives)
    {
        EditorGUILayout.LabelField("패시브 획득", EditorStyles.boldLabel);
        foreach (PassiveSkillId id in Enum.GetValues(typeof(PassiveSkillId)))
        {
            if (passives.HasPassive(id)) continue;
            if (GUILayout.Button("획득: " + PlayerSkills.GetPassiveSkillName(id)))
                passives.AcquirePassive(id);
        }
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("장착 패시브 / 진화", EditorStyles.boldLabel);
        foreach (EquippedPassive eq in passives.EquippedPassives.ToList())
        {
            EditorGUILayout.LabelField(PlayerSkills.GetPassiveSkillName(eq.Id) + " Lv." + eq.Level
                + "  [" + eq.PathTier[0] + "/" + eq.PathTier[1] + "/" + eq.PathTier[2] + "]");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("레벨 +1")) passives.UpgradePassiveLevel(eq.Id);
            for (int path = 0; path < 3; path++)
            {
                int capturedPath = path;
                bool canEvolve = passives.CanEvolvePath(eq, capturedPath);
                using (new EditorGUI.DisabledScope(!canEvolve))
                {
                    if (GUILayout.Button("진화 path" + (capturedPath + 1)))
                        passives.EvolvePassive(eq.Id, capturedPath);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
    }

    private void DrawStageSection()
    {
        if (GameManager.Instance == null) return;

        EditorGUILayout.LabelField("스테이지 (현재 " + GameManager.Instance.CurrentStage + ")", EditorStyles.boldLabel);
        if (GUILayout.Button("다음 스테이지로 스킵"))
            GameManager.Instance.SkipToNextStage();
    }
}
