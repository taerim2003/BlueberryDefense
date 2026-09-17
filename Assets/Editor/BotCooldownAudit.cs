using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 쿨타임 감사표 — 스킬 11종 × (진화 전 / 루트 2 × 1·2차) × Lv1·Lv10의 "맨몸 쿨"(스킬트리·패시브 감소 전).
// `balance` 스킬의 C1(15초 상한)·C2(진화하면 쿨이 길어진다)를 판정하는 원자료. 에디트모드에서 돈다(플레이 불필요).
//
// 🔴 게임 코드를 **리플렉션으로 그대로 부른다**(레벨업 스텝·진화 효과·Evo 덮어쓰기). 다만 두 곳은 흉내다:
//    ① EvolveSkill의 본문(선행 조건 검사만 빼고 같은 순서) ② TryUseSkill의 시전 시점 가산(화살 R0 +3초, 화살비 ×2).
//    그래서 코드가 바뀌면 틀릴 수 있다 → 실제 판의 관측값(runs.jsonl의 skills[].baseCdLast)과 분석기가 대조한다.
// 🔴 데이터는 씬(Battle/Player)의 배열이 아니라 **에셋 전체**(Prog_*·Evo_*)에서 읽는다. 씬에 안 꽂힌 에셋이 있으면 다르다.
public static class BotCooldownAudit
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const float CooldownCap = 15f;

    public static void Write(string path)
    {
        try { File.WriteAllText(path, Build()); }
        catch (Exception e)
        {
            File.WriteAllText(path, "{\"error\":" + Quote(e.GetType().Name + ": " + e.Message) + "}");
            Debug.LogError("[BotCooldownAudit] " + e);
        }
    }

    [MenuItem("Window/Blueberry Defense/Bot Playtest/쿨 감사표 콘솔 출력")]
    private static void PrintToConsole() => Debug.Log(Build());

    private static string Build()
    {
        Type ps = typeof(PlayerSkills);
        InstallLookups(ps);
        MethodInfo applyUpgrade = Req(ps, "ApplyUpgradeEffect");
        MethodInfo applyPath = Req(ps, "ApplyPathTierEffect");
        MethodInfo applyEvo = Req(ps, "ApplyEvolutionProgression");
        MethodInfo getCd = Req(ps, "GetDefaultCooldown");
        MethodInfo arrowRain = Req(ps, "ArrowRainReplacesShot");
        float assassinExtra = ConstF(ps, "AssassinArrowExtraCooldown");
        float arrowRainMult = ConstF(ps, "ArrowRainCooldownMult");
        float gcd = BalanceConstants.GlobalCooldown;

        float Effective(EquippedSkill s)
        {
            float cd = s.Cooldown;
            if (s.Id == ActiveSkillId.BasicAttack && s.PathTier[1] >= 2) cd += assassinExtra;
            if ((bool)arrowRain.Invoke(null, new object[] { s })) cd *= arrowRainMult;
            return cd;
        }

        void LevelTo10(EquippedSkill s)
        {
            for (int lv = 2; lv <= BalanceConstants.MaxSkillLevel; lv++)
            {
                s.Level = lv;
                applyUpgrade.Invoke(null, new object[] { s, lv });
            }
        }

        var rows = new List<string>();
        var violations = new List<string>();

        foreach (ActiveSkillId id in Enum.GetValues(typeof(ActiveSkillId)))
        {
            var baseSkill = new EquippedSkill { Id = id, Cooldown = (float)getCd.Invoke(null, new object[] { id }) };
            float preLv1 = Effective(baseSkill);
            LevelTo10(baseSkill);
            float preLv10 = Effective(baseSkill);
            rows.Add(Row(id, -1, 0, preLv1, preLv10));
            CheckCap(violations, id, -1, 0, preLv1, preLv10);

            for (int route = 0; route < 2; route++)
            {
                EquippedSkill s = Clone(baseSkill);
                float prevLv10 = preLv10;
                for (int tier = 1; tier <= EvolutionRoutes.MaxStageFor(id); tier++)
                {
                    // PlayerSkills.EvolveSkill 본문과 같은 순서(선행 조건 검사·도감·효과음만 뺐다).
                    int path = EvolutionRoutes.RoutePath(id, route);
                    foreach (int legacy in EvolutionRoutes.LegacyTiersFor(tier))
                    {
                        s.PathTier[path] = legacy;
                        applyPath.Invoke(null, new object[] { s, path, legacy });
                    }
                    s.PathTier[path] = EvolutionRoutes.TargetPathTier(tier);
                    s.Route = route;
                    s.EvolutionStage = tier;
                    s.Damage *= EvolutionRoutes.EvolveDamageMult;
                    s.Cooldown = Mathf.Max(gcd, s.Cooldown * EvolutionRoutes.EvolveCooldownMult);
                    s.Level = 1;
                    applyEvo.Invoke(null, new object[] { s });

                    float lv1 = Effective(s);
                    LevelTo10(s);
                    float lv10 = Effective(s);
                    rows.Add(Row(id, route, tier, lv1, lv10));
                    CheckCap(violations, id, route, tier, lv1, lv10);
                    // C2 = "진화하면 쿨이 길어진다"(사용자 원칙). 같기만 해도 원칙 미충족 — 등급을 나눠 적는다.
                    if (lv1 <= prevLv10 + 0.001f)
                        violations.Add("{\"rule\":\"C2\",\"severity\":\"" + (lv1 < prevLv10 - 0.001f ? "shorter" : "notLonger")
                            + "\",\"skill\":\"" + id + "\",\"route\":" + route + ",\"tier\":" + tier
                            + ",\"lv1\":" + F(lv1) + ",\"prevLv10\":" + F(prevLv10) + "}");
                    prevLv10 = lv10;
                }
            }
        }

        return "{\"generatedUtc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"cap\":" + F(CooldownCap)
            + ",\"note\":\"맨몸 쿨(스킬트리·패시브·되감기 감소 전). tier 0 = 진화 전, route -1 = 해당 없음\""
            + ",\"rows\":[" + string.Join(",", rows) + "],\"violations\":[" + string.Join(",", violations) + "]}";
    }

    private static void CheckCap(List<string> v, ActiveSkillId id, int route, int tier, float lv1, float lv10)
    {
        float max = Mathf.Max(lv1, lv10);
        if (max > CooldownCap + 0.001f)
            v.Add("{\"rule\":\"C1\",\"skill\":\"" + id + "\",\"route\":" + route + ",\"tier\":" + tier + ",\"cooldown\":" + F(max) + "}");
    }

    private static string Row(ActiveSkillId id, int route, int tier, float lv1, float lv10) =>
        "{\"skill\":\"" + id + "\",\"route\":" + route + ",\"tier\":" + tier + ",\"lv1\":" + F(lv1) + ",\"lv10\":" + F(lv10) + "}";

    // 씬 없이 PlayerSkills의 static 조회표를 에셋으로 채운다(BuildProgressionLookup과 같은 키 규칙).
    private static void InstallLookups(Type ps)
    {
        var prog = new Dictionary<ActiveSkillId, SkillProgression>();
        foreach (string g in AssetDatabase.FindAssets("t:SkillProgression"))
        {
            var p = AssetDatabase.LoadAssetAtPath<SkillProgression>(AssetDatabase.GUIDToAssetPath(g));
            if (p != null) prog[p.skill] = p;
        }
        var evo = new Dictionary<(ActiveSkillId, int, int), EvolutionProgression>();
        foreach (string g in AssetDatabase.FindAssets("t:EvolutionProgression"))
        {
            var e = AssetDatabase.LoadAssetAtPath<EvolutionProgression>(AssetDatabase.GUIDToAssetPath(g));
            if (e != null) evo[(e.skill, e.route, e.stage)] = e;
        }
        ReqField(ps, "progressionLookup").SetValue(null, prog);
        ReqField(ps, "evolutionLookup").SetValue(null, evo);
    }

    private static EquippedSkill Clone(EquippedSkill src)
    {
        var s = new EquippedSkill();
        foreach (FieldInfo f in typeof(EquippedSkill).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.IsInitOnly) continue;
            f.SetValue(s, f.GetValue(src));
        }
        for (int i = 0; i < src.PathTier.Length; i++) s.PathTier[i] = src.PathTier[i];
        return s;
    }

    private static MethodInfo Req(Type t, string name) =>
        t.GetMethod(name, Static) ?? throw new MissingMethodException(t.Name, name);

    private static FieldInfo ReqField(Type t, string name) =>
        t.GetField(name, Static) ?? throw new MissingFieldException(t.Name, name);

    private static float ConstF(Type t, string name) => Convert.ToSingle(ReqField(t, name).GetValue(null));

    private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
