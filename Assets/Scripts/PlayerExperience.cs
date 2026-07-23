using UnityEngine;

public class PlayerExperience : MonoBehaviour
{
    public static PlayerExperience Instance { get; private set; }

    [SerializeField] private int level = 1;
    [SerializeField] private int currentXP;
    [SerializeField] private int xpToNextLevel = 18;
    [SerializeField] private GameObject levelUpVfxPrefab;
    [SerializeField] private ScalingTable scaling; // XP 커브·후반 감쇠(전역). 미할당 시 기본값 폴백

    private float xpMultiplier = 1f;

    public int Level => level;
    public int CurrentXP => currentXP;
    public int XPToNextLevel => xpToNextLevel;

    private ScalingTable Scaling => scaling != null ? scaling : ScalingTable.Default;

    private void Awake()
    {
        Instance = this;
        xpToNextLevel = Scaling.xpToNextLevelBase;
    }

    public float XpMultiplier => xpMultiplier; // ESC 요약에서 현재 경험치 획득 배율 표기용

    public void IncreaseXPMultiplier(float amount)
    {
        xpMultiplier += amount;
    }

    public void AddXP(int amount)
    {
        // 후반 경험치 과다 획득 완화: 스테이지별 XP 배율(ScalingTable, 스테이지1=최대→기준스테이지=최소로 선형 감소)
        float stageFactor = GameManager.Instance != null ? Scaling.XpStageFactor(GameManager.Instance.CurrentStage) : 1f;
        currentXP += Mathf.RoundToInt(amount * xpMultiplier * stageFactor);

        while (currentXP >= xpToNextLevel)
        {
            currentXP -= xpToNextLevel;
            level++;
            xpToNextLevel += Scaling.xpToNextLevelPerLevel;

            if (levelUpVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(levelUpVfxPrefab, transform.position, Quaternion.identity), 2f);

            LevelUpUI.Instance.Show();
        }
    }
}
