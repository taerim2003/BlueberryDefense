// 스킬트리 노드 가격 검사기 — Unity를 켜지 않고 asset YAML을 직접 읽는다.
//
// 검사 항목
//   1) 단조성 : 모든 간선에서 자식 가격 >= 부모 가격. 자식이 해금류(SkillUnlock=1 / SpecialUnlock=3)면 예외.
//   2) 고유성 : 서로 같은 가격을 가진 노드 쌍(사용자 요구 — "겹치는 노드가 거의 없었으면")
//   3) 도달성 : 루트에서 닿지 않는 노드
//   4) 총비용 : Σ Σ_{L<maxLevel} round(cost × 1.5^L)  — BotTree.TotalCost()와 같은 식
//   5) 기준 트리: BuildReferenceSave(ratio)와 같은 "싼 것부터" 규칙으로 무엇을 사는지 재현
//
// 사용법: node Tools/SkillTree/verify-costs.js [트리비율(기본 0.45)]
// 위반이 있으면 exit 1 — pre-commit이나 CI에 그대로 걸 수 있다.
//
// ⚠️ cost 필드가 아직 없는 에셋에서도 돌아야 한다(도입 전 상태를 재는 것이 이 스크립트의 첫 임무다).
//    그 경우 옛 공식(tier 등비 + 해금 0.6배)으로 가격을 계산해 같은 검사를 돌린다.

const fs = require('fs');
const path = require('path');

const ASSET = path.join(__dirname, '..', '..', 'Assets', 'SkillTree', 'MainSkillTree.asset');
const RATIO = Number(process.argv[2] || 0.45);

// ── 옛 공식(SkillTreeData.cs) — cost 필드가 없을 때만 쓴다 ──
const TIER_BASE = 20, TIER_RATIO = 3.2, UNLOCK_MULT = 0.6;
const LEVEL_GROWTH = 1.5;
const isUnlock = t => t === 1 || t === 3;
// Unity의 Mathf.RoundToInt는 짝수 반올림(은행가 반올림)이다 — 0.5에서 갈리므로 맞춰 둔다.
const roundHalfEven = x => {
  const f = Math.floor(x), d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
};
const legacyCost = n => {
  const tierCost = n.tier <= 0 ? 1 : roundHalfEven(TIER_BASE * Math.pow(TIER_RATIO, n.tier - 1));
  return isUnlock(n.type) && n.tier > 0 ? Math.max(1, roundHalfEven(tierCost * UNLOCK_MULT)) : tierCost;
};

// ── 파싱 ──
const text = fs.readFileSync(ASSET, 'utf8');
const blocks = text.split(/\n  - id: /).slice(1);
const nodes = blocks.map(b => {
  const lines = b.split('\n');
  const id = lines[0].trim();
  const scalar = k => {
    const m = b.match(new RegExp('\\n    ' + k + ': (.*)'));
    return m ? m[1].trim() : null;
  };
  // prereqIds는 "    prereqIds:" 다음의 "    - X" 줄들. 다른 시퀀스와 섞이지 않게 그 블록만 읽는다.
  const pre = [];
  const at = b.indexOf('\n    prereqIds:');
  if (at >= 0) {
    for (const line of b.slice(at + 1).split('\n').slice(1)) {
      const m = line.match(/^    - (.+)$/);
      if (!m) break;
      pre.push(m[1].trim());
    }
  }
  const num = k => { const v = scalar(k); return v === null ? null : Number(v); };
  const type = num('type') ?? 0;
  const maxLevelRaw = num('maxLevel') ?? 1;
  return {
    id, type,
    tier: num('tier') ?? 0,
    costField: num('cost'),              // null이면 아직 도입 전
    // MaxLevelOf: Normal(0)만 maxLevel을 쓰고 나머지는 1로 강제된다
    maxLevel: type === 0 ? Math.max(1, maxLevelRaw) : 1,
    prereqIds: pre,
  };
});

const usingCostField = nodes.some(n => n.costField !== null);
nodes.forEach(n => { n.cost = usingCostField ? Math.max(1, n.costField ?? 1) : legacyCost(n); });
const byId = new Map(nodes.map(n => [n.id, n]));

console.log(`노드 ${nodes.length}개 · 가격 출처: ${usingCostField ? 'cost 필드' : '옛 tier 공식(도입 전)'}`);

let problems = 0;

// 1) 단조성
const viol = [];
for (const n of nodes) {
  if (isUnlock(n.type)) continue;                 // 해금류 자식은 더 싸도 된다
  for (const pid of n.prereqIds) {
    const p = byId.get(pid);
    if (!p) { viol.push(`${n.id}: 없는 선행 '${pid}'`); continue; }
    if (n.cost < p.cost) viol.push(`${pid}(${p.cost}) -> ${n.id}(${n.cost})  차이 ${n.cost - p.cost}`);
  }
}
console.log(`\n[1] 단조성 위반 ${viol.length}건`);
viol.forEach(v => console.log('   ', v));
problems += viol.length;

// 2) 고유성
const groups = new Map();
nodes.forEach(n => { if (!groups.has(n.cost)) groups.set(n.cost, []); groups.get(n.cost).push(n.id); });
const dups = [...groups.entries()].filter(([, ids]) => ids.length > 1).sort((a, b) => b[1].length - a[1].length);
const dupNodes = dups.reduce((a, [, ids]) => a + ids.length, 0);
console.log(`\n[2] 가격 고유성 — 서로 다른 가격 ${groups.size}종 / 노드 ${nodes.length}개 · 겹치는 노드 ${dupNodes}개`);
dups.slice(0, 8).forEach(([c, ids]) => console.log(`    ${c}정수 × ${ids.length}: ${ids.slice(0, 6).join(', ')}${ids.length > 6 ? ' …' : ''}`));

// 3) 도달성
const roots = nodes.filter(n => n.prereqIds.length === 0).map(n => n.id);
const seen = new Set(roots);
for (let changed = true; changed;) {
  changed = false;
  for (const n of nodes) {
    if (seen.has(n.id)) continue;
    if (n.prereqIds.some(p => seen.has(p))) { seen.add(n.id); changed = true; }
  }
}
const unreachable = nodes.filter(n => !seen.has(n.id)).map(n => n.id);
console.log(`\n[3] 도달 불가 ${unreachable.length}개 ${unreachable.join(', ')}`);
problems += unreachable.length;

// 4) 총비용
const nodeTotal = n => { let s = 0; for (let L = 0; L < n.maxLevel; L++) s += roundHalfEven(n.cost * Math.pow(LEVEL_GROWTH, L)); return s; };
const totalCost = nodes.reduce((a, n) => a + nodeTotal(n), 0);
const totalLevels = nodes.reduce((a, n) => a + n.maxLevel, 0);
console.log(`\n[4] 전체 만렙 총비용 ${totalCost.toLocaleString()}정수 · 총 ${totalLevels}레벨`);

// 5) 기준 트리 재현 — BuildReferenceSave와 같은 규칙(싼 것부터, 선행 충족, 레벨 비율 도달까지)
const target = Math.ceil(totalLevels * RATIO);
const lv = new Map(nodes.map(n => [n.id, 0]));
const owned = id => lv.get(id) > 0;
const canBuy = n => lv.get(n.id) < n.maxLevel && (n.prereqIds.length === 0 || n.prereqIds.some(owned));
const nextCost = n => roundHalfEven(n.cost * Math.pow(LEVEL_GROWTH, lv.get(n.id)));
let ownedLevels = 0, spent = 0, ties = 0, guard = 0;
while (ownedLevels < target && ++guard < 5000) {
  const buyable = nodes.filter(canBuy);
  if (!buyable.length) break;
  const min = Math.min(...buyable.map(nextCost));
  const cheapest = buyable.filter(n => nextCost(n) === min);
  if (cheapest.length > 1) ties++;
  const pick = cheapest[0];
  spent += min; lv.set(pick.id, lv.get(pick.id) + 1); ownedLevels++;
}
const ownedNodes = nodes.filter(n => owned(n.id));
const priciest = ownedNodes.reduce((a, n) => (a && a.cost >= n.cost ? a : n), null);
console.log(`\n[5] ${(RATIO * 100).toFixed(0)}% 기준 트리 — ${ownedLevels}/${totalLevels}레벨 · 노드 ${ownedNodes.length}개 · 지출 ${spent.toLocaleString()} · 최고가 ${priciest ? priciest.cost + '(' + priciest.id + ')' : '-'}`);
console.log(`    동가 경합 ${ties}회 (많을수록 시드에 따라 기준 트리가 달라진다)`);
console.log(`    보유 노드: ${ownedNodes.map(n => n.id).sort().join(' ')}`);

console.log(`\n${problems === 0 ? '통과 — 위반 0건' : '🔴 문제 ' + problems + '건'}`);
process.exit(problems === 0 ? 0 : 1);
