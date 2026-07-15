using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// 아웃게임(메타 프로그레션) 데이터·저장·런타임 계층.
// 인게임에서 번 정수(태양빛)를 저장하고, 타이틀의 "태양의 가호" 상점에서
// 영구 업그레이드를 구매하며, 그 효과를 다음 판 시작 시 인게임 스탯에 반영한다.
// MonoBehaviour 없음 — 순수 static/data. 씬 배치 컴포넌트는 MetaRunApplier가 담당.
// ─────────────────────────────────────────────────────────────────────────────

public enum MetaUpgradeId
{
    Attack,   // 공격력
    Health,   // 체력
    Regen,    // 회복
    Cooldown, // 쿨타임
    Duration, // 지속시간
    Xp,       // 경험
    Wealth,   // 부유(정수 획득)
    Crit,     // 치명타
}

public class MetaUpgradeDef
{
    public MetaUpgradeId Id;
    public string Name;
    public string DescFormat; // {0}에 누적 수치가 들어감
    public int MaxLevel;
    public int BaseCost;
    public float CostGrowth;
    public float PerLevel; // 레벨당 효과 크기(해석은 Id별로 다름 — %는 정수 퍼센트 값)

    // 다음 레벨(현재 level에서 level+1로) 구매 비용
    public int CostForLevel(int level) => Mathf.RoundToInt(BaseCost * Mathf.Pow(CostGrowth, level));

    // 특정 레벨에서의 누적 효과 수치(설명 표시용)
    public float TotalAt(int level) => PerLevel * level;
}

public static class MetaUpgrades
{
    public static readonly MetaUpgradeDef[] All =
    {
        new MetaUpgradeDef { Id = MetaUpgradeId.Attack,   Name = "공격력",   DescFormat = "공격 피해 +{0}%",        MaxLevel = 5,  BaseCost = 50, CostGrowth = 1.6f, PerLevel = 5f  },
        new MetaUpgradeDef { Id = MetaUpgradeId.Health,   Name = "체력",     DescFormat = "최대 체력 +{0}",         MaxLevel = 5,  BaseCost = 40, CostGrowth = 1.6f, PerLevel = 20f },
        new MetaUpgradeDef { Id = MetaUpgradeId.Regen,    Name = "회복",     DescFormat = "5초마다 체력 +{0}",      MaxLevel = 5,  BaseCost = 45, CostGrowth = 1.7f, PerLevel = 2f  },
        new MetaUpgradeDef { Id = MetaUpgradeId.Cooldown, Name = "쿨타임",   DescFormat = "재사용 대기시간 -{0}%",  MaxLevel = 3,  BaseCost = 80, CostGrowth = 2.0f, PerLevel = 2f  },
        new MetaUpgradeDef { Id = MetaUpgradeId.Duration, Name = "지속시간", DescFormat = "스킬 지속시간 +{0}%",    MaxLevel = 5,  BaseCost = 50, CostGrowth = 1.6f, PerLevel = 4f  },
        new MetaUpgradeDef { Id = MetaUpgradeId.Xp,       Name = "경험",     DescFormat = "경험치 획득 +{0}%",      MaxLevel = 10, BaseCost = 30, CostGrowth = 1.4f, PerLevel = 3f  },
        new MetaUpgradeDef { Id = MetaUpgradeId.Wealth,   Name = "부유",     DescFormat = "정수 획득 +{0}%",        MaxLevel = 10, BaseCost = 40, CostGrowth = 1.5f, PerLevel = 10f },
        new MetaUpgradeDef { Id = MetaUpgradeId.Crit,     Name = "치명타",   DescFormat = "치명타 확률 +{0}%",      MaxLevel = 5,  BaseCost = 60, CostGrowth = 1.7f, PerLevel = 2f  },
    };

    public static MetaUpgradeDef Get(MetaUpgradeId id) => System.Array.Find(All, d => d.Id == id);
}

// PlayerPrefs 기반 영구 저장(판 사이 유지).
public static class MetaSave
{
    private const string CurrencyKey = "meta.currency";
    private const string UpgradePrefix = "meta.up.";

    public static int Currency => PlayerPrefs.GetInt(CurrencyKey, 0);

    public static void AddCurrency(int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(CurrencyKey, Currency + amount);
        PlayerPrefs.Save();
    }

    public static int GetLevel(MetaUpgradeId id) => PlayerPrefs.GetInt(UpgradePrefix + id, 0);

    // 구매 시도: 잔고·최대레벨 통과 시 정수 차감 + 레벨 증가 후 true.
    public static bool TryPurchase(MetaUpgradeId id)
    {
        MetaUpgradeDef def = MetaUpgrades.Get(id);
        int level = GetLevel(id);
        if (level >= def.MaxLevel) return false;

        int cost = def.CostForLevel(level);
        if (Currency < cost) return false;

        PlayerPrefs.SetInt(CurrencyKey, Currency - cost);
        PlayerPrefs.SetInt(UpgradePrefix + id, level + 1);
        PlayerPrefs.Save();
        return true;
    }

    // 치트/디버그용 — 전체 초기화
    public static void ResetAll()
    {
        PlayerPrefs.DeleteKey(CurrencyKey);
        foreach (MetaUpgradeDef d in MetaUpgrades.All) PlayerPrefs.DeleteKey(UpgradePrefix + d.Id);
        PlayerPrefs.Save();
    }
}

// 판 시작 시 MetaRunApplier가 저장값을 읽어 세팅하는 인게임 런타임 보너스.
// 기본값 = 무효과. 전투 코드가 매 발동/계산 때 읽는다.
public static class MetaBonuses
{
    public static float CooldownMult = 1f; // 스킬 쿨타임 배율 (<1이면 감소)
    public static float DurationMult = 1f; // 스킬 지속시간 배율 (>1이면 증가)
    public static float CritBonus = 0f;    // 전역 치명타 확률 가산(0~1)
    public static float CurrencyMult = 1f; // 정수 획득 배율
    public static int RegenPer5s = 0;      // 5초마다 회복량

    public static void Reset()
    {
        CooldownMult = 1f;
        DurationMult = 1f;
        CritBonus = 0f;
        CurrencyMult = 1f;
        RegenPer5s = 0;
    }
}

// 이번 판에서 모은 정수(판 종료 시 MetaSave에 적립).
public static class MetaRun
{
    public static int RunCurrency;

    public static void Reset() => RunCurrency = 0;

    // 정수 픽업이 플레이어에게 흡수될 때 호출 — 부유(정수 획득) 배율이 여기서 적용된다.
    public static void Collect(int baseAmount)
    {
        RunCurrency += Mathf.Max(1, Mathf.RoundToInt(baseAmount * MetaBonuses.CurrencyMult));
    }
}
