#!/usr/bin/env node
// QA 러너 — QA 빌드(QARuns/builds/<id>/)를 N개 띄워 봇을 돌리고, 죽거나 멈추면 기록하고 다시 띄운다. Unity 에디터는 안 쓴다.
//
// 사용: node Tools/QA/qa-runner.js --deadline 07:00 [--instances 2] [--chaos 1] [--maxSessions 200]
//                                   [--headless | --headlessBalance] [--selftest] [--width 640 --height 360] [--staleMinutes 5]
//   --chaos K      N개 중 K개를 chaos(엣지 케이스 행동)로, 나머지를 balance(정책 그대로 정주행)로 돌린다
//   --selftest     각 chaos 슬롯의 첫 세션에서 일부러 오류 2개를 낸다(오류 수집 대조군)
//   멈추려면: QARuns/runner.stop 파일을 만든다(판 경계에서 끝내고 5분 뒤 남은 프로세스는 죽인다)
//
// 🔴 종료 조건은 기억 없이 판정된다: 마감 시각(--deadline, 없으면 시작 +8시간) · 시작한 세션 수(--maxSessions) · runner.stop.
// 🔴 새 빌드(QARuns/builds/latest.txt)가 생기면 돌던 세션에 stop을 쓰고, 판 경계에서 끝나면 새 빌드로 **새 세션**을 연다.
//    빌드가 다른 판을 한 캠페인에 섞으면 밸런스 데이터를 빌드별로 나눌 수 없어서다(멈춤·크래시 재개는 같은 빌드에서만 한다).
// 세션 폴더 구조는 BotRuns와 같다(config.json·fingerprint.json·runs.jsonl…) + errors.jsonl·perf.jsonl·player.log·qa.json.
'use strict';
const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn } = require('child_process');

const args = process.argv.slice(2);
const argVal = (k, d) => { const i = args.indexOf(k); return i >= 0 ? args[i + 1] : d; };
const PROJECT = path.resolve(__dirname, '..', '..');
const QA = path.join(PROJECT, 'QARuns');
const BUILDS = path.join(QA, 'builds');
// 🔴 실행은 **항상 이 한 경로**에서 한다(빌드 폴더에서 직접 띄우지 않는다). Development 빌드는 프로파일러 포트를 열어서
//    Windows 방화벽이 **exe 경로마다** "허용"을 묻는다 — 빌드마다 경로가 바뀌면 빌드마다 사람이 눌러야 한다.
//    새 빌드는 모든 슬롯이 멈춘 뒤 여기로 통째로 복사한다(실행 중인 exe는 덮을 수 없다).
const PLAYER = path.join(QA, 'player');
const SESSIONS = path.join(QA, 'sessions');
const STATE = path.join(QA, 'runner.json');
const STOP = path.join(QA, 'runner.stop');
const LOG = path.join(QA, 'runner.log');
// QA 빌드는 productName이 BlueberryDefense_QA라 세이브가 여기 따로 있다(Assets/Editor/QABuild.cs).
const SAVE_DIR = path.join(os.homedir(), 'AppData', 'LocalLow', 'taerimgames', 'BlueberryDefense_QA');
const CRASH_DIR = path.join(os.tmpdir(), 'taerimgames', 'BlueberryDefense_QA', 'Crashes');

const INSTANCES = parseInt(argVal('--instances', '2'), 10);
const CHAOS = Math.min(INSTANCES, parseInt(argVal('--chaos', '1'), 10));
const MAX_SESSIONS = parseInt(argVal('--maxSessions', '200'), 10);
const HEADLESS = args.includes('--headless');
// 정주행(balance)만 창 없이 — 새 창이 뜰 때마다 포커스를 뺏지 않게. chaos는 오류 스크린샷이 필요해 창을 띄운다.
const HEADLESS_BALANCE = args.includes('--headlessBalance');
const headlessFor = (slot) => slot.kind === 'render' ? false : (HEADLESS || (HEADLESS_BALANCE && slot.kind === 'balance'));
const RENDER = Math.max(0, parseInt(argVal('--render', '1'), 10));           // 1080p 창에서 프레임 비용을 재는 인스턴스 수
const RENDER_W = argVal('--renderWidth', '1920'), RENDER_H = argVal('--renderHeight', '1080');
const SELFTEST = args.includes('--selftest');
const WIDTH = argVal('--width', '640'), HEIGHT = argVal('--height', '360');
const STALE_MS = parseFloat(argVal('--staleMinutes', '5')) * 60000;
const POLL_MS = 15000;
const MAX_RESUMES = 3; // 같은 캠페인이 연달아 이만큼 죽으면 재개를 포기하고 새로 시작한다

const deadline = (() => {
  const d = argVal('--deadline', null);
  if (!d) return new Date(Date.now() + 8 * 3600e3);
  const [h, m] = d.split(':').map(Number);
  const t = new Date(); t.setHours(h, m, 0, 0);
  if (t <= new Date()) t.setDate(t.getDate() + 1);
  return t;
})();

const readJson = (p, d = null) => { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return d; } };
const stamp = () => { const d = new Date(), p = n => String(n).padStart(2, '0'); return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`; };
const log = (msg) => { const line = `${new Date().toISOString()} ${msg}`; console.log(line); fs.appendFileSync(LOG, line + '\n'); };
const alive = (pid) => { try { process.kill(pid, 0); return true; } catch { return false; } };
const tail = (p, n) => { try { return fs.readFileSync(p, 'utf8').split('\n').slice(-n).join('\n'); } catch { return null; } };

fs.mkdirSync(SESSIONS, { recursive: true });

// ───────── 한 번에 하나만 ─────────
const prev = readJson(STATE);
if (prev && prev.pid && prev.pid !== process.pid && alive(prev.pid) && !prev.finishedUtc) {
  console.error(`이미 러너가 돈다(pid ${prev.pid}). 멈추려면 ${STOP}`);
  process.exit(1);
}
if (fs.existsSync(STOP)) fs.unlinkSync(STOP);

const latestBuild = () => {
  const id = (() => { try { return fs.readFileSync(path.join(BUILDS, 'latest.txt'), 'utf8').trim(); } catch { return null; } })();
  return id && fs.existsSync(path.join(BUILDS, id, 'BlueberryDefense.exe')) ? id : null;
};

// kind: chaos(엣지 케이스·창 640×360) · render(정주행 정책이되 **1080p 창**에서 프레임 비용을 잰다) · balance(정주행·창 없음)
// 🔴 render 인스턴스가 있어야 "렌더링이 무거운가"를 답할 수 있다 — headless는 아예 안 그려서 그 비용이 0으로 나온다.
const slots = Array.from({ length: INSTANCES }, (_, i) => ({
  slot: i, kind: i < CHAOS ? 'chaos' : (i < CHAOS + RENDER ? 'render' : 'balance'),
  child: null, pid: null, session: null, buildId: null, startedMs: 0, stopRequested: false,
  resumeFrom: null, resumeStreak: 0, sessions: 0, selftestDone: false, lastOutcome: null,
}));
let sessionsStarted = 0;
let stopping = false, stopSince = 0;
const startedUtc = new Date().toISOString();

function writeState(extra = {}) {
  fs.writeFileSync(STATE, JSON.stringify({
    pid: process.pid, startedUtc, deadline: deadline.toISOString(), args, sessionsStarted, maxSessions: MAX_SESSIONS, stopping,
    latestBuild: latestBuild(),
    slots: slots.map(s => ({ slot: s.slot, kind: s.kind, pid: s.pid, session: s.session, buildId: s.buildId, sessions: s.sessions, lastOutcome: s.lastOutcome, stopRequested: s.stopRequested })),
    heartbeatUtc: new Date().toISOString(), ...extra,
  }, null, 1));
}

// ───────── 세이브 시나리오 (chaos 새 세션만) ─────────
// 부팅할 때 이 세이브를 읽는 경로를 시험한다. 캠페인은 시작하면서 세이브를 비우므로 판 진행에는 영향이 없다.
const SAVE_SCENARIOS = {
  keep: null,
  missing: 'DELETE',
  corrupt: '{"keys":["meta.currency"],"values":[',
  garbage: '\u0000\u0001not json at all',
  mismatched: JSON.stringify({ keys: ['meta.currency', 'ascension.unlocked'], values: ['5'] }),
  wrongTypes: JSON.stringify({ keys: ['meta.currency', 'ascension.unlocked', 'clear.map.Map_Wide15', 'skilltree.current', 'select.character', 'Collection.Discovered', 'loc.locale'],
    values: ['abc', '-7', '99', 'nope;;;=', 'Char_DoesNotExist', 'A:Nope:9:9,,,', 'xx-YY'] }),
};
function applySaveScenario(slot) {
  const names = Object.keys(SAVE_SCENARIOS);
  const name = Math.random() < 0.5 ? 'keep' : names[1 + Math.floor(Math.random() * (names.length - 1))];
  const file = path.join(SAVE_DIR, `save_qa_${slot.slot}.json`);
  const body = SAVE_SCENARIOS[name];
  try {
    fs.mkdirSync(SAVE_DIR, { recursive: true });
    if (body === 'DELETE') { if (fs.existsSync(file)) fs.unlinkSync(file); }
    else if (body != null) fs.writeFileSync(file, body);
  } catch (e) { log(`slot${slot.slot} 세이브 시나리오 ${name} 적용 실패: ${e.message}`); return 'keep'; }
  return name;
}

// 실행 경로(PLAYER)에 그 빌드가 깔려 있게 한다. 다른 빌드가 깔려 있고 도는 슬롯이 있으면 false(다 멈출 때까지 기다린다).
function ensurePlayer(buildId) {
  const cur = readJson(path.join(PLAYER, 'build.json'), null);
  if (cur && cur.buildId === buildId && fs.existsSync(path.join(PLAYER, 'BlueberryDefense.exe'))) return true;
  if (slots.some(s => s.child)) return false;
  log(`실행 경로에 빌드 ${buildId} 설치`);
  try {
    fs.rmSync(PLAYER, { recursive: true, force: true });
    fs.cpSync(path.join(BUILDS, buildId), PLAYER, { recursive: true });
    return true;
  } catch (e) { log(`빌드 설치 실패: ${e.message}`); return false; }
}

// ───────── 세션 시작 ─────────
function startSession(slot) {
  let buildId = slot.resumeFrom ? slot.buildId : latestBuild();
  if (!buildId) {
    if (Date.now() - (startSession.lastNoBuildLog || 0) > 10 * 60000) { startSession.lastNoBuildLog = Date.now(); log('실행할 빌드가 없다 — QARuns/build.request로 빌드를 먼저 뽑을 것'); }
    return;
  }
  // 재개는 같은 빌드에서만 — 실행 경로가 이미 새 빌드로 바뀌었으면 재개를 버리고 새로 시작한다.
  if (slot.resumeFrom && buildId !== latestBuild()) { slot.resumeFrom = null; slot.resumeStreak = 0; buildId = latestBuild(); }
  const buildDir = path.join(BUILDS, buildId);
  if (!fs.existsSync(path.join(buildDir, 'BlueberryDefense.exe'))) { log(`빌드 exe 없음: ${buildDir}`); slot.resumeFrom = null; return; }
  if (!ensurePlayer(buildId)) return;
  const exe = path.join(PLAYER, 'BlueberryDefense.exe');
  const build = readJson(path.join(buildDir, 'build.json'), {});

  const name = `${stamp()}_${buildId}_${slot.kind}${slot.slot}${slot.resumeFrom ? '-r' : ''}`;
  const dir = path.join(SESSIONS, name);
  fs.mkdirSync(dir, { recursive: true });
  const selftest = SELFTEST && slot.kind === 'chaos' && !slot.selftestDone;
  const saveScenario = slot.kind === 'chaos' && !slot.resumeFrom ? applySaveScenario(slot) : 'keep';
  const cfg = {
    label: `qa-${slot.kind}${slot.slot}`, mode: 'campaign', campaigns: 1, maxAttemptsPerGoal: 30, maxRunsPerCampaign: 200,
    characters: ['Char_Strawberry', 'Char_Pineapple', 'Char_Slot3'],
    runAudit: false, simStep: 1 / 30, seed: Math.floor(Math.random() * 1e9), stuckRealSeconds: 60, maxRunRealSeconds: 3600,
    resumeFrom: slot.resumeFrom || '', sessionDir: dir, runsRoot: SESSIONS,
    kind: slot.kind, instance: String(slot.slot), buildId, chaos: slot.kind === 'chaos',
    chaosMinInterval: 3, chaosMaxInterval: 20, skipEnding: slot.kind !== 'chaos', qaSelfTest: selftest,
  };
  // 재개면 원래 세션의 설정(시드 등)을 이어받는다 — BotLauncher의 재개와 같은 규칙.
  if (slot.resumeFrom) {
    const orig = readJson(path.join(SESSIONS, slot.resumeFrom, 'config.json'), null);
    if (orig) Object.assign(cfg, orig, { resumeFrom: slot.resumeFrom, sessionDir: dir, label: `${orig.label}-r`, qaSelfTest: false });
  }
  fs.writeFileSync(path.join(dir, 'config.json'), JSON.stringify(cfg, null, 1));
  fs.writeFileSync(path.join(dir, 'fingerprint.json'), JSON.stringify(build.fingerprint || {}));
  fs.writeFileSync(path.join(dir, 'qa.json'), JSON.stringify({ slot: slot.slot, kind: slot.kind, buildId, fingerprintShort: build.fingerprintShort || null, saveScenario, resumedFrom: slot.resumeFrom, startedUtc: new Date().toISOString(), headless: headlessFor(slot) }, null, 1));

  const argv = ['-botConfig', path.join(dir, 'config.json'), '-logFile', path.join(dir, 'player.log')];
  if (headlessFor(slot)) argv.push('-batchmode', '-nographics');
  else if (slot.kind === 'render') argv.push('-screen-fullscreen', '0', '-screen-width', RENDER_W, '-screen-height', RENDER_H);
  else argv.push('-screen-fullscreen', '0', '-screen-width', WIDTH, '-screen-height', HEIGHT);
  const child = spawn(exe, argv, { cwd: PLAYER, stdio: 'ignore', windowsHide: headlessFor(slot) });
  Object.assign(slot, { child, pid: child.pid, session: name, buildId, startedMs: Date.now(), stopRequested: false });
  if (selftest) slot.selftestDone = true;
  slot.sessions++;
  sessionsStarted++;
  log(`slot${slot.slot} 시작 ${name} (pid ${child.pid}${slot.resumeFrom ? ', 재개 ' + slot.resumeFrom : ''}${saveScenario !== 'keep' ? ', 세이브 ' + saveScenario : ''})`);
  child.on('exit', (code, signal) => onExit(slot, child, code, signal));
  child.on('error', (e) => log(`slot${slot.slot} 실행 실패: ${e.message}`));
}

// ───────── 끝났을 때 ─────────
function onExit(slot, child, code, signal) {
  if (slot.child !== child) return;
  const dir = path.join(SESSIONS, slot.session);
  const st = readJson(path.join(dir, 'status.json'), {});
  const state = st.state || 'none';
  const resumable = fs.existsSync(path.join(dir, 'resume.json'));
  let outcome;
  if (slot.killedFor) {
    outcome = slot.killedFor;
  } else if (state === 'done' || state === 'yielded') {
    outcome = state;
  } else if (state === 'error') {
    outcome = 'error';
  } else {
    // 봇이 끝을 적지 못하고 프로세스가 사라졌다 = 크래시.
    outcome = 'crash';
    const before = slot.startedMs;
    const dumps = (() => { try { return fs.readdirSync(CRASH_DIR).map(d => path.join(CRASH_DIR, d)).filter(d => fs.statSync(d).mtimeMs >= before); } catch { return []; } })();
    fs.writeFileSync(path.join(dir, 'crash.json'), JSON.stringify({ exitCode: code, signal, statusState: state, crashDirs: dumps, playerLogTail: tail(path.join(dir, 'player.log'), 60), utc: new Date().toISOString() }, null, 1));
  }
  // 멈춤·크래시는 같은 빌드에서 이어서 돌린다. 연달아 MAX_RESUMES번이면 포기.
  if ((outcome === 'hang' || outcome === 'crash') && resumable && slot.resumeStreak < MAX_RESUMES) {
    slot.resumeFrom = slot.session; slot.resumeStreak++;
  } else { slot.resumeFrom = null; slot.resumeStreak = 0; }
  log(`slot${slot.slot} 끝 ${slot.session} → ${outcome} (exit ${code}${st.error ? ', ' + String(st.error).split('\n')[0].slice(0, 160) : ''})`);
  fs.appendFileSync(path.join(QA, 'outcomes.jsonl'), JSON.stringify({ utc: new Date().toISOString(), slot: slot.slot, kind: slot.kind, session: slot.session, buildId: slot.buildId, outcome, exitCode: code, error: st.error || null, realMinutes: Math.round((Date.now() - slot.startedMs) / 600) / 100 }) + '\n');
  Object.assign(slot, { child: null, pid: null, lastOutcome: outcome, killedFor: null });
  writeState();
}

function kill(slot, why) {
  if (!slot.child) return;
  slot.killedFor = why;
  try { slot.child.kill(); } catch { }
}

// ───────── 감시 ─────────
function tick() {
  const now = Date.now();
  if (!stopping) {
    let why = null;
    if (now >= deadline.getTime()) why = '마감 시각';
    else if (fs.existsSync(STOP)) why = 'runner.stop';
    else if (sessionsStarted >= MAX_SESSIONS) why = `세션 상한 ${MAX_SESSIONS}`;
    if (why) {
      stopping = true; stopSince = now;
      log(`정지 시작: ${why} — 판 경계에서 끝내도록 stop을 쓴다`);
      for (const s of slots) if (s.child) fs.writeFileSync(path.join(SESSIONS, s.session, 'stop'), 'runner ' + why);
    }
  }

  const latest = latestBuild();
  for (const s of slots) {
    if (!s.child) {
      if (!stopping) startSession(s);
      continue;
    }
    const dir = path.join(SESSIONS, s.session);
    // 새 빌드가 나왔다 → 판 경계에서 비키게 한다(재개하지 않고 새 빌드로 새 세션).
    if (latest && s.buildId !== latest && !s.stopRequested && !stopping) {
      fs.writeFileSync(path.join(dir, 'stop'), 'runner 새 빌드 ' + latest);
      s.stopRequested = true;
      log(`slot${s.slot} 새 빌드 ${latest} — 판 경계에서 교체`);
    }
    // 심장박동: status.json이 STALE_MS 동안 안 바뀌면 멈춤. 첫 기록 전(부팅 중)은 시작 시각으로 잰다.
    // 🔴 파일을 못 읽었으면 마지막으로 본 값을 쓴다. 게임이 상태 파일을 교체하는 몇 ms 동안 파일이 없는데,
    //    그 순간 읽기에 실패해 시작 시각으로 떨어지면 멀쩡한 세션을 "5분 무응답"으로 죽인다(9/22 실제로 한 번 죽였다).
    s.lastBeat = Math.max(s.lastBeat || 0, s.startedMs);
    try { s.lastBeat = Math.max(s.lastBeat, fs.statSync(path.join(dir, 'status.json')).mtimeMs); } catch { }
    try { s.lastBeat = Math.max(s.lastBeat, fs.statSync(path.join(dir, 'status.json.tmp')).mtimeMs); } catch { }
    const beat = s.lastBeat;
    if (now - beat > STALE_MS) {
      fs.writeFileSync(path.join(dir, 'hang.json'), JSON.stringify({ utc: new Date().toISOString(), staleMinutes: (now - beat) / 60000, status: readJson(path.join(dir, 'status.json')), traceTail: tail(path.join(dir, 'trace.log'), 20), playerLogTail: tail(path.join(dir, 'player.log'), 60) }, null, 1));
      log(`slot${s.slot} 멈춤 감지(${Math.round((now - beat) / 60000)}분) → 종료`);
      kill(s, 'hang');
    }
  }

  if (stopping) {
    const running = slots.filter(s => s.child);
    if (running.length === 0) { finish(); return; }
    if (now - stopSince > 5 * 60000) for (const s of running) kill(s, 'stopped');
  }
  writeState();
}

function finish() {
  writeState({ finishedUtc: new Date().toISOString() });
  log(`러너 종료 — 세션 ${sessionsStarted}개`);
  if (fs.existsSync(STOP)) fs.unlinkSync(STOP);
  process.exit(0);
}

process.on('SIGINT', () => { if (!fs.existsSync(STOP)) fs.writeFileSync(STOP, 'SIGINT'); });

log(`러너 시작: 인스턴스 ${INSTANCES}(chaos ${CHAOS}) · 마감 ${deadline.toLocaleString()} · 세션 상한 ${MAX_SESSIONS}${HEADLESS ? ' · headless' : ''} · 빌드 ${latestBuild() || '없음'}`);
writeState();
tick();
setInterval(tick, POLL_MS);
