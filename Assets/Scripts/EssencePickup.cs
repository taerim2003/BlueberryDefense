using UnityEngine;

// 적 처치 시 확률로 드랍되는 정수(태양빛). 하트 픽업과 동일하게 잠시 그 자리에 머물다
// 플레이어에게 가속하며 날아가 닿으면 흡수되어 이번 판 정수로 적립된다.
public class EssencePickup : MonoBehaviour
{
    // GameManager가 Awake에서 주입 — 모든 적 프리팹에 개별로 물릴 필요 없이 한 곳에서 관리
    public static GameObject Prefab;

    [SerializeField] private float delayBeforeHoming = 0.3f;
    [SerializeField] private float homingSpeed = 8f;
    [SerializeField] private float homingAcceleration = 10f;
    [SerializeField] private float absorbDistance = 0.3f;
    [SerializeField] private float curveTurnDegPerSec = 260f; // 곡선 정도 — 낮을수록 크게 휜다
    [SerializeField] private GameObject absorbVfxPrefab;

    private int amount = 1;
    private float delayTimer;
    private float currentSpeed;
    private float homingTimer;
    private Vector3 flyDir;
    private Transform target;

    public void SetAmount(int value) => amount = Mathf.Max(1, value);

    private void Start()
    {
        PlayerHealth ph = FindAnyObjectByType<PlayerHealth>();
        target = ph != null ? ph.transform : null;
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

        // 날아오는 구간만 한 프레임에 FlightSteps번 진행한다 — 곡선 모양은 그대로, 도착 시간이 1/FlightSteps
        // (2026-10-01 사용자: 빨려드는 속도 2배). homingSpeed만 올리면 선회는 그대로라 곡선이 넓게 휘고
        // 한 걸음이 커져 흡수 반경을 건너뛸 수 있다. 쪼개 진행하면 한 걸음 크기도 지금과 같다.
        for (int i = 0; i < FlightSteps; i++)
            if (StepHoming(Time.deltaTime)) return;
    }

    private const int FlightSteps = 2;

    // 한 걸음 진행. 흡수했으면 true(이 오브젝트는 파괴된다).
    private bool StepHoming(float dt)
    {
        homingTimer += dt;
        currentSpeed = Mathf.Min(currentSpeed + homingAcceleration * dt, homingSpeed);
        Vector3 targetPos = target.position;

        // 선회 속도는 시간이 갈수록 커진다 — 곡선으로 출발하되 반드시 플레이어에게 도착하도록.
        Vector3 desired = (targetPos - transform.position).normalized;
        float turnRad = (curveTurnDegPerSec + 400f * homingTimer) * Mathf.Deg2Rad * dt;
        flyDir = Vector3.RotateTowards(flyDir, desired, turnRad, 0f).normalized;
        transform.position += flyDir * (currentSpeed * dt);

        if (Vector3.Distance(transform.position, targetPos) > absorbDistance) return false;

        MetaRun.Collect(amount);
        SfxPlayer.Play(SfxId.EssencePickup);

        if (absorbVfxPrefab != null)
            ObjectPool.Instance.SpawnTimed(absorbVfxPrefab, targetPos, 2f);

        Destroy(gameObject);
        return true;
    }
}
