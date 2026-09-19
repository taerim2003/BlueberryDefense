#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// 봇 한 판의 관측 기록 → `BotRuns/<세션>/runs.jsonl` 한 줄.
// 관측만 한다(게임 상태를 바꾸지 않는다). 입력은 BotInput 훅 + 매 프레임 폴링(Tick).
//
// 기록 단위 정의 (분석기 `Tools/BotPlaytest/analyze.js`가 이 이름을 그대로 읽는다 — 바꾸면 거기도 고칠 것)
//   rawDamage       = 게임이 적용한 피해(오버킬 포함, DamageMeter와 같은 값)
//   effDamage       = 그 타격 직전 체력을 넘지 않는 몫(오버킬 제외) — "유효 딜"
//   contactKills    = 박치기 거리(대기선 + 돌진 거리) 안에서 잡은 수 — "플레이어를 때리던 적을 치웠나"
//   stages[].byEnemy = 그 스테이지에 적 종류별로 받은 피해
public class BotRecorder
{
    private class SkillAcc
    {
        public float raw, eff, overkill;
        public int kills, contactKills, casts;
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
    }

    private readonly string runsPath;

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

    private PlayerSkills ps;
    private PlayerPassives pp;
    private PlayerHealth ph;

    public bool Active { get; private set; }
    public float GameTime => gameTime;

    public BotRecorder(string sessionDir)
    {
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
        lastAttacker = null;
        escapes = slows = knockbacks = totalDamageTaken = 0;
        contactRange = BalanceConstants.ContactStopDistance + BalanceConstants.HeadbuttLungeDistance + 0.6f;

        ps = Object.FindAnyObjectByType<PlayerSkills>();
        pp = Object.FindAnyObjectByType<PlayerPassives>();
        ph = Object.FindAnyObjectByType<PlayerHealth>();
        stage = null;

        BotInput.OnCast += HandleCast;
        BotInput.OnPlayerHit += HandlePlayerHit;
        BotInput.OnEnemyDamaged += HandleEnemyDamaged;
        BotInput.OnEnemyKilled += HandleEnemyKilled;
        BotInput.OnEnemyEscaped += HandleEscaped;
        BotInput.OnSlow += HandleSlow;
        BotInput.OnKnockback += HandleKnockback;
        Active = true;
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
                if (acc.acquiredStage < 0) { acc.acquiredStage = CurrentStage; acc.acquiredTime = gameTime; }
                NoteEvolution("active", s.Id.ToString(), s.EvolutionStage, s.Route);
            }
        }
        if (pp != null)
            foreach (EquippedPassive p in pp.EquippedPassives)
            {
                string key = "P:" + p.Id;
                if (!knownEvo.ContainsKey(key)) { knownEvo[key] = 0; evolutions.Add(Event("acquire", "passive", p.Id.ToString(), 0, -1)); }
                NoteEvolution("passive", p.Id.ToString(), p.EvolutionStage, p.Route);
            }
    }

    private int CurrentStage => GameManager.Instance != null ? GameManager.Instance.CurrentStage : 0;

    private void NoteEvolution(string kind, string id, int evoStage, int route)
    {
        string key = (kind == "active" ? "A:" : "P:") + id + ":evo";
        knownEvo.TryGetValue(key, out int prev);
        if (evoStage <= prev) return;
        knownEvo[key] = evoStage;
        evolutions.Add(Event("evolve", kind, id, evoStage, route));
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
        acc.cdLast = baseCd;
        acc.cdMin = Mathf.Min(acc.cdMin, baseCd);
        acc.cdMax = Mathf.Max(acc.cdMax, baseCd);
        acc.cdMultLast = cdMult;
    }

    private void HandlePlayerHit(Enemy e, int amount)
    {
        string key = EnemyKey(e);
        NoteEnemyType(e, key);
        lastAttacker = key;
        totalDamageTaken += amount;
        if (stage == null) return;
        stage.damageTaken += amount;
        stage.byEnemy.TryGetValue(key, out float v);
        stage.byEnemy[key] = v + amount;
    }

    private void HandleEnemyDamaged(Enemy e, ActiveSkillId? source, float amount, float hpBefore)
    {
        string skillKey = source.HasValue ? source.Value.ToString() : "Other";
        string enemyKey = EnemyKey(e);
        NoteEnemyType(e, enemyKey);
        float eff = Mathf.Min(amount, Mathf.Max(0f, hpBefore));

        SkillAcc acc = Skill(skillKey);
        acc.raw += amount;
        acc.eff += eff;
        acc.overkill += amount - eff;
        acc.effByEnemy.TryGetValue(enemyKey, out float v);
        acc.effByEnemy[enemyKey] = v + eff;
        lastSource[e] = skillKey;

        if (stage != null)
        {
            stage.effBySkill.TryGetValue(skillKey, out float s);
            stage.effBySkill[skillKey] = s + eff;
        }
    }

    private void HandleEnemyKilled(Enemy e)
    {
        if (stage != null) stage.kills++;
        if (!lastSource.TryGetValue(e, out string skillKey)) return;
        SkillAcc acc = Skill(skillKey);
        acc.kills++;
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
        stage = null;
    }

    // result: clear | dead | stuck | timeout. 반환값에 구매 내역 등을 더 붙인 뒤 WriteRun으로 쓴다.
    public Dictionary<string, object> EndRun(string result)
    {
        CloseStage();
        Unsubscribe();
        Active = false;

        GameManager gm = GameManager.Instance;
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
        r["deathCause"] = result == "dead" ? lastAttacker : null;
        r["escapes"] = escapes;
        r["slows"] = slows;
        r["knockbacks"] = knockbacks;
        r["stages"] = stages;
        r["evolutions"] = evolutions;
        r["picks"] = Picks;
        r["enemyTypes"] = enemyTypes.ToDictionary(k => k.Key, k => (object)k.Value);

        float totalEff = skills.Values.Sum(a => a.eff);
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
            d["share"] = totalEff > 0f ? a.eff / totalEff : 0f;
            d["kills"] = a.kills;
            d["contactKills"] = a.contactKills;
            d["casts"] = a.casts;
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
                passiveList.Add(d);
            }
        r["passives"] = passiveList;
        return r;
    }

    public void WriteRun(Dictionary<string, object> run)
    {
        File.AppendAllText(runsPath, BotJson.Write(run) + "\n");
    }

    // 세션이 중간에 끊겨도 훅이 남지 않게.
    public void Unsubscribe()
    {
        BotInput.OnCast -= HandleCast;
        BotInput.OnPlayerHit -= HandlePlayerHit;
        BotInput.OnEnemyDamaged -= HandleEnemyDamaged;
        BotInput.OnEnemyKilled -= HandleEnemyKilled;
        BotInput.OnEnemyEscaped -= HandleEscaped;
        BotInput.OnSlow -= HandleSlow;
        BotInput.OnKnockback -= HandleKnockback;
    }
}
#endif
