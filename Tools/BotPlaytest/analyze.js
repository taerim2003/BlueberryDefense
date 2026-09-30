#!/usr/bin/env node
// 봇 플레이테스트 분석기 — BotRuns/ 전체 이력 → BotRuns/report/data.json (+ report.html 템플릿으로 index.html)
//
// 사용: node Tools/BotPlaytest/analyze.js [--root BotRuns] [--quiet]
// 목표 수치: Tools/BotPlaytest/targets.json · 판단 기준: .claude/skills/balance
//
// 이터레이션 = BotRuns/iterations/NN.json 하나(루프가 씀). 그 파일의 sessions[]에 속한 세션의 판을 모아 지표를 낸다.
// iterations/가 비어 있으면 세션 하나를 이터레이션 하나로 본다(스모크·기준선 확인용).
//
// 🔴 기록 필드 이름은 Assets/Scripts/Bot/BotRecorder.cs 가 정한다 — 거기를 바꾸면 여기도 고친다.
'use strict';
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const argVal = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const PROJECT = path.resolve(__dirname, '..', '..');
const ROOT = path.resolve(PROJECT, argVal('--root', 'BotRuns'));
const QUIET = args.includes('--quiet');
const targets = JSON.parse(fs.readFileSync(path.join(__dirname, 'targets.json'), 'utf8'));

// ───────── 읽기 ─────────
const readJson = (p, d = null) => { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return d; } };
const readJsonl = (p) => {
  if (!fs.existsSync(p)) return [];
  return fs.readFileSync(p, 'utf8').split('\n').filter(l => l.trim()).map((l, i) => {
    try { return JSON.parse(l); } catch { console.warn(`[analyze] ${p}:${i + 1} 파싱 실패`); return null; }
  }).filter(Boolean);
};

function loadSessions() {
  if (!fs.existsSync(ROOT)) return [];
  return fs.readdirSync(ROOT, { withFileTypes: true })
    .filter(d => d.isDirectory() && /^\d{8}-\d{6}_/.test(d.name))
    .map(d => d.name).sort()
    .map(id => {
      const dir = path.join(ROOT, id);
      return {
        id,
        config: readJson(path.join(dir, 'config.json'), {}),
        status: readJson(path.join(dir, 'status.json'), {}),
        fingerprint: readJson(path.join(dir, 'fingerprint.json'), {}),
        cooldowns: readJson(path.join(dir, 'cooldowns.json'), null),
        runs: readJsonl(path.join(dir, 'runs.jsonl')),
        campaigns: readJsonl(path.join(dir, 'campaigns.jsonl')),
        gym: readJsonl(path.join(dir, 'gym.jsonl')),   // 스킬 시험장 셀 — 한 줄이 한 셀(스킬×상태×레벨×트리×시나리오)
      };
    });
}

function loadIterations(sessions) {
  const dir = path.join(ROOT, 'iterations');
  const files = fs.existsSync(dir) ? fs.readdirSync(dir).filter(f => /^\d+\.json$/.test(f)).sort((a, b) => parseInt(a) - parseInt(b)) : [];
  if (files.length === 0)
    return sessions.map((s, i) => ({ iteration: i, sessions: [s.id], hypothesis: null, synthetic: true }));
  const its = files.map(f => readJson(path.join(dir, f))).filter(Boolean);
  // 이름 규칙 itNN-<mode>로 세션을 이터레이션에 자동으로 묶는다(루프가 sessions[] 기록을 빠뜨려도 보고서가 비지 않게).
  for (const s of sessions) {
    const m = /^it(\d+)-/.exec(s.config.label || '');
    const it = m && its.find(x => x.iteration === parseInt(m[1], 10));
    if (!it) continue;
    it.sessions = it.sessions || [];
    if (!it.sessions.includes(s.id)) it.sessions.push(s.id);
  }
  return its;
}

// ───────── 통계 ─────────
const mean = a => a.length ? a.reduce((x, y) => x + y, 0) / a.length : null;
const median = a => { if (!a.length) return null; const s = [...a].sort((x, y) => x - y); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const std = a => { if (a.length < 2) return null; const m = mean(a); return Math.sqrt(a.reduce((x, y) => x + (y - m) ** 2, 0) / (a.length - 1)); };
const round = (v, d = 3) => v == null || !isFinite(v) ? null : Math.round(v * 10 ** d) / 10 ** d;
const goalKey = (map, asc) => `${map}:${asc}`;
const goalName = (map, asc) => (targets.goalOrder.find(g => g.map === map && g.ascension === asc) || {}).name || goalKey(map, asc);
// 판 진행률: 클리어 = 1, 사망 = 끝낸 스테이지 수 / 최종 스테이지
const progressOf = r => r.result === 'clear' ? 1 : Math.max(0, (r.stageReached - 1)) / Math.max(1, r.finalStage);

// ───────── G1·G3 (정주행) ─────────
function campaignMetrics(campaigns, runs) {
  const hardest = targets.goalOrder[targets.goalOrder.length - 1];
  const rows = campaigns.map(c => {
    const s3 = (c.goals || []).find(g => g.map === hardest.map && g.ascension === hardest.ascension);
    const s3At = s3 && s3.firstClear;
    const nodesAt = c.allNodesAt;
    let playMinutes = null, basis = null;
    if (s3At && nodesAt) {
      basis = s3At.cumGameTime >= nodesAt.cumGameTime ? s3At : nodesAt;
      playMinutes = (basis.cumGameTime + basis.cumPicks * targets.secondsPerPick + basis.cumRuns * targets.secondsBetweenRuns) / 60;
    }
    const lastRun = runs.filter(r => r.mode === 'campaign' && r.campaign === c.campaign).slice(-1)[0];
    return {
      campaign: c.campaign, outcome: c.outcome, runs: c.runs,
      gameMinutes: round(c.cumGameTime / 60, 1),
      playMinutes: round(playMinutes, 1),
      s3ClearMinutes: s3At ? round(s3At.cumGameTime / 60, 1) : null,
      allNodesMinutes: nodesAt ? round(nodesAt.cumGameTime / 60, 1) : null,
      nodesOwnedEnd: lastRun ? lastRun.nodesOwnedAfter : null,
      nodesTotal: lastRun ? lastRun.nodesTotal : null,
      // 판당 선택 횟수 — 플레이시간의 3분의 1이 여기서 나온다(2026-09-18 실측). 예측 대조에 쓴다.
      picksPerRun: lastRun && lastRun.cumRuns ? round(lastRun.cumPicks / lastRun.cumRuns, 1) : null,
      goals: (c.goals || []).map(g => ({ key: goalKey(g.map, g.ascension), name: goalName(g.map, g.ascension), attempts: g.attempts, cleared: !!g.firstClear, clearMinutes: g.firstClear ? round(g.firstClear.cumGameTime / 60, 1) : null })),
    };
  });

  const perGoal = targets.goalOrder.map(t => {
    const k = goalKey(t.map, t.ascension);
    const cleared = rows.map(r => r.goals.find(g => g.key === k)).filter(g => g && g.cleared);
    const clearRuns = runs.filter(r => r.mode === 'campaign' && goalKey(r.map, r.ascension) === k && r.result === 'clear' && !r.farming);
    return {
      key: k, name: t.name,
      meanAttempts: round(mean(cleared.map(g => g.attempts)), 2),
      attemptsStd: round(std(cleared.map(g => g.attempts)), 2),
      clearedCampaigns: cleared.length, campaigns: rows.length,
      clearRunGameMinutes: round(mean(clearRuns.map(r => r.gameTime / 60)), 1),
      // 🔴 사람 체감 판 길이(2026-09-27). gameTime은 레벨업 모달 동안 timeScale=0이라 **전투 시간만** 잰다 —
      //    사람은 그 사이에 카드를 고르고 있으므로 픽 수 x secondsPerPickHuman을 더해야 체감과 맞는다.
      //    G1(playMinutes)은 secondsPerPick(3)을 계속 써서 과거 회차와의 추이가 끊기지 않게 둔다.
      clearRunPlayMinutes: round(mean(clearRuns.map(r =>
        (r.gameTime + (r.picks ? r.picks.length : 0) * (targets.secondsPerPickHuman || targets.secondsPerPick)) / 60)), 1),
      runMinutesTarget: targets.runMinutesByTier ? (targets.runMinutesByTier[String(t.ascension)] || null) : null,
    };
  });
  // 진행 곡선: 캠페인마다 판이 끝날 때의 (추정 플레이 분, 보유 노드 비율), 목표 첫 클리어 지점 표시.
  // 요약 줄(campaigns.jsonl)이 없는 중단 세션도 판 기록만으로 곡선을 그린다.
  const campaignIds = [...new Set(runs.filter(r => r.mode === 'campaign').map(r => r.campaign))];
  const curves = campaignIds.map(id => {
    const c = { campaign: id };
    const rs = runs.filter(r => r.mode === 'campaign' && r.campaign === c.campaign);
    const seen = new Set();
    return {
      campaign: c.campaign,
      points: rs.map(r => {
        const k = goalKey(r.map, r.ascension);
        const firstClear = r.result === 'clear' && !r.farming && !seen.has(k);
        if (firstClear) seen.add(k);
        return {
          minutes: round((r.cumGameTime + r.cumPicks * targets.secondsPerPick + r.cumRuns * targets.secondsBetweenRuns) / 60, 2),
          nodes: r.nodesTotal ? round(r.nodesOwnedAfter / r.nodesTotal) : null,
          clear: firstClear ? goalName(r.map, r.ascension) : null,
        };
      }),
    };
  });

  const att = perGoal.map(g => g.meanAttempts).filter(v => v != null);
  const cv = att.length >= 2 ? std(att) / mean(att) : null;
  const play = rows.map(r => r.playMinutes).filter(v => v != null);
  return {
    campaigns: rows, perGoal, curves,
    G1: { value: round(median(play), 1), spread: round(std(play), 1), target: targets.playtimeMinutes, complete: play.length, total: rows.length,
          pass: play.length > 0 && median(play) >= targets.playtimeMinutes[0] && median(play) <= targets.playtimeMinutes[1] },
    // G3 = 난이도 밸런스의 본체. 균일성(변동계수)만 보면 "다 똑같이 10판"도 통과하므로 **절대 범위**를 같이 본다(사용자 지시 2026-09-18).
    G3: (() => {
      // 밴드가 구간별로 나뉜다(사용자 결정 2026-09-23): 앞 earlyGoalCount개는 Early, 나머지는 Late.
      // 옛 단일 밴드(attemptsPerGoal)는 폴백으로 남긴다.
      const early = targets.attemptsPerGoalEarly || targets.attemptsPerGoal || null;
      const late = targets.attemptsPerGoalLate || targets.attemptsPerGoal || null;
      const split = targets.earlyGoalCount != null ? targets.earlyGoalCount : perGoal.length;
      const bandOf = i => (i < split ? early : late);
      const outOfBand = perGoal.filter((g, i) => {
        const b = bandOf(i);
        return b && g.meanAttempts != null && (g.meanAttempts < b[0] || g.meanAttempts > b[1]);
      });
      return {
        value: round(cv, 3), target: targets.attemptsCvMax, goalsMeasured: att.length,
        band: early, bandEarly: early, bandLate: late, earlyGoalCount: split,
        outOfBand: outOfBand.map((g) => ({ name: g.name, attempts: g.meanAttempts, band: bandOf(perGoal.indexOf(g)) })),
        meanAttempts: round(mean(att), 2),
        pass: cv != null && cv <= targets.attemptsCvMax && outOfBand.length === 0,
      };
    })(),
  };
}

// ───────── G2·G4·G6 (probe) ─────────
function probeMetrics(runs) {
  const probe = runs.filter(r => r.mode === 'probe');
  const ref = probe.filter(r => r.treeRatio < 1);
  const perGoal = targets.goalOrder.map(t => {
    const k = goalKey(t.map, t.ascension);
    const rs = ref.filter(r => goalKey(r.map, r.ascension) === k);
    const byChar = {};
    for (const r of rs) (byChar[r.character] ||= []).push(progressOf(r));
    const charD = Object.fromEntries(Object.entries(byChar).map(([c, v]) => [c, round(1 - mean(v))]));
    const byRatio = {};
    for (const r of rs) (byRatio[r.treeRatio] ||= []).push(progressOf(r));
    return {
      key: k, name: t.name, n: rs.length,
      D: rs.length ? round(1 - mean(rs.map(progressOf))) : null,
      clearRate: rs.length ? round(rs.filter(r => r.result === 'clear').length / rs.length) : null,
      byRatio: Object.fromEntries(Object.entries(byRatio).map(([k2, v]) => [k2, round(1 - mean(v))])),
      byCharacter: charD,
      charSpread: Object.keys(charD).length >= 2 ? round(Math.max(...Object.values(charD)) - Math.min(...Object.values(charD))) : null,
    };
  });

  const relations = [];
  for (let i = 0; i < targets.orderRelations.length; i++) {
    const a = perGoal[i], b = perGoal[i + 1], op = targets.orderRelations[i];
    const gap = a.D != null && b.D != null ? b.D - a.D : null;
    const need = op === '<' ? targets.orderStrictGap : targets.orderLooseGap;
    relations.push({ a: a.name, op, b: b.name, gap: round(gap), need, pass: gap != null && gap >= need });
  }
  const measured = relations.filter(r => r.gap != null);

  const hardest = targets.goalOrder[targets.goalOrder.length - 1];
  const full = probe.filter(r => r.treeRatio >= 1 && r.map === hardest.map && r.ascension === hardest.ascension);
  const fullByChar = {};
  for (const r of full) (fullByChar[r.character] ||= []).push(r.result === 'clear' ? 1 : 0);
  const fullRates = Object.fromEntries(Object.entries(fullByChar).map(([c, v]) => [c, { rate: round(mean(v)), n: v.length }]));
  const minRate = Object.values(fullRates).length ? Math.min(...Object.values(fullRates).map(x => x.rate)) : null;

  const spreads = perGoal.map(g => g.charSpread).filter(v => v != null);
  const charMeanD = {};
  for (const g of perGoal) for (const [c, d] of Object.entries(g.byCharacter)) (charMeanD[c] ||= []).push(d);

  return {
    perGoal, relations,
    // probe를 안 돈 회차는 value null — "0/8"로 적으면 보고서가 서열 악화로 그린다
    G2: { value: measured.length ? `${measured.filter(r => r.pass).length}/${relations.length}` : null, pass: measured.length === relations.length && measured.every(r => r.pass), measured: measured.length },
    G4: { value: round(minRate), byCharacter: fullRates, target: targets.fullTreeClearRateMin, pass: minRate != null && minRate >= targets.fullTreeClearRateMin && Object.keys(fullRates).length >= 3 },
    G6: { value: spreads.length ? round(Math.max(...spreads)) : null, target: targets.characterSpreadMax, pass: spreads.length > 0 && Math.max(...spreads) <= targets.characterSpreadMax,
          characterMeanD: Object.fromEntries(Object.entries(charMeanD).map(([c, v]) => [c, round(mean(v))])) },
  };
}

// ───────── G5 스킬 ─────────
// 상태 = 스킬 × 진화 차수(0/1/2) × 루트. 판 안에서 진화하면 판 종료 시점 상태로 집계한다(진화체 기여가 섞인다 — 시험장(gym)이 생기면 교체).
function skillMetrics(runs, enemyTypes) {
  const states = {};
  const offered = {}, picked = {};
  for (const r of runs) {
    const owned = (r.skills || []).filter(s => s.owned);
    const n = owned.length || 1;
    for (const s of owned) {
      const key = `${s.id}|${s.evoStage}|${s.evoStage > 0 ? s.route : -1}`;
      const st = (states[key] ||= { skill: s.id, evoStage: s.evoStage, route: s.evoStage > 0 ? s.route : -1, n: 0, power: [], dps: [], antiAir: [], boss: [], shield: [], contactPerMin: [], overkill: [], early: [], progress: [], castRatio: [] });
      const t = Math.max(1, s.ownedTime);
      st.n++;
      st.power.push(s.share * n);
      st.dps.push(s.effDamage / t);
      // 발동률 = 실제 발동 / 쿨만 보면 가능했던 발동(전역 쿨 0.4초 하한). 1보다 한참 낮으면 슬롯 굶주림·대상 없음 의심.
      if (s.casts > 0 && s.baseCdLast != null) st.castRatio.push(s.casts / (t / Math.max(0.4, s.baseCdLast * (s.cdMultLast || 1))));
      let air = 0, boss = 0, shield = 0;
      for (const [e, v] of Object.entries(s.effByEnemy || {})) {
        const ty = enemyTypes[e] || {};
        if (ty.flying) air += v;   // 대공 축 = 비행 적에게 준 피해(옛 antiAir 태그 폐지, flying은 예전 세션에도 있다)
        if (ty.boss || e === 'Boss' || e.includes('Airship') || e.includes('Regent')) boss += v;
        if (ty.shield) shield += v;
      }
      st.antiAir.push(air / t); st.boss.push(boss / t); st.shield.push(shield / t);
      st.contactPerMin.push(s.contactKills / (t / 60));
      st.overkill.push(s.rawDamage > 0 ? s.overkill / s.rawDamage : 0);
      const earlyStages = (r.stages || []).filter(x => x.stage <= 5);
      const earlyTime = earlyStages.reduce((a, x) => a + x.gameTime, 0);
      if (s.acquiredStage >= 0 && s.acquiredStage <= 3 && earlyTime > 0)
        st.early.push(earlyStages.reduce((a, x) => a + ((x.effBySkill || {})[s.id] || 0), 0) / earlyTime);
      st.progress.push(progressOf(r));
    }
    for (const p of r.picks || []) {
      if (p.screen !== 'levelUp') continue;
      for (const o of p.offered || []) if (o.kind === 'active' && o.isNew) offered[o.id] = (offered[o.id] || 0) + 1;
      if (p.picked && p.picked.kind === 'active' && p.picked.isNew) picked[p.picked.id] = (picked[p.picked.id] || 0) + 1;
    }
  }

  const AXES = ['power', 'dps', 'antiAir', 'boss', 'shield', 'contactPerMin', 'early'];
  const rows = Object.values(states).map(s => {
    const o = { skill: s.skill, evoStage: s.evoStage, route: s.route, n: s.n, meanProgress: round(mean(s.progress)), overkill: round(mean(s.overkill)), castRatio: round(mean(s.castRatio)) };
    for (const a of AXES) o[a] = round(mean(s[a]));
    o.offeredNew = offered[s.skill] || 0; o.pickedNew = picked[s.skill] || 0;
    return o;
  });

  // 그룹(진화 차수)별 판정. 표본 부족(n < noiseMinSamples)인 상태는 판정에서 빼고 표시만.
  const groups = {};
  for (const g of [0, 1, 2]) {
    const members = rows.filter(r => r.evoStage === g && r.n >= targets.noiseMinSamples && r.skill !== 'Other');
    const med = {}; for (const a of AXES) med[a] = median(members.map(m => m[a]).filter(v => v != null));
    for (const m of members) {
      m.norm = Object.fromEntries(AXES.map(a => [a, med[a] > 0 && m[a] != null ? round(m[a] / med[a], 2) : null]));
    }
    const dominated = [];
    for (const a of members) for (const b of members) {
      if (a === b) continue;
      const core = ['dps', 'antiAir', 'boss', 'shield', 'contactPerMin', 'early'];
      const ok = core.every(x => a[x] != null && b[x] != null && a[x] > b[x]);
      if (ok) dominated.push({ dominant: `${a.skill}/${a.route}`, dominated: `${b.skill}/${b.route}` });
    }
    const noStrength = members.filter(m => {
      return !['dps', 'antiAir', 'boss', 'shield', 'contactPerMin', 'early'].some(a => {
        const vals = members.map(x => x[a]).filter(v => v != null).sort((x, y) => y - x);
        const cut = vals[Math.max(0, Math.ceil(vals.length / 3) - 1)];
        return m[a] != null && vals.length >= 3 && m[a] >= cut;
      });
    }).map(m => `${m.skill}/${m.route}`);
    const outOfBand = members.filter(m => m.norm.power != null && (m.norm.power < targets.skillPowerBand[0] || m.norm.power > targets.skillPowerBand[1]))
      .map(m => ({ state: `${m.skill}/${m.route}`, power: m.norm.power }));
    groups[g] = { members: members.length, dominance: dominated, noStrength, outOfBand,
      pass: members.length >= 3 && dominated.length === 0 && noStrength.length === 0 && outOfBand.length === 0 };
  }
  const measured = Object.values(groups).filter(g => g.members >= 3);
  return { rows, groups, G5: { value: `${measured.filter(g => g.pass).length}/${measured.length} 그룹`, pass: measured.length > 0 && measured.every(g => g.pass) } };
}

// ───────── G5 스킬 시험장 (gym) ─────────
// 실전 로그(위 skillMetrics)와 달리 **상태를 강제**해서 재므로 2차 진화까지 표본이 고르게 찬다.
// 🔴 판정 전에 셀 품질을 먼저 본다. 셋 중 하나라도 걸린 셀은 **숫자가 있어도 못 쓴다**:
//    ① rigOk=false          상태 강제가 실패했다(그 조합이 도달 불가거나 릭이 깨졌다)
//    ② residualAtStart>0    측정 시작에 앞 셀의 것이 남아 있었다 = **진짜 오염.** 같은 스킬끼리의 오염도 여기서만 잡힌다
//                           (실측 2026-09-27: 오브 제단 OrbAltar가 다음 오브 셀로 넘어갔다)
//    ③ arenaLimited         무대가 천장이었다 — 더 센 스킬도 같은 숫자를 낸다
//
// 🔴 **`foreignDamage > 0`은 오염이 아니다** — 그걸로 걸러내면 멀쩡한 칸 57개를 버린다(실측 2026-09-27).
//    게임은 딜 출처를 **오브젝트 종류로** 태그한다: `Whirlwind.cs`가 `ActiveSkillId.Whirlwind`를 하드코딩하고,
//    독수리투하 R1은 자기 진화 효과로 **미니 회오리를 뿜는다**(PlayerSkills.cs의 SpawnWhirlwind(isMini:true)).
//    그래서 독수리 셀에 "회오리 딜"이 찍히는데, 그건 **독수리가 낸 딜**이다(residual = 0이 그 증거다).
//    → 단독 릭은 로드아웃에 스킬이 하나뿐이므로 **셀의 전체 딜이 곧 그 스킬의 출력**이다. 판정은 dpsAll로 한다.
//    ⚠️ 같은 귀속 규칙이 실전 로그(skillMetrics)에도 적용된다 — 거기서는 회오리 지분이 독수리 R1의 몫을 흡수해 왔다.
function gymMetrics(cells) {
  if (!cells.length) return null;

  // 셀 품질. arenaLimited = 처리량 무대인데 끝에 생존 수가 상한에 한참 못 미친다 = 스킬이 무대를 앞질렀다.
  // 🔴 `carrier`는 예외다 — UFO는 투하를 마치면 **스스로 화면 밖으로 빠져나간다**(escape). 그래서 스킬이 세든
  //    약하든 끝 생존 수가 상한에 한참 못 미치고, 이 판정식이 전부 "무대 한계"로 오인한다(실측: 111칸 중 67칸).
  //    carrier의 지표는 처리량이 아니라 **escapes**(투하 전에 못 잡은 몫)다.
  const escapeBased = new Set(['carrier']);
  const limited = c => c.arenaKind === 'throughput' && !escapeBased.has(c.scenario)
    && c.maxAlive > 0 && c.aliveAtEnd < c.maxAlive * 0.8;
  for (const c of cells) {
    c.arenaLimited = limited(c);
    c.usable = c.rigOk !== false && !(c.residualAtStart > 0) && !c.arenaLimited;
    c.own = (c.skills || []).filter(s => s.owned);
    c.testSkill = c.own.find(s => s.id === c.skill) || null;
    // 이 셀에서 스킬로 귀속된 전체 딜(이름이 달라도 포함). 단독 릭에서는 곧 시험 대상의 출력이다.
    c.allSkillDamage = (c.skills || []).filter(s => s.id !== 'Other').reduce((a, s) => a + s.effDamage, 0);
    // 🔴 **앞 셀의 static 상태가 넘어온 딜을 가른다.** `residualAtStart`는 오브젝트만 세므로 이걸 못 잡는다
    //    (실측 2026-09-27: it37 baseline 25칸 중 15칸이 앞 낙뢰 셀의 딜을 안고 있었고 최대 54.7%였다.
    //     원인은 `gymSettleSeconds 1.5` < `Prog_Lightning.baseDuration 10`이다).
    //    동반·baseline 릭은 로드아웃이 명시적이므로 **로드아웃에 없는 이름의 딜은 이 셀의 것이 아니다.**
    //    단독 릭은 그렇게 못 가른다 — 독수리 R1의 미니 회오리처럼 자기 출력이 남의 이름으로 찍힌다.
    const inLoadout = new Set((c.loadout || []).map(s => String(s).split(':')[0]));
    c.outsideDamage = c.rig === 'solo' ? 0
      : (c.skills || []).filter(s => s.id !== 'Other' && !inLoadout.has(s.id)).reduce((a, s) => a + s.effDamage, 0);
    c.contaminated = c.outsideDamage > 0.01 * Math.max(1, c.allSkillDamage);
    // 파티 출력 = 로드아웃이 낼 수 있는 딜만. 동반 이득의 분자·분모가 이 값이다.
    c.partyDamage = c.allSkillDamage - c.outsideDamage;
  }
  const quality = {
    cells: cells.length,
    rigFailed: cells.filter(c => c.rigOk === false).map(c => ({ cell: c.cellKey, error: c.rigError })),
    // 🔴 진짜 오염. 같은 스킬끼리의 오염도 여기서만 잡힌다.
    residual: cells.filter(c => c.residualAtStart > 0)
      .map(c => ({ cell: c.cellKey, residual: c.residualAtStart, names: c.residualNames || null })),
    // 정보용 — 오염이 아니라 **귀속**이다. 그 스킬이 다른 스킬 이름으로 낸 딜.
    crossTagged: cells.filter(c => c.foreignDamage > 0 && !(c.residualAtStart > 0)).map(c => ({
      cell: c.cellKey, underOtherName: round(c.foreignDamage),
      share: c.allSkillDamage > 0 ? round(c.foreignDamage / c.allSkillDamage, 2) : null,
      names: (c.skills || []).filter(s => !s.owned && s.id !== 'Other' && s.effDamage > 0).map(s => s.id),
    })),
    arenaLimited: cells.filter(c => c.arenaLimited).map(c => c.cellKey),
    usable: cells.filter(c => c.usable).length,
    // 🔴 앞 셀에서 넘어온 딜. 셀을 버리지 않고 **빼서** 쓴다(빼면 route 라벨별 baseline 차이가 20~88% → 1~13%로 접힌다).
    //    값이 있으면 `gymSettleSeconds`가 그 스킬의 지속시간보다 짧다는 뜻이다.
    carryOver: cells.filter(c => c.contaminated).map(c => ({
      cell: c.cellKey, outside: round(c.outsideDamage),
      share: round(c.outsideDamage / Math.max(1, c.allSkillDamage), 3),
      names: (c.skills || []).filter(s => s.id !== 'Other' && s.effDamage > 0
        && !new Set((c.loadout || []).map(x => String(x).split(':')[0])).has(s.id)).map(s => s.id),
    })),
  };

  // 같은 셀을 격자 앞뒤에 두 번 넣었으면(gymRepeats>1) 그 차이가 **이 측정의 노이즈 폭**이다.
  // 🔴 이 값을 모르면 셀 사이 차이를 해석할 수 없다(balance-loop §3①과 같은 규칙).
  const byKey = {};
  for (const c of cells) (byKey[c.cellKey] = byKey[c.cellKey] || []).push(c);
  const reps = Object.values(byKey).filter(v => v.length > 1).map(v => {
    const e = v.map(c => c.allSkillDamage);
    const m = mean(e);
    return m > 0 ? (Math.max(...e) - Math.min(...e)) / m : 0;
  });
  const noise = { pairs: reps.length, maxSpread: reps.length ? round(Math.max(...reps), 3) : null, meanSpread: reps.length ? round(mean(reps), 3) : null };

  // 셀 집계(반복 평균). dps 무대 = 단일 대상 DPS, 처리량 무대 = 초당 처리 체력.
  const rows = Object.entries(byKey).map(([key, v]) => {
    const f = v[0];
    const t = c => Math.max(1, c.cellGameTime || c.window || 1);
    const mine = c => (c.testSkill ? c.testSkill.effDamage : 0);
    return {
      cellKey: key, skill: f.skill, evoStage: f.evoStage, route: f.evoStage > 0 ? f.route : -1,
      level: f.askLevel, treeMode: f.treeMode, rig: f.rig, scenario: f.scenario,
      growthCasts: f.growthCasts || 0, arenaKind: f.arenaKind, n: v.length,
      usable: v.every(c => c.usable),
      // 🔴 판정에 쓰는 값. 단독 릭은 로드아웃이 스킬 하나뿐이라 셀의 전체 딜이 곧 그 스킬의 출력이다 —
      //    이름이 다른 딜(독수리 R1의 미니 회오리 등)까지 포함해야 그 스킬을 옳게 잰다.
      dps: round(mean(v.map(c => c.allSkillDamage / t(c))), 1),
      // 그중 **자기 이름으로** 찍힌 몫. 낮으면 그 스킬의 출력이 남의 이름으로 집계된다는 신호다(실전 로그 해석에 필요).
      dpsOwnName: round(mean(v.map(c => mine(c) / t(c))), 1),
      ownNameShare: round(mean(v.map(c => (c.allSkillDamage > 0 ? mine(c) / c.allSkillDamage : 1))), 2),
      // 동반 릭에서 baseline과 비교할 값 — **로드아웃이 낸 딜만** 센다(앞 셀에서 넘어온 딜을 뺀다).
      dpsAll: round(mean(v.map(c => c.partyDamage / t(c))), 1),
      carryOver: round(mean(v.map(c => c.outsideDamage / t(c))), 1),
      contaminated: v.some(c => c.contaminated),
      kills: round(mean(v.map(c => c.kills)), 1),
      // 🔴 접촉선 처치·오버킬도 **스킬로 귀속된 전체**를 센다 — 자기 이름 몫만 세면 독수리 R1처럼
      //    출력의 절반 이상이 다른 이름으로 찍히는 스킬을 절반만 재게 된다.
      contactKills: round(mean(v.map(c => (c.skills || []).filter(s => s.id !== 'Other')
        .reduce((a, s) => a + (s.contactKills || 0), 0))), 1),
      escapes: round(mean(v.map(c => c.escapes || 0)), 1),
      damageTaken: round(mean(v.map(c => c.damageTaken)), 0), // 못 막은 몫 — 체력을 잠갔으니 죽음 대신 이걸 본다
      overkill: round(mean(v.map(c => {
        const rows2 = (c.skills || []).filter(s => s.id !== 'Other');
        const raw = rows2.reduce((a, s) => a + s.rawDamage, 0);
        return raw > 0 ? rows2.reduce((a, s) => a + s.overkill, 0) / raw : 0;
      })), 3),
      // 발동 수는 시전(TryUseSkill) 기준이라 자기 이름 그대로가 맞다 — 미니 회오리는 시전이 아니다.
      casts: round(mean(v.map(c => (c.testSkill ? c.testSkill.casts : 0))), 1),
      baseCd: f.gotBaseCooldown, saturation: round(mean(v.map(c => c.saturation || 0)), 2),
      aliveEnd: round(mean(v.map(c => c.aliveAtEnd)), 0), maxAlive: f.maxAlive || 0,
      enhanceNodes: f.enhanceNodes || [],
    };
  });

  const solo = rows.filter(r => r.rig === 'solo' && r.growthCasts === 0);
  const scenarios = [...new Set(solo.map(r => r.scenario))].sort();

  // 🔴 **동반 릭으로 재는 스킬은 단독 판정에서 뺀다.** 되감기는 남의 쿨을 당기고, 낙뢰·집중산탄은 버프기라
  //    혼자 두면 딜이 0에 가깝다 — 그걸 단독 표에 넣으면 "무강점"·"지배당함"이 **구조적으로 보장**되어
  //    매번 같은 실패가 뜨고 손쓸 곳이 없다(실측 2026-09-27: 그룹1 지배 56쌍 중 대부분이 이 셋 때문이었다).
  //    이 셋의 판정은 아래 `companionVerdict`가 따로 한다 — baseline 대비 전체 딜 증가분이 그 스킬의 값이다.
  //    (`SKILL_AXES`의 "근거리는 지분이 아니라 contactPerMin으로 본다"와 같은 원칙: 스킬마다 맞는 축이 다르다.)
  //    목록의 원본은 `BotPilot.CompanionTestSkills`이고 셀 헤더의 `companionPool`로 실려 온다 — 여기 또 적지 않는다
  //    (동반 셀은 격자 맨 뒤라, 돌아온 셀만 보고 유도하면 그 전까지 판정이 틀린다).
  const companionMeasured = new Set(
    cells.flatMap(c => String(c.companionPool || '').split(',').filter(Boolean))
      .concat(rows.filter(r => r.rig === 'companion').map(r => r.skill)));
  // 🔴 제외는 **스킬 단위가 아니라 상태 단위**다. 동반 풀에 있어도 딜을 내는 상태는 단독 표에 남긴다 —
  //    스킬로 빼면 전 무대에서 딜을 내는 집중산탄 네 상태·낙뢰 R1 두 상태가 파워 표에서 통째로 사라진다
  //    (실측 2026-09-27: 동반 풀 15개 상태 중 어느 무대에서도 딜이 0인 것은 아홉 개뿐이다).
  const stateOf = r => `${r.skill}|${r.evoStage}|${r.route}`;
  const dealsDamage = new Set(solo.filter(r => r.usable && r.dps > 0).map(stateOf));
  const excludedStates = [...new Set(solo.filter(r => companionMeasured.has(r.skill) && !dealsDamage.has(stateOf(r))).map(stateOf))];
  const excludedSet = new Set(excludedStates);
  const soloJudged = solo.filter(r => !excludedSet.has(stateOf(r)));

  // 🔴 G5 판정은 **같은 진화 차수 안에서** 한다(사용자 요구: 기본끼리·1차끼리·2차끼리).
  //    시나리오마다 따로 판정한다 — "무리에선 세지만 대공은 못 한다"가 정상이고, 그게 장단점이다.
  const groups = {};
  const scnMedian = {};   // [차수][무대] = 그 무대의 중앙 dps. 파워의 분모는 이 한 곳에서만 만든다.
  for (const g of [0, 1, 2]) {
    const perScenario = {};
    for (const scn of scenarios) {
      const m = soloJudged.filter(r => r.evoStage === g && r.scenario === scn && r.usable && r.level === 10);
      // 🔴 **파워의 정의는 여기 한 곳이다** — 보고서 §01은 계산하지 않고 아래 `power` 표를 읽는다.
      //    중앙값은 딜이 0인 칸을 빼고 세운다. 0을 분모에 넣으면 같은 셀의 파워가 문서 안에 여러 벌 생긴다.
      const scored = m.filter(r => r.dps > 0);
      if (scored.length < 3) { perScenario[scn] = { members: m.length, scored: scored.length, note: '표본 부족' }; continue; }
      const med = median(scored.map(r => r.dps));
      (scnMedian[g] = scnMedian[g] || {})[scn] = med;
      for (const r of m) r.norm = med > 0 ? round(r.dps / med, 2) : null;
      const band = targets.skillPowerBand;
      perScenario[scn] = {
        members: m.length, median: round(med, 1),
        top: m.slice().sort((a, b) => b.dps - a.dps).slice(0, 3).map(r => ({ skill: r.skill, route: r.route, dps: r.dps, norm: r.norm })),
        bottom: m.slice().sort((a, b) => a.dps - b.dps).slice(0, 3).map(r => ({ skill: r.skill, route: r.route, dps: r.dps, norm: r.norm })),
        outOfBand: m.filter(r => r.norm != null && (r.norm < band[0] || r.norm > band[1]))
          .map(r => ({ state: `${r.skill}/${r.route}`, norm: r.norm })),
      };
    }
    // G5a 지배 금지 — 한 상태가 **모든 시나리오에서** 다른 상태보다 높으면 장단점이 없다.
    const states = [...new Set(soloJudged.filter(r => r.evoStage === g && r.usable && r.level === 10).map(r => `${r.skill}/${r.route}`))];
    // 🔴 `evoStage === g`를 빠뜨리면 E1과 E2의 지배·무강점 판정이 **둘 다 E1 행으로** 계산된다(실측 2026-09-27).
    const dpsOf = (st, scn) => {
      const r = soloJudged.find(x => `${x.skill}/${x.route}` === st && x.evoStage === g
        && x.scenario === scn && x.usable && x.level === 10);
      return r ? r.dps : null;
    };
    const dominance = [];
    for (const a of states) for (const b of states) {
      if (a === b) continue;
      const pairs = scenarios.map(s => [dpsOf(a, s), dpsOf(b, s)]).filter(([x, y]) => x != null && y != null);
      if (pairs.length >= 3 && pairs.every(([x, y]) => x > y)) dominance.push({ dominant: a, dominated: b, scenarios: pairs.length });
    }
    // G5b 쓸모 보장 — 모든 상태가 시나리오 하나 이상에서 상위 1/3이어야 한다.
    const noStrength = states.filter(st => !scenarios.some(scn => {
      const vals = states.map(s => dpsOf(s, scn)).filter(v => v != null).sort((x, y) => y - x);
      if (vals.length < 3) return false;
      const cut = vals[Math.max(0, Math.ceil(vals.length / 3) - 1)];
      const v = dpsOf(st, scn);
      return v != null && v >= cut;
    }));
    groups[g] = {
      states: states.length, perScenario, dominance, noStrength,
      // 단독 표에서 뺀 **상태** — 어느 무대에서도 딜이 0인 동반 릭 상태만. 판정은 `companionVerdict`가 한다.
      excluded: excludedStates,
      excludedSkills: [...companionMeasured],   // 동반 릭으로 따로 판정하는 스킬(정보용)
      pass: states.length >= 3 && dominance.length === 0 && noStrength.length === 0,
    };
  }

  // 🔴 판 경과에 따라 세지는 스킬(호밍의 누적 스택)은 `growthCasts = 0` 셀만으로 밸런스를 말할 수 없다 —
  //    같은 표가 호밍을 미달과 초과로 동시에 판정한다. 하드코딩하지 않고 **측정으로 가른다**:
  //    같은 상태·무대에서 캐스트를 쌓은 셀의 dps가 0캐스트보다 2배 이상이면 성장 의존으로 표시한다.
  const growthDependent = new Set();
  for (const r of rows.filter(x => x.rig === 'solo' && x.growthCasts > 0 && x.usable)) {
    const z = rows.find(x => x.rig === 'solo' && x.growthCasts === 0 && x.usable && x.level === r.level
      && x.skill === r.skill && x.evoStage === r.evoStage && x.route === r.route && x.scenario === r.scenario);
    if (z && z.dps > 0 && r.dps / z.dps >= 2) growthDependent.add(r.skill);
  }

  // 🔴 파워 표의 **원본**. 보고서 §01이 이걸 그대로 읽는다 — 같은 값을 두 곳에서 따로 계산하지 않는다.
  //    분모는 위 `scnMedian`(딜 0인 칸 제외, 그 무대에 셋 이상)이고, 파워는 무대별 배율의 중앙값이다.
  const powerTable = [];
  for (const g of [0, 1, 2]) {
    const states = [...new Set(soloJudged.filter(r => r.evoStage === g && r.usable && r.level === 10)
      .map(r => `${r.skill}|${r.route}`))];
    for (const st of states) {
      const [skill, routeStr] = st.split('|');
      const route = Number(routeStr);
      const per = scenarios.map(scn => {
        const r = soloJudged.find(x => x.skill === skill && x.route === route && x.evoStage === g
          && x.scenario === scn && x.usable && x.level === 10);
        const med = (scnMedian[g] || {})[scn];
        return r && med > 0 ? { scn, nv: round(r.dps / med, 3), dps: r.dps } : null;
      }).filter(Boolean);
      if (per.length < 5) continue;   // 무대 다섯 곳을 못 채운 상태는 줄 세우지 않는다
      const byNv = per.slice().sort((a, b) => b.nv - a.nv);
      powerTable.push({
        skill, evoStage: g, route, n: per.length,
        power: round(median(per.map(p => p.nv)), 3),
        top: byNv[0], bot: byNv[byNv.length - 1],
        growthDependent: growthDependent.has(skill),   // 이 값은 0캐스트 기준 — 밴드 통과율 분모에서 뺀다
      });
    }
  }
  powerTable.sort((a, b) => a.evoStage - b.evoStage || b.power - a.power);

  // 성장률 — 사용자 요구: Lv1과 Lv10을 같이 본다. 상황마다 성장 폭이 다른 게 요점이다.
  const growth = [];
  for (const r10 of solo.filter(r => r.level === 10)) {
    const r1 = solo.find(r => r.level === 1 && r.skill === r10.skill && r.evoStage === r10.evoStage
      && r.route === r10.route && r.scenario === r10.scenario && r.treeMode === r10.treeMode);
    if (!r1) continue;
    growth.push({
      skill: r10.skill, evoStage: r10.evoStage, route: r10.route, scenario: r10.scenario,
      lv1: r1.dps, lv10: r10.dps, ratio: r1.dps > 0 ? round(r10.dps / r1.dps, 2) : null,
      usable: r1.usable && r10.usable,
    });
  }

  // 강화 노드 2개(은별+금별)의 값 = full − bare. 시나리오마다 다르다(비행 보너스는 공중 무대에서만 드러난다).
  const treeDelta = [];
  for (const full of solo.filter(r => r.treeMode === 'full')) {
    const bare = solo.find(r => r.treeMode === 'bare' && r.skill === full.skill && r.evoStage === full.evoStage
      && r.route === full.route && r.scenario === full.scenario && r.level === full.level);
    if (!bare) continue;
    treeDelta.push({
      skill: full.skill, evoStage: full.evoStage, route: full.route, scenario: full.scenario, level: full.level,
      nodes: full.enhanceNodes, bare: bare.dps, full: full.dps,
      gain: bare.dps > 0 ? round(full.dps / bare.dps - 1, 3) : null,
      usable: full.usable && bare.usable,
    });
  }

  // 생애 사슬 — 진화 전이 약해도 진화체가 강하면 납득할 구조다(SKILL_AXES 원칙 3 · G5c 생애 보상).
  const lifetime = [];
  for (const skill of [...new Set(solo.map(r => r.skill))].sort())
    for (const route of [0, 1])
      for (const scn of scenarios) {
        const at = st => solo.find(r => r.skill === skill && r.evoStage === st && r.scenario === scn
          && r.level === 10 && r.treeMode === 'full' && (st === 0 ? true : r.route === route));
        const [a, b, c] = [at(0), at(1), at(2)];
        if (!a || !b || !c) continue;
        lifetime.push({ skill, route, scenario: scn, pre: a.dps, t1: b.dps, t2: c.dps,
          usable: a.usable && b.usable && c.usable });
      }

  // 호밍의 누적 스택 곡선. 실제로 몇 스택까지 가는지는 정주행의 skills[].casts가 답한다 — 둘을 곱해야 실전 파워다.
  const growthCurve = rows.filter(r => r.growthCasts > 0 || (r.skill === 'Homing' && r.rig === 'solo'))
    .map(r => ({ skill: r.skill, evoStage: r.evoStage, route: r.route, scenario: r.scenario,
      level: r.level, casts: r.growthCasts, dps: r.dps, usable: r.usable }))
    .sort((a, b) => a.scenario.localeCompare(b.scenario) || a.casts - b.casts);

  // 동반 릭 — 되감기·산탄·낙뢰는 혼자 두면 딜이 0이다. baseline 대비 전체 딜 증가분이 그 스킬의 값이다.
  const companion = [];
  for (const r of rows.filter(r => r.rig === 'companion')) {
    // 🔴 baseline은 **같은 시나리오 + 같은 진화 차수**의 것을 전부 **평균 낸다**. 차수마다 무대 세기가 다르므로
    //    (GymArena.GroupHpScale ×1/×3/×9) 차수가 다른 baseline과 비교하면 무대 차이가 스킬 효과로 읽힌다.
    //    🔴 **route로는 가르지 않는다** — `GymRig.Apply`는 `rig != "baseline"`일 때만 `Evolve`를 부르므로
    //       baseline 파티는 진화하지 않는다. 실측(it37): baseline 25칸 전부 같은 loadout
    //       `[Whirlwind, Orb, EagleDrop]` · `gotRoute = -1` · `gotStage = -1`이다.
    //       셀의 route·evoStage 라벨은 **무대 세기만** 정한다. 즉 같은 (무대·차수)의 route0/route1 baseline은
    //       같은 구성의 **중복 측정**이므로 평균이 맞다. 종전에 보였던 32~88% 차이는 route가 아니라
    //       앞 셀에서 넘어온 딜이었다(빼고 나면 1~13%로 접힌다).
    const bs = rows.filter(b => b.rig === 'baseline' && b.scenario === r.scenario && b.evoStage === r.evoStage);
    if (!bs.length) continue;
    const baseDps = mean(bs.map(b => b.dpsAll));
    companion.push({ skill: r.skill, evoStage: r.evoStage, route: r.route, scenario: r.scenario,
      baseline: round(baseDps, 1), baselineCells: bs.length, withSkill: r.dpsAll,
      baselineSpread: bs.length > 1 && Math.min(...bs.map(b => b.dpsAll)) > 0
        ? round(Math.max(...bs.map(b => b.dpsAll)) / Math.min(...bs.map(b => b.dpsAll)) - 1, 3) : null,
      carryOverRemoved: round(mean(bs.map(b => b.carryOver || 0)) + (r.carryOver || 0), 1),
      gain: baseDps > 0 ? round(r.dpsAll / baseDps - 1, 3) : null,
      ownShare: r.dpsAll > 0 ? round(r.dpsOwnName / r.dpsAll, 3) : null,
      usable: r.usable && bs.every(b => b.usable) });
  }

  // 🔴 동반 릭으로 재는 스킬(되감기·낙뢰·집중산탄)의 판정. 값 = baseline 대비 **전체 딜 증가분**.
  //    기준: 기준 3스킬에 넣어서 전체 딜이 **늘어야** 쓸모가 있다. 줄면 슬롯을 먹고 손해를 끼친 것이다
  //    (`balance` §2의 "한 스킬이 발동을 독점하면 버그"가 여기서 숫자로 보인다).
  // 🔴 **DPS 무대(표적 1마리)는 동반 판정에 쓰지 않는다.** 실측(2026-09-27): 되감기 진화 전이
  //    처리량 무대에서 swarm +16% · mix_ground +14%인데 tank −60% · airship −94%였다.
  //    표적이 한 마리면 그 한 마리의 스폰 위치·경로 난수 하나가 전체 딜을 좌우해서, 반복 없이는 판정이 안 된다.
  //    (셋을 평균 내면 −25%가 나와 "되감기가 파티에 해롭다"는 잘못된 결론에 닿는다 — 실제로 한 번 닿았다.)
  //    DPS 무대 칸은 `dpsArenaCells`로 따로 세어 두고, 반복(`gymRepeats` ≥ 2)이 붙으면 그때 판정에 넣는다.
  const companionVerdict = {};
  for (const skill of [...companionMeasured].sort()) {
    const all = companion.filter(c => c.skill === skill && c.usable && c.gain != null);
    const mine = all.filter(c => (rows.find(r => r.rig === 'companion' && r.skill === skill
      && r.scenario === c.scenario && r.evoStage === c.evoStage) || {}).arenaKind !== 'dps');
    if (!mine.length) { companionVerdict[skill] = { cells: 0, dpsArenaCells: all.length - mine.length, note: '처리량 무대 표본 없음' }; continue; }
    const byState = {};
    for (const c of mine) {
      const k = c.evoStage ? `R${c.route} ${c.evoStage}차` : '진화 전';
      (byState[k] = byState[k] || []).push(c.gain);
    }
    // 🔴 상태당 시나리오가 1개뿐이면 판정하지 않는다 — 한 칸으로는 노이즈와 신호를 못 가른다.
    const states = Object.entries(byState).map(([k, v]) => ({ state: k, gain: round(mean(v), 3), n: v.length }));
    const judged = states.filter(s => s.n >= 2);
    const harmful = judged.filter(s => s.gain < 0);
    companionVerdict[skill] = {
      cells: mine.length, dpsArenaCells: all.length - mine.length, states,
      judgedStates: judged.length,
      bestGain: judged.length ? round(Math.max(...judged.map(s => s.gain)), 3) : null,
      worstGain: judged.length ? round(Math.min(...judged.map(s => s.gain)), 3) : null,
      harmful: harmful.map(s => s.state),
      // 통과 = 판정 가능한 상태 중 전체 딜을 떨어뜨리는 것이 없고, 하나 이상이 노이즈 폭을 넘어 올린다.
      pass: judged.length === 0 ? null
        : harmful.length === 0 && judged.some(s => s.gain > (noise.maxSpread || 0.05)),
    };
  }

  const measured = [0, 1, 2].filter(g => groups[g].states >= 3);
  return {
    quality, noise, rows, scenarios, groups, power: powerTable, growth, treeDelta, lifetime, growthCurve, companion, companionVerdict,
    G5: {
      value: measured.length
        ? `${measured.filter(g => groups[g].pass).length}/${measured.length} 그룹`
          + (Object.keys(companionVerdict).length ? ` · 동반 ${Object.values(companionVerdict).filter(v => v.pass).length}/${Object.keys(companionVerdict).length}` : '')
          + ` · 쓸 수 있는 셀 ${quality.usable}/${quality.cells}`
        : '표본 없음',
      pass: measured.length > 0 && measured.every(g => groups[g].pass)
        && Object.values(companionVerdict).every(v => v.pass !== false)
        && quality.residual.length === 0 && quality.rigFailed.length === 0,
    },
  };
}

// ───────── 사망·피격 ─────────
function deathMetrics(runs) {
  const byGoal = {};
  for (const r of runs) {
    const k = goalKey(r.map, r.ascension);
    const g = (byGoal[k] ||= { key: k, name: goalName(r.map, r.ascension), finalStage: r.finalStage, reached: {}, deaths: {}, causes: {}, damageByEnemy: {}, runs: 0 });
    g.runs++;
    for (let s = 1; s <= r.stageReached; s++) g.reached[s] = (g.reached[s] || 0) + 1;
    if (r.result === 'dead') {
      g.deaths[r.stageReached] = (g.deaths[r.stageReached] || 0) + 1;
      if (r.deathCause) g.causes[r.deathCause] = (g.causes[r.deathCause] || 0) + 1;
    }
    for (const st of r.stages || []) for (const [e, v] of Object.entries(st.byEnemy || {})) g.damageByEnemy[e] = (g.damageByEnemy[e] || 0) + v;
  }
  for (const g of Object.values(byGoal)) {
    g.deathRate = Object.fromEntries(Object.keys(g.reached).map(s => [s, round((g.deaths[s] || 0) / g.reached[s])]));
  }
  return targets.goalOrder.map(t => byGoal[goalKey(t.map, t.ascension)]).filter(Boolean);
}

// ───────── 쿨 감사 ─────────
function cooldownMetrics(cd) {
  if (!cd || !cd.rows) return { C1: { value: null, pass: false }, C2: { value: null, pass: false }, rows: [], violations: [] };
  const c1 = cd.violations.filter(v => v.rule === 'C1');
  const c2 = cd.violations.filter(v => v.rule === 'C2');
  return {
    rows: cd.rows, violations: cd.violations,
    // C1(쿨 15초)은 참고선이다 — targets.cooldownCapHard가 false면 위반이 있어도 목표 미달로 세지 않는다(사용자 결정 2026-09-18).
    C1: { value: c1.length, pass: targets.cooldownCapHard === false ? true : c1.length === 0, soft: targets.cooldownCapHard === false },
    C2: { value: c2.length, shorter: c2.filter(v => v.severity === 'shorter').length, pass: c2.length === 0 },
  };
}

function fingerprintDiff(a, b) {
  if (!a || !b) return [];
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  return [...keys].filter(k => a[k] !== b[k]).sort();
}

// ───────── 조립 ─────────
// ── 원정 판 집계 (2026-09-29 측정 방식 개편) ──────────────────────────────
// 봇은 판마다 header(map·ascension·loadoutId·targetSkill·targetRoute·rep)와 결과(result·stageReached·gameTime)를
// 내보내고, 구간 파생과 맵별 집계는 여기서 합니다. 계측기에 파생값을 넣으면 지표 정의가 두 벌이 됩니다.
function expeditionMetrics(runs) {
  const rows = runs.filter(r => r.mode === 'expedition');
  if (!rows.length) return null;

  const byMap = new Map();
  for (const r of rows) {
    const key = goalKey(r.map, r.ascension);
    if (!byMap.has(key)) byMap.set(key, { key, name: goalName(r.map, r.ascension), runs: 0, clears: 0, stage2: 0, stageSum: 0 });
    const g = byMap.get(key);
    g.runs++;
    if (r.result === 'clear') g.clears++;
    // 2차 도달은 진화 이벤트로 판정합니다. 판이 끝난 시점의 상태로 보면 도중에 도달하고 죽은 판을 놓칩니다.
    if ((r.evolutions || []).some(e => e.what === 'evolve' && e.evoStage === 2)) g.stage2++;
    g.stageSum += r.stageReached || 0;
  }

  // 목표 2차 진화별 집계. 대변인이 자기 스킬의 원정 판만 골라 보는 데 씁니다.
  const byGoal = new Map();
  for (const r of rows) {
    const key = `${r.targetSkill}|R${r.targetRoute}`;
    if (!byGoal.has(key)) byGoal.set(key, { key, targetSkill: r.targetSkill, targetRoute: r.targetRoute, runs: 0, clears: 0, stage2: 0 });
    const g = byGoal.get(key);
    g.runs++;
    if (r.result === 'clear') g.clears++;
    if ((r.evolutions || []).some(e => e.what === 'evolve' && e.evoStage === 2)) g.stage2++;
  }

  const fin = g => ({ ...g, clearRate: round(g.clears / g.runs, 3), stage2Rate: round(g.stage2 / g.runs, 3) });
  return {
    maps: [...byMap.values()].map(g => ({ ...fin(g), meanStage: round(g.stageSum / g.runs, 1) })),
    goals: [...byGoal.values()].map(fin),
    runs: rows.length,
  };
}

function analyze() {
  const sessions = loadSessions();
  const iterations = loadIterations(sessions);
  const byId = Object.fromEntries(sessions.map(s => [s.id, s]));
  const loop = readJson(path.join(ROOT, 'loop_state.json'), null);
  const out = { generatedUtc: new Date().toISOString(), targets, loop, iterations: [], sessions: sessions.map(s => ({ id: s.id, label: s.config.label, mode: s.config.mode, state: s.status.state, error: s.status.error || null, runs: s.runs.length })) };

  let prevFp = null;
  for (const it of iterations) {
    const ss = (it.sessions || []).map(id => byId[id]).filter(Boolean);
    const runs = ss.flatMap(s => s.runs);
    const campaigns = ss.flatMap(s => s.campaigns.map(c => ({ ...c, campaign: c.campaignKey || `${s.id}#${c.campaign}` })));
    runs.forEach(r => { if (r.mode === 'campaign') { const s = ss.find(x => x.runs.includes(r)); r.campaign = r.campaignKey || `${s.id}#${r.campaign}`; } }); // campaignKey = 양보·재개로 여러 세션에 걸친 캠페인을 하나로 묶는 키
    const enemyTypes = Object.assign({}, ...runs.map(r => r.enemyTypes || {}));
    const cd = ss.map(s => s.cooldowns).filter(Boolean).slice(-1)[0];
    const fp = ss.map(s => s.fingerprint).filter(f => f && Object.keys(f).length).slice(-1)[0] || null;

    const camp = campaignMetrics(campaigns, runs);
    const probe = probeMetrics(runs);
    const skills = skillMetrics(runs, enemyTypes);
    const cool = cooldownMetrics(cd);
    // 🔴 시험장이 돈 이터레이션은 **G5를 시험장으로 판정한다**(analyze.js의 옛 주석대로 교체).
    //    실전 로그 쪽(skills)은 계속 계산해 둔다 — 실전에서 그 스킬이 실제로 얼마나 뽑히는지는 거기만 안다.
    const gym = gymMetrics(ss.flatMap(s => s.gym));
    out.iterations.push({
      iteration: it.iteration, sessions: it.sessions, synthetic: !!it.synthetic,
      plan: it.synthetic ? null : { target: it.target, symptom: it.symptom, axisAnalysis: it.axisAnalysis, chosenAxis: it.chosenAxis, rejected: it.rejected,
        hypothesis: it.hypothesis, changes: it.changes, prediction: it.prediction, passCriterion: it.passCriterion, verdict: it.verdict, verdictNote: it.verdictNote, nextToVerify: it.nextToVerify, codeSuggestions: it.codeSuggestions,
        // 제안 에이전트들의 회의 기록. 보고서의 회의록 절이 이걸 그대로 읽는다(사용자 요청 2026-09-27).
        minutes: it.minutes || null },
      runCount: runs.length,
      // 🔴 시험장 셀은 `gym.jsonl`에 쌓이므로 runCount(runs.jsonl)에 안 들어간다.
      //    보고서의 "측정이 있는 이터레이션" 판정이 runCount만 보면 gym 회차를 통째로 건너뛴다(실제로 그랬다).
      gymCells: gym ? gym.quality.cells : 0,
      // 🔴 셀 수만으로는 "끝까지 돈 회차"를 못 가른다 — 격자를 좁혀 도는 회차가 있고(변경 확인용),
      //    중간에 양보/중단된 회차도 있다. 보고서가 기본으로 열 회차를 고를 때 이 상태를 본다.
      sessionStates: ss.map(s => (s.status && s.status.state) || null),
      // 이 이터레이션이 실제로 잡아먹은 측정 시간(실시간). 루프 종합에서 "몇 시간 써서 뭘 얻었나"를 말할 때 쓴다.
      realMinutes: round(runs.reduce((a, r) => a + (r.realTime || 0), 0) / 60, 1),
      // C2(진화하면 쿨이 길어진다)는 목표에서 뺐다 — 사용자 결정 2026-09-18.
      // 되살리려면 `C2: cool.C2`를 다시 넣고 report.html의 C2 타일·칩 주석을 푼다. cool.C2는 계속 계산된다.
      scoreboard: { G1: camp.G1, G2: probe.G2, G3: camp.G3, G4: probe.G4, G5: gym ? gym.G5 : skills.G5, G6: probe.G6, C1: cool.C1 },
      campaign: camp, probe, skills, gym, expedition: expeditionMetrics(runs), deaths: deathMetrics(runs), cooldowns: cool,
      changedFiles: fingerprintDiff(prevFp, fp),
      results: Object.fromEntries(['clear', 'dead', 'stuck', 'timeout', 'error'].map(k => [k, runs.filter(r => r.result === k).length])),
    });
    if (fp) prevFp = fp;
  }
  return out;
}

// Tools/QA/qa-analyze.js가 지표 함수를 재사용한다(require할 때는 아래 실행부가 돌지 않는다).
module.exports = { campaignMetrics, skillMetrics, gymMetrics, expeditionMetrics, deathMetrics, fingerprintDiff, goalKey, goalName, progressOf, targets, mean, median, std, round };

// ── 스킬 밸런스 판독지(readout.html)에 넣을 값 ────────────────────────────
// 🔴 수치는 플레이타임 하나와 맵별 클리어뿐입니다(사용자 결정 2026-09-29). 판정은 전부 대변인 서술로 하며,
//    그 서술은 에이전트가 `report/advocates/` 아래에 각자 파일로 씁니다 — 한 파일에 몰아 쓰면 서로 덮어씁니다.
function readAdvocates(reportDir) {
  const dir = path.join(reportDir, 'advocates');
  const out = { skills: [], passives: [], crossExam: [] };
  if (!fs.existsSync(dir)) return out;
  for (const file of fs.readdirSync(dir)) {
    if (!file.endsWith('.json')) continue;
    let body;
    try { body = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8')); }
    catch (e) { console.warn(`대변인 파일을 읽지 못했습니다: ${file} — ${e.message}`); continue; }
    if (file === '_passives.json') out.passives = Array.isArray(body) ? body : [body];
    else if (file === '_crossexam.json') out.crossExam = Array.isArray(body) ? body : [body];
    else if (!file.startsWith('_')) out.skills.push(body);
  }
  return out;
}

function buildReadout(data, reportDir) {
  const last = data.iterations[data.iterations.length - 1] || {};
  const minutes = Math.round(last.realMinutes || 0);
  const playtime = minutes >= 60
    ? `${Math.floor(minutes / 60)}시간 ${String(minutes % 60).padStart(2, '0')}분`
    : `${minutes}분`;

  // 맵별 클리어. 원정 모드 집계가 들어오면 그쪽을 먼저 쓰고, 없으면 기존 캠페인 목표별 집계를 씁니다.
  // ⚠️ 원정 판 레코드의 구간 필드(effByEvoStage 등)는 대변인이 읽는 값이고, 이 그래프는 클리어 판수만 봅니다.
  const perGoal = (last.campaign && last.campaign.perGoal) || [];
  const maps = (last.expedition && Array.isArray(last.expedition.maps) ? last.expedition.maps : perGoal)
    .map(g => ({
      name: g.name || g.key,
      runs: g.runs != null ? g.runs : (g.campaigns || 0),
      clears: g.clears != null ? g.clears : (g.clearedCampaigns || 0),
    }))
    .filter(m => m.runs > 0);

  const adv = readAdvocates(reportDir);
  return {
    iteration: last.iteration,
    generated: new Date().toISOString().slice(0, 10),
    playtime, maps,
    skills: adv.skills, passives: adv.passives, crossExam: adv.crossExam,
  };
}

if (require.main === module) {
  const data = analyze();
  const reportDir = path.join(ROOT, 'report');
  fs.mkdirSync(reportDir, { recursive: true });
  fs.writeFileSync(path.join(reportDir, 'data.json'), JSON.stringify(data, null, 1));

  // 보고서 둘. `index.html` = 난이도 루프의 이력 보고서 · `skills.html` = **스킬 밸런스 전용 판독지**.
  // 스킬 쪽을 따로 둔 이유: 난이도 보고서의 양식(이터레이션 타임라인·9목표 사다리)은 스킬을 읽는 데 안 맞는다(사용자 결정 2026-09-27).
  const payload = JSON.stringify(data).replace(/</g, '\\u003c');
  for (const [tplName, outName] of [['report.html', 'index.html'], ['skill_report.html', 'skills.html']]) {
    const tpl = path.join(__dirname, tplName);
    if (!fs.existsSync(tpl)) continue;
    fs.writeFileSync(path.join(reportDir, outName),
      fs.readFileSync(tpl, 'utf8').replace('/*__DATA__*/null', payload));
  }

  // `readout.html` = 사용자에게 배포하는 스킬 밸런스 판독지(2026-09-29 사용자 지시로 양식을 새로 만들었다).
  // 🔴 위 둘과 달리 **직전 회차 하나만** 싣는다. 수치는 플레이타임과 맵별 클리어뿐이고 판정은 전부 대변인 서술이다.
  const readoutTpl = path.join(__dirname, 'balance_readout.html');
  if (fs.existsSync(readoutTpl)) {
    const readout = buildReadout(data, reportDir);
    fs.writeFileSync(path.join(reportDir, 'readout.html'),
      fs.readFileSync(readoutTpl, 'utf8')
        .replace('/*__DATA__*/null', JSON.stringify(readout).replace(/</g, '\\u003c')));
    if (!QUIET) console.log(`→ readout.html (대변인 ${readout.skills.length}건 · 패시브 ${readout.passives.length}건)`);
  }

  if (!QUIET) {
    const last = data.iterations[data.iterations.length - 1];
    console.log(`sessions=${data.sessions.length} iterations=${data.iterations.length}`);
    if (last) {
      console.log(`iteration ${last.iteration}: runs=${last.runCount} results=${JSON.stringify(last.results)}`);
      for (const [k, v] of Object.entries(last.scoreboard)) console.log(`  ${k}: value=${JSON.stringify(v.value)} pass=${v.pass}`);
    }
    console.log('→ ' + path.join(reportDir, 'data.json'));
  }
}
