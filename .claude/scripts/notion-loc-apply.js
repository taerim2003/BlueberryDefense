#!/usr/bin/env node
// 노션 "UI 문구 편집" 시트를 게임 TSV로 되가져오는 도구.
//
// 쓰는 법:
//   node .claude/scripts/notion-loc-apply.js <시트마크다운파일> [--apply]
//   (--apply 없으면 무엇이 바뀔지만 보여주고 파일은 안 건드린다)
//
// 노션 페이지는 Claude 가 MCP(API-retrieve-page-markdown)로 읽어 파일로 저장한 뒤 이 스크립트에 넘긴다.
// 반영 뒤에는 Unity 에서 `Window > Blueberry Defense > 번역 - 모든 TSV를 표에 적재` 를 돌리고
// `Loc.T` 로 몇 건 조회해 확인할 것.
//
// 🔴 설계 원칙 — **없는 키는 절대 만들지 않는다.**
//    TSV 에 이미 있는 키에만 값을 덮어쓴다. 매칭 안 된 줄은 건드리지 않고 목록으로 보고한다.
//    시트마다 KEY 가 전체 키일 때도, 접두사가 빠진 줄기일 때도 있어서(예: `skill.BasicAttack`
//    → 실제 `skill.name.BasicAttack`) 규칙을 손으로 적어두면 낡는다. 그래서 **후보를 만들어
//    실제 TSV 에 있는 것을 고르고, 열마다 매칭률을 찍는다.** 100% 가 아니면 그 자리에서 드러난다.

const fs = require("fs");
const path = require("path");

const TSV_DIR = "Assets/Localization";

// ── 노션이 마크다운으로 돌려줄 때 바뀌는 것들을 되돌린다(카드 「복원 규칙」 실측) ──
// 🔴 **값은 trim 하지 않는다.** 앞뒤 공백이 의도인 문구가 있다(`ui.tree.click` = "  > 클릭" —
//    툴팁에서 들여쓰기 역할). 전수 왕복 검사에서 trim 때문에 3줄이 조용히 바뀌는 걸 잡았다.
//    노션은 셀을 `<td>값</td>` 한 줄로 돌려주므로 마크다운 쪽 패딩도 없다.
function restore(text) {
  if (text == null) return "";
  let s = String(text);
  // 노션이 붙인 이스케이프 백슬래시를 뗀다(중괄호·꺾쇠·물결 등). 우리 문구에 진짜 백슬래시는 없다.
  s = s.replace(/\\([^A-Za-z0-9\s])/g, "$1");
  // ⏎ 기호는 게임의 줄바꿈이다. 백슬래시를 그대로 올리면 노션이 먹어서 기호로 바꿔 올렸다.
  s = s.replace(/⏎/g, "\\n");
  return s;
}

function stripKey(text) {
  return restore(text).replace(/^`+|`+$/g, "").trim();
}

// ── 노션 마크다운의 <table> 한 개를 헤더 + 행들로 뜯는다 ──
function parseTable(md) {
  const t = md.match(/<table[^>]*>([\s\S]*?)<\/table>/);
  if (!t) throw new Error("<table> 을 못 찾았다 — 페이지 형식이 바뀌었는지 볼 것");
  const rows = [...t[1].matchAll(/<tr>([\s\S]*?)<\/tr>/g)].map((m) =>
    [...m[1].matchAll(/<td>([\s\S]*?)<\/td>/g)].map((c) => c[1])
  );
  if (rows.length < 2) throw new Error("행이 2개 미만이다");
  // 헤더는 이름표라 다듬어도 되지만, 값은 restore 가 손대지 않는다(앞뒤 공백이 의도인 문구가 있다).
  return { header: rows[0].map((h) => restore(h).trim()), rows: rows.slice(1) };
}

// ── 현재 TSV 를 통째로 읽어 키 색인을 만든다 ──
function loadTsvs() {
  const index = new Map(); // key -> {file, line, ko}
  const files = fs.readdirSync(TSV_DIR).filter((f) => f.endsWith("_ko.tsv"));
  const contents = new Map();
  for (const f of files) {
    const p = path.join(TSV_DIR, f);
    const lines = fs.readFileSync(p, "utf8").split("\n");
    contents.set(f, lines);
    lines.forEach((line, i) => {
      if (i === 0 || !line.trim()) return;
      const tab = line.indexOf("\t");
      if (tab < 0) return;
      const key = line.slice(0, tab).trim();
      if (!key) return;
      index.set(key, { file: f, line: i, ko: line.slice(tab + 1) });
    });
  }
  return { index, contents };
}

// ── 한 열이 어떤 키 규칙을 쓰는지 데이터에서 찾아낸다 ──
// KEY 가 줄기일 수도 전체일 수도 있어서, 후보를 만들어 **실제로 있는 키**를 고른다.
const INFIXES = ["name", "desc", "title"];
const PREFIXES = ["evo.active.title.", "evo.active.desc.", "evo.passive.title.", "evo.passive.desc.",
                  "evo.name.", "tree.name.", "tree.desc.", "skill.name.", "skill.desc."];

function candidatesFor(key) {
  const out = [key];
  const dot = key.indexOf(".");
  if (dot > 0) {
    const head = key.slice(0, dot), rest = key.slice(dot + 1);
    for (const inf of INFIXES) out.push(head + "." + inf + "." + rest);
  }
  for (const p of PREFIXES) out.push(p + key);
  return out;
}

function pickTemplate(rowsKeys, index) {
  // 후보 종류별로 몇 줄이 맞는지 세어 가장 많이 맞는 것을 고른다.
  const score = new Map();
  for (const k of rowsKeys) {
    if (!k) continue;
    candidatesFor(k).forEach((cand, i) => {
      if (index.has(cand)) score.set(i, (score.get(i) || 0) + 1);
    });
  }
  if (score.size === 0) return null;
  const best = [...score.entries()].sort((a, b) => b[1] - a[1])[0];
  return { candIndex: best[0], matched: best[1] };
}

function main() {
  const args = process.argv.slice(2);
  const apply = args.includes("--apply");
  const file = args.find((a) => !a.startsWith("--"));
  if (!file) { console.error("사용법: node notion-loc-apply.js <시트마크다운파일> [--apply]"); process.exit(2); }

  const md = fs.readFileSync(file, "utf8");
  const { header, rows } = parseTable(md);
  const { index, contents } = loadTsvs();

  const keyCol = header.findIndex((h) => h.toUpperCase() === "KEY");
  if (keyCol < 0) throw new Error("KEY 열이 없다. 헤더: " + header.join(" | "));

  const rowKeys = rows.map((r) => stripKey(r[keyCol] ?? ""));
  console.log("시트: " + path.basename(file));
  console.log("헤더: " + header.join(" | "));
  console.log("행 " + rows.length + "개, KEY 열 = " + keyCol);

  // 한국어 내용 열만 고른다 — EN 로 시작하는 열과 KEY 열, 그리고 분류용 열은 뺀다.
  const contentCols = header
    .map((h, i) => ({ h, i }))
    .filter(({ h, i }) => i !== keyCol && !/^EN\b/i.test(h) && !["종류", "구분", "분류"].includes(h));

  let changed = 0, same = 0, unmatched = 0;
  const diffs = [], misses = [];

  for (const { h, i } of contentCols) {
    const tpl = pickTemplate(rowKeys, index);
    // 열마다 따로 정한다: 그 열 값이 실제로 들어갈 키를 후보 중에서 고른다.
    let colMatched = 0;
    for (let r = 0; r < rows.length; r++) {
      const key = rowKeys[r];
      const raw = rows[r][i];
      if (!key || raw === undefined) continue;
      const value = restore(raw);
      if (value.trim() === "") continue; // 빈 칸은 건드리지 않는다(공백만 있는 칸도 빈 칸으로 본다)

      // 이 열의 이름(이름/설명/제목)에 맞는 후보를 우선 시도하고, 없으면 전체 후보를 훑는다.
      const infix = h.includes("설명") ? "desc" : h.includes("제목") ? "title" : h.includes("이름") ? "name" : null;
      const cands = candidatesFor(key);
      const ordered = infix
        ? cands.slice().sort((a, b) => (b.includes("." + infix + ".") ? 1 : 0) - (a.includes("." + infix + ".") ? 1 : 0))
        : cands;
      const hit = ordered.find((c) => index.has(c));
      if (!hit) { unmatched++; misses.push(h + " / " + key); continue; }
      colMatched++;

      const cur = index.get(hit).ko;
      if (cur === value) { same++; continue; }
      changed++;
      diffs.push({ key: hit, col: h, from: cur, to: value });
    }
    console.log("  열 [" + h + "] 매칭 " + colMatched + "/" + rows.length);
  }

  console.log("\n동일 " + same + "줄 · 변경 " + changed + "줄 · 매칭실패 " + unmatched + "줄");
  if (misses.length) {
    console.log("\n[매칭 실패 — 건드리지 않음]");
    misses.slice(0, 20).forEach((m) => console.log("   " + m));
    if (misses.length > 20) console.log("   … 외 " + (misses.length - 20) + "건");
  }
  if (diffs.length) {
    console.log("\n[바뀔 줄]");
    diffs.slice(0, 40).forEach((d) => {
      console.log("   " + d.key);
      console.log("      전: " + d.from);
      console.log("      후: " + d.to);
    });
    if (diffs.length > 40) console.log("   … 외 " + (diffs.length - 40) + "건");
  }

  if (!apply) { console.log("\n(미적용 — 실제로 쓰려면 --apply)"); return; }
  if (diffs.length === 0) { console.log("\n바뀔 게 없어 쓰지 않았다."); return; }

  for (const d of diffs) {
    const loc = index.get(d.key);
    const lines = contents.get(loc.file);
    lines[loc.line] = d.key + "\t" + d.to;
  }
  const touched = new Set(diffs.map((d) => index.get(d.key).file));
  for (const f of touched) {
    fs.writeFileSync(path.join(TSV_DIR, f), contents.get(f).join("\n"));
    console.log("썼다: " + f);
  }
  console.log("\n이제 Unity 에서 `Window > Blueberry Defense > 번역 - 모든 TSV를 표에 적재` 를 돌릴 것.");
}

main();
