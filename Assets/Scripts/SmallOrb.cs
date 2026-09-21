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
    public Transform ReturnTo { get; set; }     // 설정하면 첫 명중 뒤 이쪽으로 돌아온다. null이면 기존 동작.
    private bool returning;
    private const float ReturnArriveDistance = 0.5f;   // 이만큼 가까워지면 임무 완료
    // Start에서 소멸 예약에 쓰인다 — Start 전에(생성 직후) 바꿔야 먹는다.
    public float Lifetime { get => lifetime; set => lifetime = value; }

    // 🔴 추적은 **서서히 선회**한다. 예전엔 매 프레임 방향을 적에게 곧장 꽂아서, 한 점에서 나온 오브들이
    //    부채꼴로 펴지기도 전에 첫 프레임부터 같은 적을 향해 **완전히 겹쳐** 한 개로 보였다(실측 6개 → 좌표 1곳).
    private const float HomingTurnDegPerSec = 360f;

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
    }

    private void Start() => Destroy(gameObject, lifetime);

    private void Update()
    {
        // 저글러의 복귀 구간: 적이 아니라 **캐릭터**를 향해 돌아온다. 도착하면 그 자리에서 사라진다.
        // 돌아오는 길에 닿는 적도 그대로 때린다(OnTriggerEnter2D가 계속 돈다).
        if (returning)
        {
            if (ReturnTo == null) { Destroy(gameObject); return; }
            Vector2 toOwner = (Vector2)ReturnTo.position - (Vector2)transform.position;
            if (toOwner.sqrMagnitude <= ReturnArriveDistance * ReturnArriveDistance) { Destroy(gameObject); return; }

            float maxRadBack = HomingTurnDegPerSec * Mathf.Deg2Rad * Time.deltaTime;
            direction = ((Vector2)Vector3.RotateTowards(direction, toOwner.normalized, maxRadBack, 0f)).normalized;
            transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
            return;
        }

        // 추적: 아직 안 때린 적 중 가장 가까운 쪽으로 방향을 튼다.
        // ⚠️ 풀링된 적은 죽어도 참조가 null이 안 된다 — IsAlive를 같이 봐야 반납된 적을 영영 쫓지 않는다.
        if (Homing)
        {
            if (homingTarget == null || !homingTarget.IsAlive || hitEnemies.Contains(homingTarget))
                homingTarget = AcquireTarget();
            if (homingTarget != null)
            {
                Vector2 desired = ((Vector2)homingTarget.transform.position - (Vector2)transform.position).normalized;
                float maxRad = HomingTurnDegPerSec * Mathf.Deg2Rad * Time.deltaTime;
                direction = ((Vector2)Vector3.RotateTowards(direction, desired, maxRad, 0f)).normalized;
            }
        }
        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
    }

    // 아직 안 때린 산 적을 가까운 순으로 세워 TargetRank번째를 노린다. 오브가 적보다 많으면 순번이 돌아 겹친다(적이 적을 땐 몰리는 게 맞다).
    private Enemy AcquireTarget()
    {
        candidates.Clear();
        Vector2 self = transform.position;
        // 인덱스 for로 도는 건 박싱 때문이다 — IReadOnlyList의 foreach는 List<T>.Enumerator를 박싱해 힙에 올린다.
        IReadOnlyList<Enemy> active = Enemy.Active;
        for (int i = 0; i < active.Count; i++)
        {
            Enemy e = active[i];
            if (e != null && e.IsAlive && !hitEnemies.Contains(e))
                candidates.Add((((Vector2)e.transform.position - self).sqrMagnitude, e));
        }
        if (candidates.Count == 0) return null;

        // 저글러는 **무작위**로 고른다 — 가까운 순이면 오브가 많아질수록 앞줄 몇 마리에 전부 몰린다.
        if (RandomTarget) return candidates[Random.Range(0, candidates.Count)].enemy;

        candidates.Sort(ByDistance);
        return candidates[TargetRank % candidates.Count].enemy;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        enemy.TakeSkillHit(Damage, CritChance, Source);
        if (ApplyVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.SpawnTimed(impactVfxPrefab, enemy.transform.position, 2.2f);

        // 저글러: **첫 명중과 동시에 복귀로 전환**한다(2026-09-19 사용자 "한번 맞춘 후 다시 캐릭터 쪽으로 돌아와").
        // 돌아오는 길에 닿는 적도 때려야 해서 hitEnemies를 비운다 — 안 비우면 왔던 길의 적이 전부 면역이 된다.
        if (ReturnTo != null && !returning)
        {
            returning = true;
            homingTarget = null;
            hitEnemies.Clear();
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
}
