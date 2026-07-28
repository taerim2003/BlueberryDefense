using System.Collections.Generic;
using UnityEngine;

// 낙뢰 버프는 "스택형" 지속시간 버프다. 평소엔 지속시간이 쿨타임보다 짧아 한 번에 하나만 존재하지만,
// 회오리 연계(path2 T2)로 지속시간이 계속 연장되면 다음 낙뢰 재시전 시점까지 살아남을 수 있다.
// 이 경우 기존 버프를 지우고 새로 덮어쓰는 게 아니라, 각자 자기 지속시간을 갖는 별도 스택으로 쌓인다.
// 그래서 타격 한 번에 발동 확률을 스택 수만큼 독립적으로 굴린다(스택 2개면 최대 2번 발동 가능).
public static class LightningStorm
{
    private static readonly List<float> stackEndTimes = new List<float>();

    // 레벨업 주 성장축이라 시작값을 낮게 잡는다(60%였을 땐 헤드룸이 40%뿐이라 +3%p가 전혀 안 보였음).
    // 25%에서 시작해 레벨업마다 +6%p → 10레벨 55%. 초반엔 가끔 터지고 후반엔 쫙쫙 떨어진다.
    public const float BaseProcChance = 0.25f;
    public static float ProcChance = BaseProcChance;
    // 낙뢰 기본 피해. ⚠️ Prog_Lightning.baseDamage(=0)는 무시되고 **이 상수가 실제 시작 피해**다
    // (GetDefaultDamage가 낙뢰만 여기서 읽어감). 시작값 하향 12→6.
    public const float BaseProcDamage = 6f;
    public static float ProcDamage = BaseProcDamage;  // 캐스트마다 배율 적용된 '현재' 피해로 갱신됨
    public static bool RecursiveProcEnabled;
    public static float RecursiveDamageGrowth; // 힘 연계 path0 T3: 재귀 단계마다 이 비율만큼 낙뢰 피해량 누적 증가

    // 체인 라이트닝 (힘 연계, path1): 첫 낙뢰 피격 시 주변 적에게 전이
    public static bool ChainEnabled;
    public const float ChainRadius = 4f;
    public static int ChainCount = 3;

    // 회오리 연계 (path2): 중첩당 전체 공격 피해량 증가
    public static bool StackDamageEnabled;
    public const float StackDamageBonusPerStack = 0.15f;

    // 리프레쉬 연계 (패시브 path2): 낙뢰가 실제로 떨어질 때마다 호출
    public static System.Action OnProc;

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
        ProcChance = BaseProcChance;
        ProcDamage = BaseProcDamage;
        RecursiveProcEnabled = false;
        RecursiveDamageGrowth = 0f;
        ChainEnabled = false;
        ChainCount = 3;
        StackDamageEnabled = false;
    }

    // 낙뢰 시전: 기존 스택을 지우지 않고 새 스택을 추가한다(평소엔 이전 스택이 이미 만료된 상태라 사실상 1개).
    public static void AddStack(float duration)
    {
        Prune();
        stackEndTimes.Add(Time.time + duration);
    }

    // 회오리 연계 path2 T2: 현재 살아있는 모든 스택의 지속시간을 연장
    public static void ExtendActiveStacks(float amount)
    {
        Prune();
        for (int i = 0; i < stackEndTimes.Count; i++)
            stackEndTimes[i] += amount;
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
