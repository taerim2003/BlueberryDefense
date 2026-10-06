// 원정(2차 진화) 측정을 QA 빌드 exe로 headless 병렬 실행한다 — 에디터를 점유하지 않는다.
// balance-loop의 원정 측정(BotPilot RunExpedition)을 빌드에서 돌리는 러너. 결과는 BotRuns/에 써서 analyze.js가 그대로 읽는다.
// 🔴 한 회차 = 세 맵 전부(사용자 결정 2026-09-30). 맵마다 기준 트리 비율이 다르다 — 농장 0.7 · 해변 0.85 · 우주 1.0.
//    농장은 기준 트리에 2차 진화 해금이 들어가지 않으므로 1차 진화까지만 판정에 쓴다(사용자 결정 2026-09-30).
// 사용: node Tools/QA/expedition-run.js --iter 2 [--maps farm,coast,space] [--runs 3] [--perMap 2] [--items a,b,c] [--seed 93101]
// 세션 라벨이 it<NN>-exp-<맵><번호>라 analyze.js가 iterations/<NN>.json 회차로 자동으로 묶는다.
// 루프가 아니다 — 한 번 돌리고 끝난다(모든 인스턴스 종료까지 대기).
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const QA = path.join(ROOT, 'QARuns');
const BUILDS = path.join(QA, 'builds');
const PLAYER = path.join(QA, 'player');
const BOTRUNS = path.join(ROOT, 'BotRuns');
const LOADOUTS = path.join(ROOT, 'Tools', 'BotPlaytest', 'expedition_loadouts.json');

const MAPS = {
  farm:  { key: 'Map_BlueberryField', ratio: 0.7 },
  coast: { key: 'Map_Wide15',         ratio: 0.85 },
  space: { key: 'Map_Wide20',         ratio: 1.0 },
};

const argv = process.argv.slice(2);
const opt = (k, d) => { const i = argv.indexOf('--' + k); return i >= 0 && i + 1 < argv.length ? argv[i + 1] : d; };
const ITER = opt('iter', null);
const MAP_NAMES = opt('maps', 'farm,coast,space').split(',').map(s => s.trim()).filter(Boolean);
const ASC = parseInt(opt('ascension', '3'));
const RUNS = parseInt(opt('runs', '3'));
const PER_MAP = parseInt(opt('perMap', '2'));
const SEED = parseInt(opt('seed', String(93101)));
// 동시에 띄우는 플레이어 수의 상한. 봇 하나가 약 3GB를 써서, 6개를 한꺼번에 띄우면 RAM 16GB PC가 스와핑으로 멈춘다(2026-10-01).
// 상한을 넘는 인스턴스는 앞의 것이 끝나면 차례로 뜬다 — 판 구성·라벨·시드는 그대로라 결과는 같고 시간만 늘어난다.
const PARALLEL = parseInt(opt('parallel', '0')) || Infinity;

const readJson = (p, d) => { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return d; } };
const log = (m) => console.log(`[${new Date().toISOString().slice(11, 19)}] ${m}`);

function allItems() {
  const j = readJson(LOADOUTS, null);
  const list = Array.isArray(j) ? j : (j && (j.loadouts || j.items)) || Object.values(j || {})[0] || [];
  return list.map(x => x.id);
}
const ITEMS = opt('items', '') ? opt('items', '').split(',').map(s => s.trim()).filter(Boolean) : allItems();

function ensurePlayer(buildId) {
  const cur = readJson(path.join(PLAYER, 'build.json'), null);
  if (cur && cur.buildId === buildId && fs.existsSync(path.join(PLAYER, 'BlueberryDefense.exe'))) return true;
  fs.rmSync(PLAYER, { recursive: true, force: true });
  fs.cpSync(path.join(BUILDS, buildId), PLAYER, { recursive: true });
  return fs.existsSync(path.join(PLAYER, 'BlueberryDefense.exe'));
}

// 항목을 인스턴스에 라운드로빈으로 분배
function split(items, n) {
  const groups = Array.from({ length: n }, () => []);
  items.forEach((it, i) => groups[i % n].push(it));
  return groups.filter(g => g.length > 0);
}

function main() {
  if (!ITER) { console.error('--iter <회차 번호>가 필요합니다(세션을 회차에 묶는 데 씁니다).'); process.exit(1); }
  for (const m of MAP_NAMES) if (!MAPS[m]) { console.error(`모르는 맵: ${m} (farm·coast·space)`); process.exit(1); }
  const it = String(ITER).padStart(2, '0');
  const buildId = fs.readFileSync(path.join(BUILDS, 'latest.txt'), 'utf8').trim();
  log(`빌드 ${buildId} → player 복사`);
  if (!ensurePlayer(buildId)) { console.error('빌드 복사 실패'); process.exit(1); }
  const build = readJson(path.join(BUILDS, buildId, 'build.json'), {});
  const exe = path.join(PLAYER, 'BlueberryDefense.exe');
  const stamp = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14).replace(/(\d{8})(\d{6})/, '$1-$2');

  const jobs = [];
  for (const m of MAP_NAMES)
    split(ITEMS, Math.min(PER_MAP, ITEMS.length)).forEach((items, i) => jobs.push({ map: m, items, i }));
  log(`${it}회차 · 맵=[${MAP_NAMES.join(',')}] · 항목 ${ITEMS.length}개 × 항목당 ${RUNS}판 · 인스턴스 ${jobs.length}개` +
      (PARALLEL < jobs.length ? ` (동시 ${PARALLEL}개씩)` : ''));
  log(`총 ${ITEMS.length * RUNS * MAP_NAMES.length}판`);

  const children = [];
  let next = 0;
  const launchNext = () => { if (next < jobs.length) start(jobs[next], next++); };
  const start = (job, slot) => {
    const label = `it${it}-exp-${job.map}${job.i}`;
    const dir = path.join(BOTRUNS, `${stamp}_${label}`);
    fs.mkdirSync(dir, { recursive: true });
    const cfg = {
      label, mode: 'expedition',
      expeditionLoadouts: LOADOUTS,            // 절대경로 — 빌드 ProjectRoot가 player 폴더라 상대경로는 못 찾는다
      expeditionItems: job.items, expeditionRuns: RUNS,
      expeditionMap: MAPS[job.map].key, expeditionAscension: ASC, expeditionTreeRatio: MAPS[job.map].ratio,
      characters: ['Char_Strawberry', 'Char_Pineapple', 'Char_Slot3'],
      runAudit: false, simStep: 1 / 30, seed: SEED + slot,
      stuckRealSeconds: 90, maxRunRealSeconds: 3600,
      sessionDir: dir, runsRoot: BOTRUNS,
      kind: 'balance', instance: String(slot), buildId, chaos: false, skipEnding: true,
    };
    fs.writeFileSync(path.join(dir, 'config.json'), JSON.stringify(cfg, null, 1));
    fs.writeFileSync(path.join(dir, 'fingerprint.json'), JSON.stringify(build.fingerprint || {}));
    // -job-worker-count 0: 잡 워커 23개가 쓰던 CPU(봇 CPU의 절반)를 없앤다 — 진행 속도는 같다(메모리 세션 실측 2026-10-01, 1.85→0.98코어).
    const args = ['-botConfig', path.join(dir, 'config.json'), '-logFile', path.join(dir, 'player.log'), '-batchmode', '-nographics', '-job-worker-count', '0'];
    const child = spawn(exe, args, { cwd: PLAYER, stdio: 'ignore', windowsHide: true });
    children.push({ slot, label, done: false });
    log(`slot${slot} 시작 ${label} (pid ${child.pid}) 항목 ${job.items.length}개`);
    child.on('exit', (code) => {
      const c = children.find(x => x.slot === slot); c.done = true;
      const st = readJson(path.join(dir, 'status.json'), {});
      log(`slot${slot} 종료 ${label} code=${code} state=${st.state || '?'} run=${st.run != null ? st.run : '?'}`);
      launchNext();
      if (children.length === jobs.length && children.every(x => x.done)) {
        log('전 인스턴스 종료. 분석: node Tools/BotPlaytest/analyze.js');
        process.exit(0);
      }
    });
    child.on('error', (e) => log(`slot${slot} 실행 실패: ${e.message}`));
  };
  for (let k = 0; k < Math.min(PARALLEL, jobs.length); k++) launchNext();
}

main();
