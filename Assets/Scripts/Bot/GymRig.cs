#if UNITY_EDITOR || BOT_QA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 스킬 시험장의 로드아웃 강제. "이 스킬을, 이 진화 상태로, 이 레벨로, 이 트리 상태로" 세운다.
//
// 🔴 **게임 로직 코드를 한 줄도 고치지 않는다.** BotInput의 원칙(관측만 한다)을 지키고 릴리스 빌드 위험을 0으로 둔다.
//    전부 리플렉션이다 — BotCooldownAudit이 progressionLookup을 리플렉션으로 심는 것과 같은 관용구.
//
// 🔴 진화는 **실제 PlayerSkills.EvolveSkill을 부른다**(흉내가 아니다). CanEvolve의 관문 네 개를 정직하게 만족시킨다:
//    ① MetaBonuses.EvolutionUnlocked/Evolution2Unlocked  ② 표시 레벨 ≥ EvolutionRoutes.RequiredLevel
//    ③ IsRouteUnlocked가 요구하는 연계 대상 보유  ④ IsStage2KeyReady가 요구하는 열쇠 진화체
//    ③·④는 **임시 주입 → 진화 뒤 제거**다. 그래서 BotCooldownAudit(흉내로 독립 계산)이 시험장의 **대조군**이 된다 —
//    두 경로의 맨몸 쿨이 갈리면 여기가 틀렸다는 뜻이다.
public static class GymRig
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Stat = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    // ── 트리 상태 축 ──
    // 🔴 **기본은 Full이다**(사용자 지시 2026-09-27: "단순 비교를 하고 싶다면 모든 스킬이 은별 금별 강화를
    //    모두 가진 상태를 기준으로"). 실측으로 확인: 액티브 11종 모두 강화 노드가 정확히 2개이고
    //    싼 것(tier 2~3) 하나 + 비싼 것(tier 4~5) 하나 = 은별 + 금별이다.
    // Bare는 대조군으로 남긴다 — Full과의 차이가 곧 "그 스킬 강화 노드 2개의 값"이다.
    // 🔴 둘 다 **일반 노드(공격력·쿨타임 등)는 안 산다** — 전 스킬에 똑같이 얹혀 비교를 흐린다.
    //    강화 노드의 조상이 일반 노드라, 선행 조건을 정직하게 밟으면 그 교란이 딸려 온다 → 세이브에 직접 쓴다.
    public const string TreeFull = "full";
    public const string TreeBare = "bare";

    // 한 셀 = 한 줄의 측정. stage 0이면 route는 -1.
    public class Cell
    {
        public ActiveSkillId skill;
        public int route = -1;      // 0 · 1 (stage 0이면 -1)
        public int stage;           // 0 진화 전 · 1 1차 · 2 2차
        public int level = 10;      // 표시 레벨(진화 뒤에는 1부터 다시 센다)
        public string treeMode = TreeFull;
        public string rig = "solo"; // solo · companion · baseline
        public string scenario;
        public int growthCasts;     // 판 중 누적되는 스택을 미리 채운다(호밍 전용 — 아래 WarmGrowth)
        public int repeat;          // 같은 셀을 순서의 앞뒤에 넣어 오염을 보는 용도

        public string StateKey => $"{skill}|{stage}|{(stage > 0 ? route : -1)}";
        public string Key => $"{StateKey}|L{level}|{treeMode}|{rig}|{scenario}|g{growthCasts}";
    }

    // 스킬별 강화 노드 id 접두사. 계약은 Meta.cs의 SkillEffects.Compute(case "id") — 대소문자가 섞여 있어
    // 비교는 대소문자를 무시한다(Sniping_TwoTarget / sniping_Crit 둘 다 스나이핑이다).
    private static readonly Dictionary<ActiveSkillId, string[]> EnhancePrefixes = new()
    {
        { ActiveSkillId.BasicAttack, new[] { "arrow_" } },
        { ActiveSkillId.Swing,       new[] { "swing_" } },
        { ActiveSkillId.GrapeToss,   new[] { "grape_" } },
        { ActiveSkillId.Orb,         new[] { "orb_" } },
        { ActiveSkillId.EagleDrop,   new[] { "eagle_" } },
        { ActiveSkillId.Lightning,   new[] { "thunder_" } },
        { ActiveSkillId.Shotgun,     new[] { "shotgun_" } },
        { ActiveSkillId.Sniping,     new[] { "sniping_" } },
        { ActiveSkillId.Whirlwind,   new[] { "tornado_" } },
        { ActiveSkillId.Homing,      new[] { "homing_" } },
        { ActiveSkillId.Rewind,      new[] { "rewind_" } },
    };

    // ⚠️ **시작 레벨 노드는 시험장에서 효과가 없다.** MetaBonuses.ArrowStartLevel을 실제로 소비하는 곳은
    //    MetaRunApplier.ApplyPlayerStats(Start)뿐이고 시험장은 그걸 안 부른다(레벨을 직접 세우므로 불러서도 안 된다).
    //    그래서 이 셋을 가진 스킬은 Full 모드에서도 **강화 노드가 실질 1개**다. 보고서가 이 사실을 같이 적어야
    //    "은별이 약하다"로 오독되지 않는다. 대신 Lv1 셀이 Lv3으로 올라가는 사고도 이 때문에 안 난다.
    public static readonly string[] InertNodesInGym = { "arrow_StartLev", "swing_StartLev", "grape_StartLev" };

    // 🔴 판 중 누적돼 세지는 스킬 = **호밍 미사일 하나뿐**이다(전수 조사 2026-09-27:
    //    PlayerSkills·PlayerPassives에서 판 중 증가하는 파워 카운터는 EquippedSkill.GrowthStacks가 유일).
    //    FireHoming: 발당 피해 ×(1 + 0.05 × 스택), 그리고 Homing_MissileNum(은별) 보유 시 20스택마다 미사일 +1.
    //    코드 주석의 실측: 우주 어려움 한 판에 600회 안팎 → 판 끝 피해가 첫 발의 수십 배.
    //    ⚠️ 그래서 45~60초 창만 재면 호밍을 **판 시작 상태로만** 재게 된다(10회 ≈ ×1.5).
    //    → 스택을 미리 채운 셀을 같이 돌려 **"스택 대비 파워 곡선"** 을 만든다. 실제로 몇 스택까지 가는지는
    //      정주행 로그의 skills[].casts가 답한다 — 둘을 곱해야 호밍의 실전 파워가 나온다.
    public static bool GrowsDuringRun(ActiveSkillId id) => id == ActiveSkillId.Homing;

    // 동반 릭의 기준 로드아웃. 진화 전 Lv10 순수 공격기 3개 — 시험 대상이 그중 하나면 대체한다.
    private static readonly ActiveSkillId[] CompanionPool =
        { ActiveSkillId.Whirlwind, ActiveSkillId.Orb, ActiveSkillId.EagleDrop, ActiveSkillId.Sniping };

    public static ActiveSkillId[] CompanionsFor(ActiveSkillId? test) =>
        CompanionPool.Where(c => !test.HasValue || c != test.Value).Take(3).ToArray();

    // 이 셀이 실제로 무엇을 쥐게 됐는지 — 기록에 남겨 "강제가 먹혔나"를 판정한다.
    public class Result
    {
        public bool ok;
        public string error;
        public int stage, route, level, growthStacks;
        public float baseCooldown, baseDamage;
        public List<string> enhanceNodes = new();
        public List<string> loadout = new();
    }

    public static Result Apply(Cell c)
    {
        var r = new Result();
        PlayerSkills ps = UnityEngine.Object.FindAnyObjectByType<PlayerSkills>();
        PlayerPassives pp = UnityEngine.Object.FindAnyObjectByType<PlayerPassives>();
        PlayerHealth ph = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        if (ps == null) { r.error = "PlayerSkills 없음"; return r; }

        try
        {
            ApplyTree(c, r);
            PlayerSkills.ResetRunState();
            PlayerPassives.ResetRunState();
            LockHealth(ph);
            SuppressLevelUps();

            List<EquippedSkill> skills = SkillList(ps);
            List<EquippedPassive> passives = pp != null ? PassiveList(pp) : null;
            skills.Clear();
            passives?.Clear();

            // 동반 릭: 기준 3개를 먼저(슬롯 Q~E), 시험 대상을 마지막 슬롯에. baseline은 기준 3개만.
            if (c.rig != "solo")
                foreach (ActiveSkillId comp in CompanionsFor(c.rig == "baseline" ? (ActiveSkillId?)null : c.skill))
                {
                    ps.AcquireSkill(comp);
                    ps.SetSkillStartLevel(comp, BalanceConstants.MaxSkillLevel);
                }

            if (c.rig != "baseline")
            {
                ps.AcquireSkill(c.skill);
                if (!ps.HasSkill(c.skill)) { r.error = "AcquireSkill 실패: " + c.skill; return r; }
                Evolve(ps, pp, skills, passives, c, r);
                if (!string.IsNullOrEmpty(r.error)) return r;

                // 진화가 표시 레벨을 1로 되돌렸다 — 목표 레벨은 그 뒤에 세운다.
                ps.SetSkillStartLevel(c.skill, c.level);
            }

            // 주입물 제거 · 슬롯 키 재배치. 남기는 것은 기준 3개(있으면) + 시험 대상뿐이다.
            Rebuild(skills, passives, c);

            EquippedSkill test = skills.FirstOrDefault(s => s.Id == c.skill);
            if (c.rig != "baseline" && test == null) { r.error = "재구성 뒤 시험 대상이 사라졌다"; return r; }
            if (test != null && c.growthCasts > 0) test.GrowthStacks = c.growthCasts;

            r.stage = test?.EvolutionStage ?? -1;
            r.route = test?.EvolutionStage > 0 ? test.Route : -1;
            r.level = test?.Level ?? -1;
            r.growthStacks = test?.GrowthStacks ?? 0;
            r.baseCooldown = test?.Cooldown ?? 0f;
            r.baseDamage = test?.Damage ?? 0f;
            r.loadout = skills.Select(s => $"{s.Id}:s{s.EvolutionStage}r{s.Route}L{s.Level}").ToList();
            r.ok = true;
            return r;
        }
        catch (Exception e)
        {
            r.error = e.GetType().Name + ": " + e.Message;
            return r;
        }
    }

    // ── 트리 ──
    private static void ApplyTree(Cell c, Result r)
    {
        BotTree.ResetSave();
        if (c.treeMode == TreeFull)
        {
            SkillTreeData tree = BotTree.Tree;
            var levels = new Dictionary<string, int>();
            if (tree != null && EnhancePrefixes.TryGetValue(c.skill, out string[] prefixes))
                foreach (SkillNode n in tree.nodes)
                {
                    if (n.type != SkillNodeType.SkillEnhance) continue;
                    if (!prefixes.Any(p => n.id.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    levels[n.id] = SkillTreeSave.MaxLevelOf(n);
                    r.enhanceNodes.Add(n.id);
                }
            WriteTreeLevels(levels);
        }
        ReapplyMeta();
    }

    // SkillTreeSave의 CSV 직렬화를 그대로 쓴다(포맷을 흉내 내지 않는다).
    // 🔴 **선행 조건을 일부러 건너뛴다** — 강화 노드의 조상은 일반 노드라, 정직하게 사면 그것들이 전 스킬에 얹혀 교란이 된다.
    private static void WriteTreeLevels(Dictionary<string, int> levels)
    {
        Type t = typeof(SkillTreeSave);
        string key = (string)t.GetField("CurrentKey", Stat).GetRawConstantValue();
        t.GetMethod("WriteLevels", Stat).Invoke(null, new object[] { key, levels });
    }

    // MetaRunApplier.Awake이 하는 것을 다시 한다 — 세이브를 바꿨으므로 MetaBonuses를 다시 계산해야 한다.
    // 🔴 ApplyPlayerStats(Start)는 **부르지 않는다** — 그게 시작 레벨 노드를 적용하는 곳이라, 부르면
    //    Lv1 셀이 조용히 Lv3이 된다(SetSkillStartLevel은 레벨을 내리지 못한다).
    private static void ReapplyMeta()
    {
        MetaBonuses.Reset();
        MetaRun.Reset();
        var applier = UnityEngine.Object.FindAnyObjectByType<MetaRunApplier>();
        if (applier != null)
        {
            SkillEffects.Totals totals = SkillEffects.Compute(BotTree.Tree);
            typeof(MetaRunApplier).GetField("totals", Inst).SetValue(applier, totals);
            typeof(MetaRunApplier).GetMethod("ApplyRuntimeBonuses", Inst).Invoke(applier, null);
        }
        // 🔴 게이트는 항상 열어 둔다 — 트리에 진화 해금 노드가 있으면 Compute가 false로 닫는다.
        //    시험장은 진화 상태를 축으로 삼는 자리라 게이트가 닫히면 격자의 4/5가 통째로 빈다.
        MetaBonuses.EvolutionUnlocked = true;
        MetaBonuses.Evolution2Unlocked = true;
    }

    // 셀마다 창을 끝까지 돌게 체력을 잠근다. OnPlayerHit은 **방어 적용 전** 값을 넘기므로 damageTaken 집계는 그대로 정확하다.
    private static void LockHealth(PlayerHealth ph)
    {
        if (ph == null) return;
        const int Locked = 100000000;
        typeof(PlayerHealth).GetField("maxHealth", Inst).SetValue(ph, Locked);
        typeof(PlayerHealth).GetProperty("CurrentHealth", Inst).GetSetMethod(true)
            .Invoke(ph, new object[] { Locked });
    }

    // 레벨업 모달은 timeScale을 0으로 만들어 창을 깨뜨린다. XP 배율을 0으로 두면 currentXP가 안 늘어
    // AddXP의 while 루프에 못 들어간다 → LevelUpUI.Show()에 도달하지 않는다.
    private static void SuppressLevelUps()
    {
        PlayerExperience px = PlayerExperience.Instance;
        if (px != null) typeof(PlayerExperience).GetField("xpMultiplier", Inst).SetValue(px, 0f);
    }

    // ── 진화 ──
    private static void Evolve(PlayerSkills ps, PlayerPassives pp,
        List<EquippedSkill> skills, List<EquippedPassive> passives, Cell c, Result r)
    {
        for (int stage = 1; stage <= c.stage; stage++)
        {
            InjectPrereqs(skills, passives, c.skill, c.route, stage);
            ps.SetSkillStartLevel(c.skill, BalanceConstants.MaxSkillLevel);
            ps.EvolveSkill(c.skill, c.route);
            EquippedSkill s = skills.FirstOrDefault(x => x.Id == c.skill);
            if (s == null || s.EvolutionStage < stage)
            {
                r.error = $"{c.skill} route{c.route} {stage}차 진화 실패 " +
                          $"(gate={MetaBonuses.EvolutionUnlocked}/{MetaBonuses.Evolution2Unlocked} " +
                          $"level={s?.Level} route={ps.IsRouteUnlocked(c.skill, c.route)} " +
                          $"key={ps.IsStage2KeyReady(c.skill, c.route)})";
                return;
            }
        }
    }

    // 관문 ③④를 만족시킬 최소한의 상태를 주입한다. 진화 뒤 Rebuild가 전부 걷어낸다.
    // 🔴 열쇠는 "1차 이상 진화 + 루트 일치"만 보므로(IsStage2KeyReady) 필드 두 개를 세운 더미로 충분하다 —
    //    열쇠를 실제로 진화시키면 그 열쇠의 열쇠까지 재귀로 필요해진다.
    private static void InjectPrereqs(List<EquippedSkill> skills, List<EquippedPassive> passives,
        ActiveSkillId id, int route, int stage)
    {
        var (rp, ra) = EvolutionRoutes.RoutePrereq(id, route);
        if (rp.HasValue) EnsurePassive(passives, rp.Value, 0, -1);
        if (ra.HasValue && ra.Value != id) EnsureSkill(skills, ra.Value, 0, -1);

        if (stage < 2) return;
        var (kp, ka, keyRoute) = EvolutionRoutes.Stage2Prereq(id, route);
        if (kp.HasValue) EnsurePassive(passives, kp.Value, 1, keyRoute);
        if (ka.HasValue && ka.Value != id) EnsureSkill(skills, ka.Value, 1, keyRoute);
    }

    private static void EnsureSkill(List<EquippedSkill> skills, ActiveSkillId id, int stage, int route)
    {
        EquippedSkill s = skills.FirstOrDefault(x => x.Id == id);
        if (s == null)
        {
            // 쿨 0의 더미가 한 프레임이라도 살아 있으면 발동한다 — Rebuild가 걷어낼 때까지 쿨을 크게 둔다.
            s = new EquippedSkill
            {
                Id = id, Key = UnityEngine.InputSystem.Key.None,
                Cooldown = 9999f, CooldownTimer = 9999f,
            };
            skills.Add(s);
        }
        if (stage > s.EvolutionStage) { s.EvolutionStage = stage; s.Route = route; }
    }

    private static void EnsurePassive(List<EquippedPassive> passives, PassiveSkillId id, int stage, int route)
    {
        if (passives == null) return;
        EquippedPassive p = passives.FirstOrDefault(x => x.Id == id);
        if (p == null) { p = new EquippedPassive { Id = id }; passives.Add(p); }
        if (stage > p.EvolutionStage) { p.EvolutionStage = stage; p.Route = route; }
    }

    // 주입물을 걷어내고 슬롯 키를 앞에서부터 다시 붙인다.
    // 🔴 패시브는 **전부** 지운다 — 주입한 것이든 아니든 시험장에 패시브가 남으면 딜 배율이 섞인다.
    private static void Rebuild(List<EquippedSkill> skills, List<EquippedPassive> passives, Cell c)
    {
        passives?.Clear();
        var keep = new HashSet<ActiveSkillId>();
        if (c.rig != "baseline") keep.Add(c.skill);
        if (c.rig != "solo")
            foreach (ActiveSkillId comp in CompanionsFor(c.rig == "baseline" ? (ActiveSkillId?)null : c.skill))
                keep.Add(comp);
        skills.RemoveAll(s => !keep.Contains(s.Id));

        var order = new[]
        {
            UnityEngine.InputSystem.Key.Q, UnityEngine.InputSystem.Key.W,
            UnityEngine.InputSystem.Key.E, UnityEngine.InputSystem.Key.R,
        };
        for (int i = 0; i < skills.Count; i++)
        {
            skills[i].Key = order[Mathf.Min(i, order.Length - 1)];
            skills[i].CooldownTimer = 0f;   // 앞 셀의 쿨을 물려받지 않는다
            skills[i].ReadySince = -1f;
        }
    }

    // ── 리플렉션 창구 ──
    private static List<EquippedSkill> SkillList(PlayerSkills ps) =>
        (List<EquippedSkill>)typeof(PlayerSkills).GetField("equippedSkills", Inst).GetValue(ps);

    private static List<EquippedPassive> PassiveList(PlayerPassives pp) =>
        (List<EquippedPassive>)typeof(PlayerPassives).GetField("equippedPassives", Inst).GetValue(pp);
}
#endif
