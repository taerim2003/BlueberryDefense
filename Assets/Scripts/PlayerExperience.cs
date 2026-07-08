using UnityEngine;

public class PlayerExperience : MonoBehaviour
{
    public static PlayerExperience Instance { get; private set; }

    [SerializeField] private int level = 1;
    [SerializeField] private int currentXP;
    [SerializeField] private int xpToNextLevel = 18;
    [SerializeField] private GameObject levelUpVfxPrefab;

    private float xpMultiplier = 1f;

    public int Level => level;
    public int CurrentXP => currentXP;
    public int XPToNextLevel => xpToNextLevel;

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
            xpToNextLevel += 9;

            if (levelUpVfxPrefab != null)
                Destroy(Instantiate(levelUpVfxPrefab, transform.position, Quaternion.identity), 2f);

            LevelUpUI.Instance.Show();
        }
    }
}
