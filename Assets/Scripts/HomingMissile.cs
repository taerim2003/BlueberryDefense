using UnityEngine;

// 호밍 미사일: 가장 가까운 적을 추적해 명중 시 피해(+선택적 폭발). PlayerSkills.FireHoming이 스폰·설정한다.
[RequireComponent(typeof(Collider2D))]
public class HomingMissile : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 9f;
    [SerializeField] private float turnDegPerSec = 360f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private GameObject explodeVfxPrefab; // 폭발(Route2) VFX — 프로젝트의 실제 폭발 에셋을 프리팹에 배선
    [SerializeField] private float explodeVfxScale = 0.6f;
    [SerializeField] private GameObject hitVfxPrefab;     // 매 명중 시 타격 VFX
    [SerializeField] private float hitVfxScale = 0.5f;

    public float Damage { get; set; }
    public float CritChance { get; set; }
    public bool Explode { get; set; }
    public float ExplodeRadius { get; set; } = 1.5f;
    public float ExplodeRatio { get; set; } = 0.4f;

    private Enemy target;
    private Vector2 dir = Vector2.right;
    private bool hit;

    public void Init(Vector2 initialDir)
    {
        if (initialDir.sqrMagnitude > 0.0001f) dir = initialDir.normalized;
        FaceDir();
    }

    private void Start() => Destroy(gameObject, lifetime);

    private void Update()
    {
        if (target == null) target = AcquireTarget();
        if (target != null)
        {
            Vector2 desired = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            float maxRad = turnDegPerSec * Mathf.Deg2Rad * Time.deltaTime;
            dir = ((Vector2)Vector3.RotateTowards(dir, desired, maxRad, 0f)).normalized;
            FaceDir();
        }
        transform.Translate(dir * moveSpeed * Time.deltaTime, Space.World);
    }

    private void FaceDir()
    {
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);
    }

    private Enemy AcquireTarget()
    {
        Enemy best = null;
        float bestSqr = float.MaxValue;
        bool bestFlying = false;
        foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (e == null) continue;
            float sqr = ((Vector2)e.transform.position - (Vector2)transform.position).sqrMagnitude;
            // 비행 유닛 우선: 비행 후보가 하나라도 있으면 지상보다 항상 먼저 노린다. 같은 부류 안에선 가장 가까운 것.
            bool better = (e.IsFlying && !bestFlying) || (e.IsFlying == bestFlying && sqr < bestSqr);
            if (best == null || better) { best = e; bestSqr = sqr; bestFlying = e.IsFlying; }
        }
        return best;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hit) return;
        Enemy e = other.GetComponent<Enemy>();
        if (e == null) return;
        hit = true;

        Vector3 pos = transform.position;
        e.TakeSkillHit(Damage, CritChance, ActiveSkillId.Homing);

        if (hitVfxPrefab != null)
        {
            GameObject hv = ObjectPool.Instance.Spawn(hitVfxPrefab, pos, Quaternion.identity);
            hv.transform.localScale = Vector3.one * hitVfxScale;
            ObjectPool.Instance.Despawn(hv, 1f);
        }

        if (Explode)
        {
            if (explodeVfxPrefab != null)
            {
                GameObject vfx = ObjectPool.Instance.Spawn(explodeVfxPrefab, pos, Quaternion.identity);
                vfx.transform.localScale = Vector3.one * explodeVfxScale;
                ObjectPool.Instance.Despawn(vfx, 1.2f);
            }
            foreach (Enemy o in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            {
                if (o == null || o == e) continue;
                if (Vector2.Distance(pos, o.transform.position) <= ExplodeRadius)
                    o.TakeSkillHit(Damage * ExplodeRatio, CritChance, ActiveSkillId.Homing);
            }
        }
        Destroy(gameObject);
    }
}
