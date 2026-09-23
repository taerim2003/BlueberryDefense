using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Whirlwind : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float damagingMoveSpeedMultiplier = 0.4f;
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float tickInterval = 0.3f;
    // 레벨업으로 이 배율이 내려가면 더 자주 갈아버린다(회오리 성장의 보조축). 1 = 프리팹 기본 주기.
    public float TickIntervalMult { get; set; } = 1f;
    // 이동 속도 배율. 회오리 R1 2차 「하늘의 울음」이 1차 대회오리보다 빠르게 움직이라고 올린다(2026-09-19 사용자).
    // 적을 갈고 있을 때 느려지는 배율(damagingMoveSpeedMultiplier)에는 곱해지고 나서 적용된다.
    public float SpeedMultiplier { get; set; } = 1f;
    [SerializeField] private GameObject impactVfxPrefab;
    [SerializeField] private float groundY = 0f; // 공중(비행 적 처치 지점 등)에서 생성돼도 이 높이까지 자연스럽게 낙하
    [SerializeField] private float gravity = 25f;

    private float fallVelocity;

    // 스프라이트 피벗이 중앙이라 groundY는 "중심 높이"다. 크기가 작은 미니 회오리는 같은 groundY에서
    // 바닥선이 위로 뜨므로, 소환하는 쪽에서 큰 회오리 바닥선에 맞춘 값을 넣어준다.
    public float GroundY { set => groundY = value; }

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 틱마다 개별적으로 치명타를 굴린다
    public int MaxHitCount { get; set; } // 0보다 크면 이만큼 때렸을 때 소멸. 수명은 그와 별개로 늘 걸린다(Start)
    public float SlowDuration { get; set; } = 3f;
    public float ExtraLifetime { get; set; } // 레벨업 고유 강화: 지속시간(초) 추가
    // 0보다 크면 프리팹의 lifetime 대신 이 값을 쓴다 — `Prog_Whirlwind.baseDuration`이 넘겨 준다.
    public float BaseLifetimeOverride { get; set; }
    // 켜면 표적을 **x·y 둘 다** 쫓는다(중력 없음) — 회오리 R0 「분열 회오리」 본체가 하늘 높이 뜬 비행선까지 따라가게(사용자 결정 2026-09-18).
    // 표적이 없으면 원래대로 땅으로 내려와 좌우로만 움직인다. 미니 회오리는 끈 채로 태어나 소멸 자리에서 떨어진다.
    public bool HomeInY { get; set; }

    // 회오리 R0(화살 연계): 이 회오리가 사라질 때 그 자리(소멸 시점 위치)를 알려준다 — PlayerSkills가 미니를 남긴다.
    // ⚠️ 씬 언로드·게임 종료로 파괴될 때는 부르지 않는다. 안 그러면 판이 끝나는 순간 미니가 우수수 생긴다.
    public System.Action<Vector3> OnExpired;

    private static bool quitting;
    private void OnApplicationQuit() => quitting = true;

    private void OnDestroy()
    {
        if (quitting || !gameObject.scene.isLoaded) return;
        OnExpired?.Invoke(transform.position);
    }

    private int hitCount;
    // 표적 재탐색 주기. 예전엔 회오리마다 **매 프레임** 전체 적을 훑었다 — 미니 회오리는 본체 소멸마다
    // 2~3개씩 늘고 개수 상한이 없어서(위 Start 주석의 463개 실측) 회오리 수 × 적 수가 그대로 프레임 비용이 됐다.
    // 지터를 넣지 않는 건 결정성 때문이다 — 봇 재측정에서 같은 seed의 결과가 그대로여야 최적화가 밸런스 중립임을 확인할 수 있다.
    private const float RetargetInterval = 0.1f;
    private float nextRetargetTime;
    private Enemy cachedTarget;
    private readonly HashSet<Enemy> overlappingEnemies = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTickTime = new Dictionary<Enemy, float>();
    private readonly List<Enemy> tickBuffer = new List<Enemy>();

    private void Start()
    {
        float baseLife = BaseLifetimeOverride > 0f ? BaseLifetimeOverride : lifetime;
        // 🔴 타격 횟수로 사라지는 회오리(미니)에도 수명을 건다(사용자 결정 2026-09-18). 예전엔 MaxHitCount > 0이면
        //    수명이 없어 적이 없을 때 왼쪽으로 끝없이 걸어가 쌓였다(봇 실측: 적 0에 미니 463개, x=-295, 4fps).
        Destroy(gameObject, (baseLife + ExtraLifetime) * MetaBonuses.DurationMult);
    }

    private void Update()
    {
        // 풀링된 적은 죽어도 null이 되지 않는다 — IsAlive로 걸러야 반납된 적을 계속 때리지 않는다.
        overlappingEnemies.RemoveWhere(e => e == null || !e.IsAlive);

        Enemy target = FindTarget();
        Vector2 direction;
        if (HomeInY && target != null)
        {
            fallVelocity = 0f;
            Vector2 to = (Vector2)target.transform.position - (Vector2)transform.position;
            direction = to.sqrMagnitude > 0.0001f ? to.normalized : Vector2.zero;
        }
        else
        {
            ApplyGravity();
            direction = target == null ? Vector2.left
                : target.transform.position.x >= transform.position.x ? Vector2.right : Vector2.left;
        }
        float speed = (overlappingEnemies.Count > 0 ? moveSpeed * damagingMoveSpeedMultiplier : moveSpeed) * SpeedMultiplier;
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
        if (HomeInY) ClampBelowScreenTop();

        // 틱 중 처치·소멸로 overlappingEnemies가 바뀌므로 복사본을 돈다 — 매 프레임 새 리스트를 만들지 않고 버퍼를 재사용한다.
        tickBuffer.Clear();
        tickBuffer.AddRange(overlappingEnemies);
        foreach (Enemy enemy in tickBuffer)
        {
            if (enemy == null || !enemy.IsAlive || Time.time < nextTickTime.GetValueOrDefault(enemy, 0f)) continue;
            nextTickTime[enemy] = Time.time + tickInterval * TickIntervalMult;

            enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.Whirlwind);
            if (ApplyGemSlow) enemy.ApplySlow(0.3f, SlowDuration);
            if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);

            if (impactVfxPrefab != null)
                ObjectPool.Instance.SpawnImpactVfx(impactVfxPrefab, enemy.transform.position, ObjectPool.ImpactVfxLifetime);

            if (MaxHitCount > 0 && ++hitCount >= MaxHitCount)
            {
                Destroy(gameObject);
                return;
            }
        }
    }

    private void ApplyGravity()
    {
        if (transform.position.y <= groundY)
        {
            fallVelocity = 0f;
            return;
        }

        fallVelocity += gravity * Time.deltaTime;
        float newY = Mathf.Max(groundY, transform.position.y - fallVelocity * Time.deltaTime);
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    // 공중 추적 회오리가 화면 위로 잘리지 않게: 그림 윗변이 카메라 윗변 아래(여백 ScreenTopMargin)에 머문다(사용자 결정 2026-09-18).
    // 그보다 높은 표적은 바로 밑에서 기다리며 판정 상자가 닿는 만큼만 때린다.
    private const float ScreenTopMargin = 0.3f;
    private SpriteRenderer spriteRenderer;
    // Camera.main은 태그 검색이라 회오리마다 매 프레임 부를 게 못 된다. 씬이 바뀌어 카메라가 파괴되면
    // Unity의 가짜 null 판정에 걸려 저절로 다시 찾는다.
    private static Camera mainCam;

    private void ClampBelowScreenTop()
    {
        if (mainCam == null) mainCam = Camera.main;
        Camera cam = mainCam;
        if (cam == null) return;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        float halfHeight = spriteRenderer != null ? spriteRenderer.bounds.extents.y : 0.75f * transform.localScale.y;
        float maxY = cam.transform.position.y + cam.orthographicSize - halfHeight - ScreenTopMargin;
        if (transform.position.y > maxY) transform.position = new Vector3(transform.position.x, maxY, transform.position.z);
    }

    // 회오리마다 부르므로 FindObjectsByType 대신 활성 목록을 읽는다(Enemy.Active 주석).
    // 표적이 아직 살아 있으면 주기가 올 때까지 그대로 쓴다 — 갈아타기가 최대 RetargetInterval만큼 늦어지지만,
    // 회오리 이동 속도에 비하면 0.1초는 눈에 띄지 않는다. 표적이 죽으면 주기와 무관하게 즉시 다시 찾는다.
    private Enemy FindTarget()
    {
        if (cachedTarget != null && cachedTarget.IsAlive && Time.time < nextRetargetTime)
            return cachedTarget;

        nextRetargetTime = Time.time + RetargetInterval;

        Enemy target = null;
        float nearestSqrDist = float.MaxValue;
        Vector2 self = transform.position;
        // 인덱스 for로 도는 건 박싱 때문이다 — IReadOnlyList의 foreach는 List<T>.Enumerator를 박싱해 힙에 올린다.
        IReadOnlyList<Enemy> active = Enemy.Active;
        for (int i = 0; i < active.Count; i++)
        {
            Enemy enemy = active[i];
            if (enemy == null || !enemy.IsAlive) continue;
            float sqrDist = ((Vector2)enemy.transform.position - self).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                target = enemy;
            }
        }

        cachedTarget = target;
        return target;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null) overlappingEnemies.Add(enemy);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            overlappingEnemies.Remove(enemy);
            nextTickTime.Remove(enemy);
        }
    }
}
