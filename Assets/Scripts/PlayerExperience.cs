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
        // 후반 경험치 과다 획득 완화: 스테이지 1에서 100%, 20에서 50%로 선형 감소(그 이후는 50% 유지)
        float stageFactor = 1f;
        if (GameManager.Instance != null)
        {
            int stage = Mathf.Clamp(GameManager.Instance.CurrentStage, 1, 20);
            stageFactor = Mathf.Lerp(1f, 0.5f, (stage - 1) / 19f);
        }
        currentXP += Mathf.RoundToInt(amount * xpMultiplier * stageFactor);

        while (currentXP >= xpToNextLevel)
        {
            currentXP -= xpToNextLevel;
            level++;
            xpToNextLevel += 9;

            if (levelUpVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(levelUpVfxPrefab, transform.position, Quaternion.identity), 2f);

            LevelUpUI.Instance.Show();
        }
    }
}
