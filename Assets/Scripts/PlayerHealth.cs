using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    private const int MaxOverheal = 200;

    [SerializeField] private int maxHealth = 100;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public int Overheal { get; private set; }

    // 건강 연계 path2: 실제로 체력이 깎일 때마다(오버힐 흡수분 제외) 호출됨
    public System.Action<int> OnDamageTaken;

    private float metaRegenTimer;

    private void Awake()
    {
        // 선택된 캐릭터의 기본 체력을 반영(없으면 프리팹 SerializeField = 현행). MetaRunApplier의 추가체력은 Start에서 얹힘.
        if (RunConfig.Character != null) maxHealth = RunConfig.Character.baseHealth;
        CurrentHealth = maxHealth;
    }

    // 메타 "회복" 업그레이드: 5초마다 일정량 회복(기본 regen과 별개로 스택)
    private void Update()
    {
        if (MetaBonuses.RegenPer5s <= 0) return;
        if (GameManager.Instance != null && (GameManager.Instance.IsGameOver || GameManager.Instance.IsGameClear)) return;

        metaRegenTimer += Time.deltaTime;
        if (metaRegenTimer >= 5f)
        {
            metaRegenTimer -= 5f;
            Heal(MetaBonuses.RegenPer5s);
        }
    }

    public void TakeDamage(int amount)
    {
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        // 방어 패시브: 들어온 피해를 먼저 깎고, 남은 것만 보호막·체력이 받는다.
        // 최소 1은 남긴다 — 감소율이 커져도 완전 무적이 되지 않게.
        if (PlayerPassives.DamageReduction > 0f && amount > 0)
            amount = Mathf.Max(1, Mathf.RoundToInt(amount * (1f - PlayerPassives.DamageReduction)));

        if (Overheal > 0)
        {
            int absorbed = Mathf.Min(Overheal, amount);
            Overheal -= absorbed;
            amount -= absorbed;
        }
        if (amount <= 0) return;

        CurrentHealth -= amount;
        OnDamageTaken?.Invoke(amount);
        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            GameManager.Instance?.GameOver();
        }
    }

    public void Heal(int amount)
    {
        CurrentHealth = Mathf.Min(CurrentHealth + amount, maxHealth);
    }

    public void AddOverheal(int amount)
    {
        // 체력이 최대보다 낮으면 먼저 회복에 쓰고, 남는 만큼만(또는 이미 풀피면 전부) 오버힐 보호막으로 전환
        if (CurrentHealth < maxHealth)
        {
            int healAmount = Mathf.Min(amount, maxHealth - CurrentHealth);
            CurrentHealth += healAmount;
            amount -= healAmount;
        }
        if (amount <= 0) return;

        Overheal = Mathf.Min(Overheal + amount, MaxOverheal);
    }

    public void IncreaseMaxHealth(int amount)
    {
        maxHealth += amount;
        CurrentHealth += amount;
    }

    public void FullHeal()
    {
        CurrentHealth = maxHealth;
    }
}
