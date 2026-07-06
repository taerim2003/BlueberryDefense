using UnityEngine;

public class PlayerExperience : MonoBehaviour
{
    public static PlayerExperience Instance { get; private set; }

    [SerializeField] private int level = 1;
    [SerializeField] private int currentXP;
    [SerializeField] private int xpToNextLevel = 10;

    private float xpMultiplier = 1f;

    private void Awake()
    {
        Instance = this;
    }

    public void IncreaseXPMultiplier(float amount)
    {
        xpMultiplier += amount;
    }

    public void AddXP(int amount)
    {
        currentXP += Mathf.RoundToInt(amount * xpMultiplier);

        while (currentXP >= xpToNextLevel)
        {
            currentXP -= xpToNextLevel;
            level++;
            xpToNextLevel += 5;
            LevelUpUI.Instance.Show();
        }
    }
}
