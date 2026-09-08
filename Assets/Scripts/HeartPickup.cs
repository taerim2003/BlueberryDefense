using UnityEngine;

// 몬스터 처치 시 낮은 확률로 드랍되는 하트. 잠시 그 자리에 머물다가 플레이어에게 가속하며 날아가
// 닿으면 흡수되어 체력을 회복시킨다.
public class HeartPickup : MonoBehaviour
{
    [SerializeField] private int healAmount = 5;
    [SerializeField] private float delayBeforeHoming = 0.3f;
    [SerializeField] private float homingSpeed = 8f;
    [SerializeField] private float homingAcceleration = 10f;
    [SerializeField] private float absorbDistance = 0.3f;
    [SerializeField] private float curveTurnDegPerSec = 260f; // 곡선 정도 — 낮을수록 크게 휜다
    [SerializeField] private GameObject absorbVfxPrefab;

    private float delayTimer;
    private float currentSpeed;
    private float homingTimer;
    private Vector3 flyDir;
    private PlayerHealth target;

    private void Start()
    {
        target = FindAnyObjectByType<PlayerHealth>();
    }

    private void Update()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        delayTimer += Time.deltaTime;
        if (delayTimer < delayBeforeHoming) return;

        // 직선으로 빨려들면 밋밋해서, 무작위 방향으로 한 번 튄 뒤 곡선을 그리며 붙게 한다.
        if (flyDir == Vector3.zero)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            flyDir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
        }

        homingTimer += Time.deltaTime;
        currentSpeed = Mathf.Min(currentSpeed + homingAcceleration * Time.deltaTime, homingSpeed);
        Vector3 targetPos = target.transform.position;

        // 선회 속도는 시간이 갈수록 커진다 — 곡선으로 출발하되 반드시 플레이어에게 도착하도록.
        Vector3 desired = (targetPos - transform.position).normalized;
        float turnRad = (curveTurnDegPerSec + 400f * homingTimer) * Mathf.Deg2Rad * Time.deltaTime;
        flyDir = Vector3.RotateTowards(flyDir, desired, turnRad, 0f).normalized;
        transform.position += flyDir * (currentSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= absorbDistance)
        {
            // 스킬트리 "건강: 체력회복템 회복량 2배"(건강 패시브를 얻었을 때만 켜진다)
            target.Heal(PlayerPassives.HealItemDouble ? healAmount * 2 : healAmount);
            SfxPlayer.Play(SfxId.HeartPickup);

            if (absorbVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(absorbVfxPrefab, targetPos, Quaternion.identity), 2f);

            Destroy(gameObject);
        }
    }
}
