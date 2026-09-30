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

    // 🔴 스택 상한(2026-09-29 사용자). 종전엔 상한이 없어 지속시간 안에 쌓이는 만큼 무한히 늘었다.
    //    1차 「뇌운 축적」은 4에서 시작해 레벨업 "최대 스택" 카드 3장으로 10까지 간다.
    //    2차 「초대형 축적 번개」는 20에서 시작한다(발동 임계 15보다 높아야 임계를 넘길 여유가 생긴다).
    public const int BaseMaxStacks = 4;
    public static int MaxStacks = BaseMaxStacks;

    // 레벨업 주 성장축이라 시작값을 낮게 잡는다(60%였을 땐 헤드룸이 40%뿐이라 +3%p가 전혀 안 보였음).
    // 25%에서 시작해 레벨업마다 +6%p → 10레벨 55%. 초반엔 가끔 터지고 후반엔 쫙쫙 떨어진다.
    public const float BaseProcChance = 0.25f;
    public static float ProcChance = BaseProcChance;
    // 낙뢰 기본 피해. ⚠️ Prog_Lightning.baseDamage(=0)는 무시되고 **이 상수가 실제 시작 피해**다
    // (GetDefaultDamage가 낙뢰만 여기서 읽어감). 시작값 하향 12→6→4(세션16: 1레벨 파워 축소).
    // 2026-09-26 4→9: QA 2,366판에서 미진화 낙뢰가 dps 144로 전체 최하(2위 오브 441의 1/3)였고
    // 진행률도 0.681로 하위권이었다 — 1차 진화 뒤엔 20,292로 최상위라 "진화 전 구간만" 벌이었다.
    public const float BaseProcDamage = 9f;
    public static float ProcDamage = BaseProcDamage;  // 캐스트마다 배율 적용된 '현재' 피해로 갱신됨

    // 중첩당 전체 공격 피해량 증가.
    // ⚠️ 원래 도달 불가능한 path2에 잠들어 있던 효과다 — 2026-08-06 명세에서 **R0(되감기 연계, path0)** 으로 옮겨
    //    실제로 열리게 됐다. 2차 진화가 스택당 보너스를 키우므로 const가 아니라 static이다.
    public static bool StackDamageEnabled;
    public const float BaseStackDamageBonus = 0.15f;
    public static float StackDamageBonusPerStack = BaseStackDamageBonus;

    // 낙뢰가 실제로 떨어질 때마다 호출. 구독자는 PlayerPassives(폐지된 리프레쉬 패시브의 path2 효과 — 치트 창으로만 도달)뿐이다.
    public static System.Action OnProc;

    // ── 낙뢰 R0 2차 「초대형 축적 번개」 ────────────────────────────────────
    // 노션 문구: "낙뢰 버프가 **15번** 쌓이면 초대형 낙뢰가 떨어진다"(2026-09-29 사용자: 20 → 15)
    // 2026-09-19 사용자: "일정 스택 이상 번개가 쌓이면 번개가 **이 번개로 변화**해.
    //   주위 적들에게 큰 피해를 입히지만 **약간의 쿨타임(0.5초 정도)** 이 있음."
    // ⚠️ 평소 낙뢰를 **대체**한다(추가가 아니다). 쿨 중에는 평소 낙뢰가 그대로 나간다.
    public static bool HugeBoltEnabled;
    public const int HugeBoltStackThreshold = 15;
    public const float HugeBoltCooldown = 0.5f;
    public static float HugeBoltDamageMult = 6f;   // 평소 낙뢰 피해의 배수
    public const float BaseHugeBoltRadius = 4f;
    public static float HugeBoltRadius = BaseHugeBoltRadius;  // 주위 적에게 퍼지는 반경(유닛). 레벨업 "크기"가 곱해진다
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
        StackDamageEnabled = false;
        StackDamageBonusPerStack = BaseStackDamageBonus;
        MaxStacks = BaseMaxStacks;
        HugeBoltEnabled = false;
        HugeBoltRadius = BaseHugeBoltRadius;
        HugeBoltVfxPrefab = null;
        hugeBoltReadyAt = 0f;
    }

    // 걸려 있는 낙뢰 버프를 통째로 걷는다. 스택이 0이면 RollProcCount가 0을 돌려주므로 발동 자체가 멈춘다.
    // 쓰임: 피뢰침(낙뢰 R1 1차)이 기본 낙뢰 버프를 **대체**할 때(PlayerSkills의 Lightning 분기).
    public static void ClearStacks() => stackEndTimes.Clear();

    // 낙뢰 시전: 스택이 켜져 있으면 기존 스택 위에 새 스택을 추가하고, 꺼져 있으면 기존 것을 갈아끼운다.
    public static void AddStack(float duration)
    {
        Prune();
        if (!StackingEnabled) stackEndTimes.Clear();
        // 상한에 닿으면 **가장 먼저 꺼질 스택을 갱신**한다. 그냥 버리면 만스택에서 버프가 통째로 끊긴다.
        if (StackingEnabled && stackEndTimes.Count >= Mathf.Max(1, MaxStacks))
        {
            int oldest = 0;
            for (int i = 1; i < stackEndTimes.Count; i++)
                if (stackEndTimes[i] < stackEndTimes[oldest]) oldest = i;
            stackEndTimes[oldest] = Time.time + duration;
            return;
        }
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
