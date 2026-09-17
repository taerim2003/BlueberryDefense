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
    G3: { value: round(cv, 3), target: targets.attemptsCvMax, pass: cv != null && cv <= targets.attemptsCvMax, goalsMeasured: att.length },
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
    G2: { value: `${measured.filter(r => r.pass).length}/${relations.length}`, pass: measured.length === relations.length && measured.every(r => r.pass), measured: measured.length },
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
      const st = (states[key] ||= { skill: s.id, evoStage: s.evoStage, route: s.evoStage > 0 ? s.route : -1, n: 0, power: [], dps: [], antiAir: [], boss: [], shield: [], contactPerMin: [], overkill: [], early: [], progress: [] });
      const t = Math.max(1, s.ownedTime);
      st.n++;
      st.power.push(s.share * n);
      st.dps.push(s.effDamage / t);
      let air = 0, boss = 0, shield = 0;
      for (const [e, v] of Object.entries(s.effByEnemy || {})) {
        const ty = enemyTypes[e] || {};
        if (ty.antiAir) air += v;
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
    const o = { skill: s.skill, evoStage: s.evoStage, route: s.route, n: s.n, meanProgress: round(mean(s.progress)), overkill: round(mean(s.overkill)) };
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
    C1: { value: c1.length, pass: c1.length === 0 },
    C2: { value: c2.length, shorter: c2.filter(v => v.severity === 'shorter').length, pass: c2.length === 0 },
  };
}

function fingerprintDiff(a, b) {
  if (!a || !b) return [];
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  return [...keys].filter(k => a[k] !== b[k]).sort();
}

// ───────── 조립 ─────────
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
    const campaigns = ss.flatMap(s => s.campaigns.map(c => ({ ...c, campaign: `${s.id}#${c.campaign}` })));
    runs.forEach(r => { if (r.mode === 'campaign') { const s = ss.find(x => x.runs.includes(r)); r.campaign = `${s.id}#${r.campaign}`; } });
    const enemyTypes = Object.assign({}, ...runs.map(r => r.enemyTypes || {}));
    const cd = ss.map(s => s.cooldowns).filter(Boolean).slice(-1)[0];
    const fp = ss.map(s => s.fingerprint).filter(f => f && Object.keys(f).length).slice(-1)[0] || null;

    const camp = campaignMetrics(campaigns, runs);
    const probe = probeMetrics(runs);
    const skills = skillMetrics(runs, enemyTypes);
    const cool = cooldownMetrics(cd);
    out.iterations.push({
      iteration: it.iteration, sessions: it.sessions, synthetic: !!it.synthetic,
      plan: it.synthetic ? null : { target: it.target, symptom: it.symptom, axisAnalysis: it.axisAnalysis, chosenAxis: it.chosenAxis, rejected: it.rejected,
        hypothesis: it.hypothesis, changes: it.changes, prediction: it.prediction, passCriterion: it.passCriterion, verdict: it.verdict, verdictNote: it.verdictNote, nextToVerify: it.nextToVerify, codeSuggestions: it.codeSuggestions },
      runCount: runs.length,
      scoreboard: { G1: camp.G1, G2: probe.G2, G3: camp.G3, G4: probe.G4, G5: skills.G5, G6: probe.G6, C1: cool.C1, C2: cool.C2 },
      campaign: camp, probe, skills, deaths: deathMetrics(runs), cooldowns: cool,
      changedFiles: fingerprintDiff(prevFp, fp),
      results: Object.fromEntries(['clear', 'dead', 'stuck', 'timeout', 'error'].map(k => [k, runs.filter(r => r.result === k).length])),
    });
    if (fp) prevFp = fp;
  }
  return out;
}

const data = analyze();
const reportDir = path.join(ROOT, 'report');
fs.mkdirSync(reportDir, { recursive: true });
fs.writeFileSync(path.join(reportDir, 'data.json'), JSON.stringify(data, null, 1));

const tpl = path.join(__dirname, 'report.html');
if (fs.existsSync(tpl)) {
  const html = fs.readFileSync(tpl, 'utf8').replace('/*__DATA__*/null', JSON.stringify(data).replace(/</g, '\\u003c'));
  fs.writeFileSync(path.join(reportDir, 'index.html'), html);
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
