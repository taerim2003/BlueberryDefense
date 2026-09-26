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
    public float ExplodeVfxMult { get; set; } = 1f; // 레벨업 "폭발 범위" 몫 — 폭발 그림을 반경과 같은 비율로 키운다
    public float ExplodeRatio { get; set; } = 0.4f;
    // 노릴 적의 순번. FireHoming이 발사 순서대로 0,1,2…를 준다 — 전부 같은 적으로 몰리는 걸 막는 장치다.
    public int TargetRank { get; set; }
    // 호밍 R0 2차 「초강력 슈퍼 로켓」 — 순번이 아니라 **체력이 가장 높은 적** 하나를 쫓는다.
    public bool TargetHighestHealth { get; set; }

    // 재타겟은 미사일마다 자주 일어난다. 후보 리스트를 매번 새로 만들지 않으려고 공용 버퍼를 쓴다.
    // 정렬 키(비행 여부·거리)를 **담을 때 미리 재서** 넣는다 — 비교 함수 안에서 transform.position을 읽으면
    // 네이티브 접근이 비교 횟수(N log N)만큼 일어난다. 먼저 재면 N번으로 끝난다.
    private static readonly List<(int flying, float sqrDist, Enemy enemy)> candidates = new();

    // 비행 유닛 우선: 비행 후보가 하나라도 있으면 지상보다 항상 먼저 노린다(flying 0 < 지상 1). 같은 부류 안에선 가까운 순.
    // 비교 함수를 정적으로 둔다 — 위치를 캡처하는 람다는 재타겟마다 할당되는데, 노리던 적이 죽는 프레임엔
    // 그 적에게 몰렸던 미사일(2차 진화면 수십 발)이 한꺼번에 재타겟한다.
    private static readonly System.Comparison<(int flying, float sqrDist, Enemy enemy)> ByPriority = (a, b) =>
    {
        if (a.flying != b.flying) return a.flying - b.flying;
        return a.sqrDist.CompareTo(b.sqrDist);
    };

    // 재타겟 주기. 예전엔 표적이 없는 미사일이 **매 프레임** 전체 적을 훑고 정렬했다 —
    // 후반엔 미사일 수십~90발 × 적 수백이라 그것만으로 프레임을 먹었다
    // (2026-09-18 봇 260런 실측: 호밍을 쓴 판의 시뮬 배속 3.64x, 안 쓴 판 6.06x — 스킬 중 격차 1위).
    // 표적이 없는 동안엔 아래 Update 주석대로 **직진**하므로 이 지연이 동작을 바꾸지 않는다.
    private const float RetargetInterval = 0.15f;
    private float nextRetargetTime;

    private Enemy target;
    private Vector2 dir = Vector2.left; // 전방 = 적이 오는 쪽(-x). 이 프로젝트의 전 스킬 공통 관례다.
    private bool hit;
    private float life;
    private Vector3 baseScale;

    private void Awake() => baseScale = transform.localScale;

    // ObjectPool에서 꺼낼 때마다 도는 초기화 — FireHoming은 이 뒤에 값을 채우고 Init을 부른다.
    // ⚠️ 풀 재사용엔 Awake·Start가 다시 안 돈다. 런타임에 바뀌는 필드를 추가하면 여기서도 되돌릴 것.
    //    크기를 되돌려 두므로 호출부의 `localScale *=`가 누적되지 않는다.
    private void OnEnable()
    {
        transform.localScale = baseScale;
        target = null;
        dir = Vector2.left;
        hit = false;
        life = lifetime;
        Damage = 0f;
        CritChance = 0f;
        Explode = false;
        ExplodeRadius = 1.5f;
        ExplodeVfxMult = 1f;
        ExplodeRatio = 0.4f;
        TargetRank = 0;
        TargetHighestHealth = false; // 🔴 안 되돌리면 재사용된 미사일이 일반 호밍인데도 체력 1위만 쫓는다
        nextRetargetTime = 0f; // 꺼내자마자 한 번은 즉시 표적을 잡는다(주기는 그 다음부터)
    }

    public void Init(Vector2 initialDir)
    {
        if (initialDir.sqrMagnitude > 0.0001f) dir = initialDir.normalized;
        FaceDir();
    }

    // 풀 반납. 꼬리(PixelTrail의 파티클)를 비우고 멈춰 둔다 — 안 그러면 다음에 꺼낼 때
    // 거리 기반 방출이 반납 자리에서 새 발사 자리까지 점선을 한 줄 그을 수 있다(재생은 ObjectPool.Spawn이 다시 건다).
    private void Despawn()
    {
        if (TryGetComponent(out ParticleSystem trail))
            trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ObjectPool.Instance.Despawn(gameObject);
    }

    private void Update()
    {
        // IsAlive까지 봐야 한다 — 풀링된 적은 죽어도 참조가 null이 되지 않아서, null만 보면
        // 반납된(또는 재활용된) 적을 계속 쫓으며 재타겟을 영영 안 한다.
        if ((target == null || !target.IsAlive) && Time.time >= nextRetargetTime)
        {
            // 다음 주기에 TargetRank로 위상을 어긋내 둔다 — 한 시전의 수십 발은 같은 프레임에 발사되므로,
            // 고정 간격만 두면 9프레임마다 전량이 **한꺼번에** 재타겟해서 주기를 둔 의미가 없어진다.
            nextRetargetTime = Time.time + RetargetInterval * (0.5f + (TargetRank % 16) / 32f);
            target = AcquireTarget();
        }

        life -= Time.deltaTime;
        if (life <= 0f) { Despawn(); return; }

        // 🔴 미사일은 **어떤 상황에서도 멈추지 않는다**(사용자 결정 2026-09-14 — 09-10의 "제자리 대기"를 폐지).
        // 노릴 적이 없으면 회전만 건너뛰고 지금 방향으로 직진한다: 소환 직후면 Init이 준 발사 방향,
        // 날아가던 중 대상이 죽었으면 직전까지 향하던 방향. 새 적이 나타나면 위 재타겟이 다시 잡는다.
        if (target != null)
        {
            Vector2 desired = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            float maxRad = turnDegPerSec * Mathf.Deg2Rad * Time.deltaTime;
            dir = ((Vector2)Vector3.RotateTowards(dir, desired, maxRad, 0f)).normalized;
        }
        FaceDir();
        transform.Translate(dir * moveSpeed * Time.deltaTime, Space.World);
    }

    private void FaceDir()
    {
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, ang);
    }

    private Enemy AcquireTarget()
    {
        // 초강력 슈퍼 로켓(호밍 R0 2차)은 **체력이 가장 높은 적**을 노린다(2026-09-19 사용자 명세).
        // 평소의 "비행 우선 → 가까운 순 → TargetRank번째"와 완전히 다른 기준이라 먼저 갈라낸다.
        if (TargetHighestHealth)
        {
            Enemy best = null;
            IReadOnlyList<Enemy> all = Enemy.Active;
            for (int i = 0; i < all.Count; i++)
            {
                Enemy e = all[i];
                if (e == null || !e.IsAlive) continue;
                if (best == null || e.CurrentHealth > best.CurrentHealth) best = e;
            }
            return best;
        }

        candidates.Clear();
        Vector2 origin = transform.position;
        // FindObjectsByType을 쓰면 안 된다(Enemy.Active 주석). 인덱스 for로 도는 건 박싱 때문이다 —
        // IReadOnlyList의 foreach는 List<T>.Enumerator를 **박싱해 힙에 올려서**, 재타겟마다 쓰레기가 하나씩 생긴다.
        IReadOnlyList<Enemy> active = Enemy.Active;
        for (int i = 0; i < active.Count; i++)
        {
            Enemy e = active[i];
            if (e != null && e.IsAlive)
                candidates.Add((e.IsFlying ? 0 : 1, ((Vector2)e.transform.position - origin).sqrMagnitude, e));
        }
        if (candidates.Count == 0) return null;

        candidates.Sort(ByPriority);
        // 미사일이 적보다 많으면 순번이 한 바퀴 돌아 겹친다 — 그건 그대로 둔다(적이 적을 땐 몰리는 게 맞다).
        return candidates[TargetRank % candidates.Count].enemy;
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
                vfx.transform.localScale = Vector3.one * explodeVfxScale * ExplodeVfxMult;
                // Effect_Explosion은 4프레임 16fps(0.25초)에 `despawnOnFinish`가 꺼져 있다 —
                // 반환을 늦추면 **마지막 연기 프레임이 그대로 얼어붙어** 남는다. 재생 길이 바로 뒤에 회수한다.
                ObjectPool.Instance.Despawn(vfx, 0.3f);
            }
            using (Enemy.GetSnapshot(out List<Enemy> enemies))
                foreach (Enemy o in enemies)
                {
                    // 🔴 직격한 적(e)도 폭발분을 받는다(2026-09-27 사용자 "폭발데미지 같은게 안느껴져").
                    //    미사일마다 **다른 적**을 쫓으므로 직격 대상을 빼면, 그 적 주변에 다른 적이 없을 때
                    //    폭발 피해가 정확히 0이 되어 그림만 떴다. 이미 죽었으면 TakeDamage의 isDead 가드가 막는다.
                    if (o == null) continue;
                    if (Vector2.Distance(pos, o.transform.position) <= ExplodeRadius)
                        o.TakeSkillHit(Damage * ExplodeRatio, CritChance, ActiveSkillId.Homing);
                }
        }
        Despawn();
    }
}
