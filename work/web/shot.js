/* 无头截图：以 DOM 桩加载真实页面脚本 → 驱动 N 帧 → 记录绘制调用 → 输出 /tmp/drawlist.json */
'use strict';
const fs = require('fs');
const ROOT = '/home/wayne/date/icey/web/';
const W = 1280, H = 720, FRAMES = parseInt(process.argv[2] || '60', 10);
const html = fs.readFileSync(ROOT + 'index.html', 'utf8');
const scripts = [...html.matchAll(/<script src="([^"]+)"/g)].map(m => m[1].split('?')[0]);   // 去掉缓存版本号

const rec = [], stack = [{ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 }];
const mul = (m, n) => ({ a: m.a * n.a + m.c * n.b, b: m.b * n.a + m.d * n.b,
                         c: m.a * n.c + m.c * n.d, d: m.b * n.c + m.d * n.d,
                         e: m.a * n.e + m.c * n.f + m.e, f: m.b * n.e + m.d * n.f + m.f });
const ctx = {
  canvas: { width: W, height: H }, fillStyle: '#000', strokeStyle: '#fff', globalAlpha: 1, filter: 'none', lineWidth: 1,
  save() { stack.push({ ...stack[stack.length - 1] }); }, restore() { stack.pop(); },
  translate(x, y) { stack[stack.length - 1] = mul(stack[stack.length - 1], { a: 1, b: 0, c: 0, d: 1, e: x, f: y }); },
  rotate(r) { const c = Math.cos(r), s = Math.sin(r); stack[stack.length - 1] = mul(stack[stack.length - 1], { a: c, b: s, c: -s, d: c, e: 0, f: 0 }); },
  setTransform(m) { stack[stack.length - 1] = { a: m.a, b: m.b, c: m.c, d: m.d, e: m.e, f: m.f }; },
  clearRect() { }, strokeRect() { },
  fillRect(x, y, w, h) { rec.push({ op: 'fill', m: { ...stack[stack.length - 1] }, x, y, w, h, color: ctx.fillStyle, alpha: ctx.globalAlpha }); },
  drawImage(img, ...a) {
    let sx = 0, sy = 0, sw = img.naturalWidth, sh = img.naturalHeight, dx, dy, dw, dh;
    if (a.length >= 8) { [sx, sy, sw, sh, dx, dy, dw, dh] = a; }
    else { [dx, dy, dw, dh] = a; }
    rec.push({ op: 'img', m: { ...stack[stack.length - 1] }, img: img._name || '', sx, sy, sw, sh, dx, dy, dw, dh, alpha: ctx.globalAlpha });
  },
};

// —— DOM / 浏览器桩 ——
const els = {};
const mkEl = (tag = 'div') => {
  const el = { tagName: tag, id: '', hidden: false, textContent: '', innerHTML: '', style: { setProperty() { } }, dataset: {},
    width: W, height: H, _cls: new Set(), _ev: {},
    classList: { add(c) { el._cls.add(c); }, remove(c) { el._cls.delete(c); }, toggle(c, v) { v === undefined ? (el._cls.has(c) ? el._cls.delete(c) : el._cls.add(c)) : (v ? el._cls.add(c) : el._cls.delete(c)); }, contains(c) { return el._cls.has(c); } },
    addEventListener(t, f) { (el._ev[t] = el._ev[t] || []).push(f); }, setPointerCapture() { }, removeAttribute(k) { if (k === 'hidden') el.hidden = true; }, hasAttribute(k) { return k === 'hidden' ? el.hidden : false; },
    getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 100 }; }, appendChild() { }, querySelectorAll() { return []; }, querySelector() { return null; },
    getContext() { return ctx; }, toDataURL() { return 'data:,'; }, play() { return Promise.resolve(); }, pause() { }, remove() { } };
  return el;
};
const byId = {};
global.document = {
  getElementById(id) { if (!byId[id]) { byId[id] = mkEl(); byId[id].id = id; } return byId[id]; },
  createElement: (t) => mkEl(t), querySelector: () => global.document.getElementById('bar'), querySelectorAll: () => [], body: { appendChild() { } }, addEventListener() { },
};
global.window = global; global.innerWidth = W; global.innerHeight = H; global.devicePixelRatio = 1;
const evs = {}; global.addEventListener = (t, f) => { (evs[t] = evs[t] || []).push(f); };
global.requestAnimationFrame = cb => { global._raf = cb; return 1; };
global.performance = { now: () => Date.now() };
global.localStorage = { _d: {}, getItem(k) { return this._d[k] ?? null; }, setItem(k, v) { this._d[k] = v; } };
global.matchMedia = () => ({ matches: false });
global.Image = class { constructor() { this.naturalWidth = 256; this.complete = true; } set src(v) { this._name = v; setTimeout(() => this.onload && this.onload(), 0); } };
global.Audio = class { play() { return Promise.resolve(); } pause() { } };
global.fetch = async u => ({ json: async () => JSON.parse(fs.readFileSync(ROOT + u, 'utf8')) });

for (const s of scripts) require(ROOT + s);

(async () => {
  for (const f of (evs['load'] || [])) await f();
  await new Promise(r => setTimeout(r, 400));
  for (let i = 0; i < FRAMES; i++) { const cb = global._raf; global._raf = null; if (!cb) break; cb(performance.now() + i * 16); }
  const U = global.Unity;
  fs.writeFileSync('/tmp/drawlist.json', JSON.stringify({
    W, H, canvas: { w: ctx.canvas.width, h: ctx.canvas.height }, draws: rec,
    objects: U.SceneManager.active.gameObjects.length,
    spawn: global.__spawn || null,
    camera: U.SceneManager.mainCamera ? { x: U.SceneManager.mainCamera.transform.matrix.e, y: U.SceneManager.mainCamera.transform.matrix.f, size: U.SceneManager.mainCamera.orthographicSize } : null,
  }));
  console.log('  已记录绘制调用:', rec.length, '｜场景对象:', U.SceneManager.active.gameObjects.length);
})();
