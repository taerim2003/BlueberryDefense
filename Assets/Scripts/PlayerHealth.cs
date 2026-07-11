using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    private const int MaxOverheal = 50;

    [SerializeField] private int maxHealth = 100;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public int Overheal { get; private set; }

    private void Awake()
    {
        CurrentHealth = maxHealth;
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
        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            GameManager.Instance?.GameOver();
        }
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
