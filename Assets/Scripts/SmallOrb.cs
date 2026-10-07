using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SmallOrb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lifetime = 2f;
    [SerializeField] private GameObject impactVfxPrefab;

    private Vector2 direction = Vector2.left;
    private bool hasHit;
    private Enemy homingTarget;

    public float Damage { get; set; }
    public bool ApplyVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 명중 시 개별적으로 치명타를 굴린다

    // ── 오브 R1(호밍 연계) 전용 ──
    // 산탄 알(기본 용도)은 둘 다 꺼진 채로 직진한다. 오브 R1만 켠다.
    public bool Homing { get; set; }
    public int PierceRemaining { get; set; }        // 남은 관통 횟수. 0이면 첫 명중에 소멸(=산탄 알의 기존 동작)
    public ActiveSkillId Source { get; set; } = ActiveSkillId.Orb; // 데미지 집계용 출처
    // 노릴 적의 순번(가까운 순). 발사 순서대로 0,1,2…를 준다 — HomingMissile.TargetRank와 같은 장치.
    public int TargetRank { get; set; }

    // ── 오브 R1 2차 「저글러」 전용 ──────────────────────────────────────────
    // "화면 내 **무작위** 적을 추적해서 한 번 맞춘 후 다시 캐릭터 쪽으로 돌아온다"(2026-09-19 사용자).
    public bool RandomTarget { get; set; }      // 가까운 순 대신 무작위로 고른다 — 오브가 한 적에게 몰리지 않는다
    // 🔴 2026-10-07 사용자: 첫 명중 즉시 복귀 → **관통(PierceRemaining)을 다 쓰거나 근처에 적이 없으면** 복귀.
    //    맞힌 뒤에는 JugglerSeekRange 안의 다른 적을 무작위로 골라 계속 쫓는다. 첫 표적만 거리 제한이 없다
    //    (출발점이 캐릭터라 제한을 걸면 적이 멀 때 나가자마자 돌아와 사라진다).
    public Transform ReturnTo { get; set; }     // 설정하면 위 조건에서 이쪽으로 돌아온다. null이면 기존 동작.
    private bool returning;
    private bool hitOnce;   // 저글러가 한 번이라도 맞혔나 — 그 뒤부터 재표적에 거리 제한이 걸린다
    private bool spent;     // 저글러가 관통을 다 썼다 — 돌아가는 길에 닿는 적을 더는 때리지 않는다
    private const float JugglerSeekRange = 5f;         // 맞힌 뒤 다음 적을 찾는 반경(유닛). 이 안에 없으면 돌아간다
    private const float ReturnArriveDistance = 0.5f;   // 이만큼 가까워지면 임무 완료
    // Start에서 소멸 예약에 쓰인다 — Start 전에(생성 직후) 바꿔야 먹는다.
    public float Lifetime { get => lifetime; set => lifetime = value; }

    // 🔴 추적은 **서서히 선회**한다. 예전엔 매 프레임 방향을 적에게 곧장 꽂아서, 한 점에서 나온 오브들이
    //    부채꼴로 펴지기도 전에 첫 프레임부터 같은 적을 향해 **완전히 겹쳐** 한 개로 보였다(실측 6개 → 좌표 1곳).
    private const float HomingTurnDegPerSec = 360f;

    // ── 추적 오브의 관성 (사용자 지시 2026-09-27) ────────────────────────────
    // "속도가 훨씬 빨라야 하고, 방향 전환이 없다면 가속도 개념도 있어야 해. 약간 관성을 줘서
    //  방향 전환할 때 원심력 같은게 작용하도록"
    // 🔴 원심력의 정체는 **횡가속이 일정하면 각속도가 속도에 반비례한다**는 것이다(ω = a/v).
    //    그래서 빠를수록 크게 돌고, 꺾는 만큼 속도가 깎여 코너에서 다시 조여진다.
    //    ⚠️ 하한(MinTurn)이 없으면 빨라진 오브가 적 주위를 **영영 맴돌며 못 맞힌다**.
    private const float HomingSpeedMult = 1.8f;        // 프리팹 moveSpeed에 곱하는 출발 속도
    private const float HomingMaxSpeedMult = 3.2f;     // 직진으로 붙일 수 있는 최고 속도
    private const float HomingMinSpeedMult = 1.1f;     // 아무리 꺾어도 이 밑으로는 안 떨어진다
    private const float HomingAccel = 14f;             // 직진 중 가속(유닛/초²)
    private const float HomingLateralAccel = 42f;      // 선회에 쓸 수 있는 횡가속 — 이 값이 선회 반경을 정한다
    private const float HomingMinTurnDegPerSec = 220f; // 각속도 하한
    private const float HomingTurnSpeedBleed = 0.9f;   // 1라디안 꺾을 때마다 깎이는 속도 비율

    private float speed;   // 지금 속도. 산탄 알(비추적)은 moveSpeed에 고정된 채로 쓴다.

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();
    // 정렬 키(거리)를 담을 때 미리 재서 넣는다 — 비교 함수 안에서 transform.position을 읽으면
    // 네이티브 접근이 비교 횟수(N log N)만큼 일어난다. 먼저 재면 N번으로 끝난다.
    private static readonly List<(float sqrDist, Enemy enemy)> candidates = new List<(float, Enemy)>();
    // 정적 비교자. 위치를 캡처하는 람다는 **재타겟마다 클로저와 델리게이트를 새로 할당**한다
    // (HomingMissile이 같은 이유로 정적 비교자를 쓴다).
    private static readonly System.Comparison<(float sqrDist, Enemy enemy)> ByDistance =
        (a, b) => a.sqrDist.CompareTo(b.sqrDist);

    public void Init(Vector2 dir, float damage, bool applyVulnerable)
    {
        direction = dir.normalized;
        Damage = damage;
        ApplyVulnerable = applyVulnerable;
        // 추적만 빨라지고 가속한다. 산탄 알은 예전 그대로 등속 직진이다.
        // ⚠️ 호출부가 Homing을 Init보다 **먼저** 켠다 — 순서가 바뀌면 이 분기가 안 먹는다.
        speed = Homing ? moveSpeed * HomingSpeedMult : moveSpeed;
    }

    private void Start() => Destroy(gameObject, lifetime);

    private void Update()
    {
        // 저글러의 복귀 구간: 적이 아니라 **캐릭터**를 향해 돌아온다. 도착하면 그 자리에서 사라진다.
        // 돌아오는 길에 닿는 적도 관통이 남아 있는 동안은 때린다(OnTriggerEnter2D가 계속 돈다).
        if (returning)
        {
            if (ReturnTo == null) { Destroy(gameObject); return; }
            Vector2 toOwner = (Vector2)ReturnTo.position - (Vector2)transform.position;
            if (toOwner.sqrMagnitude <= ReturnArriveDistance * ReturnArriveDistance) { Destroy(gameObject); return; }

            float maxRadBack = HomingTurnDegPerSec * Mathf.Deg2Rad * Time.deltaTime;
            direction = ((Vector2)Vector3.RotateTowards(direction, toOwner.normalized, maxRadBack, 0f)).normalized;
            transform.Translate(direction * speed * Time.deltaTime, Space.World);
            FaceDirection();
            return;
        }

        // 추적: 아직 안 때린 적 중 가장 가까운 쪽으로 방향을 튼다.
        // ⚠️ 풀링된 적은 죽어도 참조가 null이 안 된다 — IsAlive를 같이 봐야 반납된 적을 영영 쫓지 않는다.
        if (Homing)
        {
            if (homingTarget == null || !homingTarget.IsAlive || hitEnemies.Contains(homingTarget))
            {
                homingTarget = AcquireTarget();
                // 저글러: 한 번 맞힌 뒤 근처에 쫓을 적이 없으면 돌아간다.
                if (homingTarget == null && ReturnTo != null && hitOnce) { BeginReturn(); return; }
            }

            float turnedRad = 0f;
            if (homingTarget != null)
            {
                Vector2 desired = ((Vector2)homingTarget.transform.position - (Vector2)transform.position).normalized;
                // 각속도 = 횡가속 ÷ 속도. 빠를수록 크게 돈다 — 이게 원심력으로 보이는 부분이다.
                float omega = Mathf.Max(HomingMinTurnDegPerSec * Mathf.Deg2Rad,
                                        HomingLateralAccel / Mathf.Max(0.01f, speed));
                Vector2 before = direction;
                direction = ((Vector2)Vector3.RotateTowards(direction, desired, omega * Time.deltaTime, 0f)).normalized;
                turnedRad = Vector2.Angle(before, direction) * Mathf.Deg2Rad;
            }

            // 직진하면 붙고, 꺾은 만큼 깎인다 — 코너에서 느려졌다가 빠져나오며 다시 가속한다.
            speed += HomingAccel * Time.deltaTime;
            speed -= speed * turnedRad * HomingTurnSpeedBleed;
            speed = Mathf.Clamp(speed, moveSpeed * HomingMinSpeedMult, moveSpeed * HomingMaxSpeedMult);
        }
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
        if (Homing) FaceDirection();
    }

    // 추적 오브는 날아가는 방향으로 그림을 돌린다(사용자 지시 2026-09-30: 플레이어 쪽으로 되돌아갈 때도 왼쪽을 봤다).
    // 그림의 앞이 왼쪽(−x)이라 180°를 뺀다 — 왼쪽으로 날면 0°로 예전 모습 그대로다.
    // 이동은 Space.World라 회전이 경로에 끼어들지 않는다.
    private void FaceDirection()
    {
        float deg = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 180f;
        transform.rotation = Quaternion.Euler(0f, 0f, deg);
    }

    // 아직 안 때린 산 적을 가까운 순으로 세워 TargetRank번째를 노린다. 오브가 적보다 많으면 순번이 돌아 겹친다(적이 적을 땐 몰리는 게 맞다).
    private Enemy AcquireTarget()
    {
        candidates.Clear();
        Vector2 self = transform.position;
        // 인덱스 for로 도는 건 박싱 때문이다 — IReadOnlyList의 foreach는 List<T>.Enumerator를 박싱해 힙에 올린다.
        IReadOnlyList<Enemy> active = Enemy.Active;
        // 저글러는 한 번 맞힌 뒤부터 JugglerSeekRange 안에서만 다음 적을 찾는다.
        float maxSqr = ReturnTo != null && hitOnce ? JugglerSeekRange * JugglerSeekRange : float.MaxValue;
        for (int i = 0; i < active.Count; i++)
        {
            Enemy e = active[i];
            if (e == null || !e.IsAlive || hitEnemies.Contains(e)) continue;
            float sqr = ((Vector2)e.transform.position - self).sqrMagnitude;
            if (sqr <= maxSqr) candidates.Add((sqr, e));
        }
        if (candidates.Count == 0) return null;

        // 저글러는 **무작위**로 고른다 — 가까운 순이면 오브가 많아질수록 앞줄 몇 마리에 전부 몰린다.
        if (RandomTarget) return candidates[Random.Range(0, candidates.Count)].enemy;

        candidates.Sort(ByDistance);
        return candidates[TargetRank % candidates.Count].enemy;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit || spent) return;
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        enemy.TakeSkillHit(Damage, CritChance, Source, ApplyVulnerable ? StatusIconLibrary.Vulnerable : null);
        if (ApplyVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.SpawnImpactVfx(impactVfxPrefab, enemy.transform.position, ObjectPool.ImpactVfxLifetime);

        // 저글러: 관통이 남아 있으면 다음 적을 찾아가고, 다 썼으면 캐릭터에게 돌아간다(2026-10-07 사용자).
        // 방패는 관통과 무관하게 끊는다 — 쫓는 중이면 거기서 돌아가고, 돌아가는 중이면 종전대로 사라진다.
        if (ReturnTo != null)
        {
            hitOnce = true;
            bool blocked = enemy.BlocksProjectiles;
            if (returning && blocked) { hasHit = true; Destroy(gameObject); return; }
            if (PierceRemaining <= 0 || blocked) { spent = true; BeginReturn(); return; }
            PierceRemaining--;
            homingTarget = null;   // 즉시 재타겟(복귀 중이면 Update가 표적을 안 본다)
            return;
        }

        // 관통이 남았으면 살아서 다음 적을 찾아간다(오브 R1). 방패는 관통과 무관하게 끊는다.
        if (PierceRemaining > 0 && !enemy.BlocksProjectiles)
        {
            PierceRemaining--;
            homingTarget = null; // 즉시 재타겟 — 다음 프레임을 기다리지 않는다
            return;
        }

        hasHit = true;
        Destroy(gameObject);
    }

    // 저글러의 복귀 전환. 돌아오는 길에 닿는 적도 때려야 해서 hitEnemies를 비운다 — 안 비우면 왔던 길의 적이 전부 면역이 된다.
    private void BeginReturn()
    {
        if (returning) return;
        returning = true;
        homingTarget = null;
        hitEnemies.Clear();
    }
}
