using System.Collections;
using UnityEngine;

public class OrbAltar : MonoBehaviour
{
    [SerializeField] private GameObject smallOrbPrefab;
    [SerializeField] private float lifetime = 15f;
    [SerializeField] private float orbInterval = 1f;
    [SerializeField] private float lightningInterval = 2.5f;
    [SerializeField] private int orbDirectionCount = 8;
    [SerializeField] private float forwardSpreadDegrees = 90f; // 전방(왼쪽 180도 기준) 부채꼴 각도

    public float OrbDamage { get; set; }
    public float LightningDamage { get; set; }
    public bool ApplyVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 미니 오브/낙뢰 각각 명중할 때마다 개별적으로 치명타를 굴린다

    private const string BuffKey = "OrbAltar";

    private void Start()
    {
        BuffTracker.Set(BuffKey, Time.time + lifetime);
        StartCoroutine(OrbRoutine());
        StartCoroutine(LightningRoutine());
        Destroy(gameObject, lifetime);
    }

    private void OnDestroy()
    {
        BuffTracker.Clear(BuffKey);
    }

    private IEnumerator OrbRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(orbInterval);
            FireOrbBurst();
        }
    }

    private void FireOrbBurst()
    {
        if (smallOrbPrefab == null) return;

        // 캐릭터는 왼쪽(180도)을 바라보므로, 그 방향을 중심으로 부채꼴로 전방 발사
        for (int i = 0; i < orbDirectionCount; i++)
        {
            float t = orbDirectionCount > 1 ? (float)i / (orbDirectionCount - 1) : 0.5f;
            float angleDeg = 180f - forwardSpreadDegrees * 0.5f + t * forwardSpreadDegrees;
            float angle = angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            GameObject obj = Instantiate(smallOrbPrefab, transform.position, Quaternion.identity);
            obj.transform.localScale *= 0.5f;
            SmallOrb orb = obj.GetComponent<SmallOrb>();
            orb.Init(dir, OrbDamage, ApplyVulnerable);
            orb.CritChance = CritChance;
        }
    }

    private IEnumerator LightningRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(lightningInterval);
            foreach (Enemy enemy in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                float hitDamage = PlayerPassives.ApplyCrit(LightningDamage, CritChance, out bool isCrit);
                enemy.TakeDamage(hitDamage, isLightningProc: true, isCrit: isCrit);
            }
        }
    }
}
