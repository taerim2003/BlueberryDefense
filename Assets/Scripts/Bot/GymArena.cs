#if UNITY_EDITOR || BOT_QA
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 스킬 시험장의 무대. 스테이지 표 기반 스폰을 끄고 **시나리오가 선언한 적만** 내보낸다.
//
// 왜 표를 안 쓰나: 시험장은 "이 스킬이 이 상황에서 얼마나 하나"를 재는 자리라 적 세기가 **고정 상수**여야 한다.
// StageTable·AscensionTable을 타면 맵 밸런스를 고칠 때마다 스킬 측정값이 따라 움직여 앞뒤 회차를 비교할 수 없다.
//
// 🔴 적 프리팹은 **로드된 MapDefinition 로스터의 합집합**에서 이름으로 찾는다. 무대 맵 하나의 로스터를 쓰면 안 된다 —
//    비행선은 Map_Wide20에만, 콩콩이는 농장에 없고, 서핑은 우주에 없다. 로스터로 꺼내면 그 시나리오가 조용히 빈 칸이 된다.
public class GymArena
{
    // 한 시나리오 = 고정된 적 구성. 창(window) 동안 total 마리까지 batch씩 interval 간격으로 내보낸다.
    //
    // 🔴 수치는 **포화 방지**가 목적이다(밸런스 값이 아니다). 기준 스킬이 전멸시키거나 전멸당하면
    //    모든 스킬이 같아 보여 아무것도 못 가른다. 교정 절차는 `balance` 스킬의 시험장 절 —
    //    기준 스킬(진화 전 회오리 Lv10)의 처치율이 20~90% 밖이면 total·hpMult를 고친다.
    public class Def
    {
        public string id;
        public string[] enemies;          // 프리팹 이름. batch를 채울 때 순서대로 돌려 쓴다
        public int total;                 // 총 마리 수. **0 = 무제한**(창이 끝날 때까지 계속 내보낸다)
        public int maxAlive;              // 동시 생존 상한. 0 = 없음. 상한에 닿으면 스폰을 멈춘다
        public int batch = 1;             // 한 번에 몇 마리
        public float interval = 2f;       // 스폰 간격(게임초)
        public float hpMult = 1f, speedMult = 1f, damageMult = 1f;
        public float window = 45f;        // 셀 길이(게임초)
        public bool companion;            // Rig 2(동반)에도 쓰나
        public string note;

        // 🔴 **무대가 천장이 되면 안 된다.** 스모크 실측(2026-09-27): swarm에 90마리(총 6480 HP)를 넣었더니
        //    회오리 2차가 90마리 전부를 죽여 effDamage가 6480에 고정됐다 — 더 센 스킬을 넣어도 같은 숫자가 나온다.
        //    진화 전 5046 / 1차 6255 / 2차 6480으로 **파워 ×6 차이가 ×1.28로 눌렸다.**
        //    그래서 무대는 두 종류로 갈린다:
        //      dps        = 창 안에 안 죽는 단일 표적 1마리 → effDamage가 곧 단일 대상 DPS(천장 없음)
        //      throughput = 무제한 스폰 + 생존 상한 → effDamage가 곧 처리량, damageTaken이 곧 못 막은 몫
        //    판정은 `effDamage / spawnedHp`로 포화를 본다 — 1에 가까우면 그 셀은 아무것도 못 가른다.
        public bool Dps => total > 0 && maxAlive == 0;
    }

    // 🔴 **레벨은 시나리오가 안 정한다 — 모든 시나리오를 Lv1·Lv10 둘 다 돈다**(사용자 지시 2026-09-27).
    //    한 스킬의 성장률(Lv10 ÷ Lv1)은 상황마다 다르다: 물량에서만 크게 자라는 스킬과 단일 대상에서만 자라는
    //    스킬을 Lv10만 보면 같아 보인다. 레벨 목록은 BotConfig.gymLevels가 소유한다(기본 1·10).
    public static readonly int[] DefaultLevels = { 1, 10 };

    // 축은 `.claude/skills/balance/SKILL_AXES.md`의 표에서 왔다 — 그 축마다 한 시나리오씩이다.
    // 뒤쪽 mix_*·boss는 단일 종류로는 안 잡히는 것을 잡는다: 섞이면 무엇을 먼저 때리는가(타겟 선정),
    // 방패 뒤에 숨은 적에게 닿는가, 군중제어가 안 듣는 상대에게 무엇이 남는가.
    public static readonly Def[] Table =
    {
        // ── 처리량 무대: 무제한 스폰 + 생존 상한. effDamage = 처리량, damageTaken = 못 막은 몫 ──
        //    스폰 속도는 **가장 센 스킬의 처리량보다 빠르게** 잡는다(안 그러면 다시 무대가 천장이 된다).
        //    실측 기준: 회오리 2차가 45초에 6480 HP = 144 HP/초를 처리했다 → 최소 그 두 배를 내보낸다.
        //    🔴 스폰 속도를 **실측한 최강 처리량의 3배 이상**으로 잡는다. 스모크2(2026-09-27)에서 회오리 2차 Lv10이
        //       45초에 16,403 HP = 365 HP/초를 처리했다. 그보다 느리게 내보내면 강한 스킬이 무대를 비워
        //       effDamage가 **스폰 속도**를 재게 된다(포화 0.91~1.00으로 실제로 그랬다).
        //       판별력의 조건은 `aliveAtEnd == maxAlive` — 강한 스킬도 생존 상한에 눌려 있어야 한다.
        //    🔴 그리고 **체력 배율**로 공급량을 맞춘다. 마리 수만 늘리면 동시 생존 상한과 프레임 비용이 같이 터진다 —
        //       공급 HP/초 = (batch ÷ interval) × 기본체력 × hpMult 가 최강 스킬의 3배 이상이 되게 잡는다.
        //       (실측 최강: 회오리 2차 Lv10이 tank에서 1,806 HP/초. 목표 공급량 약 6,000 HP/초.)
        new Def { id = "swarm", enemies = new[] { "Enemy_Blueberry" }, maxAlive = 90, batch = 6, interval = 0.4f,
                  hpMult = 30f, window = 45f, companion = true, note = "무리 딜 · 오버킬 · 초반 파워" },
        new Def { id = "air_slow", enemies = new[] { "Enemy_PaperPlaneBlueberry" }, maxAlive = 70, batch = 6, interval = 0.4f,
                  hpMult = 50f, window = 45f, note = "대공 — 느린 비행" },
        new Def { id = "carrier", enemies = new[] { "Enemy_UfoBlueberry" }, maxAlive = 14, batch = 2, interval = 1.2f,
                  hpMult = 8f, window = 50f, note = "투하 전 격추 — escapes가 놓친 몫이다(처리량 무대가 아니다)" },
        new Def { id = "shield", enemies = new[] { "Enemy_ShieldBlueberry" }, maxAlive = 45, batch = 4, interval = 0.6f,
                  hpMult = 20f, window = 45f, note = "투사체 차단 관통" },
        new Def { id = "hopper", enemies = new[] { "Enemy_HopperBlueberry" }, maxAlive = 50, batch = 4, interval = 0.6f,
                  hpMult = 25f, window = 45f, note = "점프 중 지상 판정 회피" },
        new Def { id = "rider", enemies = new[] { "Enemy_RiderBlueberry" }, maxAlive = 50, batch = 4, interval = 0.55f,
                  hpMult = 30f, window = 45f, note = "고속 돌진 · 짧은 반응 시간" },
        new Def { id = "elite", enemies = new[] { "Enemy_RegentBlueberry" }, maxAlive = 55, batch = 5, interval = 0.6f,
                  hpMult = 25f, window = 45f, note = "중간 체력 물량" },
        new Def { id = "late", enemies = new[] { "Enemy_Blueberry", "Enemy_ShieldBlueberry" }, maxAlive = 45, batch = 4,
                  interval = 0.8f, hpMult = 100f, window = 45f, note = "후반 스케일 대응 — 최대체력 비례 효과" },

        // ── 혼합 구성: 실제 판에 가까운 상황 ──
        new Def { id = "mix_ground", enemies = new[] { "Enemy_Blueberry", "Enemy_RiderBlueberry", "Enemy_HopperBlueberry" },
                  maxAlive = 65, batch = 5, interval = 0.5f, hpMult = 25f, window = 45f, companion = true,
                  note = "지상 혼합 — 속도가 다른 적이 섞일 때 타겟 선정" },
        new Def { id = "mix_air", enemies = new[] { "Enemy_PaperPlaneBlueberry", "Enemy_UfoBlueberry" },
                  maxAlive = 45, batch = 4, interval = 0.7f, hpMult = 50f, window = 45f,
                  note = "공중 혼합 — 빠른 비행 + 캐리어" },
        new Def { id = "mix_wall", enemies = new[] { "Enemy_ShieldBlueberry", "Enemy_RegentBlueberry", "Enemy_Blueberry" },
                  maxAlive = 55, batch = 5, interval = 0.6f, hpMult = 25f, window = 45f, companion = true,
                  note = "벽 흉내 — 방패 뒤의 적에게 닿는가" },

        // ── DPS 무대: 창 안에 안 죽는 표적 1마리. effDamage가 곧 단일 대상 DPS(천장 없음) ──
        //    🔴 체력을 일부러 도달 불가로 크게 둔다 — 죽으면 그 순간부터 딜이 0이 되어 강한 스킬이 오히려 손해를 본다.
        new Def { id = "tank", enemies = new[] { "Enemy_RegentBlueberry" }, total = 1, batch = 1, interval = 1f,
                  hpMult = 4000f, window = 45f, companion = true, note = "단일 대상 딜(지상)" },
        new Def { id = "airship", enemies = new[] { "Enemy_Airship" }, total = 1, batch = 1, interval = 1f,
                  hpMult = 500f, window = 45f, companion = true, note = "단일 대상 딜(공중) — 비행선" },
        new Def { id = "boss", enemies = new[] { "Enemy_BossBlueberry" }, total = 1, batch = 1, interval = 1f,
                  hpMult = 400f, window = 45f, note = "군중제어 저항 표적 — CC 의존 스킬이 드러난다" },
    };

    public static Def Find(string id) => Table.FirstOrDefault(d => d.id == id);

    // 🔴 **무대는 진화 차수마다 세기가 다르다.** 한 무대로 전 범위를 담을 수 없다 —
    //    실측(2026-09-27): 진화 전 독수리 Lv1이 73 dps, 2차 회오리 Lv10이 5,253 dps로 **250배** 차이다.
    //    한쪽에 맞추면 다른 쪽은 무대가 천장이 되거나(강한 쪽) 아무것도 못 죽인다(약한 쪽).
    //    사용자 요구가 **그룹 안에서의 비교**(기본끼리·1차끼리·2차끼리)이므로 그룹마다 배율을 준다.
    //    ⚠️ 그래서 **처리량 무대의 숫자는 그룹을 넘어 비교하면 안 된다.** 차수 간 비교는
    //       천장이 없는 dps 무대(tank·airship·boss)와 생애 사슬로 한다.
    public static float GroupHpScale(int evoStage) => evoStage >= 2 ? 9f : evoStage == 1 ? 3f : 1f;

    // ── 무대 ──
    private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
    private EnemySpawner spawner;
    private Vector3 spawnPoint;
    private Def cell;
    private float timer;
    private int cycle;

    public int Spawned { get; private set; }
    public float SpawnedHp { get; private set; }   // 내보낸 적의 최대체력 합 — 포화 판정(effDamage ÷ SpawnedHp)의 분모
    public List<string> MissingPrefabs { get; } = new List<string>();

    // 생존 수를 매 프레임 FindObjectsByType으로 세면 무대가 측정을 방해한다(프레임 비용이 곧 렉이다).
    // 내보낸 적만 들고 있다가 비활성(= 풀 반납 = 죽거나 도망)된 것을 걷어낸다.
    private readonly List<Enemy> live = new List<Enemy>();

    // 판에 한 번. 표 기반 스폰을 끄고 스폰 지점을 기억한다.
    // 🔴 spawner.enabled=false면 SpawnedThisStage가 안 늘어 StageSpawnComplete가 영원히 false다 →
    //    GameManager가 스테이지를 넘기지 않는다. 그래서 창 길이를 시험장이 온전히 소유한다.
    public bool TakeOver()
    {
        spawner = Object.FindAnyObjectByType<EnemySpawner>();
        if (spawner == null) return false;
        spawner.enabled = false;
        spawnPoint = spawner.transform.position;
        BuildPrefabIndex();
        AuditPrefabs();
        // 🔴 스냅샷 **전에** 풀을 만들어 둔다 — ObjectPool.Instance는 지연 생성이라, 첫 셀 도중에 생기면
        //    스냅샷에 없어서 무대 청소가 풀 루트를 지운다(그러면 그 뒤 모든 스폰이 깨진다).
        _ = ObjectPool.Instance;
        SnapshotStage();
        return prefabs.Count > 0;
    }

    // 무대에 **원래 있던** 오브젝트를 기억한다. 셀 사이에 치울 것 = "이 목록에 없는 것"이다.
    // 🔴 타입 목록으로 치우면 새 스킬이 생길 때마다 낡는다(실측: 스킬 이펙트 14종이 풀을 안 쓰고 Instantiate를 쓴다).
    //    스냅샷 기준은 타입에 무관하고, 플레이어·카메라·캔버스·풀 같은 인프라는 스냅샷에 들어 있어 안 건드린다.
    private readonly HashSet<int> stageObjects = new HashSet<int>();

    private void SnapshotStage()
    {
        stageObjects.Clear();
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (go != null) stageObjects.Add(go.transform.root.gameObject.GetInstanceID());
    }

    // 로드된 MapDefinition 전부의 로스터를 이름으로 합친다(에디터·빌드 공통).
    // 우주 전용 변종(*_Space)은 이름이 달라 따로 잡힌다.
    private void BuildPrefabIndex()
    {
        foreach (MapDefinition m in Resources.FindObjectsOfTypeAll<MapDefinition>())
        {
            if (m == null) continue;
            Add(m.enemyPrefab); Add(m.treasureEnemyPrefab); Add(m.eliteEnemyPrefab);
            Add(m.paperPlaneEnemyPrefab); Add(m.ufoEnemyPrefab); Add(m.shieldEnemyPrefab);
            Add(m.riderEnemyPrefab); Add(m.hopperEnemyPrefab); Add(m.surferEnemyPrefab);
            Add(m.airshipEnemyPrefab); Add(m.bossEnemyPrefab);
            Add(m.bossEnemyPrefabEasy); Add(m.bossEnemyPrefabHard);
        }
    }

    private void Add(GameObject go)
    {
        if (go != null && !prefabs.ContainsKey(go.name)) prefabs[go.name] = go;
    }

    // 표의 모든 시나리오가 프리팹을 찾을 수 있는지 미리 본다 — 못 찾으면 그 시나리오는 **조용히 빈 칸**이 된다.
    public void AuditPrefabs()
    {
        MissingPrefabs.Clear();
        foreach (Def d in Table)
            foreach (string n in d.enemies)
                if (!prefabs.ContainsKey(n) && !MissingPrefabs.Contains(n)) MissingPrefabs.Add(n);
    }

    // hpScale = 진화 차수별 무대 배율(GroupHpScale). 실제로 적에게 곱하는 값은 def.hpMult × hpScale이다.
    public float EffectiveHpMult { get; private set; }

    public void Begin(Def def, float hpScale)
    {
        cell = def;
        EffectiveHpMult = def.hpMult * Mathf.Max(0.01f, hpScale);
        timer = 0f;     // 첫 무리는 셀 시작 즉시
        cycle = 0;
        Spawned = 0;
        SpawnedHp = 0f;
        live.Clear();
    }

    // 전투 중 매 프레임. dt는 게임 시간.
    public void Tick(float dt)
    {
        if (cell == null) return;
        Prune();
        if (cell.total > 0 && Spawned >= cell.total) return;      // total 0 = 무제한
        if (cell.maxAlive > 0 && live.Count >= cell.maxAlive) return;

        timer -= dt;
        if (timer > 0f) return;
        timer += cell.interval;

        int n = cell.batch;
        if (cell.total > 0) n = Mathf.Min(n, cell.total - Spawned);
        if (cell.maxAlive > 0) n = Mathf.Min(n, cell.maxAlive - live.Count);
        for (int i = 0; i < n; i++)
        {
            string name = cell.enemies[cycle++ % cell.enemies.Length];
            if (!prefabs.TryGetValue(name, out GameObject prefab)) continue;
            // 스포너 정문과 같은 두 줄(EnemySpawner.SpawnEnemies) — 겹치지 않게 뒤로 벌린다.
            Enemy e = Enemy.Spawn(prefab, spawnPoint + Vector3.left * (1.2f * i));
            if (e == null) continue;
            e.ApplyStageMultipliers(EffectiveHpMult, cell.speedMult, cell.damageMult);
            live.Add(e);
            Spawned++;
            SpawnedHp += BaseHp(prefab) * EffectiveHpMult;
        }
    }

    // 프리팹의 기본 체력. Enemy.definition은 private이고 런타임 maxHealth에 접근자가 없어 SO에서 읽는다(프리팹당 1회 캐시).
    // 포화 판정의 분모라서 필요하다 — effDamage가 SpawnedHp에 붙으면 그 셀은 무대가 천장인 것이다.
    private readonly Dictionary<GameObject, float> baseHp = new Dictionary<GameObject, float>();

    private float BaseHp(GameObject prefab)
    {
        if (baseHp.TryGetValue(prefab, out float hp)) return hp;
        var e = prefab.GetComponent<Enemy>();
        var fi = typeof(Enemy).GetField("definition",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var def = fi != null && e != null ? fi.GetValue(e) as EnemyDefinition : null;
        hp = def != null ? def.maxHealth : 0f;
        baseHp[prefab] = hp;
        return hp;
    }

    // 죽거나 도망간 적은 풀로 반납돼 비활성이 된다 — 그걸로 생존 여부를 가른다.
    private void Prune()
    {
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] == null || !live[i].gameObject.activeSelf) live.RemoveAt(i);
    }

    public int Alive { get { Prune(); return live.Count; } }

    // 셀 사이 무대 비우기 — 적뿐 아니라 **플레이어가 깔아 둔 지속 오브젝트까지** 치운다.
    //
    // 🔴 적만 치우면 앞 셀이 다음 셀을 오염시킨다. 실측(2026-09-27 스모크2): 회오리 2차 셀 **다음** 독수리 셀에서
    //    `Whirlwind eff=3487 · 처치 48 · casts=0 · owned=false`가 찍혔다 — 로드아웃에선 지웠는데 **회오리 오브젝트가
    //    살아남아** 새 셀의 적을 갈았다. 96셀 중 1셀. 정착 1.5초로는 지속시간이 긴 지속기를 못 넘긴다.
    //
    // 🔴 ObjectPool.Despawn에는 **중복 반납 가드가 없다** — 지연 반납(delay > 0)이 걸린 오브젝트를 여기서 또 반납하면
    //    대기열이 같은 오브젝트를 풀에 두 번 넣어 한 오브젝트가 두 번 스폰된다. 그래서 대기열을 읽어 **건너뛴다.**
    //    (전수 조사 2026-09-27: 지연 반납을 쓰는 것은 전부 VFX이고, 딜을 주는 지속 오브젝트 — 회오리·오브·투사체·
    //     미사일·포도·충격파·연쇄번개 — 는 모두 delay 0으로 스스로 반납한다. 그래서 대기열만 피하면 안전하다.)
    public void Clear()
    {
        cell = null;
        live.Clear();

        // ⓪ 🔴 **돌고 있는 스킬 코루틴을 먼저 멈춘다.** 셀 사이에 남는 것은 오브젝트가 아니라 **오브젝트를 계속
        //    만들어내는 코루틴**이다 — 오브 R1의 `SpawnHomingSmallOrbsRoutine`, 독수리의 비, 화살비, 회오리 발생기,
        //    산탄 연사가 모두 시간에 걸쳐 뿌린다. `PlayerSkills.Sealed`는 새 시전만 막고 **이미 도는 코루틴은 못 막는다.**
        //    그래서 아래에서 다 치워도 다음 셀에 새로 태어난다(실측 2026-09-27: 오브 R1 18칸에 `SmallOrb_Skill`·`Juggler_Orb`가 남았다).
        //    전수 확인: PlayerSkills의 StartCoroutine은 **전부 시전 시점**이고 Awake·Start에서 시작하는 것이 없다 →
        //    다 멈춰도 잃는 상태가 없다. (Awake에서 시작하는 코루틴이 생기면 이 줄이 그걸 죽인다 — 그때 화이트리스트로 바꿀 것.)
        var ps = Object.FindAnyObjectByType<PlayerSkills>();
        if (ps != null) ps.StopAllCoroutines();

        HashSet<GameObject> pending = PendingDespawns();

        // ① 풀에서 나온 것 — 반납한다(대기열에 있는 것은 건너뛴다).
        foreach (PooledInstance p in Object.FindObjectsByType<PooledInstance>(FindObjectsSortMode.None))
        {
            if (p == null || !p.gameObject.activeSelf) continue;
            if (pending.Contains(p.gameObject)) continue;   // 대기열이 이미 들고 있다 — 두 번 넣지 않는다
            ObjectPool.Instance.Despawn(p.gameObject);
        }

        // ② 풀을 안 쓰는 스킬 이펙트 — 스냅샷에 없고 **Collider2D가 있는** 런타임 루트를 지운다.
        //    적을 때리는 지속 오브젝트는 전부 충돌로 맞히므로 콜라이더가 있다(회오리는 `[RequireComponent(typeof(Collider2D))]`).
        //    🔴 "스냅샷에 없는 것을 전부"로 넓혔다가 **세션을 죽였다**(실측 2026-09-27):
        //       `SfxPlayer`가 **지연 생성**하는 AudioSource 오브젝트를 같이 지워서, 그 뒤 모든 스킬 시전이
        //       `MissingReferenceException`을 던지고 판이 멈췄다. 지연 생성 인프라가 있으므로 조건 없는 파괴는 금지다.
        foreach (Collider2D col in Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (col == null) continue;
            GameObject root = col.transform.root.gameObject;
            if (stageObjects.Contains(root.GetInstanceID())) continue;   // 원래 무대에 있던 것
            if (root.GetComponent<PooledInstance>() != null) continue;   // ①이 처리했다
            Object.Destroy(root);
        }

        // ②-B 콜라이더가 없어도 **프로젝트 스크립트를 들고 있는** 런타임 루트는 스킬 이펙트다 — 지운다.
        //    🔴 기준을 "프로젝트 MonoBehaviour 보유"로 잡은 이유: 지연 생성되는 인프라 중 유일하게 위험했던
        //       `SfxPlayer`의 오브젝트는 **AudioSource만** 달려 있다(스크립트가 없다). 그래서 이 기준이 둘을 가른다.
        //       ("스냅샷에 없는 것 전부"로 넓히면 그 오브젝트까지 지워져 세션이 죽는다 — 실측으로 한 번 죽였다.)
        //    ObjectPool은 TakeOver에서 미리 만들어 스냅샷에 넣어 두므로 여기 안 걸린다.
        foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            GameObject root = mb.transform.root.gameObject;
            if (stageObjects.Contains(root.GetInstanceID())) continue;
            if (root.GetComponent<PooledInstance>() != null) continue;   // ①이 처리했다
            if (root.GetComponent<Canvas>() != null) continue;           // 지연 생성 UI
            if (root.GetComponent<BotPilot>() != null) continue;         // 봇 자신
            if (root.GetComponent<ObjectPool>() != null) continue;       // 풀 루트(지연 생성)
            if (root.GetComponent<QAMute>() != null) continue;           // 음소거 유지 오브젝트
            Object.Destroy(root);
        }

        // ③ 콜라이더 없는 **발생기** — 스스로는 안 때리지만 계속 때리는 것을 뿜는다. ②가 못 잡는다.
        //    실측(2026-09-27): 회오리 R0 2차의 `tornadoMaker`가 살아남아 다음 셀에서 딜 18,627 · 처치 42를 냈다.
        //    🔴 캐시 필드를 비울 필요는 없다 — Unity는 파괴된 오브젝트에 `== null`이 참이라
        //       `if (tornadoMaker == null) tornadoMaker = Instantiate(...)`가 다음 셀에 알아서 다시 만든다.
        DestroyCachedEmitters();
    }

    // 발생기 = 스스로는 안 때리지만 때리는 것을 뿜는 것. 둘로 나뉜다:
    //  ① PlayerSkills가 **필드에 캐시**하는 것 — 이름이 계약이다.
    //  ② 캐시 없이 Instantiate만 되는 것 — 컴포넌트 **타입**이 계약이다(콜라이더가 없어 ②의 스윕에 안 걸린다).
    // 🔴 목록은 낡는다. 그래서 **`Residual()`이 감시자**다 — 셀 시작에 남의 것이 하나라도 있으면
    //    `residualAtStart`에 찍혀 분석기가 그 칸을 걸러낸다. 새 발생기가 생기면 그 숫자가 먼저 말해 준다.
    //    (실측 2026-09-27: 당시 있던 `OrbAltar`가 이 방식으로 잡혔다 — 오브 R1 1차 셀 18칸에서 잔존 1.
    //     같은 스킬끼리의 오염이라 foreignDamage 가드에는 안 걸렸다. 오브 제단은 2026-09-29에 코드째 지워져 ②가 비었다.)
    private static readonly string[] EmitterFields = { "tornadoMaker", "skyShredderShip" };
    private static readonly string[] EmitterTypes = { };
    public List<string> MissingEmitterFields { get; } = new List<string>();

    private void DestroyCachedEmitters()
    {
        var ps = Object.FindAnyObjectByType<PlayerSkills>();
        if (ps != null)
            foreach (string name in EmitterFields)
            {
                var fi = typeof(PlayerSkills).GetField(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (fi == null)
                {
                    if (!MissingEmitterFields.Contains(name)) MissingEmitterFields.Add(name);
                    continue;
                }
                if (fi.GetValue(ps) is GameObject go && go != null) Object.Destroy(go);
            }

        foreach (string typeName in EmitterTypes)
        {
            System.Type t = System.Type.GetType(typeName + ", Assembly-CSharp");
            if (t == null)
            {
                if (!MissingEmitterFields.Contains(typeName)) MissingEmitterFields.Add(typeName);
                continue;
            }
            foreach (Object o in Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (o is Component c && c != null && !stageObjects.Contains(c.transform.root.gameObject.GetInstanceID()))
                    Object.Destroy(c.transform.root.gameObject);
            }
        }
    }

    // 무대에 남아 있는 **남의 것**. Clear가 실제로 먹혔는지 측정 시작 직전에 세어 기록한다 —
    // 0이 아니면 그 셀은 앞 셀에 오염됐다. 🔴 **이름을 같이 남긴다** — 개수만으로는 무엇을 치워야 하는지 알 수 없고,
    //    타입 목록을 추측으로 늘리게 된다(실측 2026-09-27: 개수만 찍다가 오브·호밍·독수리를 세 번에 나눠 뒤쫓았다).
    public List<string> ResidualNames { get; } = new List<string>();

    public int Residual()
    {
        ResidualNames.Clear();
        foreach (Collider2D col in Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (col == null) continue;
            GameObject root = col.transform.root.gameObject;
            if (stageObjects.Contains(root.GetInstanceID())) continue;
            if (live.Contains(root.GetComponent<Enemy>())) continue;   // 이번 셀이 내보낸 적
            string name = root.name.Replace("(Clone)", "");
            if (!ResidualNames.Contains(name)) ResidualNames.Add(name);
        }
        return ResidualNames.Count;
    }

    private static HashSet<GameObject> PendingDespawns()
    {
        var set = new HashSet<GameObject>();
        try
        {
            var fi = typeof(ObjectPool).GetField("pendingDespawns",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fi == null || ObjectPool.Instance == null) return set;
            if (fi.GetValue(ObjectPool.Instance) is System.Collections.IEnumerable list)
                foreach (object item in list)
                {
                    var of = item.GetType().GetField("obj");
                    if (of?.GetValue(item) is GameObject go) set.Add(go);
                }
        }
        catch (System.Exception) { /* 못 읽으면 빈 집합 — 대기열 항목을 건너뛰지 못하니 아래 검증으로 잡는다 */ }
        return set;
    }
}
#endif
