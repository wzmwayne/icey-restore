/* ICEY · 网页版切片（教程关）—— 场景渲染 + 键盘/虚拟按键控制
 * 素材来源：由玩家自有游戏包导出的 scene.json（精灵矩形 + 变换 + AABB 碰撞盒），不含原包内容。
 * 键盘默认模拟桌面端布局；ICEY 无跳跃，核心为 移动 / 攻击 / 冲刺 / 重击 / 特殊 / 选择。
 */
'use strict';

const SCENES = ['C0L1S1_t', 'C0L1S2_t', 'C0L1S3_t', 'C0L1S4_t', 'C1L1S1_t'];
const DEFAULTS = {
  left: 'a', right: 'd',
  attack: 'j',    // 轻攻击
  heavy: 'i',     // 重攻击
  execute: 'o',   // 裁决回血（QTE）
  jump: 'k',      // 跳跃
  dash: 'l',      // 闪避 / 位移（类似飞）
  spiral: 'n',    // 大螺旋（耗血技能）
  flash: 'm',     // 闪现攻击（耗血技能）
  special: 'u',   // 特殊
  choiceL: 'q', choiceR: 'e'
};
const ACTIONS = ['attack', 'heavy', 'execute', 'jump', 'dash', 'spiral', 'flash', 'special'];
const KEYMAP_KEY = 'icey_web_keymap';
const VPAD_KEY = 'icey_web_vpad';

const keymap = Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem(KEYMAP_KEY) || '{}'));
const down = {};        // 当前按下的动作
const vpad = {};        // 虚拟按键按下状态

/* ───────────── 画布 ───────────── */
const cv = document.getElementById('cv'), ctx = cv.getContext('2d', { alpha: false });
let DPR = Math.min(window.devicePixelRatio || 1, 2);

function resize() {
  cv.width = Math.floor(innerWidth * DPR); cv.height = Math.floor(innerHeight * DPR);
}
addEventListener('resize', resize); resize();

/* ───────────── 场景数据 ───────────── */
let scene = null, sprites = [], colliders = [], images = new Map(), cam = { x: 0, y: 0, size: 6 };
let world = { minX: -10, maxX: 10, minY: -10, maxY: 10 };

async function loadScene(name) {
  const res = await fetch(`scenes/${name}/scene.json`);
  scene = await res.json();
  const trById = new Map(scene.transforms.map(t => [t.id, t]));
  const byGo = new Map(scene.transforms.map(t => [t.go, t]));

  // 世界变换：沿父链累加位置与缩放（M1 只取 2D 旋转）
  function worldOf(tr) {
    let x = 0, y = 0, sx = 1, sy = 1;
    const r = 0, s = 1;
    let cur = tr, guard = 0;
    while (cur && guard++ < 64) {
      x = x * (cur.scale[0] || 1) + cur.pos[0];
      y = y * (cur.scale[1] || 1) + cur.pos[1];
      sx *= cur.scale[0] || 1; sy *= cur.scale[1] || 1;
      cur = cur.father ? trById.get(cur.father) : null;
    }
    return { x, y, sx, sy, z: tr.pos[2] };
  }

  sprites = [];
  for (const s of scene.sprites) {
    if (!s.active) continue;
    const tr = byGo.get(s.go);
    if (!tr) continue;
    const w = worldOf(tr);
    const texPath = s.tex || '';
    if (texPath && !images.has(texPath)) {
      const img = new Image(); img.src = 'scenes/' + texPath.replace(/^\.\.\//, '');
      images.set(texPath, img);
    }
    const rect = s.rect || [0, 0, 0, 0];
    const ppu = s.ppu || 100;
    sprites.push({
      img: images.get(texPath), rect, order: s.order, layer: s.layer, color: s.color || [1, 1, 1, 1],
      flipX: s.flipX, flipY: s.flipY, w: rect[2] / ppu, h: rect[3] / ppu,
      x: w.x, y: w.y, sx: w.sx, sy: w.sy, z: w.z
    });
  }
  colliders = [];
  for (const c of (scene.colliders || [])) {
    const tr = byGo.get(c.go); if (!tr) continue;
    const w = worldOf(tr);
    const cw = Math.abs(c.size[0] * w.sx), ch = Math.abs(c.size[1] * w.sy);
    colliders.push({ x: w.x + (c.offset[0] || 0) * w.sx, y: w.y + (c.offset[1] || 0) * w.sy,
                     hw: cw / 2, hh: ch / 2 });
  }
  // 视野包围盒
  let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
  for (const s of sprites) {
    minX = Math.min(minX, s.x - s.w / 2); maxX = Math.max(maxX, s.x + s.w / 2);
    minY = Math.min(minY, s.y - s.h / 2); maxY = Math.max(maxY, s.y + s.h / 2);
  }
  world = { minX, maxX, minY, maxY };
  fitView();
  document.getElementById('stat').textContent = `${scene.sprites.length} 精灵 · ${colliders.length} 碰撞盒`;
  player.x = minX + 4; player.y = 6; player.vy = 0;
}

function fitView() {
  const w = world.maxX - world.minX, h = world.maxY - world.minY;
  cam.size = Math.max(h / 2 + 1, (w / 2 + 1) / (cv.width / cv.height));
  cam.x = (world.minX + world.maxX) / 2; cam.y = (world.minY + world.maxY) / 2;
}

/* ───────────── 占位玩家（真实角色精灵将在后续接入） ───────────── */
const player = { x: 0, y: 2, vy: 0, w: 0.7, h: 1.6, face: 1, speed: 5.2, onGround: false,
                 jumpV: 13.5, act: {} };

function groundAt(x) {
  let top = world.minY;
  for (const c of colliders) {
    if (x < c.x - c.hw - 0.1 || x > c.x + c.hw + 0.1) continue;
    top = Math.max(top, c.y + c.hh);
  }
  return top;
}

/** 水平方向被墙挡住则退回（简单 AABB 推挤） */
function resolveX(nx) {
  const y = player.y, hw = player.w / 2, hh = player.h / 2;
  for (const c of colliders) {
    if (y + hh < c.y - c.hh || y - hh > c.y + c.hh) continue;   // 垂直不重叠
    if (c.y + c.hh - (y - hh) < 0.35) continue;                 // 允许踩上去的台阶
    if (nx + hw > c.x - c.hw && nx - hw < c.x + c.hw) return true;
  }
  return false;
}

function updatePlayer(dt) {
  const ax = (act('left') ? -1 : 0) + (act('right') ? 1 : 0);
  if (ax) player.face = ax;
  const nx = player.x + ax * player.speed * dt;
  if (!resolveX(nx)) player.x = nx;
  player.x = Math.max(world.minX + 1, Math.min(world.maxX - 1, player.x));
  // 跳跃（K）：需落地；其余动作空中亦可（先作视觉反馈，接真实战斗逻辑时替换）
  if (pressed('jump') && player.onGround) { player.vy = player.jumpV; player.onGround = false; player.act.jump = 0.2; }
  const g = groundAt(player.x);
  player.vy += -30 * dt; player.y += player.vy * dt;
  if (player.y <= g + player.h / 2) { player.y = g + player.h / 2; player.vy = 0; player.onGround = true; }
  else player.onGround = false;
  for (const a of ACTIONS) if (pressed(a)) player.act[a] = (a === 'dash' || a === 'flash') ? 0.28 : 0.2;
  for (const a of ACTIONS) if (player.act[a] > 0) player.act[a] -= dt;
  if (act('follow')) { }
  if (followOn) { cam.x += (player.x - cam.x) * Math.min(1, dt * 6); cam.y += (player.y + 1 - cam.y) * Math.min(1, dt * 6); }
}

/* ───────────── 输入 ───────────── */
function act(a) { return !!down[a] || !!vpad[a]; }
const prev = {};
function pressed(a) { const v = act(a); const was = prev[a]; prev[a] = v; return v && !was; }

function codeToKey(e) {
  let k = e.key;
  if (k === ' ') k = 'space';
  if (k === 'Shift') k = 'shift';
  return (k || '').toLowerCase();
}
addEventListener('keydown', e => {
  const k = codeToKey(e);
  for (const a in keymap) if (keymap[a] === k) { down[a] = true; e.preventDefault(); }
});
addEventListener('keyup', e => {
  const k = codeToKey(e);
  for (const a in keymap) if (keymap[a] === k) { down[a] = false; e.preventDefault(); }
});

// 虚拟按键
const pad = document.getElementById('pad');
function setVpad(on) {
  pad.classList.toggle('mobile', on);
  document.getElementById('btnVpad').classList.toggle('on', on);
  localStorage.setItem(VPAD_KEY, on ? '1' : '0');
}
pad.querySelectorAll('.btn').forEach(b => {
  const a = b.dataset.act;
  const on = ev => { ev.preventDefault(); vpad[a] = true; b.classList.add('press'); };
  const off = ev => { ev.preventDefault(); vpad[a] = false; b.classList.remove('press'); };
  b.addEventListener('pointerdown', on); b.addEventListener('pointerup', off);
  b.addEventListener('pointercancel', off); b.addEventListener('pointerleave', off);
});

/* ───────────── 渲染 ───────────── */
function draw() {
  const W = cv.width, H = cv.height;
  ctx.fillStyle = '#0b0b0e'; ctx.fillRect(0, 0, W, H);
  const scale = H / (cam.size * 2);            // 像素 / 世界单位
  const cx = W / 2 - cam.x * scale, cy = H / 2 + cam.y * scale;
  const list = sprites.slice().sort((a, b) => (a.layer - b.layer) || (a.order - b.order) || (b.z - a.z));
  for (const s of list) {
    if (!s.img || !s.img.complete) continue;
    const w = s.w * s.sx * scale, h = s.h * s.sy * scale;
    if (w <= 0 || h <= 0) continue;
    const x = cx + s.x * scale - w / 2, y = cy - s.y * scale - h / 2;
    if (x + w < -50 || x > W + 50) continue;
    ctx.globalAlpha = s.color[3];
    ctx.drawImage(s.img, s.rect[0], s.rect[1], s.rect[2], s.rect[3], x, y, s.flipX ? -w : w, s.flipY ? -h : h);
  }
  ctx.globalAlpha = 1;
  // 占位玩家
  const pw = player.w * scale, ph = player.h * scale;
  const px = cx + player.x * scale - pw / 2, py = cy - player.y * scale - ph / 2;
  const A = player.act;
  ctx.fillStyle = A.execute > 0 ? '#ff9ecb'
    : A.spiral > 0 ? '#c8a0ff'
    : A.flash > 0 ? '#9effd0'
    : A.dash > 0 ? '#8ae0ff'
    : A.heavy > 0 ? '#ff8a5c'
    : A.attack > 0 ? '#ffd479'
    : '#e8e8f2';
  ctx.fillRect(px, py, pw, ph);
  if (A.dash > 0 || A.flash > 0) { ctx.globalAlpha = .35; ctx.fillRect(px - pw * .5, py, pw * 2, ph); ctx.globalAlpha = 1; }
  ctx.fillStyle = '#22222e'; ctx.fillRect(px + (player.face > 0 ? pw * .55 : pw * .1), py + ph * .2, pw * .35, ph * .18);
}

/* ───────────── 主循环 ───────────── */
let last = performance.now(), followOn = true, fpsT = 0, fpsN = 0;
function loop(t) {
  const dt = Math.min(0.05, (t - last) / 1000); last = t;
  updatePlayer(dt); draw();
  fpsN++; if (t - fpsT > 500) { document.getElementById('stat').textContent = `${sprites.length} 精灵 · ${colliders.length} 碰撞盒 · ${(fpsN * 1000 / (t - fpsT)).toFixed(0)} fps`; fpsT = t; fpsN = 0; }
  requestAnimationFrame(loop);
}

/* ───────────── 界面接线 ───────────── */
const sel = document.getElementById('scene');
for (const n of SCENES) { const o = document.createElement('option'); o.value = n; o.textContent = n; sel.appendChild(o); }
sel.onchange = () => loadScene(sel.value).then(() => requestAnimationFrame(loop));
document.getElementById('btnFit').onclick = () => fitView();
document.getElementById('btnFollow').onclick = e => { followOn = !followOn; e.target.classList.toggle('on', followOn); };
document.getElementById('btnVpad').onclick = () => setVpad(!pad.classList.contains('mobile'));
document.getElementById('btnKeys').onclick = () => document.getElementById('keys').classList.toggle('show');

const inputs = document.querySelectorAll('#keys input');
function fillInputs() { inputs.forEach(i => i.value = (keymap[i.dataset.k] || '').toUpperCase()); }
inputs.forEach(i => {
  i.addEventListener('keydown', e => {
    e.preventDefault();
    const k = codeToKey(e);
    keymap[i.dataset.k] = k;
    localStorage.setItem(KEYMAP_KEY, JSON.stringify(keymap));
    fillInputs();
  });
});
document.getElementById('btnReset').onclick = () => {
  Object.assign(keymap, DEFAULTS); localStorage.setItem(KEYMAP_KEY, JSON.stringify(keymap)); fillInputs();
};
fillInputs();
setVpad(localStorage.getItem(VPAD_KEY) === '1' || (matchMedia('(pointer: coarse)').matches && localStorage.getItem(VPAD_KEY) !== '0'));

loadScene(SCENES[0]).then(() => requestAnimationFrame(loop));
