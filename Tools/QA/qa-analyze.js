#!/usr/bin/env node
// QA 분석기 — QARuns/sessions/* 전체 → QARuns/report/data.json (+ qa-report.html 템플릿으로 index.html, shots/)
//
// 사용: node Tools/QA/qa-analyze.js [--quiet]
//
// 두 갈래:
//   오류  = 모든 세션(balance·chaos)의 errors.jsonl을 sig로 묶는다 + crash.json·hang.json·perf.jsonl.
//           상태는 QARuns/errors/triage.json(루프가 쓴다)에서 오고, "고쳤다는 빌드 뒤에 또 나옴"은 여기서 regressed로 판정한다.
//   밸런스 = balance 세션의 판만, **밸런스 지문(build.json의 fingerprintShort)별로** 나눠 집계한다.
//           chaos 판은 치트성 행동(스테이지 건너뛰기 등)이 섞여 있어서 넣지 않는다.
// 🔴 밸런스 쪽에는 판정(pass/fail)·추천을 넣지 않는다 — 결정은 사용자가 한다. 표본 수와 구간만 붙인다.
// 🔴 지표 함수는 Tools/BotPlaytest/analyze.js 것을 그대로 쓴다(에디터 봇 28회차 데이터와 같은 정의로 비교하려고).
'use strict';
const fs = require('fs');
const path = require('path');
const A = require('../BotPlaytest/analyze.js');

const args = process.argv.slice(2);
const QUIET = args.includes('--quiet');
const PROJECT = path.resolve(__dirname, '..', '..');
const QA = path.join(PROJECT, 'QARuns');
const SESSIONS = path.join(QA, 'sessions');
const BUILDS = path.join(QA, 'builds');
const REPORT = path.join(QA, 'report');
const TRIAGE = path.join(QA, 'errors', 'triage.json');

const readJson = (p, d = null) => { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return d; } };
const readJsonl = (p) => {
  if (!fs.existsSync(p)) return [];
  return fs.readFileSync(p, 'utf8').split('\n').filter(l => l.trim()).map(l => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
};
const { round, mean } = A;
const firstLine = s => String(s || '').split('\n')[0];

// 비율의 95% 윌슨 구간 — 표본이 작을 때 "3판 중 3판 = 100%"를 그대로 믿지 않게.
function wilson(k, n) {
  if (!n) return null;
  const z = 1.96, p = k / n, d = 1 + z * z / n;
  const c = (p + z * z / (2 * n)) / d, h = z * Math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d;
  return [round(Math.max(0, c - h)), round(Math.min(1, c + h))];
}

// ───────── 읽기 ─────────
function loadBuilds() {
  if (!fs.existsSync(BUILDS)) return [];
  return fs.readdirSync(BUILDS, { withFileTypes: true }).filter(d => d.isDirectory())
    .map(d => readJson(path.join(BUILDS, d.name, 'build.json'))).filter(Boolean)
    .sort((a, b) => a.buildId.localeCompare(b.buildId));
}

function loadSessions() {
  if (!fs.existsSync(SESSIONS)) return [];
  return fs.readdirSync(SESSIONS, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name).sort().map(id => {
    const dir = path.join(SESSIONS, id);
    const config = readJson(path.join(dir, 'config.json'), {});
    return {
      id, dir, config,
      qa: readJson(path.join(dir, 'qa.json'), {}),
      status: readJson(path.join(dir, 'status.json'), {}),
      runs: readJsonl(path.join(dir, 'runs.jsonl')),
      campaigns: readJsonl(path.join(dir, 'campaigns.jsonl')),
      errors: readJsonl(path.join(dir, 'errors.jsonl')),
      perf: readJsonl(path.join(dir, 'perf.jsonl')),
      frametimes: readJsonl(path.join(dir, 'frametimes.jsonl')),
      crash: readJson(path.join(dir, 'crash.json'), null),
      hang: readJson(path.join(dir, 'hang.json'), null),
      kind: config.kind || 'balance', buildId: config.buildId,
    };
  });
}

// ───────── 오류 ─────────
function errorReport(sessions, builds, triage) {
  const buildOrder = Object.fromEntries(builds.map((b, i) => [b.buildId, i]));
  const runsPerBuild = {};
  for (const s of sessions) runsPerBuild[s.buildId] = (runsPerBuild[s.buildId] || 0) + s.runs.length;

  const sigs = {};
  for (const s of sessions) {
    // 판당 100번 넘게 난 오류는 파일에 안 적히고 runs의 qa.errors에만 개수가 남는다 — 둘 중 큰 쪽을 쓴다.
    const lines = {};
    for (const e of s.errors) lines[e.sig] = (lines[e.sig] || 0) + 1;
    const counted = {};
    for (const r of s.runs) for (const [sig, n] of Object.entries((r.qa && r.qa.errors) || {})) counted[sig] = (counted[sig] || 0) + n;
    for (const e of s.errors) {
      const g = (sigs[e.sig] ||= { sig: e.sig, type: e.type, source: e.source, message: firstLine(e.message), frame: e.frame, stack: e.stack,
        count: 0, sessions: new Set(), builds: {}, kinds: {}, firstUtc: e.utc, lastUtc: e.utc, sample: null, lastActionHist: {}, screenshot: null, stages: {} });
      if (e.utc < g.firstUtc) g.firstUtc = e.utc;
      if (e.utc >= g.lastUtc) { g.lastUtc = e.utc; g.sample = { session: s.id, run: e.run, map: e.map, ascension: e.ascension, character: e.character, stage: e.stage, scene: e.scene, gameTime: e.gameTime, timeScale: e.timeScale, modalPaused: e.modalPaused, lastActions: e.lastActions }; }
      g.sessions.add(s.id);
      g.kinds[s.kind] = (g.kinds[s.kind] || 0) + 1;
      g.stages[e.stage] = (g.stages[e.stage] || 0) + 1;
      const la = (e.lastActions || []).slice(-1)[0];
      if (la) { const name = la.trim().split(' ').slice(-1)[0]; g.lastActionHist[name] = (g.lastActionHist[name] || 0) + 1; }
      if (e.screenshot && fs.existsSync(path.join(s.dir, e.screenshot))) g.screenshot = { session: s.id, file: e.screenshot };
    }
    for (const sig of Object.keys(lines)) {
      const g = sigs[sig];
      const n = Math.max(lines[sig], counted[sig] || 0);
      g.count += n;
      g.builds[s.buildId] = (g.builds[s.buildId] || 0) + n;
    }
  }

  // 크래시·멈춤도 sig처럼 한 줄로 — player.log 마지막 오류 줄로 묶는다.
  const crashes = [], hangs = [];
  for (const s of sessions) {
    if (s.crash) crashes.push({ session: s.id, buildId: s.buildId, kind: s.kind, utc: s.crash.utc, exitCode: s.crash.exitCode, crashDirs: s.crash.crashDirs,
      logTail: String(s.crash.playerLogTail || '').split('\n').slice(-15).join('\n') });
    if (s.hang) hangs.push({ session: s.id, buildId: s.buildId, kind: s.kind, utc: s.hang.utc, staleMinutes: round(s.hang.staleMinutes, 1),
      stage: s.hang.status && s.hang.status.stage, traceTail: s.hang.traceTail });
  }

  const rows = Object.values(sigs).map(g => {
    const t = triage[g.sig] || {};
    let status = t.status || 'new';
    // 고쳤다고 적은 빌드보다 뒤 빌드에서 또 나오면 재발.
    if ((status === 'fixed' || status === 'fixing') && t.fixedIn != null && buildOrder[t.fixedIn] != null) {
      const after = Object.keys(g.builds).filter(b => buildOrder[b] != null && buildOrder[b] >= buildOrder[t.fixedIn]);
      if (after.length) status = 'regressed';
    }
    const perBuild = Object.fromEntries(Object.entries(g.builds).map(([b, n]) => [b, { count: n, runs: runsPerBuild[b] || 0, per100Runs: runsPerBuild[b] ? round(100 * n / runsPerBuild[b], 1) : null }]));
    return {
      sig: g.sig, type: g.type, source: g.source, message: g.message, frame: g.frame, stack: g.stack,
      count: g.count, sessions: g.sessions.size, perBuild, kinds: g.kinds, stages: g.stages,
      firstUtc: g.firstUtc, lastUtc: g.lastUtc, sample: g.sample,
      lastAction: Object.entries(g.lastActionHist).sort((a, b) => b[1] - a[1]).slice(0, 3),
      screenshot: g.screenshot ? `shots/${g.sig}.png` : null, screenshotSrc: g.screenshot,
      status, triageNote: t.note || null, fixedIn: t.fixedIn || null, cause: t.cause || null,
    };
  }).sort((a, b) => (a.status === 'regressed' ? -1 : 0) - (b.status === 'regressed' ? -1 : 0) || b.count - a.count);

  const perf = sessions.flatMap(s => s.perf.map(p => ({ ...p, session: s.id, buildId: s.buildId, kind: s.kind })));
  const perfByBuild = {};
  for (const p of perf) {
    const b = (perfByBuild[p.buildId] ||= { spikes: 0, worstMs: 0, runs: runsPerBuild[p.buildId] || 0 });
    b.spikes++; b.worstMs = Math.max(b.worstMs, p.ms);
  }
  // 🔴 프레임 비용은 벽시계가 아니라 FrameTimingManager로 본다(QAFrameStats) — 창이 가려지면 화면 제출이
  //    초당 1회로 억제돼 벽시계 프레임이 0.5초씩 끊기는데, 그건 게임의 부하가 아니다(9/23 실측).
  const ftRows = sessions.flatMap(s => s.frametimes.map(r => ({ ...r, kind: s.kind, buildId: s.buildId })))
    .filter(r => r.scene === 'Battle' && r.timingsValid);
  const pooledBucket = (n) => n < 500 ? '<500' : n < 2000 ? '500-1999' : n < 5000 ? '2000-4999' : '5000+';
  const frameCost = {};
  for (const r of ftRows) {
    const key = `${r.screen}|${pooledBucket(r.pooled)}`;
    const b = (frameCost[key] ||= { screen: r.screen, pooled: pooledBucket(r.pooled), n: 0, cpu: [], gpu: [], fps: [], particles: [], numbers: [] });
    b.n++; b.cpu.push(r.cpuMs50); b.gpu.push(r.gpuMs50); b.fps.push(r.fps);
    b.particles.push(r.particleSystems); b.numbers.push(r.damageNumbers);
  }
  const medOf = (a) => a.length ? [...a].sort((x, y) => x - y)[a.length >> 1] : null;
  const frameCostRows = Object.values(frameCost).map(b => ({
    screen: b.screen, pooled: b.pooled, n: b.n,
    cpuMs: round(medOf(b.cpu), 1), gpuMs: round(medOf(b.gpu), 1), fps: Math.round(medOf(b.fps) || 0),
    particleSystems: Math.round(medOf(b.particles) || 0), damageNumbers: Math.round(medOf(b.numbers) || 0),
    cpuMsP95: round([...b.cpu].sort((x, y) => x - y)[Math.floor(b.cpu.length * 0.95)] || 0, 1),
  })).sort((a, b) => a.screen.localeCompare(b.screen) || a.pooled.localeCompare(b.pooled));

  // 살아 있는 적 수와 렉의 관계 — **정주행 판만** 본다(chaos의 렉은 대부분 봇의 언어 전환·해상도 변경이 만든다).
  const bucketOf = (n) => n < 50 ? '<50' : n < 150 ? '50-149' : n < 250 ? '150-249' : '250+';
  const byEnemies = {};
  for (const p of perf.filter(x => x.kind === 'balance')) {
    const b = (byEnemies[bucketOf(p.enemies)] ||= { spikes: 0, ms: [] });
    b.spikes++; b.ms.push(p.ms);
  }
  for (const b of Object.values(byEnemies)) {
    b.ms.sort((x, y) => x - y);
    b.medianMs = b.ms[b.ms.length >> 1];
    b.worstMs = b.ms[b.ms.length - 1];
    delete b.ms;
  }
  // 판당 렉 프레임(정주행) — 렉이 몇 판에 몰리는지.
  const runSpikes = sessions.filter(s => s.kind === 'balance').flatMap(s => s.runs.filter(r => r.qa).map(r => ({ spikes: r.qa.frameSpikes, peak: r.qa.enemyPeak, map: r.map, ascension: r.ascension })));
  const runsWithSpikes = runSpikes.filter(r => r.spikes > 0);
  const byGoal = {};
  for (const r of runsWithSpikes) {
    const k = `${r.map}:${r.ascension}`;
    const g = (byGoal[k] ||= { runs: 0, spikes: 0, peak: 0 });
    g.runs++; g.spikes += r.spikes; g.peak = Math.max(g.peak, r.peak);
  }
  return {
    rows, crashes, hangs,
    perf: {
      byBuild: perfByBuild, worst: perf.sort((a, b) => b.ms - a.ms).slice(0, 20),
      byEnemies, byGoal, balanceRuns: runSpikes.length, balanceRunsWithSpikes: runsWithSpikes.length,
      frameCost: frameCostRows, frameSamples: ftRows.length,
    },
    statusCounts: rows.reduce((o, r) => (o[r.status] = (o[r.status] || 0) + 1, o), {}),
  };
}

// ───────── 밸런스 ─────────
function balanceReport(sessions, builds) {
  const fpOf = Object.fromEntries(builds.map(b => [b.buildId, b.fingerprintShort]));
  // 옛 빌드 폴더는 정리돼서(QABuild가 최근 3개만 둔다) build.json이 없을 수 있다 → 세션이 가진 기록으로 찾는다:
  // qa.json의 fingerprintShort → 세션 fingerprint.json이 아는 빌드의 지문과 같으면 그 빌드의 것.
  const fpByContent = new Map(builds.map(b => [JSON.stringify(b.fingerprint || {}), b.fingerprintShort]));
  const fpFor = (s) => fpOf[s.buildId] || s.qa.fingerprintShort
    || fpByContent.get(JSON.stringify(readJson(path.join(s.dir, 'fingerprint.json'), {}))) || 'unknown';
  const groups = {};
  for (const s of sessions.filter(x => x.kind === 'balance' || x.kind === 'render')) {
    const fp = fpFor(s);
    const g = (groups[fp] ||= { fingerprint: fp, builds: new Set(), sessions: [], fpFull: null });
    g.builds.add(s.buildId);
    g.sessions.push(s);
    const b = builds.find(x => x.buildId === s.buildId);
    if (b) g.fpFull = b.fingerprint;
  }

  const ordered = Object.values(groups).sort((a, b) => [...a.builds].sort()[0].localeCompare([...b.builds].sort()[0]));
  let prevFp = null;
  return ordered.map(g => {
    const ss = g.sessions;
    const allRuns = ss.flatMap(s => s.runs);
    // 양보·재개로 여러 세션에 걸친 캠페인을 하나로 묶는다(analyze.js와 같은 규칙).
    for (const s of ss) for (const r of s.runs) r.campaign = r.campaignKey || `${s.id}#${r.campaign}`;
    const runs = allRuns.filter(r => r.result !== 'yielded' && r.result !== 'abandoned');
    const campaigns = ss.flatMap(s => s.campaigns.map(c => ({ ...c, campaign: c.campaignKey || `${s.id}#${c.campaign}` })));
    const enemyTypes = Object.assign({}, ...runs.map(r => r.enemyTypes || {}));

    const camp = A.campaignMetrics(campaigns, runs);
    const skills = A.skillMetrics(runs, enemyTypes);
    const deaths = A.deathMetrics(runs);

    // 스킬을 가진 판 vs 안 가진 판의 클리어율(목표를 섞은 단순 비교 — 쉬운 목표에서 많이 뽑힌 스킬이 유리하게 나온다).
    const skillIds = [...new Set(runs.flatMap(r => (r.skills || []).filter(x => x.owned).map(x => x.id)))].filter(x => x !== 'Other');
    const skillWin = skillIds.map(id => {
      const w = runs.filter(r => (r.skills || []).some(x => x.owned && x.id === id));
      const wo = runs.filter(r => !(r.skills || []).some(x => x.owned && x.id === id));
      const k1 = w.filter(r => r.result === 'clear').length, k0 = wo.filter(r => r.result === 'clear').length;
      return { skill: id, withN: w.length, withClear: w.length ? round(k1 / w.length) : null, withCI: wilson(k1, w.length),
               withoutN: wo.length, withoutClear: wo.length ? round(k0 / wo.length) : null };
    }).sort((a, b) => b.withN - a.withN);

    const byChar = {};
    for (const r of runs) {
      const c = (byChar[r.character] ||= { character: r.character, n: 0, clears: 0, progress: [], byGoal: {} });
      c.n++; if (r.result === 'clear') c.clears++;
      c.progress.push(A.progressOf(r));
      const k = A.goalKey(r.map, r.ascension);
      const gg = (c.byGoal[k] ||= { n: 0, clears: 0 }); gg.n++; if (r.result === 'clear') gg.clears++;
    }
    const characters = Object.values(byChar).map(c => ({ character: c.character, n: c.n, clearRate: round(c.clears / c.n), ci: wilson(c.clears, c.n), meanProgress: round(mean(c.progress)), byGoal: c.byGoal }));

    const results = {};
    for (const r of allRuns) results[r.result] = (results[r.result] || 0) + 1;
    const out = {
      fingerprint: g.fingerprint, builds: [...g.builds].sort(), sessions: ss.length, runs: runs.length, results,
      realHours: round(allRuns.reduce((a, r) => a + (r.realTime || 0), 0) / 3600, 2),
      changedFromPrev: prevFp ? A.fingerprintDiff(prevFp, g.fpFull) : [],
      campaign: { perGoal: camp.perGoal, campaigns: camp.campaigns, curves: camp.curves, playMinutes: { median: camp.G1.value, spread: camp.G1.spread, complete: camp.G1.complete, total: camp.G1.total, target: camp.G1.target } },
      skills: skills.rows.filter(r => r.skill !== 'Other').sort((a, b) => b.n - a.n), skillWin, deaths, characters,
      // 구간별 밴드(2026-09-23). attemptsBand는 대시보드 하위호환용으로 Early를 넣는다.
      attemptsBand: A.targets.attemptsPerGoalEarly || A.targets.attemptsPerGoal || null,
      attemptsBandEarly: A.targets.attemptsPerGoalEarly || A.targets.attemptsPerGoal || null,
      attemptsBandLate: A.targets.attemptsPerGoalLate || A.targets.attemptsPerGoal || null,
      earlyGoalCount: A.targets.earlyGoalCount != null ? A.targets.earlyGoalCount : null,
    };
    if (g.fpFull) prevFp = g.fpFull;
    return out;
  });
}

// ───────── 조립 ─────────
const builds = loadBuilds();
const sessions = loadSessions();
const triage = readJson(TRIAGE, {});
const errors = errorReport(sessions, builds, triage);
const balance = balanceReport(sessions, builds);

const chaosActions = {};
for (const s of sessions) for (const r of s.runs) for (const a of r.chaosActions || []) chaosActions[a.action] = (chaosActions[a.action] || 0) + 1;
const outcomes = readJsonl(path.join(QA, 'outcomes.jsonl'));

const data = {
  generatedUtc: new Date().toISOString(),
  runner: readJson(path.join(QA, 'runner.json'), null),
  buildStatus: readJson(path.join(QA, 'build.status.json'), null),
  builds: builds.map(b => ({ buildId: b.buildId, builtUtc: b.builtUtc, gitHead: (b.gitHead || '').slice(0, 8), gitDirtyFiles: b.gitDirtyFiles, fingerprintShort: b.fingerprintShort, durationSec: b.durationSec })),
  totals: {
    sessions: sessions.length,
    runs: sessions.reduce((a, s) => a + s.runs.length, 0),
    byKind: sessions.reduce((o, s) => { const k = (o[s.kind] ||= { sessions: 0, runs: 0 }); k.sessions++; k.runs += s.runs.length; return o; }, {}),
    realHours: round(sessions.reduce((a, s) => a + s.runs.reduce((x, r) => x + (r.realTime || 0), 0), 0) / 3600, 2),
    outcomes: outcomes.reduce((o, x) => (o[x.outcome] = (o[x.outcome] || 0) + 1, o), {}),
  },
  errors, balance, chaosActions,
  sessions: sessions.slice(-60).map(s => ({ id: s.id, kind: s.kind, buildId: s.buildId, state: s.status.state || null, runs: s.runs.length, errors: s.errors.length, saveScenario: s.qa.saveScenario || null, crash: !!s.crash, hang: !!s.hang })),
};

fs.mkdirSync(path.join(REPORT, 'shots'), { recursive: true });
for (const r of errors.rows) {
  if (!r.screenshotSrc) continue;
  try { fs.copyFileSync(path.join(SESSIONS, r.screenshotSrc.session, r.screenshotSrc.file), path.join(REPORT, 'shots', `${r.sig}.png`)); } catch { r.screenshot = null; }
  delete r.screenshotSrc;
}
fs.writeFileSync(path.join(REPORT, 'data.json'), JSON.stringify(data, null, 1));
const tpl = path.join(__dirname, 'qa-report.html');
if (fs.existsSync(tpl)) {
  const html = fs.readFileSync(tpl, 'utf8').replace('/*__DATA__*/null', JSON.stringify(data).replace(/</g, '\\u003c'));
  fs.writeFileSync(path.join(REPORT, 'index.html'), html);
}

if (!QUIET) {
  console.log(`sessions=${data.totals.sessions} runs=${data.totals.runs} builds=${builds.length} realHours=${data.totals.realHours}`);
  console.log(`errors: sig ${errors.rows.length}개 ${JSON.stringify(errors.statusCounts)} · crash ${errors.crashes.length} · hang ${errors.hangs.length}`);
  for (const r of errors.rows.filter(x => x.status === 'new' || x.status === 'regressed').slice(0, 15))
    console.log(`  [${r.status}] ${r.sig} ×${r.count} ${r.type} ${r.message.slice(0, 100)}`);
  for (const b of balance) console.log(`balance fp=${b.fingerprint} builds=${b.builds.join(',')} runs=${b.runs} ${JSON.stringify(b.results)}`);
  console.log('→ ' + path.join(REPORT, 'data.json'));
}
