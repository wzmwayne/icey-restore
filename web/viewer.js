/* ICEY · 网页版切片（教程关）—— 场景渲染 + 键盘/虚拟按键控制
 * 素材来源：由玩家自有游戏包导出的 scene.json（精灵矩形 + 变换 + AABB 碰撞盒），不含原包内容。
 * 键盘默认模拟桌面端布局；ICEY 无跳跃，核心为 移动 / 攻击 / 冲刺 / 重击 / 特殊 / 选择。
 */
'use strict';

/* 真实剧情顺序：第一章第一小关起（C1L1S1 = 迷路森林） */
const SCENES = ['C1L1S1', 'C1L1S2', 'C1L1S3', 'C1L1S4', 'C1L1S5'];
/* 教程提示：取自游戏自身的本地化词条 ui/tutorial/* */
const TUT = [
  { k: 'move',             zh: '左右移动', ok: () => Math.abs(joy.x) > .2 || down.left || down.right },
  { k: 'attack',           zh: '轻攻击',   ok: () => did('attack') },
  { k: 'heavyAttack',      zh: '重攻击',   ok: () => did('heavy') },
  { k: 'jump',             zh: '跳跃',     ok: () => did('jump') },
  { k: 'flash',            zh: '消耗能量进行冲刺，非战斗状态下能量恢复速度加快', ok: () => did('dash') },
  { k: 'jumpAcross',       zh: '跳跃并冲刺，可以更方便地越过障碍', ok: () => did('jump') && did('dash') },
  { k: 'excute',           zh: '将敌人的核芯捏碎，恢复自身血量及护盾', ok: () => did('execute') },
  { k: 'armor',            zh: '护盾，存在时霸体且降低所受伤害', ok: () => did('spiral') },
  { k: 'breakAmmor',       zh: '连续的重攻击可以击破敌人的防御', ok: () => did('heavy') && did('heavy') },
  { k: 'charging',         zh: '当能量晶体出现时，长按吸收，松开释放全屏攻击', ok: () => did('special') },
  { k: 'moreComboMoreAttack', zh: '连击数越高，伤害越高', ok: () => clicks >= 6 },
  { k: 'warning',          zh: '看到 ！ 注意敌人的反击', ok: () => clicks >= 8 },
];
const done = new Set(); let tutIdx = 0, clicks = 0;
function did(a) { return !!player.act[a]; }
const DEFAULTS = {
  left: 'a', right: 'd', up: 'w', down: 's',
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
const HIDE_KEY = 'icey_web_hide_helpers';
/* 编辑器辅助/遮罩类对象：默认隐藏（BlackMask 是 64×32 的全屏黑罩，会盖住整关） */
const HELPER_RE = /^(BlackMask|Black|Mask.*|CLOSE|CLOSER|CloseIcon\w*|CrossIcon|Arrow\s*\(\d+\))$/;
const DIRMODE_KEY = 'icey_web_dirmode';
/* 动作 → 中文名（虚拟按键键面用） */
const CN = { attack: '轻攻', heavy: '重攻', execute: '裁决', jump: '跳跃', dash: '闪避',
             spiral: '大螺旋', flash: '闪现', special: '特殊', choiceL: '选择', choiceR: '选择',
             left: '左', right: '右', up: '上', down: '下' };
/* 远景层：不参与默认视野计算 */
const BG_RE = /^BG\d/i;
const VPAD_KEY = 'icey_web_vpad';

const keymap = Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem(KEYMAP_KEY) || '{}'));
let hideHelpers = localStorage.getItem(HIDE_KEY) !== '0';
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
let play = world;

async function loadScene(name) {
  const res = await fetch(`scenes/${name}/scene.json`);
  scene = await res.json();
  const trById = new Map(scene.transforms.map(t => [t.id, t]));
  const byGo = new Map(scene.transforms.map(t => [t.go, t]));

  // 正确的 2D 仿射矩阵链：world = parent × TRS(local)（位置/缩放/绕 Z 旋转）
  const IDENT = { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 };
  const mats = new Map();
  function compose(parent, t) {
    const ang = 2 * Math.atan2(t.rot[2] || 0, t.rot[3] || 1);      // 四元数 → 绕 Z 角
    const co = Math.cos(ang), si = Math.sin(ang);
    const sx = t.scale[0] || 1, sy = t.scale[1] || 1;
    const a = co * sx, b = si * sx, c = -si * sy, d = co * sy;
    const m = parent || IDENT;
    return { a: m.a * a + m.c * b, b: m.b * a + m.d * b,
             c: m.a * c + m.c * d, d: m.b * c + m.d * d,
             e: m.a * t.pos[0] + m.c * t.pos[1] + m.e,
             f: m.b * t.pos[0] + m.d * t.pos[1] + m.f };
  }
  function matOf(tr) {
    if (!tr) return IDENT;
    if (mats.has(tr.id)) return mats.get(tr.id);
    const m = compose(tr.father ? matOf(trById.get(tr.father)) : null, tr);
    mats.set(tr.id, m); return m;
  }
  function worldOf(tr) {
    const m = matOf(tr);
    return { x: m.e, y: m.f, sx: Math.hypot(m.a, m.b), sy: Math.hypot(m.c, m.d),
             rot: Math.atan2(m.b, m.a), m };
  }

  sprites = [];
  for (const s of scene.sprites) {
    if (!s.active) continue;
    if (hideHelpers && s.name && HELPER_RE.test(s.name)) continue;
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
    const pv = s.pivot || [0.5, 0.5];
    const w = world;
    sprites.push({
      img: images.get(texPath), rect, order: s.order, layer: s.layer, color: s.color || [1, 1, 1, 1],
      flipX: s.flipX, flipY: s.flipY, w: rect[2] / ppu, h: rect[3] / ppu,
      pivot: pv, x: w.x, y: w.y, sx: w.sx, sy: w.sy, rot: w.rot, z: 0
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
  // 世界包围盒（全部，用于边界）；可玩包围盒（排除远景层，用于默认视野）
  let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
  let pMinX = 1e9, pMaxX = -1e9, pMinY = 1e9, pMaxY = -1e9;
  for (const s of sprites) {
    minX = Math.min(minX, s.x - s.w / 2); maxX = Math.max(maxX, s.x + s.w / 2);
    minY = Math.min(minY, s.y - s.h / 2); maxY = Math.max(maxY, s.y + s.h / 2);
    if (BG_RE.test(s.name || '')) continue;
    pMinX = Math.min(pMinX, s.x - s.w / 2); pMaxX = Math.max(pMaxX, s.x + s.w / 2);
    pMinY = Math.min(pMinY, s.y - s.h / 2); pMaxY = Math.max(pMaxY, s.y + s.h / 2);
  }
  world = { minX, maxX, minY, maxY };
  play = (pMinX < 1e8) ? { minX: pMinX, maxX: pMaxX, minY: pMinY, maxY: pMaxY } : world;
  fitView();
  document.getElementById('stat').textContent = `${sprites.length} 精灵 · ${colliders.length} 碰撞盒`;
  player.x = play.minX + 3; player.y = play.minY + 6; player.vy = 0;
}

function fitView() {
  const src = play;                       // 默认只框可玩区域，不把远景与黑罩算进去
  const w = src.maxX - src.minX, h = src.maxY - src.minY;
  cam.size = Math.max(h / 2 + 1, (w / 2 + 1) / (cv.width / cv.height));
  cam.size = Math.max(3, Math.min(12, cam.size));      // 合理区间，避免拉太远
  cam.x = src.minX + 6;                                // 出生点附近
  cam.y = src.minY + cam.size * 0.55;
}
function zoom(f) { cam.size = Math.max(2.5, Math.min(20, cam.size * f)); }

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
  updateDir();
  const axKey = (act('left') ? -1 : 0) + (act('right') ? 1 : 0);
  const ax = Math.max(-1, Math.min(1, axKey + (joy.active ? joy.x : 0)));   // 摇杆模拟量 + 键盘
  if (ax) player.face = Math.sign(ax);
  const nx = player.x + ax * player.speed * dt;
  if (!resolveX(nx)) player.x = nx;
  player.x = Math.max(world.minX + 1, Math.min(world.maxX - 1, player.x));
  // 跳跃（K）：需落地；其余动作空中亦可（先作视觉反馈，接真实战斗逻辑时替换）
  if (pressed('jump') && player.onGround) { player.vy = player.jumpV; player.onGround = false; player.act.jump = 0.2; }
  const g = groundAt(player.x);
  player.vy += -30 * dt; player.y += player.vy * dt;
  if (player.y <= g + player.h / 2) { player.y = g + player.h / 2; player.vy = 0; player.onGround = true; }
  else player.onGround = false;
  for (const a of ACTIONS) if (pressed(a)) { player.act[a] = (a === 'dash' || a === 'flash') ? 0.28 : 0.2; clicks++; }
  for (const a of ACTIONS) if (player.act[a] > 0) player.act[a] -= dt;
  if (act('follow')) { }
  if (followOn) { cam.x += (player.x - cam.x) * Math.min(1, dt * 6); cam.y += (player.y + 1 - cam.y) * Math.min(1, dt * 6); }
}

/* ───────────── 全局禁用手势 / 右键菜单 ───────────── */
for (const ev of ['contextmenu', 'selectstart', 'dragstart', 'dblclick',
                  'gesturestart', 'gesturechange', 'gestureend']) {
  addEventListener(ev, e => e.preventDefault(), { passive: false });
}
addEventListener('auxclick', e => e.preventDefault());
addEventListener('touchmove', e => { if (e.touches.length > 1) e.preventDefault(); }, { passive: false });

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
let dirMode = localStorage.getItem(DIRMODE_KEY) || 'stick';   // stick | dpad
let vpadDir = -1;                                             // 十字键按下中的方向扇区

/* 虚拟按键键面跟随键位改绑自动同步 */
function syncPadLabels() {
  document.querySelectorAll('#pad .btn[data-act]').forEach(b => {
    const a = b.dataset.act;
    b.innerHTML = `${(keymap[a] || '?').toUpperCase()}<small>${CN[a] || ''}</small>`;
  });
}

/* 虚拟摇杆：输出模拟轴 joy.x ∈ [-1,1]（含死区），与键盘方向合并 */
const stick = document.getElementById('stick'), knob = document.getElementById('knob');
const STICK_R = 44, DEAD = 0.16;
const joy = { x: 0, y: 0, active: false, id: null, dir8: -1, up: false, down: false };
/* 八向量化：0=E 1=NE 2=N 3=NW 4=W 5=SW 6=S 7=SE，-1=无 */
function quant8(x, y) {
  if (Math.hypot(x, y) < 0.3) return -1;
  const deg = (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
  return Math.round(deg / 45) % 8;
}
const DIRV = [[1, 0], [0.7071, 0.7071], [0, 1], [-0.7071, 0.7071], [-1, 0], [-0.7071, -0.7071], [0, -1], [0.7071, -0.7071]];
function updateDir() {
  const kx = (down.left ? -1 : 0) + (down.right ? 1 : 0);
  const ky = (down.up ? 1 : 0) + (down.down ? -1 : 0);
  const dv = (dirMode === 'dpad' && vpadDir >= 0) ? DIRV[vpadDir] : null;   // 十字键优先
  const vx = dv ? dv[0] : (joy.active ? joy.x : 0), vy = dv ? dv[1] : (joy.active ? joy.y : 0);
  const x = Math.max(-1, Math.min(1, kx + vx)), y = Math.max(-1, Math.min(1, ky + vy));
  joy.dir8 = quant8(x, y);
  joy.up = joy.dir8 === 1 || joy.dir8 === 2 || joy.dir8 === 3;
  joy.down = joy.dir8 === 5 || joy.dir8 === 6 || joy.dir8 === 7;
}

function stickTo(ev) {
  const r = stick.getBoundingClientRect();
  const dx = ev.clientX - (r.left + r.width / 2), dy = ev.clientY - (r.top + r.height / 2);
  const len = Math.hypot(dx, dy) || 1, cl = Math.min(len, STICK_R);
  const nx = dx / len * cl, ny = dy / len * cl;
  knob.style.transform = `translate(${nx.toFixed(1)}px, ${ny.toFixed(1)}px)`;
  const deg = Math.atan2(-ny, nx) * 180 / Math.PI + 90;      // 箭头指向（0°=上）
  knob.style.setProperty('--rot', deg.toFixed(0) + 'deg');
  stick.classList.add('act');
  const v = nx / STICK_R, vv = -ny / STICK_R;
  joy.x = Math.abs(v) < DEAD ? 0 : Math.max(-1, Math.min(1, (v - Math.sign(v) * DEAD) / (1 - DEAD)));
  joy.y = Math.abs(vv) < DEAD ? 0 : Math.max(-1, Math.min(1, (vv - Math.sign(vv) * DEAD) / (1 - DEAD)));
}
if (stick) {
  stick.addEventListener('pointerdown', ev => {
    joy.active = true; joy.id = ev.pointerId;
    try { stick.setPointerCapture(ev.pointerId); } catch (_) {}
    stickTo(ev); ev.preventDefault();
  });
  stick.addEventListener('pointermove', ev => { if (joy.active && ev.pointerId === joy.id) { stickTo(ev); ev.preventDefault(); } });
  const end = ev => {
    if (ev.pointerId !== joy.id) return;
    joy.active = false; joy.x = 0; joy.y = 0; joy.dir8 = -1; joy.up = joy.down = false; joy.id = null;
    knob.style.transform = ''; stick.classList.remove('act');
  };
  stick.addEventListener('pointerup', end);
  stick.addEventListener('pointercancel', end);
}

/* 十字键：按下/抬起（支持滑指） */
const dpad = document.getElementById('dpad');
dpad.querySelectorAll('.dbtn[data-dir]').forEach(b => {
  const i = +b.dataset.dir;
  const on = ev => { ev.preventDefault(); vpadDir = i; pad.querySelectorAll('.dbtn').forEach(x => x.classList.remove('press')); b.classList.add('press'); };
  const off = ev => { ev.preventDefault(); if (vpadDir === i) vpadDir = -1; b.classList.remove('press'); };
  b.addEventListener('pointerdown', on); b.addEventListener('pointerup', off);
  b.addEventListener('pointercancel', off); b.addEventListener('pointerleave', off);
});
function setDirMode(m) {
  dirMode = m; localStorage.setItem(DIRMODE_KEY, m);
  document.getElementById('btnDirMode').textContent = '方向：' + (m === 'stick' ? '摇杆' : '十字键');
  document.getElementById('dpad').hidden = (m !== 'dpad');
  stick.style.display = (m === 'dpad') ? 'none' : '';
  vpadDir = -1;
}

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
    // 轴心对齐：pivot(0,0)=左下角，(1,1)=右上角
    const x = cx + s.x * scale - s.pivot[0] * w;
    const y = cy - s.y * scale - (1 - s.pivot[1]) * h;
    if (x + w < -80 || x > W + 80) continue;
    ctx.globalAlpha = s.color[3];
    if (s.rot) {
      ctx.save();
      ctx.translate(x + s.pivot[0] * w, y + (1 - s.pivot[1]) * h);
      ctx.rotate(s.rot);
      ctx.drawImage(s.img, s.rect[0], s.rect[1], s.rect[2], s.rect[3],
                    -s.pivot[0] * w, -(1 - s.pivot[1]) * h, s.flipX ? -w : w, s.flipY ? -h : h);
      ctx.restore();
    } else {
      ctx.drawImage(s.img, s.rect[0], s.rect[1], s.rect[2], s.rect[3], x, y, s.flipX ? -w : w, s.flipY ? -h : h);
    }
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
  fpsN++;
  if (t - fpsT > 400) {
    const DIRS = ['→', '↗', '↑', '↖', '←', '↙', '↓', '↘'];
    document.getElementById('stat').textContent =
      `${sprites.length} 精灵 · ${colliders.length} 碰撞盒 · 视野 ${cam.size.toFixed(1)} · 方向 ${joy.dir8 < 0 ? '—' : DIRS[joy.dir8]} · ${(fpsN * 1000 / (t - fpsT)).toFixed(0)} fps`;
    fpsT = t; fpsN = 0;
  }
  requestAnimationFrame(loop);
}

/* ───────────── 界面接线 ───────────── */
const sel = document.getElementById('scene');
for (const n of SCENES) { const o = document.createElement('option'); o.value = n; o.textContent = n; sel.appendChild(o); }
sel.onchange = () => loadScene(sel.value).then(() => requestAnimationFrame(loop));
document.getElementById('btnFit').onclick = () => fitView();
document.getElementById('btnZoomIn').onclick = () => zoom(1.25);
document.getElementById('btnZoomOut').onclick = () => zoom(0.8);
document.getElementById('btnHide').onclick = e => {
  hideHelpers = !hideHelpers; localStorage.setItem(HIDE_KEY, hideHelpers ? '1' : '0');
  e.target.classList.toggle('on', hideHelpers); loadScene(sel.value).then(() => requestAnimationFrame(loop));
};
document.getElementById('btnFollow').onclick = e => { followOn = !followOn; e.target.classList.toggle('on', followOn); };
document.getElementById('btnVpad').onclick = () => setVpad(!pad.classList.contains('mobile'));
document.getElementById('btnDirMode').onclick = () => setDirMode(dirMode === 'stick' ? 'dpad' : 'stick');
document.getElementById('btnHide').classList.toggle('on', hideHelpers);
document.getElementById('btnKeys').onclick = () => document.getElementById('keys').classList.toggle('show');

const inputs = document.querySelectorAll('#keys input');
function fillInputs() { inputs.forEach(i => i.value = (keymap[i.dataset.k] || '').toUpperCase()); }
inputs.forEach(i => {
  i.addEventListener('keydown', e => {
    e.preventDefault();
    const k = codeToKey(e);
    keymap[i.dataset.k] = k;
    localStorage.setItem(KEYMAP_KEY, JSON.stringify(keymap));
    fillInputs(); syncPadLabels();
  });
});
document.getElementById('btnReset').onclick = () => {
  Object.assign(keymap, DEFAULTS); localStorage.setItem(KEYMAP_KEY, JSON.stringify(keymap)); fillInputs(); syncPadLabels();
};
fillInputs();
syncPadLabels();
setDirMode(dirMode);
setVpad(localStorage.getItem(VPAD_KEY) === '1' || (matchMedia('(pointer: coarse)').matches && localStorage.getItem(VPAD_KEY) !== '0'));

loadScene(SCENES[0]).then(() => requestAnimationFrame(loop));
