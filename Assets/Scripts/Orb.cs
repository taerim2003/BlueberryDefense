using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Orb : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float slowMultiplier = 0.8f; // 이동속도 배율(20% 감속). 실제 값은 Orb_Skill·BigOrb_Skill 프리팹
    [SerializeField] private float slowDuration = 2f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private float tickInterval = 0.3f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 틱마다 개별적으로 치명타를 굴린다
    // 🔴 기본 오브는 둔화를 **걸지 않는다** — 스킬트리 「끈적한 오브」(orb_BasicSlow)를 사야 켜진다(FireOrb가 세팅).
    //    아래 두 보너스(진화 R0)는 둔화가 켜져 있을 때만 의미가 있다.
    public bool SlowsEnemies { get; set; }
    public float SlowMultiplierBonus { get; set; } // 뺄셈 (0~slowMultiplier)
    public float SlowDurationBonus { get; set; } // 덧셈(초)
    // 이 오브가 **평생** 붙잡을 수 있는 적 수(레벨업 주 성장축). FireOrb가 세팅.
    // 매 틱 리셋되는 동시 타격 한도가 아니라 소모성 예산이다 — 다 쓰고 붙잡은 적이 전부 정리되면 오브가 사라진다.
    public int MaxTargets { get; set; } = 4;

    // ── 오브 R0 2차 「초대형 오브」 전용 손잡이 (2026-09-19 사용자 명세) ──────────
    // "모든 것을 관통하는 초대형 오브를 소환해 주위 적들을 끌어당긴다" (노션 UI 문구) +
    // "오브 자체는 엄청 천천히 움직이고 · 방패병도 관통 · 관통 무한 · 대신 지속시간이 있다"
    public bool PiercesShields { get; set; }          // 방패 블루베리에 안 막히고 통과한다
    public float SpeedMultiplier { get; set; } = 1f;  // 이동 속도 배율(초대형은 아주 낮다)
    public float LifetimeOverride { get; set; }       // >0이면 프리팹 수명 대신 이 값을 쓴다
    public float PullInterval { get; set; }           // 0이면 끌어당기지 않는다(= 기존 오브 전부)
    public float PullRadius { get; set; } = 4f;
    public float PullDistance { get; set; } = 1.6f;

    private float nextPullTime;

    private const float ImpactSfxCooldown = 0.9f; // Whirlwind와 동일한 이유: 임팩트 클립 길이가 틱 간격(0.3초)보다 길어서 매 틱 재생하면 겹쳐 쌓인다.

    private float nextImpactSfxTime;
    private bool consumed; // 방패에 막혀 소멸 확정 — 같은 프레임에 다른 방패와도 겹쳐 있으면 중복 타격되는 것을 막는다
    private int budgetRemaining = -1;   // 아직 붙잡을 수 있는 적 수. -1 = 미초기화(Start에서 MaxTargets로 채움)
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly HashSet<Enemy> claimed = new HashSet<Enemy>(); // 예산을 이미 소모한 적 — 얘들은 계속 무료로 간다
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();
    // 틱 순서를 정할 버퍼. 매 프레임 새 리스트를 만들면 그대로 GC 연료가 된다 — 비우고 다시 채운다.
    // 정렬 키(거리)는 담을 때 미리 재 둔다(비교 함수 안에서 transform.position을 읽으면 비교 횟수만큼 네이티브 접근이 일어난다).
    private readonly List<(float sqrDist, Enemy enemy)> ordered = new List<(float, Enemy)>();
    // 정적 비교자 — `this`를 캡처하는 람다는 정렬마다 클로저와 델리게이트를 새로 할당한다.
    private static readonly System.Comparison<(float sqrDist, Enemy enemy)> ByDistance =
        (a, b) => a.sqrDist.CompareTo(b.sqrDist);

    private void Start()
    {
        budgetRemaining = Mathf.Max(1, MaxTargets);
        // 수명은 보통 **안전망**이다(아무도 못 만난 오브가 영원히 날아가지 않게).
        // 🔴 단 초대형 오브(LifetimeOverride > 0)에서는 이것이 **주 소멸 조건**이다 — 관통이 무한이라
        //    예산이 바닥나는 일이 없어서, 여기서 끊지 않으면 화면 끝까지 영원히 간다.
        float life = LifetimeOverride > 0f ? LifetimeOverride : lifetime;
        Destroy(gameObject, life * MetaBonuses.DurationMult);
        nextPullTime = Time.time + PullInterval;
    }

    private void Update()
    {
        transform.Translate(Vector2.left * moveSpeed * SpeedMultiplier * Time.deltaTime);
        TickPull();

        // 풀링된 적은 죽어도 null이 되지 않는다 — IsAlive로 걸러야 반납된 적을 계속 붙잡고 있지 않는다.
        overlappingEnemies.RemoveWhere(e => e == null || !e.IsAlive);

        // 겹쳐 있는 적을 전부 갈아버리지 않고 **가까운 순으로** 예산이 닿는 만큼만 붙잡는다.
        // 예산은 "처음 만난 적"에만 소모되고, 한 번 붙잡은 적은 죽을 때까지 계속 간다.
        // 레벨업으로 이 예산이 올라가는 게 오브의 주 성장축(BalanceConstants.OrbBaseTargets부터 시작해 만렙까지 오른다).
        ordered.Clear();
        Vector2 self = transform.position;
        foreach (Enemy e in overlappingEnemies)
            if (e != null) ordered.Add((((Vector2)e.transform.position - self).sqrMagnitude, e));
        ordered.Sort(ByDistance);

        // 틱 중 처치로 overlappingEnemies가 바뀌므로 이 복사본을 돈다(기존과 같다 — 리스트만 재사용한다).
        for (int i = 0; i < ordered.Count; i++)
        {
            Enemy enemy = ordered[i].enemy;
            if (enemy == null || !enemy.IsAlive || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;

            if (!claimed.Contains(enemy))
            {
                if (budgetRemaining <= 0) continue; // 예산 소진 — 새 적은 더 못 잡는다(이미 잡은 적은 아래로 계속 진행)
                claimed.Add(enemy);
                budgetRemaining--;
            }

            nextTickTime[enemy] = Time.time + tickInterval;

            float baseDamage = Damage;
            enemy.TakeSkillHit(baseDamage, CritChance, ActiveSkillId.Orb);
            if (SlowsEnemies)
                enemy.ApplySlow(Mathf.Clamp01(slowMultiplier - SlowMultiplierBonus), slowDuration + SlowDurationBonus);
            if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

            if (impactVfxPrefab != null)
            {
                GameObject vfx = ObjectPool.Instance.Spawn(impactVfxPrefab, enemy.transform.position, Quaternion.identity);
                if (Time.time < nextImpactSfxTime)
                {
                    foreach (AudioSource src in vfx.GetComponentsInChildren<AudioSource>()) src.Stop();
                }
                else
                {
                    nextImpactSfxTime = Time.time + ImpactSfxCooldown;
                }
                ObjectPool.Instance.Despawn(vfx, 2.2f);
            }
        }

        // 예산을 다 쓰고 붙잡고 있던 적이 전부 정리되면(죽었거나 오브가 지나쳤거나) 임무 완료 — 그 자리에서 사라진다.
        // 여기서 IsAlive를 빠뜨리면 붙잡은 적이 죽어도 claimed가 안 비어서 **오브가 영영 안 사라진다**.
        claimed.RemoveWhere(e => e == null || !e.IsAlive);
        if (budgetRemaining <= 0 && claimed.Count == 0)
            Destroy(gameObject);
    }

    // 주기적으로 주위 적을 오브 쪽으로 끌어당긴다(초대형 오브). PullInterval이 0이면 아무 일도 안 한다.
    // ⚠️ 겹쳐 있는 적만이 아니라 **반경 안의 모든 적**이 대상이다 — 그래야 "빨아들인다"가 된다.
    private void TickPull()
    {
        if (PullInterval <= 0f || Time.time < nextPullTime) return;
        nextPullTime = Time.time + PullInterval;

        float sqrRadius = PullRadius * PullRadius;
        Vector2 self = transform.position;
        foreach (Enemy e in Enemy.Active)   // 끌어당기기만 한다(처치·스폰 없음) — 복사본 불필요
        {
            if (e == null || !e.IsAlive) continue;
            if (((Vector2)e.transform.position - self).sqrMagnitude > sqrRadius) continue;
            e.ApplyPullTowardX(self.x, PullDistance);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed) return;

        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null) return;

        // 방패 블루베리: 오브도 통과하지 못하고 여기서 소멸 — 마지막으로 한 번 타격을 주고 사라진다.
        // 🔴 예외는 PiercesShields — **대형(1차)·초대형(2차) 오브**가 그렇다(2026-09-20 사용자로 1차까지 확대).
        //    관통이 오브의 주 성장축이라 방패 하나로 막히면 진화 자체가 무력화된다.
        if (enemy.BlocksProjectiles && !PiercesShields)
        {
            consumed = true;
            enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Orb);
            if (impactVfxPrefab != null)
                ObjectPool.Instance.SpawnImpactVfx(impactVfxPrefab, enemy.transform.position, ObjectPool.ImpactVfxLifetime);
            Destroy(gameObject);
            return;
        }

        overlappingEnemies.Add(enemy);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            overlappingEnemies.Remove(enemy);
            // 오브가 지나쳐버린 적은 더 이상 "갈고 있는 중"이 아니다 — 예산은 이미 썼으니 돌려주지 않는다.
            claimed.Remove(enemy);
            nextTickTime.Remove(enemy);
        }
    }
}
