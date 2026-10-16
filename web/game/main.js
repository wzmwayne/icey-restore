/* ICEY · WEB SLICE —— 教程切片入口（类 Unity 运行时）
 * 组成：SceneLoader(导出场景) + Renderer2D + 输入桥(键盘/摇杆/十字键/虚拟键) + 临时玩家控制器
 * 说明：临时玩家控制器仅用于本轮验证渲染与场景装载；下一轮替换为移植后的 PlayerActionController/PlayerAttribute。
 */
'use strict';

(function () {
  const U = window.Unity, R = window.Render2D, SL = window.SceneLoader;

  /* ───────────── 输入桥：DOM → Unity.Input ───────────── */
  const KEYMAP_KEY = 'icey_web_keymap', DIRMODE_KEY = 'icey_web_dirmode', VPAD_KEY = 'icey_web_vpad';
  const DEFAULTS = {
    left: 'a', right: 'd', up: 'w', down: 's',
    attack: 'j', heavy: 'i', execute: 'o', jump: 'k', dash: 'l',
    spiral: 'n', flash: 'm', special: 'u', choiceL: 'q', choiceR: 'e',
  };
  const CN = { attack: '轻攻', heavy: '重攻', execute: '裁决', jump: '跳跃', dash: '闪避',
               spiral: '大螺旋', flash: '闪现', special: '特殊', choiceL: '选择', choiceR: '选择' };
  const ACTIONS = ['attack', 'heavy', 'execute', 'jump', 'dash', 'spiral', 'flash', 'special'];
  const keymap = Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem(KEYMAP_KEY) || '{}'));
  Object.assign(U.Input.keymap, keymap);

  const down = U.Input._down;
  let vpadDir = -1, dirMode = localStorage.getItem(DIRMODE_KEY) || 'stick';
  const joy = { x: 0, y: 0, active: false, id: null };
  const DIRV = [[1, 0], [0.7071, 0.7071], [0, 1], [-0.7071, 0.7071], [-1, 0], [-0.7071, -0.7071], [0, -1], [0.7071, -0.7071]];

  const keyOf = e => { let k = e.key; if (k === ' ') k = 'space'; if (k === 'Shift') k = 'shift'; return (k || '').toLowerCase(); };
  addEventListener('keydown', e => { const k = keyOf(e); for (const a in keymap) if (keymap[a] === k) { down[a] = true; e.preventDefault(); } }, { passive: false });
  addEventListener('keyup', e => { const k = keyOf(e); for (const a in keymap) if (keymap[a] === k) { down[a] = false; e.preventDefault(); } }, { passive: false });

  function updateAxis() {
    const kx = (down.left ? -1 : 0) + (down.right ? 1 : 0);
    const ky = (down.up ? 1 : 0) + (down.down ? -1 : 0);
    const dv = (dirMode === 'dpad' && vpadDir >= 0) ? DIRV[vpadDir] : null;
    const vx = dv ? dv[0] : (joy.active ? joy.x : 0), vy = dv ? dv[1] : (joy.active ? joy.y : 0);
    U.Input.axis.x = Math.max(-1, Math.min(1, kx + vx));
    U.Input.axis.y = Math.max(-1, Math.min(1, ky + vy));
    const ax = U.Input.axis.x, ay = U.Input.axis.y;
    if (Math.hypot(ax, ay) < 0.3) U.Input.dir8 = -1;
    else U.Input.dir8 = Math.round((((Math.atan2(ay, ax) * 180 / Math.PI) + 360) % 360) / 45) % 8;
  }

  const stick = document.getElementById('stick'), knob = document.getElementById('knob'), pad = document.getElementById('pad');
  if (stick) {
    const R0 = 44, DEAD = 0.16;
    const move = ev => {
      const r = stick.getBoundingClientRect();
      const dx = ev.clientX - (r.left + r.width / 2), dy = ev.clientY - (r.top + r.height / 2);
      const len = Math.hypot(dx, dy) || 1, cl = Math.min(len, R0);
      const nx = dx / len * cl, ny = dy / len * cl;
      knob.style.transform = `translate(${nx.toFixed(1)}px,${ny.toFixed(1)}px)`;
      knob.style.setProperty('--rot', (Math.atan2(-ny, nx) * 180 / Math.PI + 90).toFixed(0) + 'deg');
      const f = (v) => Math.abs(v) < DEAD ? 0 : Math.max(-1, Math.min(1, (v - Math.sign(v) * DEAD) / (1 - DEAD)));
      joy.x = f(nx / R0); joy.y = f(-ny / R0);
    };
    stick.addEventListener('pointerdown', ev => { joy.active = true; joy.id = ev.pointerId; try { stick.setPointerCapture(ev.pointerId); } catch (_) { } stick.classList.add('act'); move(ev); ev.preventDefault(); });
    stick.addEventListener('pointermove', ev => { if (joy.active && ev.pointerId === joy.id) { move(ev); ev.preventDefault(); } });
    const end = ev => { if (ev.pointerId !== joy.id) return; joy.active = false; joy.x = joy.y = 0; joy.id = null; knob.style.transform = ''; stick.classList.remove('act'); };
    stick.addEventListener('pointerup', end); stick.addEventListener('pointercancel', end);
  }

  const dpad = document.getElementById('dpad');
  dpad && dpad.querySelectorAll('.dbtn[data-dir]').forEach(b => {
    const i = +b.dataset.dir;
    const on = ev => { ev.preventDefault(); vpadDir = i; b.classList.add('press'); };
    const off = ev => { ev.preventDefault(); if (vpadDir === i) vpadDir = -1; b.classList.remove('press'); };
    b.addEventListener('pointerdown', on); b.addEventListener('pointerup', off);
    b.addEventListener('pointercancel', off); b.addEventListener('pointerleave', off);
  });

  pad && pad.querySelectorAll('.btn[data-act]').forEach(b => {
    const a = b.dataset.act;
    const on = ev => { ev.preventDefault(); down[a] = true; b.classList.add('press'); };
    const off = ev => { ev.preventDefault(); down[a] = false; b.classList.remove('press'); };
    b.addEventListener('pointerdown', on); b.addEventListener('pointerup', off);
    b.addEventListener('pointercancel', off); b.addEventListener('pointerleave', off);
  });

  function syncPadLabels() {
    pad && pad.querySelectorAll('.btn[data-act]').forEach(b => {
      const a = b.dataset.act;
      b.innerHTML = `${(keymap[a] || '?').toUpperCase()}<small>${CN[a] || ''}</small>`;
    });
  }
  function setVpad(on) { pad && pad.classList.toggle('mobile', on); localStorage.setItem(VPAD_KEY, on ? '1' : '0'); document.getElementById('btnVpad').classList.toggle('on', on); }
  function setDirMode(m) {
    dirMode = m; localStorage.setItem(DIRMODE_KEY, m);
    document.getElementById('btnDirMode').textContent = '方向：' + (m === 'stick' ? '摇杆' : '十字键');
    if (dpad) dpad.hidden = m !== 'dpad';
    if (stick) stick.style.display = m === 'dpad' ? 'none' : '';
  }

  /* ───────────── 临时玩家控制器（下一轮由移植的 PlayerActionController 取代） ───────────── */
  class PlaceholderPlayer extends U.MonoBehaviour {
    constructor() {
      super();
      this.speed = 5.2; this.jumpV = 13.5; this.w = 0.7; this.h = 1.6;
      this.vy = 0; this.onGround = false; this.face = 1;
    }
    Start() {
      this.sr = this.GetComponent(U.SpriteRenderer);
      this.StartCoroutine(SL.quadSprite(70, 160, '#e8e8f2').then(sp => { if (this.sr) this.sr.sprite = sp; }));
    }
    groundAt(x) {
      let top = this.worldMinY;
      for (const go of U.SceneManager.active.gameObjects) {
        const bc = go.GetComponent(U.BoxCollider2D);
        if (!bc) continue;
        const b = bc.bounds;
        if (x < b.center.x - b.size.x / 2 - 0.1 || x > b.center.x + b.size.x / 2 + 0.1) continue;
        top = Math.max(top, b.center.y + b.size.y / 2);
      }
      return top;
    }
    blocked(nx) {
      const y = this.transform.localPosition.y, hw = this.w / 2, hh = this.h / 2;
      for (const go of U.SceneManager.active.gameObjects) {
        const bc = go.GetComponent(U.BoxCollider2D);
        if (!bc) continue;
        const b = bc.bounds;
        if (y + hh < b.center.y - b.size.y / 2 || y - hh > b.center.y + b.size.y / 2) continue;
        if (b.center.y + b.size.y / 2 - (y - hh) < 0.35) continue;
        if (nx + hw > b.center.x - b.size.x / 2 && nx - hw < b.center.x + b.size.x / 2) return true;
      }
      return false;
    }
    Update() {
      const dt = U.Time.deltaTime, ax = U.Input.GetAxisRaw('Horizontal');
      if (ax) this.face = Math.sign(ax);
      const nx = this.transform.localPosition.x + ax * this.speed * dt;
      if (!this.blocked(nx)) this.transform.localPosition.x = nx;
      if (this.transform.localPosition.x > this.worldMaxX - 1) this.transform.localPosition.x = this.worldMaxX - 1;
      if (U.Input.GetButtonDown('jump') && this.onGround) { this.vy = this.jumpV; this.onGround = false; }
      this.vy -= 30 * dt;
      let y = this.transform.localPosition.y + this.vy * dt;
      const g = this.groundAt(this.transform.localPosition.x);
      if (y <= g + this.h / 2) { y = g + this.h / 2; this.vy = 0; this.onGround = true; } else this.onGround = false;
      this.transform.localPosition.y = y;
      if (this.sr) this.sr.flipX = this.face < 0;
    }
  }

  /* ───────────── 启动 ───────────── */
  const SCENES = ['C1L1S1', 'C1L1S2', 'C1L1S3', 'C1L1S4', 'C1L1S5'];
  let renderer, cameraGO, playerGO, play = { minX: -10, maxX: 10, minY: -5, maxY: 5 };

  async function bootScene(name) {
    for (const go of U.SceneManager.active.gameObjects.slice()) go._destroyed = true;
    U.SceneManager.update();
    const r = await SL.load('scenes/' + name, { hideHelpers: document.getElementById('btnHide').classList.contains('on') === false });
    cameraGO = r.camera; play = r.play;
    playerGO = U.createGameObject('Player(Placeholder)');
    playerGO.transform.localPosition.set(play.minX + 3, play.minY + 6, 0);
    const p = playerGO.AddComponent(PlaceholderPlayer);
    p.worldMinY = play.minY - 4; p.worldMaxX = play.maxX;
    cameraGO.AddComponent(CameraFollow).target = playerGO;
    document.getElementById('stat').textContent =
      `${name} · 精灵 ${r.data.sprites.length - r.helperHidden} · 碰撞盒 ${r.data.colliders.length} · 贴图 ${r.textures}`;
  }

  class CameraFollow extends U.MonoBehaviour {
    Update() {
      if (!this.target) return;
      const t = this.target.transform.localPosition, c = this.transform.localPosition;
      const k = Math.min(1, U.Time.deltaTime * 6);
      this.transform.localPosition.set(c.x + (t.x - c.x) * k, c.y + (t.y + 1 - c.y) * k, c.z);
    }
  }

  window.addEventListener('load', async () => {
    renderer = new R.Renderer2D(document.getElementById('cv'));
    addEventListener('resize', () => renderer.resize());
    U.SceneManager.init();
    syncPadLabels(); setDirMode(dirMode); setVpad(localStorage.getItem(VPAD_KEY) === '1' || (matchMedia('(pointer: coarse)').matches && localStorage.getItem(VPAD_KEY) !== '0'));

    const sel = document.getElementById('scene');
    SCENES.forEach(n => { const o = document.createElement('option'); o.value = n; o.textContent = n; sel.appendChild(o); });
    sel.onchange = () => bootScene(sel.value);

    document.getElementById('btnGizmo').onclick = e => { renderer.showGizmos = !renderer.showGizmos; e.target.classList.toggle('on', renderer.showGizmos); };
    document.getElementById('btnVpad').onclick = () => setVpad(!pad.classList.contains('mobile'));
    document.getElementById('btnDirMode').onclick = () => setDirMode(dirMode === 'stick' ? 'dpad' : 'stick');
    document.getElementById('btnFit').onclick = () => { if (cameraGO) cameraGO.GetComponent(U.Camera).orthographicSize = Math.max(3, Math.min(12, (play.maxY - play.minY) / 2 + 1)); };
    document.getElementById('btnKeys').onclick = () => document.getElementById('keys').classList.toggle('show');
    document.querySelectorAll('#keys input').forEach(i => {
      i.value = (keymap[i.dataset.k] || '').toUpperCase();
      i.addEventListener('keydown', e => {
        e.preventDefault();
        keymap[i.dataset.k] = keyOf(e); Object.assign(U.Input.keymap, keymap);
        localStorage.setItem(KEYMAP_KEY, JSON.stringify(keymap));
        document.querySelectorAll('#keys input').forEach(x => x.value = (keymap[x.dataset.k] || '').toUpperCase());
        syncPadLabels();
      });
    });
    document.getElementById('btnReset').onclick = () => {
      Object.assign(keymap, DEFAULTS); Object.assign(U.Input.keymap, keymap);
      localStorage.setItem(KEYMAP_KEY, JSON.stringify(keymap));
      document.querySelectorAll('#keys input').forEach(x => x.value = (keymap[x.dataset.k] || '').toUpperCase());
      syncPadLabels();
    };

    await bootScene(SCENES[0]);
    U.PlayerLoop.start(() => { updateAxis(); renderer.draw(cameraGO); });
  });
})();
