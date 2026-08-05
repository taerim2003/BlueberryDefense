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
        scroll = EditorGUILayout.BeginScrollView(scroll);

        // 메타 자원(스킬트리) — Player/Play 모드 없이도 항상 사용 가능(PlayerPrefs 저장). 타이틀 씬에서도 됨.
        DrawMetaResourceSection();
        DrawAscensionSection();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("아래 인게임 치트는 Play 모드에서만 사용할 수 있습니다.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        PlayerSkills skills = FindAnyObjectByType<PlayerSkills>();
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        PlayerPassives passives = FindAnyObjectByType<PlayerPassives>();

        if (skills == null || health == null || passives == null)
        {
            EditorGUILayout.HelpBox("Player를 찾을 수 없습니다 (인게임 씬에서 사용하세요).", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawExperienceSection();
        DrawHealthSection(health);
        DrawSkillAcquireSection(skills);
        DrawEquippedSkillsSection(skills);
        DrawPassiveSection(passives);
        DrawStageSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawMetaResourceSection()
    {
        EditorGUILayout.LabelField("메타 자원 (스킬트리)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"정수 {SkillTreeSave.EssenceEarned}");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("정수 +100")) SkillTreeSave.AddEssence(100);
        if (GUILayout.Button("정수 +1000")) SkillTreeSave.AddEssence(1000);
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("스킬트리 자원·해금 전체 초기화")) SkillTreeSave.ResetAll();

        EditorGUILayout.Space();
    }

    private void DrawAscensionSection()
    {
        EditorGUILayout.LabelField("승천 (난이도 등급)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"이번 판 승천 {RunConfig.AscensionLevel}  ·  해금 최고 {AscensionSave.Unlocked}");

        // 이번 판 등급 설정(Play 중이면 다음 스폰부터 즉시 반영). 버튼 수 = 기본표 최고 레벨.
        EditorGUILayout.BeginHorizontal();
        for (int lv = 1; lv <= AscensionTable.Default.MaxLevel; lv++)
            if (GUILayout.Button($"승천 {lv}")) RunConfig.AscensionLevel = lv;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("해금 최대로")) AscensionSave.UnlockUpTo(AscensionTable.Default.MaxLevel);
        if (GUILayout.Button("승천 해금 초기화")) AscensionSave.Reset();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        DrawMapUnlockSection();
    }

    // 맵 해금은 MapClearSave(맵별 클리어 기록)로 판정하는데, 이 기록은 세션26에 생겨서
    // **그 이전 클리어가 소급되지 않는다.** 그래서 개발 세이브에서도 첫 맵 말고는 전부 잠겨 보인다 —
    // 테스트할 때마다 실제로 깨고 올 수는 없으니 여기서 직접 기록을 넣고 뺀다.
    private void DrawMapUnlockSection()
    {
        EditorGUILayout.LabelField("맵 해금 (MapClearSave)", EditorStyles.boldLabel);

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition");
        foreach (string g in guids)
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(g));
            if (map == null) continue;

            int cleared = MapClearSave.ClearedAscension(map.name);
            string label = string.IsNullOrEmpty(map.displayName) ? map.name : map.displayName;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{label} — 클리어 승천 {cleared} {(map.IsUnlocked ? "(해금됨)" : "(잠김)")}",
                GUILayout.Width(260));
            if (GUILayout.Button("승천+1 클리어 기록", GUILayout.Width(140)))
                MapClearSave.RecordClear(map.name, cleared + 1);
            if (GUILayout.Button("기록 삭제", GUILayout.Width(80)))
                MapClearSave.Reset(map.name);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
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
            EditorGUILayout.LabelField(eq.DisplayName + " Lv." + eq.Level
                + "  (누적 " + eq.TotalLevel + " · " + eq.EvolutionStage + "차 진화)");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("레벨 +1")) skills.UpgradeSkillLevel(eq.Id);
            if (GUILayout.Button("만렙")) while (skills.CanUpgradeSkill(eq)) skills.UpgradeSkillLevel(eq.Id); // 진화 게이트(만렙)까지 한 번에
            using (new EditorGUI.DisabledScope(!skills.CanEvolve(eq)))
            {
                foreach (int route in PlayerSkills.SelectableRoutes(eq))
                {
                    int captured = route;
                    if (GUILayout.Button("진화 → " + EvolutionRoutes.EvolvedName(eq.Id, captured, eq.EvolutionStage + 1)))
                        skills.EvolveSkill(eq.Id, captured);
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
            EditorGUILayout.LabelField(eq.DisplayName + " Lv." + eq.Level
                + "  (누적 " + eq.TotalLevel + " · " + eq.EvolutionStage + "차 진화)");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("레벨 +1")) passives.UpgradePassiveLevel(eq.Id);
            if (GUILayout.Button("만렙")) while (passives.CanUpgradePassive(eq)) passives.UpgradePassiveLevel(eq.Id);
            using (new EditorGUI.DisabledScope(!passives.CanEvolve(eq)))
            {
                foreach (int route in PlayerPassives.SelectableRoutes(eq))
                {
                    int captured = route;
                    if (GUILayout.Button("진화 → " + EvolutionRoutes.EvolvedName(eq.Id, captured, eq.EvolutionStage + 1)))
                        passives.EvolvePassive(eq.Id, captured);
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

        if (GUILayout.Button("방패 블루베리 스폰 (테스트)"))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy_ShieldBlueberry.prefab");
            EnemySpawner spawner = FindAnyObjectByType<EnemySpawner>();
            Vector3 pos = spawner != null ? spawner.transform.position : new Vector3(9f, 0f, 0f);
            if (prefab != null) Instantiate(prefab, pos, Quaternion.identity);
        }
    }
}
