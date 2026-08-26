using System.Collections.Generic;
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
    // 노릴 적의 순번. FireHoming이 발사 순서대로 0,1,2…를 준다 — 전부 같은 적으로 몰리는 걸 막는 장치다.
    public int TargetRank { get; set; }

    // 재타겟은 미사일마다 자주 일어난다. 후보 리스트를 매번 새로 만들지 않으려고 공용 버퍼를 쓴다.
    private static readonly List<Enemy> candidates = new();

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
        // IsAlive까지 봐야 한다 — 풀링된 적은 죽어도 참조가 null이 되지 않아서, null만 보면
        // 반납된(또는 재활용된) 적을 계속 쫓으며 재타겟을 영영 안 한다.
        if (target == null || !target.IsAlive) target = AcquireTarget();
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
        candidates.Clear();
        foreach (Enemy e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            if (e != null && e.IsAlive) candidates.Add(e);
        if (candidates.Count == 0) return null;

        Vector2 self = transform.position;
        // 비행 유닛 우선: 비행 후보가 하나라도 있으면 지상보다 항상 먼저 노린다. 같은 부류 안에선 가까운 순.
        candidates.Sort((a, b) =>
        {
            if (a.IsFlying != b.IsFlying) return a.IsFlying ? -1 : 1;
            float sa = ((Vector2)a.transform.position - self).sqrMagnitude;
            float sb = ((Vector2)b.transform.position - self).sqrMagnitude;
            return sa.CompareTo(sb);
        });
        // 미사일이 적보다 많으면 순번이 한 바퀴 돌아 겹친다 — 그건 그대로 둔다(적이 적을 땐 몰리는 게 맞다).
        return candidates[TargetRank % candidates.Count];
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
                // Effect_Explosion은 4프레임 16fps(0.25초)에 `despawnOnFinish`가 꺼져 있다 —
                // 반환을 늦추면 **마지막 연기 프레임이 그대로 얼어붙어** 남는다. 재생 길이 바로 뒤에 회수한다.
                ObjectPool.Instance.Despawn(vfx, 0.3f);
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
