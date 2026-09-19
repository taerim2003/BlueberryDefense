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
  const app = byId['app'];
  const txt = app ? app.textContent : '';
  console.log('렌더 성공 — append 호출 ' + count + '회, 최상위 자식 ' + (app ? app.children.length : 0) + '개, 텍스트 ' + txt.length + '자');
  if (/\bnull\b|\bundefined\b|NaN/.test(txt)) {
    const bad = txt.match(/.{0,40}(null|undefined|NaN).{0,40}/g).slice(0, 5);
    console.log('⚠ 화면 텍스트에 null/undefined/NaN이 보인다:');
    for (const b of bad) console.log('   …' + b.replace(/\s+/g, ' ') + '…');
  } else console.log('null/undefined/NaN 누출 없음');
  // 새로 넣은 것들이 실제로 그려졌는지
  // 해설 블록은 prose()가 그리는 세 칸 라벨로 센다 — 예전 문구('이 표가 말하는 것')는 더 이상 안 쓴다.
  for (const [name, needle] of [['핵심 결론', '핵심 결론'], ['해설 블록(현재 상황)', '현재 상황'], ['해설 블록(개선안)', '개선안'], ['해설 블록(기대 목표)', '기대 목표']]) {
    const n = (txt.match(new RegExp(needle, 'g')) || []).length;
    console.log(`${name}: ${n}개 ${n ? '렌더됨' : '🔴 안 나옴'}`);
  }
} catch (e) {
  console.log('🔴 렌더 실패 — ' + e.message);
  console.log((e.stack || '').split('\n').slice(0, 4).join('\n'));
  process.exit(1);
}
