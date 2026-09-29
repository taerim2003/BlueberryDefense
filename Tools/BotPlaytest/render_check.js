// 보고서를 진짜로 그려 본다. 문법 통과 ≠ 렌더 성공 — 런타임 예외가 나면 화면이 통째로 빈다.
// jsdom이 없어서 report.html이 실제로 쓰는 DOM API만 최소 구현한다.
const fs = require('fs');

class El {
  constructor(tag) { this.tag = tag; this.kids = []; this.attrs = {}; this.text = ''; this.className = ''; this.innerHTML = ''; this.hidden = false; }
  append(...xs) { for (const x of xs) { if (x === null || x === undefined) throw new Error(`append(${x}) — <${this.tag}>에 null/undefined를 붙였다`); this.kids.push(x); } }
  setAttribute(k, v) { this.attrs[k] = v; }
  getAttribute(k) { return this.attrs[k]; }
  addEventListener() {}
  get textContent() { return (this.text || '') + this.kids.map(k => (k instanceof El ? k.textContent : String(k))).join(''); }
  // 🔴 설정자가 없으면 `el.textContent = …` 대입이 조용히 사라져, 그 글자가 검사 대상에서 빠진다.
  //    실제 DOM과 같게 자식을 비우고 글자를 넣는다(2026-09-29).
  set textContent(v) { this.text = String(v); this.kids = []; }
  get style() { return this._style || (this._style = { setProperty() {} }); }
  set style(v) { this.attrs.style = v; }
  querySelectorAll() { return []; }
  replaceChildren() { this.kids = []; }
  get children() { return this.kids.filter(k => k instanceof El); }
  get childNodes() { return this.kids; }
  get classList() { return { add(){}, remove(){}, toggle(){} }; }
}
const doc = {
  createElement: t => new El(t),
  createElementNS: (ns, t) => new El(t),
  createTextNode: t => String(t),
  getElementById: id => (byId[id] = byId[id] || new El('div')),
};
const byId = {};
global.document = doc;
global.Node = El;
global.window = { addEventListener() {} };
global.getComputedStyle = () => ({ getPropertyValue: () => '#000000' });

const src = fs.readFileSync(process.argv[2], 'utf8');
const script = src.match(/<script>([\s\S]*)<\/script>/)[1];

let count = 0;
const origAppend = El.prototype.append;
El.prototype.append = function (...xs) { count += xs.length; return origAppend.apply(this, xs); };

try {
  new Function(script)();
  // 🔴 `#app` 하나만 보면 컨테이너를 여러 개 쓰는 보고서에서 텍스트를 한 글자도 못 읽는다.
  //    그러면 아래 null 누출 검사와 기대 문구 검사가 **빈 문자열을 검사하고 통과**한다(2026-09-29).
  //    루트가 있으면 그것만, 없으면 스크립트가 꺼내 쓴 모든 컨테이너를 합쳐서 본다.
  const app = byId['app'];
  const roots = app ? [app] : Object.values(byId);
  const txt = roots.map(e => e.textContent).join('\n');
  const kids = roots.reduce((n, e) => n + e.children.length, 0);
  console.log('렌더 성공 — append 호출 ' + count + '회, 최상위 자식 ' + kids + '개, 텍스트 ' + txt.length + '자');
  if (/\bnull\b|\bundefined\b|NaN/.test(txt)) {
    const bad = txt.match(/.{0,40}(null|undefined|NaN).{0,40}/g).slice(0, 5);
    console.log('⚠ 화면 텍스트에 null/undefined/NaN이 보인다:');
    for (const b of bad) console.log('   …' + b.replace(/\s+/g, ' ') + '…');
  } else console.log('null/undefined/NaN 누출 없음');
  // 새로 넣은 것들이 실제로 그려졌는지
  // 기대 문자열은 인자로 받는다 — 보고서가 둘이고(난이도 index.html · 스킬 skills.html) 각자 다른 라벨을 쓴다.
  //   node render_check.js <html> [--expect "문구1,문구2,…"]
  // 안 주면 난이도 보고서의 기본 라벨을 본다(그쪽이 기존 호출부다).
  const i = process.argv.indexOf('--expect');
  const needles = i > 0 && process.argv[i + 1]
    ? process.argv[i + 1].split(',').map(s => s.trim()).filter(Boolean)
    : ['핵심 결론', '현재 상황', '개선안', '기대 목표'];
  let missing = 0;
  for (const needle of needles) {
    const n = txt.split(needle).length - 1;
    if (!n) missing++;
    console.log(`  ${needle}: ${n}개 ${n ? '렌더됨' : '🔴 안 나옴'}`);
  }
  if (missing) { console.log('🔴 기대 문구 ' + missing + '건이 화면에 없다'); process.exit(1); }
} catch (e) {
  console.log('🔴 렌더 실패 — ' + e.message);
  console.log((e.stack || '').split('\n').slice(0, 4).join('\n'));
  process.exit(1);
}
