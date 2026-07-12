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
    [SerializeField] private GameObject absorbVfxPrefab;

    private float delayTimer;
    private float currentSpeed;
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

        currentSpeed = Mathf.Min(currentSpeed + homingAcceleration * Time.deltaTime, homingSpeed);
        Vector3 targetPos = target.transform.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, currentSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= absorbDistance)
        {
            target.Heal(healAmount);

            if (absorbVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(absorbVfxPrefab, targetPos, Quaternion.identity), 2f);

            Destroy(gameObject);
        }
    }
}
