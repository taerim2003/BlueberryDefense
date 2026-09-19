// MainSkillTree.asset의 각 노드에 `cost:` 줄을 주입한다(tier: 바로 다음 줄).
//
// 왜 스크립트인가 — 84개를 손으로 치면 오타가 반드시 나고, 그 오타는 "공짜 노드"나
// 단조성 위반(부모보다 싼 자식)으로 조용히 나타난다. 주입 + 자동 검사가 유일하게 안전하다.
//
// 재실행 안전(idempotent): 이미 cost가 있으면 삽입이 아니라 값 교체.
// 사용법: node Tools/SkillTree/inject-costs.js
// 실행 후 반드시: node Tools/SkillTree/verify-costs.js

const fs = require('fs');
const path = require('path');
const ASSET = path.join(__dirname, '..', '..', 'Assets', 'SkillTree', 'MainSkillTree.asset');

// 그래프 깊이 순서대로. 같은 깊이 안에서도 서로 다른 값 — 겹치는 가격이 없다.
// 앵커: atk_1 = 8(1차 진화로 가는 길이라 가장 싸다) · arrow_StartLev = 100(루트 직결 중 가장 비싸다)
//       1차 진화까지의 총액 = Root 1 + atk_1 8 + New_Lightning 11 + New_Evolution 52 = 72정수(옛 71과 사실상 동일)
const COSTS = {
  Root_Skilltree: 1,

  atk_1: 8, gold_1: 15, New_Reroll: 36, arrow_StartLev: 100,

  New_Lightning: 11, New_Orb: 17, crit_1: 22, critdmg_1: 40, reroll_2: 44, gold_2: 104,

  hp_1: 25, fly_1: 28, New_Shotgun: 45, cool_1: 48, New_Evolution: 52, cool_2: 55,
  critdmg_2: 63, tornado_CoolDownBonus: 108, reroll_4: 185,

  exp_1: 32, New_Homing: 60, eagle_DropNum: 80, hp_2: 89, orb_BasicSlow: 106, fly_2: 112,
  shotgun_BonusHit: 114, New_Rewind: 128, atk_4: 500, cool_4: 547, exp_4: 593,

  atk_2: 123, boss_2: 131, crit_2: 139, Homing_MissileNum: 148, reroll_3: 156, rewind_NoGcd: 165,
  atk_3: 203, gold_3: 220, hp_3: 238, knowledge_BaseXp: 255, arrow_Pierce: 640,

  boss_1: 50, Sniping_TwoTarget: 173, thunder_Stack: 182, assassin_BaseCrit: 273, critdmg_3: 290,
  health_BaseHp: 308, swing_StartLev: 325, hp_4: 687, shotgun_Crit: 733, hp_6: 6000,

  exp_2: 190, boss_3: 343, cool_3: 360, exp_3: 378, boss_4: 780, gold_4: 827, tornado_Fly: 873,
  atk_5: 1850, gold_5: 1985, knowledge_EvoHint: 2120,

  accel_BaseCool: 395, defense_BaseReduce: 413, strength_BaseDmg: 430, eagle_fly: 920,
  homing_Cooldown: 967, orb_Pierce: 1013, Rewind_Slow: 1060, sniping_Crit: 1107,
  thunder_Cooldown: 1153, cool_5: 2255,

  reroll_5: 1200, boss_5: 2390, exp_5: 2525, hp_5: 2660, atk_6: 6450,

  New_Evolution2: 1900, assassin_FullCritHit: 2795, health_HealItem: 2930, swing_Knockback: 3200,
  accel_FastSkillDmg: 6900, defense_Revive: 7350, strength_SlowSkill: 7800,

  // 대공 3종(2026-09-19 사용자 — G4는 화력이 아니라 대응 폭 문제라서).
  // 🔴 전부 1250 이상이다. 45% 기준 트리의 최고가가 308이라 기준 트리는 이걸 못 산다 —
  //    G2(기준 트리 서열)를 안 건드리고 G4(풀트리)만 움직이게 하는 장치다. 값을 내리면 그 분리가 깨진다.
  fly_3: 1460,
};

// 🔴 boss_1의 선행을 crit_2(139) → crit_1(22)로 옮긴다.
//    안 옮기면 boss_1이 139 이상이어야 하고, 그러면 boss_2(131)보다 비싸져 "I이 II보다 비싸다"가 된다.
//    boss_1은 자식이 없는 말단이라 파급이 없다.
const REWIRE = { boss_1: { from: 'crit_2', to: 'crit_1' } };

const text = fs.readFileSync(ASSET, 'utf8');
const eol = text.includes('\r\n') ? '\r\n' : '\n';       // 줄바꿈 섞이면 git diff가 전 파일로 번진다
const lines = text.split(/\r?\n/);

const seen = new Set();
const out = [];
let current = null, injected = 0, replaced = 0, rewired = 0, inPrereq = false;

for (const line of lines) {
  // 노드 시작은 정확히 2칸 들여쓴 "- id:" 뿐이다. prereqIds 안의 "    - X"와 구분된다.
  const m = line.match(/^  - id: (.+)$/);
  if (m) {
    current = m[1].trim();
    inPrereq = false;
    if (!(current in COSTS)) throw new Error(`가격표에 없는 노드: ${current}`);
    if (seen.has(current)) throw new Error(`중복 노드 id: ${current}`);
    seen.add(current);
    out.push(line);
    continue;
  }

  if (/^    prereqIds:/.test(line)) { inPrereq = true; out.push(line); continue; }

  // prereqIds 항목 재배선
  if (inPrereq) {
    const pm = line.match(/^    - (.+)$/);
    if (pm) {
      const r = REWIRE[current];
      if (r && pm[1].trim() === r.from) { out.push(`    - ${r.to}`); rewired++; continue; }
      out.push(line);
      continue;
    }
    inPrereq = false;   // 시퀀스 끝
  }

  // 이미 cost가 있으면 값만 교체(재실행 안전)
  if (current && /^    cost: /.test(line)) { out.push(`    cost: ${COSTS[current]}`); replaced++; continue; }

  out.push(line);

  // tier 줄 바로 뒤에 삽입 — SkillNode의 필드 선언 순서(tier 다음 cost)와 맞춘다
  if (current && /^    tier: /.test(line) && !text.includes(`\n    cost:`)) { out.push(`    cost: ${COSTS[current]}`); injected++; }
}

const missing = Object.keys(COSTS).filter(id => !seen.has(id));
if (missing.length) throw new Error(`에셋에 없는 노드가 가격표에 있다: ${missing.join(', ')}`);

fs.writeFileSync(ASSET, out.join(eol));
console.log(`노드 ${seen.size}개 · 삽입 ${injected} · 교체 ${replaced} · 선행 재배선 ${rewired}건`);
if (seen.size !== 85) console.log('🔴 노드 수가 85가 아니다');
if (injected + replaced !== seen.size) console.log(`🔴 가격이 붙은 노드가 ${injected + replaced}개뿐이다`);
