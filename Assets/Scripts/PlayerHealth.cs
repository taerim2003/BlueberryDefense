using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    private const int MaxOverheal = 50;

    [SerializeField] private int maxHealth = 100;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public int Overheal { get; private set; }

    // 건강 연계 path2: 실제로 체력이 깎일 때마다(오버힐 흡수분 제외) 호출됨
    public System.Action<int> OnDamageTaken;

    private float metaRegenTimer;

    private void Awake()
    {
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
