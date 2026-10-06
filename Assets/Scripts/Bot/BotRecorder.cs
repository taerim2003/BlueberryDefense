#if UNITY_EDITOR || BOT_QA
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// 봇 한 판의 관측 기록 → `BotRuns/<세션>/runs.jsonl` 한 줄.
// 관측만 한다(게임 상태를 바꾸지 않는다). 입력은 BotInput 훅 + 매 프레임 폴링(Tick).
//
// 기록 단위 정의 (분석기 `Tools/BotPlaytest/analyze.js`가 이 이름을 그대로 읽는다 — 바꾸면 거기도 고칠 것)
//   rawDamage       = 게임이 적용한 피해(오버킬 포함, DamageMeter와 같은 값) — 스킬 판정의 기본 지표(사용자 지시 2026-09-30)
//   pureDamage      = rawDamage에서 외부 강화(패시브·스킬트리 피해 배율, 되감기 강화, 낙뢰 중첩, 치명타, 취약,
//                     비행 적·보스 추가 피해)를 걷어 낸 값 — 스킬 자체의 힘. 진화가 주는 확정 치명 같은 고유 효과도 빠진다.
//   effDamage       = 그 타격 직전 체력을 넘지 않는 몫(오버킬 제외) — "유효 딜"
//   share           = 판 전체 rawDamage 중 이 스킬의 몫. effShare = 같은 것을 effDamage로.
//   contactKills    = 박치기 거리(대기선 + 돌진 거리) 안에서 잡은 수 — "플레이어를 때리던 적을 치웠나"
//   stages[].byEnemy = 그 스테이지에 적 종류별로 받은 피해
//   xpGained        = 판 전체에 적립된 경험치(배율·스테이지 감쇠 적용 후, 반올림 전)
//
//   passives[] — 각 패시브가 "없었다면 잃었을 것". 배열은 전부 `...ByEvoStage`(0=진화 전 · 1=1차, 그 순간의 차수에 귀속).
//     피해는 rawDamage와 같은 기준(오버킬 포함). 서로 겹치는 몫(힘 치명 배율 × 암살 치명 확률 등)은 각 패시브에 다 들어간다 — 합하지 말 것.
//   Strength    damage       = 힘이 없었다면 줄었을 스킬 피해 — 덧셈 배율 풀의 힘 몫(Q 전용 R1·"힘 2배" 강화 포함) + R0이 올린 치명 배율 몫
//   Assassinate damage       = 암살이 없었다면 줄었을 피해 — 암살이 더한 치명 확률(상한 개방 포함) 몫 + R1이 올린 치명 배율 몫
//               critExtra    = 보유 중 치명타가 더한 피해 전체(치명타가 아니었을 때 대비, 암살 몫이 아닌 치명타 포함)
//               bountyXp     = R0 「현상금」 치명 처치 추가 경험치(적립 기준)
//   Health      maxHp        = 건강이 올린 최대체력(획득·레벨업·R0 「강건함」 복리)
//               heal         = 건강 효과로 회복한 체력 — R1 하트 드랍 증가 몫(기댓값) + 트리 "회복템 2배" 몫, 최대체력에 잘린 뒤
//   Defense     blocked      = 피해 감소로 막은 피해(박치기 원래 피해 − 감소 후 피해)
//               damage       = R1 「가시 갑주」 반사 피해
//               autoSwings   = R0 「망치 반격」 자동 휘두르기 발동 수(피해는 휘두르기 스킬 쪽에 섞여 분리 불가)
//               revived      = 트리 "사망 시 1회 부활"을 이 판에 썼나(bool)
//   Knowledge   xp           = 지식이 더 얻게 한 경험치 — 경험치 배율의 지식 몫 + R1 「전투 통찰」 호밍 처치 추가분(적립 기준)
//               treasureKills= 보유 중 처치한 보물 블루베리 수(R0 「보물 탐지」의 추가 등장분과 스테이지 확정분을 못 가른다)
//   Accel       cdSaved      = 가속 쿨감으로 줄어든 재사용 대기(초) — 시전마다 (감소 전 쿨 − 감소 후 쿨)
//               refreshProcs = R0 「리프레쉬」 쿨타임 초기화 발동 수, refreshSaved = 그로 건너뛴 대기(초)
//               hitCutSaved  = R1 「고통 가속」 피격 시 실제로 당겨진 대기(초, 스킬별 남은 쿨에서 잘린 뒤 합)
//               damage       = 트리 "가속: 쿨 4초 이하 스킬 +30%" 피해 몫(가속 보유 조건)
public class BotRecorder
{
    private class SkillAcc
    {
        public float raw, eff, overkill, pure;
        public int kills, contactKills, casts;
        // 진화 차수(0=진화 전 · 1=1차 · 2=2차)별 구간 누적 — 원정(expedition)의 판정 단위.
        // 구간 경계 시각은 evolutions 이벤트(evolve의 gameTime)가 갖는다 — 지속시간은 분석기가 거기서 계산한다.
        public readonly float[] effT = new float[3];
        public readonly float[] rawT = new float[3];
        public readonly float[] pureT = new float[3];
        public readonly int[] castsT = new int[3];
        public readonly int[] killsT = new int[3];
        public float cdLast, cdMin = float.MaxValue, cdMax, cdMultLast = 1f;
        public int acquiredStage = -1;
        public float acquiredTime = -1f;
        public readonly Dictionary<string, float> effByEnemy = new Dictionary<string, float>();
    }

    private class StageAcc
    {
        public int stage;
        public float timeStart;
        public int hpStart;
        public int damageTaken;
        public int kills;
        public readonly Dictionary<string, float> byEnemy = new Dictionary<string, float>();
        public readonly Dictionary<string, float> effBySkill = new Dictionary<string, float>();
        // 실시간 관찰(live.jsonl)용 — 이 층에서 스킬별로 한 일.
        public readonly Dictionary<string, float> rawBySkill = new Dictionary<string, float>();
        public readonly Dictionary<string, float> pureBySkill = new Dictionary<string, float>();
        public readonly Dictionary<string, int> castsBySkill = new Dictionary<string, int>();
        public readonly Dictionary<string, int> killsBySkill = new Dictionary<string, int>();
    }

    // ── 실시간 관찰 기록(live.jsonl) ──
    // 🔴 대변인 에이전트가 측정 **도중에** 자기 스킬을 관찰하려고 읽는다(사용자 지시 2026-09-30).
    //    runs.jsonl은 판이 끝나야 한 줄이 생기므로, 층이 끝날 때마다·스킬을 얻거나 진화하는 순간마다 여기 한 줄씩 바로 쓴다.
    //    줄 종류 t = run-start · acquire · evolve · stage · run-end. 모든 줄에 run(판 번호)·target(목표 스킬)·map이 붙는다.
    private void Live(string type, Dictionary<string, object> body)
    {
        if (header == null || sessionRoot == null) return;
        var d = BotJson.Obj();
        d["t"] = type;
        foreach (string k in new[] { "run", "loadoutId", "targetSkill", "targetRoute", "map", "character" })
            if (header.TryGetValue(k, out object v)) d[k] = v;
        d["gameTime"] = gameTime;
        if (body != null) foreach (var kv in body) d[kv.Key] = kv.Value;
        try { File.AppendAllText(Path.Combine(sessionRoot, "live.jsonl"), BotJson.Write(d) + "\n"); } catch (IOException) { }
    }

    private static void Bump(Dictionary<string, float> m, string k, float v) { m.TryGetValue(k, out float o); m[k] = o + v; }
    private static void Bump(Dictionary<string, int> m, string k, int v) { m.TryGetValue(k, out int o); m[k] = o + v; }

    // 패시브 한 종의 누적. 필드 이름 → 진화 차수별 값. 필드 목록은 PassiveFields가 정한다(0이어도 기록되게 미리 만든다).
    private const int PassiveEvoSlots = EvolutionRoutes.MaxPassiveStage + 1;
    private class PassiveAcc
    {
        public readonly Dictionary<string, float[]> byEvo = new Dictionary<string, float[]>();
    }

    private static string[] PassiveFields(PassiveSkillId id) => id switch
    {
        PassiveSkillId.Strength => new[] { "damage" },
        PassiveSkillId.Assassinate => new[] { "damage", "critExtra", "bountyXp" },
        PassiveSkillId.Health => new[] { "maxHp", "heal" },
        PassiveSkillId.Defense => new[] { "blocked", "damage", "autoSwings" },
        PassiveSkillId.Knowledge => new[] { "xp", "treasureKills" },
        PassiveSkillId.Accel => new[] { "cdSaved", "refreshProcs", "refreshSaved", "hitCutSaved", "damage" },
        _ => new string[0],
    };

    // 스킬별 마지막 피해 계산의 배율 풀 구성(BotInput.OnDamageShares) — lastBuff와 같은 근사.
    private struct Shares { public float pool, strength, accel; }
    // 스킬별 마지막 치명 확률(BotInput.OnCritChance): withA = 실제 확률, aShare = 그중 암살 몫.
    private struct CritInfo { public float withA, aShare; }

    private readonly string runsPath;
    private readonly string sessionRoot;

    private Dictionary<string, object> header;
    private float gameTime;
    private float realStart;
    private StageAcc stage;
    private List<object> stages;
    private Dictionary<string, SkillAcc> skills;
    private Dictionary<string, Dictionary<string, object>> enemyTypes;
    private Dictionary<Enemy, string> lastSource;
    private List<object> evolutions;
    public List<object> Picks { get; private set; }
    private Dictionary<string, int> knownEvo;
    private string lastAttacker;
    private int escapes, slows, knockbacks, totalDamageTaken;
    private float contactRange;
    // 스킬별 마지막 피해 계산의 외부 강화 배율(BotInput.OnBaseDamage). 투사체·지속 효과는 발사 뒤 잠시 뒤에 맞으므로
    // "마지막 계산값"으로 근사한다 — 그 사이 강화가 바뀐 몫만큼 오차가 난다.
    private Dictionary<string, float> lastBuff;

    // ── 패시브 측정 ──
    private Dictionary<PassiveSkillId, PassiveAcc> passiveAcc;
    private Dictionary<string, Shares> lastShares;
    private Dictionary<string, CritInfo> lastCrit;
    private float lastCritRoll;          // 직전 ApplyCrit의 확률(다음 피해 이벤트가 소비, 없으면 -1)
    // 폴링 기준값 — 진화·레벨업은 모달 안에서 일어나므로 프레임 사이 변화량을 그 패시브에 귀속한다.
    private float lastCritMult, lastXpMult;
    private int lastMaxHp;
    private bool lastRevive, revived;
    private Dictionary<PassiveSkillId, float> critMultShare; // 치명 배율 중 힘 R0·암살 R1이 더한 몫
    private float knowledgeXpShare;      // 경험치 배율 중 지식 몫
    private float lastStageFactor = 1f;
    private float lastCastCd;            // 직전 시전이 건 쿨(리프레쉬가 0으로 만든 값)
    private float xpGained;

    private PlayerSkills ps;
    private PlayerPassives pp;
    private PlayerHealth ph;

    public bool Active { get; private set; }
    public float GameTime => gameTime;

    public BotRecorder(string sessionDir)
    {
        sessionRoot = sessionDir;
        runsPath = Path.Combine(sessionDir, "runs.jsonl");
    }

    public void BeginRun(Dictionary<string, object> runHeader)
    {
        header = runHeader;
        gameTime = 0f;
        realStart = Time.realtimeSinceStartup;
        stages = new List<object>();
        skills = new Dictionary<string, SkillAcc>();
        enemyTypes = new Dictionary<string, Dictionary<string, object>>();
        lastSource = new Dictionary<Enemy, string>();
        evolutions = new List<object>();
        Picks = new List<object>();
        knownEvo = new Dictionary<string, int>();
        lastBuff = new Dictionary<string, float>();
        lastAttacker = null;
        escapes = slows = knockbacks = totalDamageTaken = 0;
        contactRange = BalanceConstants.ContactStopDistance + BalanceConstants.HeadbuttLungeDistance + 0.6f;

        ps = Object.FindAnyObjectByType<PlayerSkills>();
        pp = Object.FindAnyObjectByType<PlayerPassives>();
        ph = Object.FindAnyObjectByType<PlayerHealth>();
        stage = null;

        passiveAcc = new Dictionary<PassiveSkillId, PassiveAcc>();
        lastShares = new Dictionary<string, Shares>();
        lastCrit = new Dictionary<string, CritInfo>();
        critMultShare = new Dictionary<PassiveSkillId, float>();
        lastCritRoll = -1f;
        lastCritMult = PlayerPassives.AssassinateCritMultiplier;
        lastXpMult = PlayerExperience.Instance != null ? PlayerExperience.Instance.XpMultiplier : 1f;
        lastMaxHp = ph != null ? ph.MaxHealth : 0;
        lastRevive = PlayerPassives.ReviveOnce;
        revived = false;
        knowledgeXpShare = 0f;
        lastStageFactor = 1f;
        lastCastCd = 0f;
        xpGained = 0f;
        BotInput.OnDamageShares += HandleDamageShares;
        BotInput.OnCritChance += HandleCritChance;
        BotInput.OnCritRoll += HandleCritRoll;
        BotInput.OnPlayerDamageTaken += HandlePlayerDamageTaken;
        BotInput.OnHeartPickup += HandleHeartPickup;
        BotInput.OnKillXp += HandleKillXp;
        BotInput.OnXpAdded += HandleXpAdded;
        PlayerSkills.OnRefreshProc += HandleRefreshProc;

        BotInput.OnCast += HandleCast;
        BotInput.OnPlayerHit += HandlePlayerHit;
        BotInput.OnEnemyDamaged += HandleEnemyDamaged;
        BotInput.OnBaseDamage += HandleBaseDamage;
        BotInput.OnEnemyKilled += HandleEnemyKilled;
        BotInput.OnEnemyEscaped += HandleEscaped;
        BotInput.OnSlow += HandleSlow;
        BotInput.OnKnockback += HandleKnockback;
        Active = true;
        Live("run-start", null);
    }

    // 전투 중 매 프레임.
    public void Tick()
    {
        if (!Active) return;
        gameTime += Time.deltaTime;

        GameManager gm = GameManager.Instance;
        if (gm != null && (stage == null || stage.stage != gm.CurrentStage))
        {
            CloseStage();
            stage = new StageAcc { stage = gm.CurrentStage, timeStart = gameTime, hpStart = ph != null ? ph.CurrentHealth : 0 };
        }

        if (ps != null)
        {
            foreach (EquippedSkill s in ps.EquippedSkills)
            {
                SkillAcc acc = Skill(s.Id.ToString());
                if (acc.acquiredStage < 0)
                {
                    acc.acquiredStage = CurrentStage; acc.acquiredTime = gameTime;
                    var ev = Event("acquire", "active", s.Id.ToString(), s.EvolutionStage, s.Route);
                    ev.Remove("gameTime");
                    Live("acquire", ev);
                }
                NoteEvolution("active", s.Id.ToString(), s.EvolutionStage, s.Route);
            }
        }
        if (pp != null)
        {
            PassiveSkillId? evolvedNow = null;
            foreach (EquippedPassive p in pp.EquippedPassives)
            {
                string key = "P:" + p.Id;
                if (!knownEvo.ContainsKey(key))
                {
                    knownEvo[key] = 0;
                    var ev = Event("acquire", "passive", p.Id.ToString(), 0, -1);
                    evolutions.Add(ev);
                    Live("acquire", ev.Where(kv => kv.Key != "gameTime").ToDictionary(kv => kv.Key, kv => kv.Value));
                }
                int before = PEvo(p.Id);
                NoteEvolution("passive", p.Id.ToString(), p.EvolutionStage, p.Route);
                if (PEvo(p.Id) > before) evolvedNow = p.Id;
            }
            PollPassiveStats(evolvedNow);
        }
    }

    // 패시브가 올린 스탯을 프레임 사이 변화량으로 잰다(레벨업·진화는 모달 안에서 일어나 다음 프레임에 보인다).
    // 최대체력·경험치 배율은 판 중간에 패시브 말고는 올리는 곳이 없다(메타 보너스는 판 시작 때 한 번, 그때는 패시브가 없다).
    // 치명 배율은 힘 R0·암살 R1 진화만 올린다 — 그 프레임에 진화한 패시브에 귀속한다.
    private void PollPassiveStats(PassiveSkillId? evolvedNow)
    {
        float cm = PlayerPassives.AssassinateCritMultiplier;
        if (cm != lastCritMult)
        {
            if (evolvedNow.HasValue)
            {
                critMultShare.TryGetValue(evolvedNow.Value, out float v);
                critMultShare[evolvedNow.Value] = v + (cm - lastCritMult);
            }
            lastCritMult = cm;
        }
        if (ph != null && ph.MaxHealth != lastMaxHp)
        {
            if (ph.MaxHealth > lastMaxHp && pp.HasPassive(PassiveSkillId.Health))
                PAdd(PassiveSkillId.Health, "maxHp", ph.MaxHealth - lastMaxHp);
            lastMaxHp = ph.MaxHealth;
        }
        if (PlayerExperience.Instance != null && PlayerExperience.Instance.XpMultiplier != lastXpMult)
        {
            if (pp.HasPassive(PassiveSkillId.Knowledge)) knowledgeXpShare += PlayerExperience.Instance.XpMultiplier - lastXpMult;
            lastXpMult = PlayerExperience.Instance.XpMultiplier;
        }
        bool rv = PlayerPassives.ReviveOnce;
        if (lastRevive && !rv) revived = true;
        lastRevive = rv;
    }

    private int PEvo(PassiveSkillId id)
    {
        knownEvo.TryGetValue("P:" + id + ":evo", out int v);
        return Mathf.Clamp(v, 0, PassiveEvoSlots - 1);
    }

    private bool Has(PassiveSkillId id) => pp != null && pp.HasPassive(id);

    private PassiveAcc PAcc(PassiveSkillId id)
    {
        if (!passiveAcc.TryGetValue(id, out PassiveAcc a))
        {
            passiveAcc[id] = a = new PassiveAcc();
            foreach (string f in PassiveFields(id)) a.byEvo[f] = new float[PassiveEvoSlots];
        }
        return a;
    }

    private void PAdd(PassiveSkillId id, string field, float v)
    {
        PassiveAcc a = PAcc(id);
        if (!a.byEvo.TryGetValue(field, out float[] arr)) a.byEvo[field] = arr = new float[PassiveEvoSlots];
        arr[PEvo(id)] += v;
    }

    private float CritMultShare(PassiveSkillId id) => critMultShare.TryGetValue(id, out float v) ? v : 0f;

    private int CurrentStage => GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;

    private void NoteEvolution(string kind, string id, int evoStage, int route)
    {
        string key = (kind == "active" ? "A:" : "P:") + id + ":evo";
        knownEvo.TryGetValue(key, out int prev);
        if (evoStage <= prev) return;
        knownEvo[key] = evoStage;
        var ev = Event("evolve", kind, id, evoStage, route);
        evolutions.Add(ev);
        Live("evolve", ev.Where(kv => kv.Key != "gameTime").ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private Dictionary<string, object> Event(string what, string kind, string id, int evoStage, int route)
    {
        var e = BotJson.Obj();
        e["what"] = what; e["kind"] = kind; e["id"] = id; e["evoStage"] = evoStage; e["route"] = route;
        e["stage"] = CurrentStage; e["gameTime"] = gameTime;
        return e;
    }

    private SkillAcc Skill(string key)
    {
        if (!skills.TryGetValue(key, out SkillAcc acc)) skills[key] = acc = new SkillAcc();
        return acc;
    }

    // 이 스킬의 현재 진화 차수(귀속 시점). knownEvo는 Tick이 프레임마다 갱신한다. "Other"는 0으로 떨어진다.
    private int EvoStageOf(string skillKey)
    {
        knownEvo.TryGetValue("A:" + skillKey + ":evo", out int v);
        return Mathf.Clamp(v, 0, 2);
    }

    private static string EnemyKey(Enemy e) => e.IsBoss ? "Boss" : e.DefinitionName;

    private void NoteEnemyType(Enemy e, string key)
    {
        if (enemyTypes.ContainsKey(key)) return;
        var t = BotJson.Obj();
        // 대공 축은 이제 "비행 적에게 준 피해"다 — 때릴 수 있나를 막던 requiresAntiAir는 폐지됐다(2026-09-19).
        t["flying"] = e.IsFlying; t["shield"] = e.BlocksProjectiles;
        t["carrier"] = e.IsCarrier; t["boss"] = e.IsBoss; t["treasure"] = e.IsTreasure;
        enemyTypes[key] = t;
    }

    // ── 훅 ──
    private void HandleCast(EquippedSkill s, float baseCd, float cdMult)
    {
        SkillAcc acc = Skill(s.Id.ToString());
        acc.casts++;
        acc.castsT[EvoStageOf(s.Id.ToString())]++;
        if (stage != null) Bump(stage.castsBySkill, s.Id.ToString(), 1);
        acc.cdLast = baseCd;
        acc.cdMin = Mathf.Min(acc.cdMin, baseCd);
        acc.cdMax = Mathf.Max(acc.cdMax, baseCd);
        acc.cdMultLast = cdMult;

        // 가속: 이 시전의 쿨에서 가속 감소율이 깎은 몫. PlayerSkills.TryUseSkill과 같은 식(max(0.05, 1 - 감소율))을 되돌린다.
        lastCastCd = baseCd * cdMult;
        float cut = PlayerPassives.AccelCooldownReduction;
        if (cut > 0f && Has(PassiveSkillId.Accel))
            PAdd(PassiveSkillId.Accel, "cdSaved", lastCastCd / Mathf.Max(0.05f, 1f - cut) - lastCastCd);
    }

    // 가속 R0 「리프레쉬」: TryUseSkill이 OnCast 직후에 부른다 — 방금 건 쿨 전체를 건너뛴 것이다.
    private void HandleRefreshProc()
    {
        if (!Has(PassiveSkillId.Accel)) return;
        PAdd(PassiveSkillId.Accel, "refreshProcs", 1f);
        PAdd(PassiveSkillId.Accel, "refreshSaved", lastCastCd);
    }

    private void HandleDamageShares(ActiveSkillId skill, float pool, float strength, float accelBonus) =>
        lastShares[skill.ToString()] = new Shares { pool = pool, strength = strength, accel = accelBonus };

    // 암살 없이의 확률 = 암살 가산분을 빼고 **기본 상한(70%)**을 다시 씌운 값 — R1이 연 상한도 암살 몫이다.
    private void HandleCritChance(ActiveSkillId skill, float chance, float cap)
    {
        float withA = Mathf.Min(chance, cap);
        float assn = Has(PassiveSkillId.Assassinate) ? PlayerPassives.AssassinateCritChance : 0f;
        float without = Mathf.Clamp(Mathf.Min(chance - assn, BalanceConstants.MaxCritChance), 0f, withA);
        lastCrit[skill.ToString()] = new CritInfo { withA = withA, aShare = withA - without };
    }

    private void HandleCritRoll(float chance) => lastCritRoll = chance;

    // 피격 반응이 돌기 직전 — 쿨이 아직 안 당겨진 상태라 실제로 당겨질 양을 잴 수 있다.
    private void HandlePlayerDamageTaken(int amount)
    {
        float cut = PlayerPassives.AccelCooldownCutOnHit;
        if (cut > 0f && ps != null && Has(PassiveSkillId.Accel))
        {
            float saved = 0f;
            foreach (EquippedSkill s in ps.EquippedSkills) saved += Mathf.Clamp(s.CooldownTimer, 0f, cut);
            PAdd(PassiveSkillId.Accel, "hitCutSaved", saved);
        }
        if (PlayerPassives.DefenseAutoSwingDamageMult > 0f && ps != null && ps.HasSkill(ActiveSkillId.Swing) && Has(PassiveSkillId.Defense))
            PAdd(PassiveSkillId.Defense, "autoSwings", 1f);
    }

    // 건강 회복 몫(기댓값): 건강이 없으면 하트가 1/드랍배율만큼만 나오고 2배 강화도 없다.
    // 둘 다 최대체력에 잘린 실제 회복량으로 비교한다.
    private void HandleHeartPickup(PlayerHealth target, int heal)
    {
        if (target == null || !Has(PassiveSkillId.Health)) return;
        int room = Mathf.Max(0, target.MaxHealth - target.CurrentHealth);
        float actual = Mathf.Min(PlayerPassives.HealItemDouble ? heal * 2 : heal, room);
        float plain = Mathf.Min(heal, room);
        float dropMult = Mathf.Max(1f, PlayerPassives.HeartDropMultiplier);
        PAdd(PassiveSkillId.Health, "heal", actual - plain / dropMult);
    }

    // 처치 경험치의 패시브 몫. 적립은 보석이 도착할 때(AddXP)라 그 배율로 환산한다 — 배율·스테이지 감쇠는 도착 직전 값으로 근사.
    private void HandleKillXp(Enemy e, int xpValue, bool isCrit, ActiveSkillId? source)
    {
        float bounty = isCrit ? PlayerPassives.AssassinateKillXpMultiplier : 1f;
        float homing = source == ActiveSkillId.Homing ? PlayerPassives.HomingKillXpMultiplier : 1f;
        float mult = PlayerExperience.Instance != null ? PlayerExperience.Instance.XpMultiplier : 1f;
        if (bounty > 1f && Has(PassiveSkillId.Assassinate))
            PAdd(PassiveSkillId.Assassinate, "bountyXp", xpValue * homing * (bounty - 1f) * mult * lastStageFactor);
        // 지식 배율 몫은 HandleXpAdded가 적립 전체에 대해 센다 — 여기선 지식 몫을 뺀 배율로만 환산해 두 번 세지 않는다.
        if (homing > 1f && Has(PassiveSkillId.Knowledge))
            PAdd(PassiveSkillId.Knowledge, "xp", xpValue * bounty * (homing - 1f) * (mult - knowledgeXpShare) * lastStageFactor);
        if (e != null && e.IsTreasure && Has(PassiveSkillId.Knowledge))
            PAdd(PassiveSkillId.Knowledge, "treasureKills", 1f);
    }

    private void HandleXpAdded(int amount, float mult, float stageFactor)
    {
        lastStageFactor = stageFactor;
        xpGained += amount * mult * stageFactor;
        if (knowledgeXpShare > 0f && Has(PassiveSkillId.Knowledge))
            PAdd(PassiveSkillId.Knowledge, "xp", amount * knowledgeXpShare * stageFactor);
    }

    // 피해 한 건의 패시브 몫. 배율 풀·치명 확률은 그 스킬의 마지막 계산값으로 근사한다(lastBuff와 같은 한계).
    private void NotePassiveDamage(string skillKey, float amount, bool isCrit)
    {
        float roll = lastCritRoll;
        lastCritRoll = -1f;
        if (pp == null || amount <= 0f) return;

        // 출처 없는 피해 = 가시 갑주 반사뿐이다(Enemy.TakeDamage를 source 없이 부르는 곳이 PlayerPassives.HandleDamageTaken 하나).
        if (skillKey == "Other")
        {
            if (PlayerPassives.HealthRetaliationMultiplier > 0f && Has(PassiveSkillId.Defense))
                PAdd(PassiveSkillId.Defense, "damage", amount);
            return;
        }

        float cm = PlayerPassives.AssassinateCritMultiplier + MetaBonuses.CritDamageBonus;
        lastShares.TryGetValue(skillKey, out Shares sh);

        if (Has(PassiveSkillId.Strength))
        {
            float poolKeep = sh.pool > 0f ? 1f - sh.strength / sh.pool : 1f;
            float critKeep = isCrit && cm > 0f ? (cm - CritMultShare(PassiveSkillId.Strength)) / cm : 1f;
            float lost = amount * (1f - poolKeep * critKeep);
            if (lost > 0f) PAdd(PassiveSkillId.Strength, "damage", lost);
        }

        if (sh.accel > 0f && Has(PassiveSkillId.Accel))
            PAdd(PassiveSkillId.Accel, "damage", amount * sh.accel / (1f + sh.accel));

        if (isCrit && cm > 0f && Has(PassiveSkillId.Assassinate))
        {
            PAdd(PassiveSkillId.Assassinate, "critExtra", amount * (cm - 1f) / cm);
            // 이 치명타가 암살 없이도 났을 확률 p0. 굴림 확률이 스킬 확률보다 높게 강제된(확정 치명 진화) 타격은 암살 몫 0.
            bool known = lastCrit.TryGetValue(skillKey, out CritInfo ci);
            float c = roll > 0f ? roll : (known ? ci.withA : 0f);
            float c0 = c;
            if (known && !(c >= 1f && ci.withA < 1f)) c0 = Mathf.Max(0f, c - ci.aShare);
            float p0 = c > 0f ? c0 / c : 1f;
            float without = p0 * amount * (cm - CritMultShare(PassiveSkillId.Assassinate)) / cm + (1f - p0) * amount / cm;
            if (amount > without) PAdd(PassiveSkillId.Assassinate, "damage", amount - without);
        }
    }

    private void HandlePlayerHit(Enemy e, int amount)
    {
        string key = EnemyKey(e);
        NoteEnemyType(e, key);
        lastAttacker = key;
        totalDamageTaken += amount;
        NoteDefenseBlock(amount);
        if (stage == null) return;
        stage.damageTaken += amount;
        stage.byEnemy.TryGetValue(key, out float v);
        stage.byEnemy[key] = v + amount;
    }

    // 방어: 박치기 원래 피해 − 감소 후 피해. 이 훅은 PlayerHealth.TakeDamage 직후라 감소율이 그대로다(같은 식을 되풀이한다).
    private void NoteDefenseBlock(int amount)
    {
        float dr = PlayerPassives.DamageReduction;
        if (dr <= 0f || amount <= 0 || !Has(PassiveSkillId.Defense)) return;
        PAdd(PassiveSkillId.Defense, "blocked", amount - Mathf.Max(1, Mathf.RoundToInt(amount * (1f - dr))));
    }

    private void HandleBaseDamage(ActiveSkillId skill, float buffMult) => lastBuff[skill.ToString()] = buffMult;

    private void HandleEnemyDamaged(Enemy e, ActiveSkillId? source, float amount, float hpBefore, bool isCrit, float preEnemy)
    {
        string skillKey = source.HasValue ? source.Value.ToString() : "Other";
        string enemyKey = EnemyKey(e);
        NoteEnemyType(e, enemyKey);
        float eff = Mathf.Min(amount, Mathf.Max(0f, hpBefore));
        float critMult = isCrit ? PlayerPassives.AssassinateCritMultiplier + MetaBonuses.CritDamageBonus : 1f;
        float buff = lastBuff.TryGetValue(skillKey, out float b) && b > 0f ? b : 1f;
        float pure = critMult > 0f ? preEnemy / (buff * critMult) : preEnemy;

        SkillAcc acc = Skill(skillKey);
        int evo = EvoStageOf(skillKey);
        acc.raw += amount;
        acc.eff += eff;
        acc.pure += pure;
        acc.effT[evo] += eff;
        acc.rawT[evo] += amount;
        acc.pureT[evo] += pure;
        acc.overkill += amount - eff;
        acc.effByEnemy.TryGetValue(enemyKey, out float v);
        acc.effByEnemy[enemyKey] = v + eff;
        lastSource[e] = skillKey;
        NotePassiveDamage(skillKey, amount, isCrit);

        if (stage != null)
        {
            stage.effBySkill.TryGetValue(skillKey, out float s);
            stage.effBySkill[skillKey] = s + eff;
            Bump(stage.rawBySkill, skillKey, amount);
            Bump(stage.pureBySkill, skillKey, pure);
        }
    }

    private void HandleEnemyKilled(Enemy e)
    {
        if (stage != null) stage.kills++;
        if (!lastSource.TryGetValue(e, out string skillKey)) return;
        if (stage != null) Bump(stage.killsBySkill, skillKey, 1);
        SkillAcc acc = Skill(skillKey);
        acc.kills++;
        acc.killsT[EvoStageOf(skillKey)]++;
        if (ph != null && Mathf.Abs(ph.transform.position.x - e.transform.position.x) <= contactRange)
            acc.contactKills++;
    }

    private void HandleEscaped(Enemy e) => escapes++;
    private void HandleSlow(Enemy e, float mult, float duration) => slows++;
    private void HandleKnockback(Enemy e, float distance) => knockbacks++;

    private void CloseStage()
    {
        if (stage == null) return;
        var d = BotJson.Obj();
        d["stage"] = stage.stage;
        d["gameTime"] = gameTime - stage.timeStart;
        d["hpStart"] = stage.hpStart;
        d["hpEnd"] = ph != null ? ph.CurrentHealth : 0;
        d["damageTaken"] = stage.damageTaken;
        d["kills"] = stage.kills;
        d["byEnemy"] = stage.byEnemy.ToDictionary(k => k.Key, k => (object)k.Value);
        d["effBySkill"] = stage.effBySkill.ToDictionary(k => k.Key, k => (object)k.Value);
        stages.Add(d);

        // 실시간 관찰: 이 층에서 보유 스킬·패시브가 한 일. share = 이 층 전체 피해(과잉 피해 포함) 중 몫.
        float stageRaw = stage.rawBySkill.Values.Sum();
        var live = BotJson.Obj();
        live["stage"] = stage.stage;
        live["stageTime"] = gameTime - stage.timeStart;
        live["hpStart"] = stage.hpStart;
        live["hpEnd"] = ph != null ? ph.CurrentHealth : 0;
        live["damageTaken"] = stage.damageTaken;
        live["kills"] = stage.kills;
        live["playerLevel"] = PlayerExperience.Instance != null ? PlayerExperience.Instance.Level : 0;
        var sk = new List<object>();
        if (ps != null)
            foreach (EquippedSkill s in ps.EquippedSkills)
            {
                string id = s.Id.ToString();
                stage.rawBySkill.TryGetValue(id, out float raw);
                stage.pureBySkill.TryGetValue(id, out float pure);
                stage.castsBySkill.TryGetValue(id, out int casts);
                stage.killsBySkill.TryGetValue(id, out int kills);
                var o = BotJson.Obj();
                o["id"] = id; o["level"] = s.Level; o["evoStage"] = s.EvolutionStage; o["route"] = s.Route;
                o["raw"] = raw; o["pure"] = pure; o["casts"] = casts; o["kills"] = kills;
                o["share"] = stageRaw > 0f ? raw / stageRaw : 0f;
                sk.Add(o);
            }
        live["skills"] = sk;
        var pv = new List<object>();
        if (pp != null)
            foreach (EquippedPassive p in pp.EquippedPassives)
            {
                var o = BotJson.Obj();
                o["id"] = p.Id.ToString(); o["level"] = p.Level; o["evoStage"] = p.EvolutionStage; o["route"] = p.Route;
                pv.Add(o);
            }
        live["passives"] = pv;
        Live("stage", live);
        stage = null;
    }

    // result: clear | dead | stuck | timeout. 반환값에 구매 내역 등을 더 붙인 뒤 WriteRun으로 쓴다.
    public Dictionary<string, object> EndRun(string result)
    {
        if (Active && pp != null) PollPassiveStats(null); // 마지막 프레임의 부활 소비 등을 놓치지 않게
        CloseStage();
        Unsubscribe();
        Active = false;

        GameManager gm = GameManager.Instance;
        var end = BotJson.Obj();
        end["result"] = result;
        end["stageReached"] = gm != null ? gm.CurrentStage : 0;
        end["damageTaken"] = totalDamageTaken;
        Live("run-end", end);
        var r = new Dictionary<string, object>(header);
        r["result"] = result;
        r["stageReached"] = gm != null ? gm.CurrentStage : 0;
        r["finalStage"] = gm != null ? gm.FinalStage : 0;
        r["gameTime"] = gameTime;
        r["realTime"] = Time.realtimeSinceStartup - realStart;
        r["playerLevel"] = PlayerExperience.Instance != null ? PlayerExperience.Instance.Level : 0;
        r["runEssence"] = MetaRun.RunCurrency;
        r["hpEnd"] = ph != null ? ph.CurrentHealth : 0;
        r["maxHp"] = ph != null ? ph.MaxHealth : 0;
        r["damageTaken"] = totalDamageTaken;
        r["xpGained"] = xpGained;
        r["deathCause"] = result == "dead" ? lastAttacker : null;
        r["escapes"] = escapes;
        r["slows"] = slows;
        r["knockbacks"] = knockbacks;
        r["stages"] = stages;
        r["evolutions"] = evolutions;
        r["picks"] = Picks;
        r["enemyTypes"] = enemyTypes.ToDictionary(k => k.Key, k => (object)k.Value);

        float totalEff = skills.Values.Sum(a => a.eff);
        float totalRaw = skills.Values.Sum(a => a.raw);
        var skillList = new List<object>();
        foreach (var kv in skills)
        {
            SkillAcc a = kv.Value;
            var d = BotJson.Obj();
            d["id"] = kv.Key;
            EquippedSkill eq = ps != null ? ps.EquippedSkills.FirstOrDefault(s => s.Id.ToString() == kv.Key) : null;
            d["owned"] = eq != null;
            d["level"] = eq != null ? eq.Level : 0;
            d["evoStage"] = eq != null ? eq.EvolutionStage : 0;
            d["route"] = eq != null ? eq.Route : -1;
            d["slot"] = eq != null ? ps.EquippedSkills.ToList().IndexOf(eq) : -1;
            d["acquiredStage"] = a.acquiredStage;
            d["acquiredTime"] = a.acquiredTime;
            d["ownedTime"] = a.acquiredTime >= 0 ? gameTime - a.acquiredTime : 0f;
            d["rawDamage"] = a.raw;
            d["effDamage"] = a.eff;
            d["overkill"] = a.overkill;
            d["pureDamage"] = a.pure;
            d["share"] = totalRaw > 0f ? a.raw / totalRaw : 0f;
            d["effShare"] = totalEff > 0f ? a.eff / totalEff : 0f;
            d["kills"] = a.kills;
            d["contactKills"] = a.contactKills;
            d["casts"] = a.casts;
            d["effByEvoStage"] = a.effT.Select(x => (object)x).ToList();
            d["rawByEvoStage"] = a.rawT.Select(x => (object)x).ToList();
            d["pureByEvoStage"] = a.pureT.Select(x => (object)x).ToList();
            d["castsByEvoStage"] = a.castsT.Select(x => (object)x).ToList();
            d["killsByEvoStage"] = a.killsT.Select(x => (object)x).ToList();
            d["baseCdLast"] = a.casts > 0 ? a.cdLast : (float?)null;
            d["baseCdMin"] = a.casts > 0 ? a.cdMin : (float?)null;
            d["baseCdMax"] = a.casts > 0 ? a.cdMax : (float?)null;
            d["cdMultLast"] = a.cdMultLast;
            d["effByEnemy"] = a.effByEnemy.ToDictionary(k => k.Key, k => (object)k.Value);
            skillList.Add(d);
        }
        r["skills"] = skillList;

        var passiveList = new List<object>();
        if (pp != null)
            foreach (EquippedPassive p in pp.EquippedPassives)
            {
                var d = BotJson.Obj();
                d["id"] = p.Id.ToString(); d["level"] = p.Level; d["evoStage"] = p.EvolutionStage; d["route"] = p.Route;
                foreach (var kv in PAcc(p.Id).byEvo)
                    d[kv.Key + "ByEvoStage"] = kv.Value.Select(x => (object)x).ToList();
                if (p.Id == PassiveSkillId.Defense) d["revived"] = revived;
                passiveList.Add(d);
            }
        r["passives"] = passiveList;
        return r;
    }

    public void WriteRun(Dictionary<string, object> run)
    {
        File.AppendAllText(runsPath, BotJson.Write(run) + "\n");
    }

    // 시험장(gym)은 같은 지표를 다른 파일에 쌓는다 — 정주행과 섞으면 분석기가 한 판과 한 셀을 같은 표에 넣는다.
    // 🔴 **지표 정의는 건드리지 않는다.** 정주행과 같은 정의로 재야 두 측정을 나란히 놓을 수 있다.
    public void WriteRunTo(string fileName, Dictionary<string, object> run)
    {
        File.AppendAllText(Path.Combine(sessionRoot, fileName), BotJson.Write(run) + "\n");
    }

    // 세션이 중간에 끊겨도 훅이 남지 않게.
    public void Unsubscribe()
    {
        BotInput.OnCast -= HandleCast;
        BotInput.OnPlayerHit -= HandlePlayerHit;
        BotInput.OnEnemyDamaged -= HandleEnemyDamaged;
        BotInput.OnBaseDamage -= HandleBaseDamage;
        BotInput.OnEnemyKilled -= HandleEnemyKilled;
        BotInput.OnEnemyEscaped -= HandleEscaped;
        BotInput.OnSlow -= HandleSlow;
        BotInput.OnKnockback -= HandleKnockback;
        BotInput.OnDamageShares -= HandleDamageShares;
        BotInput.OnCritChance -= HandleCritChance;
        BotInput.OnCritRoll -= HandleCritRoll;
        BotInput.OnPlayerDamageTaken -= HandlePlayerDamageTaken;
        BotInput.OnHeartPickup -= HandleHeartPickup;
        BotInput.OnKillXp -= HandleKillXp;
        BotInput.OnXpAdded -= HandleXpAdded;
        PlayerSkills.OnRefreshProc -= HandleRefreshProc;
    }
}
#endif
