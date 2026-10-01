#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 봇의 시전 판단(사용자 승인 2026-09-30) — "쿨이 끝나면 무조건 발사"를 스킬별 발사 허가로 바꾼다.
// 전 스킬·전 맵 공통이다. 세 겹으로 판단한다:
//   ① 상한 대기 — 무엇이든 쿨 준비 후 maxHoldSeconds가 지나면 그냥 쏜다(딜이 잠드는 왜곡 방지).
//   ② 강화 라우팅 — 충전 되감기·과충전(PlayerSkills.EmpowerPending)이 걸려 있으면, 준비된 스킬 중
//      empowerPriority가 가장 높은 것에게 양보한다(사용자 지시 2026-09-30: "센 스킬 써야 할 거 아니야").
//   ③ 상태 규칙 — (스킬, 루트, 차수)로 가장 구체적인 규칙 하나를 고른다. 진화하면 시전 감이 달라진다
//      (사용자: 휘두르기 R1 충격파는 맵 끝까지 가니 즉시). type "always"는 게이트 없음이다.
// 🔴 수치는 Tools/BotPlaytest/cast_policy.json이 정한다(없으면 Defaults) — 파일 수정은 봇 정책 단절이 아니다.
// 🔴 gym에는 설치하지 않는다 — 셀의 적 구성은 시나리오가 강제하므로 게이트는 측정만 왜곡한다. PlayBattle에서만 켠다.
[Serializable]
public class CastRule
{
    public string skill;    // ActiveSkillId 이름
    public int route = -1;  // -1 = 루트 무관(기본 규칙). 0/1 = 그 루트로 진화한 상태에만
    public int minTier;     // 0 = 차수 무관. 1/2 = 그 차수 이상에만 (route와 함께 쓴다)
    public string type;     // "always"(게이트 없음) | "enemiesWithin"(가로거리 range 안 적 count 이상) | "cooldownDebt"(남의 남은 쿨 합 seconds 이상)
    public float range;
    public int count;
    public float seconds;
}

[Serializable]
public class CastPolicyFile
{
    // 조건이 안 차도 이 시간(쿨 준비 시점부터)이 지나면 발사한다 — 한산한 구간에서 딜이 통째로 잠들면 반대 방향으로 왜곡된다.
    public float maxHoldSeconds = 3f;
    // 강화(다음 스킬 피해 보너스)가 대기 중일 때 소비 우선순위 — 앞에 있을수록 센 스킬. 목록 밖은 최하위.
    public string[] empowerPriority;
    public CastRule[] rules;
}

public static class BotCastPolicy
{
    public const string FileName = "Tools/BotPlaytest/cast_policy.json";
    public static string Description { get; private set; } = "off";

    private static CastPolicyFile policy;
    private static Dictionary<ActiveSkillId, List<CastRule>> rulesById;
    private static Dictionary<ActiveSkillId, int> empowerRank;
    private static PlayerSkills ps;

    public static void Install()
    {
        if (policy == null) Load();
        ps = UnityEngine.Object.FindAnyObjectByType<PlayerSkills>();
        BotInput.CastGate = WantsCast;
    }

    public static void Clear() => BotInput.CastGate = null;

    private static void Load()
    {
        string path = Path.Combine(BotConfig.ProjectRoot, FileName);
        try
        {
            if (File.Exists(path))
            {
                policy = JsonUtility.FromJson<CastPolicyFile>(File.ReadAllText(path));
                Description = "file";
            }
        }
        catch (Exception e) { Debug.LogWarning("[Bot] cast_policy 읽기 실패, 기본값 사용: " + e.Message); }
        if (policy == null || policy.rules == null || policy.rules.Length == 0)
        {
            policy = Defaults();
            Description = "default";
        }

        rulesById = new Dictionary<ActiveSkillId, List<CastRule>>();
        foreach (CastRule r in policy.rules)
        {
            if (!Enum.TryParse(r.skill, out ActiveSkillId id)) { Debug.LogWarning("[Bot] cast_policy의 스킬 이름을 모른다: " + r.skill); continue; }
            if (!rulesById.TryGetValue(id, out List<CastRule> list)) rulesById[id] = list = new List<CastRule>();
            list.Add(r);
        }
        empowerRank = new Dictionary<ActiveSkillId, int>();
        if (policy.empowerPriority != null)
            for (int i = 0; i < policy.empowerPriority.Length; i++)
                if (Enum.TryParse(policy.empowerPriority[i], out ActiveSkillId id)) empowerRank[id] = i;
    }

    // 파일이 없을 때의 기본값 — cast_policy.json과 같은 값으로 유지할 것(QA 빌드 폴더에는 파일이 없다).
    // 거리 앵커: 접촉 위협선 = ContactStopDistance 2.3 + HeadbuttLungeDistance 1.4 ≈ 3.7 (BalanceConstants).
    private static CastPolicyFile Defaults() => new CastPolicyFile
    {
        maxHoldSeconds = 3f,
        empowerPriority = new[] { "Sniping", "Swing", "Shotgun", "EagleDrop", "Orb", "GrapeToss", "Homing", "Whirlwind", "BasicAttack", "Lightning" },
        rules = new[]
        {
            new CastRule { skill = "Swing",     type = "enemiesWithin", range = 4.5f, count = 2 },
            // 휘두르기 R1(충격파)은 맵 끝까지 가는 광역이라 붙기를 기다릴 이유가 없다(사용자 2026-09-30).
            new CastRule { skill = "Swing",     route = 1, minTier = 1, type = "always" },
            new CastRule { skill = "Shotgun",   type = "enemiesWithin", range = 6f, count = 2 },
            // 산탄 R0(보너스 탄환 계열)은 공격기가 아니라 타수 버프기다 — 가동률이 생명이라 즉시 쓴다.
            new CastRule { skill = "Shotgun",   route = 0, minTier = 1, type = "always" },
            new CastRule { skill = "EagleDrop", type = "enemiesWithin", range = 10f, count = 3 },
            new CastRule { skill = "Rewind",    type = "cooldownDebt",  seconds = 6f },
        },
    };

    private static bool WantsCast(EquippedSkill s)
    {
        // ① 상한 대기 — 어떤 게이트든 이 시간을 넘겨 잡아 두지 않는다.
        if (s.ReadySince >= 0f && Time.time - s.ReadySince >= policy.maxHoldSeconds) return true;

        // ② 강화 라우팅 — 다음 시전이 강화를 소비하므로, 준비된 스킬 중 가장 센 것에게 양보한다.
        //    되감기 자신은 소비하지 않으므로(TryUseSkill) 라우팅 대상이 아니다.
        if (s.Id != ActiveSkillId.Rewind && PlayerSkills.EmpowerPending && !IsBestEmpowerCarrier(s)) return false;

        // ③ 상태 규칙 — (스킬, 루트, 차수)에 가장 구체적인 것 하나.
        CastRule r = Resolve(s);
        if (r == null || r.type == "always") return true;

        if (r.type == "cooldownDebt")
        {
            if (ps == null) return true;
            float debt = 0f;
            foreach (EquippedSkill o in ps.EquippedSkills)
                if (o != s && o.CooldownTimer > 0f) debt += o.CooldownTimer;
            return debt >= r.seconds;
        }

        if (r.type == "enemiesWithin")
        {
            if (ps == null) return true;
            float px = ps.transform.position.x;
            int n = 0;
            IReadOnlyList<Enemy> list = Enemy.Active;   // 매 프레임 도는 자리라 스냅샷 대신 읽기 전용 목록을 그대로 센다
            for (int i = 0; i < list.Count; i++)
            {
                Enemy e = list[i];
                if (e != null && e.IsAlive && Mathf.Abs(e.transform.position.x - px) <= r.range && ++n >= r.count)
                    return true;
            }
            return false;
        }

        return true; // 모르는 타입은 게이트하지 않는다 — 정책 오타가 봇을 침묵시키면 안 된다
    }

    // 진화 상태에 맞는 가장 구체적인 규칙. 루트+차수 일치 규칙이 기본 규칙을 이긴다.
    private static CastRule Resolve(EquippedSkill s)
    {
        if (rulesById == null || !rulesById.TryGetValue(s.Id, out List<CastRule> list)) return null;
        CastRule best = null;
        foreach (CastRule r in list)
        {
            if (r.minTier > 0 && (s.EvolutionStage < r.minTier || (r.route >= 0 && s.Route != r.route))) continue;
            if (best == null || r.minTier > best.minTier) best = r;
        }
        return best;
    }

    // 준비된(쿨 0 · 자동시전 아님 · 되감기 아님) 스킬 중 s보다 우선순위가 높은 것이 없으면 s가 운반자다.
    private static bool IsBestEmpowerCarrier(EquippedSkill s)
    {
        if (ps == null) return true;
        int myRank = empowerRank.TryGetValue(s.Id, out int mr) ? mr : int.MaxValue;
        foreach (EquippedSkill o in ps.EquippedSkills)
        {
            if (o == s || o.Id == ActiveSkillId.Rewind || o.CooldownTimer > 0f || PlayerSkills.IsAutoCastOnly(o)) continue;
            int rank = empowerRank.TryGetValue(o.Id, out int r) ? r : int.MaxValue;
            if (rank < myRank) return false;
        }
        return true;
    }
}
#endif
