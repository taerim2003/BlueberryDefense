using System.Collections.Generic;
using UnityEngine;

// 낙뢰 버프는 지속시간 버프다. 🔴 **스택은 R0 진화(되감기 연계)를 올려야만 쌓인다**(사용자 결정 2026-09-17).
// 진화 전엔 다시 쓰면 기존 버프를 지우고 지속시간만 새로 시작한다 — 쿨감으로 쿨이 지속시간보다 짧아져도 안 겹친다.
// 진화 후엔 기존 버프를 지우지 않고 각자 자기 지속시간을 갖는 별도 스택으로 쌓인다.
// 그래서 타격 한 번에 발동 확률을 스택 수만큼 독립적으로 굴린다(스택 2개면 최대 2번 발동 가능).
public static class LightningStorm
{
    private static readonly List<float> stackEndTimes = new List<float>();

    // 스택이 쌓이는가. 낙뢰 시전 시 PlayerSkills가 AddStack **앞에서** 갱신한다. 꺼져 있으면 HUD에 스택 숫자도 안 뜬다.
    public static bool StackingEnabled;

    // 레벨업 주 성장축이라 시작값을 낮게 잡는다(60%였을 땐 헤드룸이 40%뿐이라 +3%p가 전혀 안 보였음).
    // 25%에서 시작해 레벨업마다 +6%p → 10레벨 55%. 초반엔 가끔 터지고 후반엔 쫙쫙 떨어진다.
    public const float BaseProcChance = 0.25f;
    public static float ProcChance = BaseProcChance;
    // 낙뢰 기본 피해. ⚠️ Prog_Lightning.baseDamage(=0)는 무시되고 **이 상수가 실제 시작 피해**다
    // (GetDefaultDamage가 낙뢰만 여기서 읽어감). 시작값 하향 12→6→4(세션16: 1레벨 파워 축소).
    public const float BaseProcDamage = 4f;
    public static float ProcDamage = BaseProcDamage;  // 캐스트마다 배율 적용된 '현재' 피해로 갱신됨
    public static bool RecursiveProcEnabled;
    public static float RecursiveDamageGrowth; // 힘 연계 path0 T3: 재귀 단계마다 이 비율만큼 낙뢰 피해량 누적 증가

    // 체인 라이트닝 (힘 연계, path1): 첫 낙뢰 피격 시 주변 적에게 전이
    public static bool ChainEnabled;
    public const float ChainRadius = 4f;
    public static int ChainCount = 3;

    // 중첩당 전체 공격 피해량 증가.
    // ⚠️ 원래 도달 불가능한 path2에 잠들어 있던 효과다 — 2026-08-06 명세에서 **R0(되감기 연계, path0)** 으로 옮겨
    //    실제로 열리게 됐다. 2차 진화가 스택당 보너스를 키우므로 const가 아니라 static이다.
    public static bool StackDamageEnabled;
    public const float BaseStackDamageBonus = 0.15f;
    public static float StackDamageBonusPerStack = BaseStackDamageBonus;

    // 리프레쉬 연계 (패시브 path2): 낙뢰가 실제로 떨어질 때마다 호출
    public static System.Action OnProc;

    // ── 낙뢰 R0 2차 「초대형 축적 번개」 ────────────────────────────────────
    // 노션 문구: "낙뢰 버프가 **20스택** 중첩되었을 시 초대형 낙뢰가 떨어짐"
    // 2026-09-19 사용자: "일정 스택 이상 번개가 쌓이면 번개가 **이 번개로 변화**해.
    //   주위 적들에게 큰 피해를 입히지만 **약간의 쿨타임(0.5초 정도)** 이 있음."
    // ⚠️ 평소 낙뢰를 **대체**한다(추가가 아니다). 쿨 중에는 평소 낙뢰가 그대로 나간다.
    public static bool HugeBoltEnabled;
    public const int HugeBoltStackThreshold = 20;
    public const float HugeBoltCooldown = 0.5f;
    public static float HugeBoltDamageMult = 6f;   // 평소 낙뢰 피해의 배수
    public static float HugeBoltRadius = 4f;       // 주위 적에게 퍼지는 반경(유닛)
    public static GameObject HugeBoltVfxPrefab;    // PlayerSkills가 시전할 때 넣어 준다(Enemy 프리팹 12장 배선을 피한다)
    private static float hugeBoltReadyAt;

    // 지금 초대형 번개를 쓸 수 있으면 true를 돌려주고 **쿨을 건다**(한 번만 소비된다).
    public static bool TryConsumeHugeBolt()
    {
        if (!HugeBoltEnabled || HugeBoltVfxPrefab == null) return false;
        if (Time.time < hugeBoltReadyAt) return false;
        if (ActiveStackCount < HugeBoltStackThreshold) return false;
        hugeBoltReadyAt = Time.time + HugeBoltCooldown;
        return true;
    }

    public static int ActiveStackCount
    {
        get
        {
            Prune();
            return stackEndTimes.Count;
        }
    }

    // 가장 늦게 꺼지는 스택의 남은 시각 (HUD 타이머 표시용)
    public static float LatestEndTime
    {
        get
        {
            Prune();
            float latest = 0f;
            foreach (float t in stackEndTimes) latest = Mathf.Max(latest, t);
            return latest;
        }
    }

    // 이 판에서만 유효한 static 상태 초기화 (RunState에서 호출).
    // OnProc 구독은 각 컴포넌트가 Awake/OnDestroy로 관리하므로 여기서 건드리지 않는다.
    public static void ResetRunState()
    {
        stackEndTimes.Clear();
        StackingEnabled = false;
        ProcChance = BaseProcChance;
        ProcDamage = BaseProcDamage;
        RecursiveProcEnabled = false;
        RecursiveDamageGrowth = 0f;
        ChainEnabled = false;
        ChainCount = 3;
        StackDamageEnabled = false;
        StackDamageBonusPerStack = BaseStackDamageBonus;
        HugeBoltEnabled = false;
        HugeBoltVfxPrefab = null;
        hugeBoltReadyAt = 0f;
    }

    // 낙뢰 시전: 스택이 켜져 있으면 기존 스택 위에 새 스택을 추가하고, 꺼져 있으면 기존 것을 갈아끼운다.
    public static void AddStack(float duration)
    {
        Prune();
        if (!StackingEnabled) stackEndTimes.Clear();
        stackEndTimes.Add(Time.time + duration);
    }

    // 타격 1회당 살아있는 스택 수만큼 독립적으로 발동 확률을 판정한다.
    public static int RollProcCount()
    {
        Prune();
        int procs = 0;
        for (int i = 0; i < stackEndTimes.Count; i++)
            if (Random.value < ProcChance) procs++;
        return procs;
    }

    private static void Prune()
    {
        for (int i = stackEndTimes.Count - 1; i >= 0; i--)
            if (Time.time >= stackEndTimes[i]) stackEndTimes.RemoveAt(i);
    }
}
