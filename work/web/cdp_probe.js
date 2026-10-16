/* 用 CDP 直接驱动 Chromium：收控制台/异常 + 读页面状态 + 截图 */
'use strict';
const URL_PAGE = process.argv[2] || 'http://127.0.0.1:8000/web/index.html';
const OUT = process.argv[3] || '/tmp/cdp_shot.png';
const WAIT = parseInt(process.argv[4] || '6000', 10);

const http = require('http');
const fs = require('fs');
const get = (path) => new Promise((res, rej) => http.get({ host: '127.0.0.1', port: 9222, path }, r => {
  let b = ''; r.on('data', d => b += d); r.on('end', () => res(JSON.parse(b || '{}')));
}).on('error', rej));

(async () => {
  const list = await get('/json/list');
  const page = (list.find(t => t.type === 'page') || list[0]);
  if (!page) { console.log('  没有 page target'); process.exit(1); }
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  let id = 0; const pending = new Map(); const logs = [];
  const send = (method, params) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); return; }
    if (m.method === 'Runtime.consoleAPICalled') logs.push('[console.' + m.params.type + '] ' + m.params.args.map(a => a.value ?? a.description ?? a.type).join(' ').slice(0, 300));
    if (m.method === 'Runtime.exceptionThrown') logs.push('[EXCEPTION] ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text).slice(0, 600));
    if (m.method === 'Log.entryAdded') logs.push('[log.' + m.params.entry.level + '] ' + (m.params.entry.text || '').slice(0, 300));
  };
  await new Promise(r => ws.onopen = r);
  await send('Runtime.enable'); await send('Log.enable'); await send('Page.enable'); await send('Network.enable');
  await send('Network.setCacheDisabled', { cacheDisabled: true });          // 关掉缓存，避免测到旧文件
  const bust = URL_PAGE + (URL_PAGE.includes('?') ? '&' : '?') + 't=' + Date.now();
  await send('Page.navigate', { url: bust });
  await new Promise(r => setTimeout(r, WAIT));
  const ev = async (expr) => {
    const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    return r?.result?.value;
  };
  const state = await ev(`JSON.stringify({
    build: window.__BUILD || null,
    hasUnity: typeof Unity !== 'undefined',
    hasPhys: typeof Physics2D !== 'undefined',
    hasScene: typeof SceneLoader !== 'undefined',
    err: (document.getElementById('err')||{}).textContent || null,
    diag: (document.getElementById('diag')||{}).textContent || null,
    diagHidden: (document.getElementById('diag')||{}).hidden,
    objects: (typeof Unity !== 'undefined' && Unity.SceneManager.active) ? Unity.SceneManager.active.gameObjects.length : -1,
    sprites: (typeof Unity !== 'undefined' && Unity.SceneManager.active) ? Unity.SceneManager.active.gameObjects.filter(g=>{const s=g.GetComponent(Unity.SpriteRenderer); return s && s.sprite;}).length : -1,
    frames: (typeof Unity !== 'undefined') ? Unity.Time.frameCount : -1,
    canvas: [document.querySelector('canvas').width, document.querySelector('canvas').height]
  })`);
  console.log('  页面状态:', state);
  if (logs.length) { console.log('  控制台/异常（前 12 条）:'); logs.slice(0, 12).forEach(l => console.log('    ' + l)); }
  else console.log('  控制台干净（无异常）');
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  if (shot && shot.data) { fs.writeFileSync(OUT, Buffer.from(shot.data, 'base64')); console.log('  截图:', OUT, fs.statSync(OUT).size, '字节'); }
  ws.close(); process.exit(0);
})();
