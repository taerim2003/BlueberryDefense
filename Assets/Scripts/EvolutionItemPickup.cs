using UnityEngine;

// 벽 스테이지 엘리트가 떨구는 진화 아이템. 정수/하트 픽업과 같은 방식으로 플레이어에게
// 날아가 흡수되고, 흡수되는 순간 진화 선택 모달(LevelUpUI.ShowEvolutionReward)을 연다.
public class EvolutionItemPickup : MonoBehaviour
{
    // GameManager가 Awake에서 주입 — 적 프리팹마다 참조를 물리지 않고 한 곳에서 관리(EssencePickup과 동일)
    public static GameObject Prefab;

    [SerializeField] private float delayBeforeHoming = 0.5f;
    [SerializeField] private float homingSpeed = 7f;
    [SerializeField] private float homingAcceleration = 9f;
    [SerializeField] private float absorbDistance = 0.4f;
    [SerializeField] private float bobAmplitude = 0.25f; // 대기 중 위아래로 둥실거리는 폭
    [SerializeField] private float bobSpeed = 3f;
    [SerializeField] private GameObject absorbVfxPrefab;

    private float delayTimer;
    private float currentSpeed;
    private Vector3 restPosition;
    private Transform target;

    // 적이 죽을 때 호출 — 프리팹이 배선돼 있을 때만 실제로 생성한다.
    public static void Drop(Vector3 position)
    {
        if (Prefab == null) return;
        Instantiate(Prefab, position, Quaternion.identity);
    }

    private void Start()
    {
        restPosition = transform.position;
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
        if (delayTimer < delayBeforeHoming)
        {
            // 잠깐 그 자리에 떠 있으면서 "떨어진 게 있다"는 걸 보여준다
            transform.position = restPosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmplitude);
            return;
        }

        currentSpeed = Mathf.Min(currentSpeed + homingAcceleration * Time.deltaTime, homingSpeed);
        Vector3 targetPos = target.position;
        transform.position = Vector3.MoveTowards(transform.position, targetPos, currentSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) <= absorbDistance)
        {
            if (absorbVfxPrefab != null)
                ObjectPool.Instance.Despawn(ObjectPool.Instance.Spawn(absorbVfxPrefab, targetPos, Quaternion.identity), 2f);

            if (LevelUpUI.Instance != null) LevelUpUI.Instance.ShowEvolutionReward();
            Destroy(gameObject);
        }
    }
}
