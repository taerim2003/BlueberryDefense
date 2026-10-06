// 대변인이 측정 **도중에** 자기 스킬을 관찰하는 도구(사용자 지시 2026-09-30).
// 봇이 쓰는 live.jsonl(층마다·획득·진화·판 시작/끝)에서 새 줄을 읽어, 담당 스킬이 나온 것만 한국어 한 줄씩 요약해 출력한다.
// 새 기록이 없으면 이 스크립트가 대신 기다린다(에이전트가 토큰을 쓰지 않고 대기하게).
//
// 사용: node Tools/BotPlaytest/live-watch.js --iter 2 --skill Shotgun   --state <상태파일> [--wait 540]
//       node Tools/BotPlaytest/live-watch.js --iter 2 --passive Strength,Health,... --state <상태파일> [--wait 540]
// 출력 끝줄: "상태: 새 기록" · "상태: 대기 만료(새 기록 없음)" · "상태: 측정 종료" — 측정 종료면 최종 판정을 쓰면 된다.
const fs = require('fs');
const path = require('path');

const BOTRUNS = path.resolve(__dirname, '..', '..', 'BotRuns');
const argv = process.argv.slice(2);
const opt = (k, d) => { const i = argv.indexOf('--' + k); return i >= 0 && i + 1 < argv.length ? argv[i + 1] : d; };
const ITER = String(opt('iter', '')).padStart(2, '0');
const SKILL = opt('skill', null);
const PASSIVE = opt('passive', null);                  // 쉼표로 여러 개 — 패시브 담당 한 명이 6종을 한 번에 본다
const PASSIVES = PASSIVE ? PASSIVE.split(',') : [];
const STATE = opt('state', null);
const WAIT = parseInt(opt('wait', '540')) * 1000;
if (!ITER || (!SKILL && !PASSIVE) || !STATE) { console.error('--iter, --skill 또는 --passive, --state가 필요합니다'); process.exit(1); }

const KO = { BasicAttack: '화살 사격', Whirlwind: '회오리', Orb: '오브', Lightning: '낙뢰', EagleDrop: '독수리 투하', Sniping: '스나이핑',
  Homing: '호밍 미사일', Shotgun: '산탄 발사', Rewind: '되감기', Swing: '휘두르기', GrapeToss: '독성 포도알',
  Strength: '힘', Health: '건강', Knowledge: '지식', Assassinate: '암살', Defense: '방어', Accel: '가속' };
const MAP = { Map_BlueberryField: '농장', Map_Wide15: '해변', Map_Wide20: '우주' };
const RESULT = { clear: '클리어', dead: '사망', stuck: '멈춤', timeout: '시간 초과', yielded: '양보' };
const ko = (id) => KO[id] || id;
const pct = (x) => Math.round((x || 0) * 100) + '%';
const num = (x) => Math.round(x || 0).toLocaleString('en-US');
const evoName = (st, route) => st >= 2 ? `2차(R${route})` : st === 1 ? `1차(R${route})` : '진화 전';

function sessions() {
  if (!fs.existsSync(BOTRUNS)) return [];
  return fs.readdirSync(BOTRUNS).filter(n => new RegExp(`_it${ITER}-`).test(n)).map(n => path.join(BOTRUNS, n));
}
function readJson(p, d) { try { return JSON.parse(fs.readFileSync(p, 'utf8')); } catch { return d; } }
function allDone(dirs) {
  if (!dirs.length) return false;
  return dirs.every(d => { const s = readJson(path.join(d, 'status.json'), {}); return ['done', 'error', 'aborted'].includes(s.state); });
}

// 판(세션+run) 단위로 "이 판에 내 스킬이 있었나"를 기억한다 — run-end 줄 자체엔 보유 목록이 없다.
function tag(l) { return `[${MAP[l.map] || l.map} · ${ko(l.targetSkill)} R${l.targetRoute} 원정 #${l.run}]`; }

function relevantLines(lines, st, dir) {
  const out = [];
  for (const l of lines) {
    const key = dir + '#' + l.run;
    if (l.t === 'stage') {
      const mines = SKILL ? (l.skills || []).filter(s => s.id === SKILL) : (l.passives || []).filter(p => PASSIVES.includes(p.id));
      if (!mines.length) continue;
      st.seen[key] = true;
      for (const mine of mines) if (SKILL) {
        out.push(`${tag(l)} ${l.stage}층 · 레벨 ${mine.level} · ${evoName(mine.evoStage, mine.route)} · 이 층 피해 비중 ${pct(mine.share)} · 순수 피해 ${num(mine.pure)} · 시전 ${mine.casts}회 · 처치 ${mine.kills} · 받은 피해 ${num(l.damageTaken)} · 층 시간 ${Math.round(l.stageTime)}초`);
      } else {
        const top = (l.skills || []).slice().sort((a, b) => b.share - a.share).map(s => `${ko(s.id)} ${pct(s.share)}`).join(' · ');
        out.push(`${tag(l)} ${ko(mine.id)} · ${l.stage}층 · 레벨 ${mine.level} · ${evoName(mine.evoStage, mine.route)} · 받은 피해 ${num(l.damageTaken)} · 체력 ${l.hpStart}→${l.hpEnd} · 플레이어 레벨 ${l.playerLevel} · 피해 비중 ${top}`);
      }
    } else if ((l.t === 'acquire' || l.t === 'evolve') && (SKILL ? l.id === SKILL : PASSIVES.includes(l.id))) {
      st.seen[key] = true;
      out.push(l.t === 'acquire' ? `${tag(l)} ${ko(l.id)} ${l.stage}층에서 획득` : `${tag(l)} ${ko(l.id)} ${l.stage}층에서 ${evoName(l.evoStage, l.route)} 진화`);
    } else if (l.t === 'run-end' && st.seen[key]) {
      let line = `${tag(l)} 판 종료: ${RESULT[l.result] || l.result} · ${l.stageReached}층 · 받은 피해 합 ${num(l.damageTaken)}`;
      if (PASSIVE) {
        const runs = fs.existsSync(path.join(dir, 'runs.jsonl')) ? fs.readFileSync(path.join(dir, 'runs.jsonl'), 'utf8').trim().split('\n') : [];
        const rec = runs.map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean).find(r => r.run === l.run);
        for (const p of (rec && rec.passives || []).filter(x => PASSIVES.includes(x.id))) {
          const fields = Object.keys(p).filter(k => k.endsWith('ByEvoStage')).map(k => `${k.replace('ByEvoStage', '')}=${(p[k] || []).map(v => num(v)).join('/')}`);
          line += `\n    ${ko(p.id)} 패시브 기록(진화 전/1차): ${fields.join(', ')}${p.revived ? ' · 부활 사용' : ''}`;
        }
      }
      out.push(line);
    }
  }
  return out;
}

async function main() {
  const st = readJson(STATE, { offsets: {}, seen: {} });
  const deadline = Date.now() + WAIT;
  for (;;) {
    const dirs = sessions();
    const out = [];
    for (const d of dirs) {
      const f = path.join(d, 'live.jsonl');
      if (!fs.existsSync(f)) continue;
      const buf = fs.readFileSync(f, 'utf8');
      const from = st.offsets[d] || 0;
      if (buf.length <= from) continue;
      const chunk = buf.slice(from);
      const lastNl = chunk.lastIndexOf('\n');
      if (lastNl < 0) continue;                     // 줄이 아직 다 안 써졌다
      const lines = chunk.slice(0, lastNl).split('\n').filter(Boolean).map(x => { try { return JSON.parse(x); } catch { return null; } }).filter(Boolean);
      st.offsets[d] = from + lastNl + 1;
      out.push(...relevantLines(lines, st, d));
    }
    if (out.length) {
      fs.writeFileSync(STATE, JSON.stringify(st));
      console.log(out.join('\n'));
      console.log('상태: 새 기록');
      return;
    }
    if (allDone(dirs)) { fs.writeFileSync(STATE, JSON.stringify(st)); console.log('상태: 측정 종료'); return; }
    if (Date.now() > deadline) { fs.writeFileSync(STATE, JSON.stringify(st)); console.log('상태: 대기 만료(새 기록 없음)'); return; }
    await new Promise(r => setTimeout(r, 15000));
  }
}
main();
