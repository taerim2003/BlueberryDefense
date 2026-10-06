#if UNITY_EDITOR || BOT_QA
using System;
using System.IO;
using System.Linq;
using UnityEngine;

// 2차 진화 원정의 고정 로드아웃 표(Tools/BotPlaytest/expedition_loadouts.json) 읽기.
// 🔴 봇은 이 표대로만 픽한다 — 즉석 휴리스틱 없음(사용자 결정 2026-09-29). 서브 구성을 바꾸는 것은
//    봇 정책 변경이 아니라 데이터 변경이라 측정 기준이 안 흔들린다. 진화 조건의 원본은 EvolutionRoutes.cs이고
//    이 표는 그 조건을 만족하는 "선택"만 담는다(조건을 복사하지 않는다 — balance-loop 스킬).
[Serializable]
public class ExpeditionTarget { public string skill; public int route; }

// evolvePriority 한 줄. skill 또는 passive 중 하나만 채워진다(JSON에 없는 쪽은 빈 문자열).
[Serializable]
public class ExpeditionEvolveStep { public string skill; public string passive; public int route; public int tier; }

[Serializable]
public class ExpeditionLoadout
{
    public string id;
    public ExpeditionTarget target;
    public string character;     // 캐릭터 에셋 이름(Char_*)
    public string[] actives;     // 4칸, 획득 우선순위 순. [0] = 시작 스킬(자동 보유)
    public string[] passives;    // 4칸, 획득 우선순위 순
    public ExpeditionEvolveStep[] evolvePriority;

    public ActiveSkillId TargetSkill => (ActiveSkillId)Enum.Parse(typeof(ActiveSkillId), target.skill);

    // 새 카드 우선순위 — 액티브가 패시브보다 먼저(진화 재료의 축), 각 목록 안에서는 적힌 순서. -1 = 목록 밖.
    public int PickRank(ActiveSkillId? a, PassiveSkillId? p)
    {
        if (a.HasValue) { int i = Array.IndexOf(actives, a.Value.ToString()); return i < 0 ? -1 : i; }
        if (p.HasValue) { int i = Array.IndexOf(passives, p.Value.ToString()); return i < 0 ? -1 : 100 + i; }
        return -1;
    }

    public bool ListsActive(ActiveSkillId id) => Array.IndexOf(actives, id.ToString()) >= 0;

    // evolvePriority에서 (종류, id, 티어)에 맞는 스텝의 순번. 없으면 int.MaxValue.
    public int EvolveRank(bool isPassive, string id, int tier)
    {
        for (int i = 0; i < evolvePriority.Length; i++)
        {
            string sid = isPassive ? evolvePriority[i].passive : evolvePriority[i].skill;
            if (!string.IsNullOrEmpty(sid) && sid == id && evolvePriority[i].tier == tier) return i;
        }
        return int.MaxValue;
    }

    // 이 스킬을 지금 진화시킬 때 골라야 하는 루트. 로드아웃에 스텝이 없으면 -1.
    public int RouteFor(bool isPassive, string id, int tier)
    {
        foreach (ExpeditionEvolveStep s in evolvePriority)
        {
            string sid = isPassive ? s.passive : s.skill;
            if (!string.IsNullOrEmpty(sid) && sid == id && s.tier == tier) return s.route;
        }
        return -1;
    }

    // 로드아웃의 미보유 칸이 남아 있나 — 리롤 판단의 입력.
    public bool Incomplete(PlayerSkills ps, PlayerPassives pp) =>
        (ps != null && actives.Any(n => !ps.HasSkill((ActiveSkillId)Enum.Parse(typeof(ActiveSkillId), n))))
        || (pp != null && passives.Any(n => !pp.HasPassive((PassiveSkillId)Enum.Parse(typeof(PassiveSkillId), n))));
}

[Serializable]
public class ExpeditionLoadoutFile
{
    public ExpeditionLoadout[] items;

    public static ExpeditionLoadout[] Load(string path)
    {
        try
        {
            ExpeditionLoadoutFile f = JsonUtility.FromJson<ExpeditionLoadoutFile>(File.ReadAllText(path));
            return f != null ? f.items : null;
        }
        catch (Exception e) { Debug.LogError("[Bot] 로드아웃 읽기 실패: " + path + " — " + e.Message); return null; }
    }
}
#endif
