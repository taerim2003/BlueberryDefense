using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private GameObject impactVfxPrefab;

    public float Damage { get; set; }
    public bool ApplyGemSlow { get; set; }
    public bool ApplyGemVulnerable { get; set; }
    public float CritChance { get; set; } // 타격 기준: 명중할 때마다 개별적으로 치명타를 굴린다
    public float SpeedMultiplier { get; set; } = 1f;
    // 초당 SpeedMultiplier 증가량. 0이면 등속(기본값) — 화살비처럼 "떨어지면서 빨라지는" 투사체만 켠다.
    public float Acceleration { get; set; } = 0f;
    public int PierceRemaining { get; set; }
    public bool CanHitFlying { get; set; } // 기본 path T1: 비행 적 타격 가능
    public System.Action<Enemy, bool> OnHitBonus { get; set; } // (적, 이번 타격의 치명타 여부)

    // ── 유도(암살 사격의 추격 화살 전용) ────────────────────────────────────
    // 기본 화살·화살비는 끄고 쓴다(직선). 켜면 매 프레임 기수를 목표 쪽으로 조금씩 돌리고,
    // 목표가 죽으면 가장 가까운 산 적으로 갈아탄다 — HomingMissile과 같은 장치다.
    public bool Homing { get; set; }
    public Enemy HomingTarget { get; set; }
    public float TurnDegPerSec { get; set; } = 540f;

    private readonly HashSet<Enemy> hitEnemies = new HashSet<Enemy>();

    // 소멸이 확정됐는지. `Destroy(gameObject)`는 프레임 끝에 실행되므로, 같은 물리 스텝에서 이미 잡힌
    // 나머지 충돌 콜백은 그대로 호출된다 → 적이 겹쳐 서 있으면 관통 예산이 0인데도 그 프레임에 닿은
    // 적을 전부 때리게 된다(기본공격이 뭉친 무리를 한 번에 쓸어버리던 원인). 이 플래그로 즉시 끊는다.
    private bool consumed;

    // 발사음은 이 컴포넌트가 아니라 PlayerSkills.FireBasicAttack에서 한 캐스트당 정확히 한 번만 재생한다
    // (Projectile은 캐스트 한 번에 여러 발 생성될 수 있어, 발사체 쪽에 소리를 두면 재생 시점이 GameObject
    // 생성/컴포넌트 초기화 타이밍에 얽혀 불안정해진다).

    // 화면에서 충분히 벗어나면 스스로 사라진다. 이게 없으면 소멸 경로가 **명중뿐**이라
    // 빗나간 화살이 한 판 내내 씬에 쌓인다(화살비는 화면을 덮는 방식이라 대부분 빗나간다).
    // ⚠️ 여백을 좁히지 말 것 — 화살비는 화면 **위 최대 2유닛 바깥**에서 생성되고(PlayerSkills.ArrowRainRoutine)
    //    적은 화면 밖 x=-9에서 걸어 나온다. 여백이 그보다 좁으면 살아 있어야 할 화살이 태어나자마자 지워진다.
    private const float OffscreenMargin = 3f;

    private Vector3 baseScale;
    private SpriteRenderer sr;
    private Sprite baseSprite;

    private void Awake()
    {
        baseScale = transform.localScale;
        sr = GetComponentInChildren<SpriteRenderer>();
        baseSprite = sr != null ? sr.sprite : null;
    }

    // ObjectPool에서 꺼낼 때마다 도는 초기화 — 호출부(PlayerSkills)는 이 뒤에 값을 채운다.
    // ⚠️ 풀 재사용엔 Awake가 다시 안 돈다. 런타임에 바뀌는 필드를 추가하면 여기서도 되돌릴 것.
    //    크기를 되돌려 두므로 호출부의 `localScale *=`가 누적되지 않는다.
    private void OnEnable()
    {
        transform.localScale = baseScale;
        Damage = 0f;
        ApplyGemSlow = false;
        ApplyGemVulnerable = false;
        CritChance = 0f;
        SpeedMultiplier = 1f;
        Acceleration = 0f;
        PierceRemaining = 0;
        CanHitFlying = false;
        OnHitBonus = null;
        Homing = false;
        HomingTarget = null;
        TurnDegPerSec = 540f;
        hitEnemies.Clear();
        consumed = false;
        if (TryGetComponent(out Collider2D col)) col.enabled = true;

        // 진화 화살은 그림을 갈아 끼우고 플립북을 붙인 채 반납된다(PlayerSkills.ApplyEvolvedArrowSprite) —
        // 그대로 두면 화살비·일반 화살이 진화 그림으로 나온다.
        if (sr != null)
        {
            if (sr.TryGetComponent(out SpriteFlipbook fb)) fb.enabled = false;
            sr.sprite = baseSprite;
        }
    }

    private void Update()
    {
        if (Homing) Steer();
        if (Acceleration != 0f) SpeedMultiplier += Acceleration * Time.deltaTime;
        transform.Translate(Vector2.left * moveSpeed * SpeedMultiplier * Time.deltaTime);
        if (IsFarOffscreen(transform.position, -transform.right)) Consume(); // 로컬 left로 날아간다
    }

    private void Steer()
    {
        // ⚠️ null만 보면 안 된다 — 풀링된 적은 죽어도 참조가 살아 있어서 시체를 영영 쫓는다(HomingMissile과 같은 이유).
        if (HomingTarget == null || !HomingTarget.IsAlive)
            HomingTarget = NearestLivingEnemy(transform.position, CanHitFlying, hitEnemies);
        if (HomingTarget == null) return;

        Vector2 desired = (Vector2)HomingTarget.transform.position - (Vector2)transform.position;
        if (desired.sqrMagnitude < 0.0001f) return;

        // 이 컴포넌트는 **로컬 left**로 날아간다 → left가 목표를 향하도록 기수를 돌린다(+180).
        float want = Mathf.Atan2(desired.y, desired.x) * Mathf.Rad2Deg + 180f;
        transform.rotation = Quaternion.Euler(0f, 0f,
            Mathf.MoveTowardsAngle(transform.eulerAngles.z, want, TurnDegPerSec * Time.deltaTime));
    }

    // 가장 가까운 산 적. 못 맞히는 비행 적과 이미 때린 적은 후보에서 뺀다
    // (관통이 0인 추격 화살이 이미 때린 적을 다시 쫓으면 그 자리를 맴돌기만 한다).
    public static Enemy NearestLivingEnemy(Vector3 from, bool canHitFlying, HashSet<Enemy> exclude = null)
    {
        Enemy best = null;
        float bestSqr = float.MaxValue;
        // 인덱스 for로 도는 건 박싱 때문이다 — IReadOnlyList의 foreach는 List<T>.Enumerator를 박싱해 힙에 올린다.
        IReadOnlyList<Enemy> active = Enemy.Active;
        for (int i = 0; i < active.Count; i++)
        {
            Enemy e = active[i];
            if (e == null || !e.IsAlive) continue;
            if (e.RequiresAntiAir && !canHitFlying) continue;
            if (exclude != null && exclude.Contains(e)) continue;
            float d = ((Vector2)e.transform.position - (Vector2)from).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = e; }
        }
        return best;
    }

    // 🔴 **멀어지는 축만** 지운다. 화살비는 캐릭터 앞까지 닿으려고 화면 오른쪽 한참 바깥(맵 배율에 따라 x≈14~18)에서도
    //    생기는데, 그건 화면 **쪽으로 날아오는** 화살이다. 거리만 보면 여백을 아무리 늘려도 맵·화면비에 따라 태어나자마자 지워진다.
    //    날아가는 방향이 늘 한 축 이상은 화면 밖으로 향하므로, 결국 그 축에서 멀어지며 지워진다.
    // Camera.main은 태그 검색이라 투사체마다 매 프레임 부를 게 못 된다. 씬이 바뀌어 카메라가 파괴되면
    // Unity의 가짜 null 판정에 걸려 저절로 다시 찾는다.
    private static Camera mainCam;

    private static bool IsFarOffscreen(Vector3 position, Vector2 heading)
    {
        if (mainCam == null) mainCam = Camera.main;
        Camera cam = mainCam;
        if (cam == null) return false; // 카메라를 못 찾으면 지우지 않는다(멀쩡한 화살을 날리는 것보다 낫다)

        Vector3 d = position - cam.transform.position;
        return (Mathf.Abs(d.y) > cam.orthographicSize + OffscreenMargin && d.y * heading.y >= 0f)
            || (Mathf.Abs(d.x) > cam.orthographicSize * cam.aspect + OffscreenMargin && d.x * heading.x >= 0f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed) return;

        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy == null || (enemy.RequiresAntiAir && !CanHitFlying) || hitEnemies.Contains(enemy)) return;
        hitEnemies.Add(enemy);

        // 기본공격 멀티히트: baseDamage를 N회로 쪼개 각각 크리 개별 판정(총 데미지 유지). 반환=서브히트 중 크리 있었는지
        bool isCrit = enemy.TakeSkillHit(Damage, CritChance, ActiveSkillId.BasicAttack);
        if (ApplyGemSlow) enemy.ApplySlow(0.3f, 3f);
        if (ApplyGemVulnerable) enemy.ApplyVulnerable(1.5f, 3f);
        OnHitBonus?.Invoke(enemy, isCrit);

        if (impactVfxPrefab != null)
            ObjectPool.Instance.SpawnTimed(impactVfxPrefab, transform.position, 2f);

        // 방패 블루베리는 관통을 끊는다 — 남은 관통 횟수와 무관하게 여기서 소멸(뒤에 있는 적은 못 맞힘)
        if (enemy.BlocksProjectiles)
        {
            Consume();
            return;
        }

        if (PierceRemaining > 0)
        {
            PierceRemaining--;
            return;
        }

        Consume();
    }

    // 이번 프레임의 남은 충돌 콜백까지 확실히 차단하고 풀에 반납한다.
    // 플래그만으로 같은 스텝의 콜백은 막히고, 콜라이더를 끄면 다음 스텝까지 안전하다(재사용 시 OnEnable이 되돌린다).
    private void Consume()
    {
        consumed = true;
        if (TryGetComponent(out Collider2D col)) col.enabled = false;
        ObjectPool.Instance.Despawn(gameObject);
    }
}
