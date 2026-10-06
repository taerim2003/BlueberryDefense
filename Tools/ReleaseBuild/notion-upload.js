// Upload a build zip to the Notion "프로토타입 빌드" page as a file block, right after the last file on the page.
// usage: node Tools/ReleaseBuild/notion-upload.js <zipPath> [pageId]
//
// The Notion MCP tools have no file upload, so this calls the REST API directly with the token the
// `notion` MCP server already uses (~/.claude.json → notion.env.OPENAPI_MCP_HEADERS). The token is never printed.
// Files over 20MB must go multi_part (5–20MB parts); 10MB parts are used.
// Name: BlueberryDefense_YYYYMMDD.zip, or ..._HHmm.zip when that name is already on the page.
const fs = require('fs');
const os = require('os');
const path = require('path');

const DEFAULT_PAGE = '3616394c-d983-801c-8057-d74dec76c92b';
const [zipPath, pageId = DEFAULT_PAGE] = process.argv.slice(2);
if (!zipPath) { console.error('usage: node notion-upload.js <zipPath> [pageId]'); process.exit(2); }

function findHeaders(o) {
  if (!o || typeof o !== 'object') return null;
  if (o.notion && o.notion.env && o.notion.env.OPENAPI_MCP_HEADERS) return o.notion.env.OPENAPI_MCP_HEADERS;
  for (const v of Object.values(o)) { const r = findHeaders(v); if (r) return r; }
  return null;
}
const headersJson = findHeaders(JSON.parse(fs.readFileSync(path.join(os.homedir(), '.claude.json'), 'utf8')));
if (!headersJson) { console.error('ERROR notion MCP token not found in ~/.claude.json'); process.exit(1); }
const H = { Authorization: JSON.parse(headersJson).Authorization, 'Notion-Version': '2022-06-28' };
const base = 'https://api.notion.com/v1';

async function api(method, url, body) {
  const res = await fetch(base + url, { method, headers: { ...H, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  const j = await res.json();
  if (!res.ok) throw new Error(method + ' ' + url + ' -> ' + res.status + ' ' + JSON.stringify(j));
  return j;
}
const pad = n => String(n).padStart(2, '0');
const fileBlocks = async () => (await api('GET', '/blocks/' + pageId + '/children?page_size=100')).results.filter(b => b.type === 'file');

(async () => {
  const now = new Date();
  const day = now.getFullYear() + pad(now.getMonth() + 1) + pad(now.getDate());
  const before = await fileBlocks();
  const taken = new Set(before.map(b => b.file && b.file.name));
  let name = 'BlueberryDefense_' + day + '.zip';
  if (taken.has(name)) name = 'BlueberryDefense_' + day + '_' + pad(now.getHours()) + pad(now.getMinutes()) + '.zip';

  const buf = fs.readFileSync(zipPath);
  const PART = 10 * 1024 * 1024;
  const parts = Math.ceil(buf.length / PART);
  console.log('name', name, 'size', buf.length, 'parts', parts);

  const up = await api('POST', '/file_uploads', { mode: 'multi_part', number_of_parts: parts, filename: name, content_type: 'application/zip' });
  for (let i = 0; i < parts; i++) {
    const chunk = buf.subarray(i * PART, Math.min(buf.length, (i + 1) * PART));
    for (let attempt = 1; ; attempt++) {
      const fd = new FormData();
      fd.append('file', new Blob([chunk], { type: 'application/zip' }), name);
      fd.append('part_number', String(i + 1));
      const res = await fetch(base + '/file_uploads/' + up.id + '/send', { method: 'POST', headers: H, body: fd });
      if (res.ok) { console.log('part', i + 1, '/', parts); break; }
      const t = await res.text();
      if (attempt >= 3) throw new Error('part ' + (i + 1) + ' failed: ' + res.status + ' ' + t);
      console.log('part', i + 1, 'retry', attempt, res.status);
    }
  }
  const done = await api('POST', '/file_uploads/' + up.id + '/complete', {});
  if (done.status !== 'uploaded') throw new Error('complete status ' + done.status);

  const body = { children: [{ type: 'file', file: { type: 'file_upload', file_upload: { id: up.id }, name } }] };
  if (before.length) body.after = before[before.length - 1].id;
  await api('PATCH', '/blocks/' + pageId + '/children', body);

  // 되읽어 확인 — 응답이 성공이어도 페이지에 실제로 붙었는지는 다시 읽어야 안다.
  const after = await fileBlocks();
  const ok = after.some(b => b.file && b.file.name === name && b.file.type === 'file');
  console.log(ok ? 'VERIFIED' : 'NOT FOUND', name, 'files on page', before.length, '->', after.length);
  if (!ok) process.exit(1);
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
