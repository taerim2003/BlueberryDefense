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
    [SerializeField] private GameObject absorbVfxPrefab;

    private int amount = 1;
    private float delayTimer;
    private float currentSpeed;
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

        currentSpeed = Mathf.Min(currentSpeed + homingAcceleration * Time.deltaTime, homingSpeed);
        Vector3 targetPos = target.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, currentSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= absorbDistance)
        {
            MetaRun.Collect(amount);

            if (absorbVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(absorbVfxPrefab, targetPos, Quaternion.identity), 2f);

            Destroy(gameObject);
        }
    }
}
