/* ICEY · WEB SLICE —— 教程切片入口（类 Unity 运行时）
 * 组成：SceneLoader(导出场景) + Renderer2D + 输入桥(键盘/摇杆/十字键/虚拟键) + 临时玩家控制器
 * 说明：临时玩家控制器仅用于本轮验证渲染与场景装载；下一轮替换为移植后的 PlayerActionController/PlayerAttribute。
 */
'use strict';

(function () {
  const U = window.Unity, R = window.Render2D, SL = window.SceneLoader;
  const GP = window.GamePlayer, GT = window.GameTutorial;

  /** 全局配置：默认难度 0（影响冲刺冷却等原作规则） */
  window.GameConfig = window.GameConfig || { difficulty: 0 };

  /* 探针：?probe=1 时把启动阶段回传（静态服务器访问日志即为信道，便于无头排查） */
  const PROBE = location.search.indexOf('probe') >= 0;
  const report = (m) => { if (PROBE) fetch('/__probe_' + encodeURIComponent(m).slice(0, 120) + '.gif').catch(() => { }); };
  report('scripts-loaded');

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
    // 桥只负责写入摇杆/十字键；轴向合成由引擎（Unity.Input.updateAxes）完成
    const dv = (dirMode === 'dpad' && vpadDir >= 0) ? DIRV[vpadDir] : null;
    U.Input.joystick.x = dv ? dv[0] : (joy.active ? joy.x : 0);
    U.Input.joystick.y = dv ? dv[1] : (joy.active ? joy.y : 0);
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

  /* 临时玩家控制器已由 game/player.js 的 PlayerActionController 取代 */

  /* ───────────── 启动 ───────────── */
  const SCENES = ['C1L1S1', 'C1L1S2', 'C1L1S3', 'C1L1S4', 'C1L1S5'];
  let renderer, cameraGO, playerGO, play = { minX: -10, maxX: 10, minY: -5, maxY: 5 };

  async function bootScene(name) {
    for (const go of U.SceneManager.active.gameObjects.slice()) go._destroyed = true;
    U.SceneManager.update();
    const hideEl = document.getElementById('btnHide');
    const r = await SL.load('scenes/' + name, { hideHelpers: !hideEl || !hideEl.classList.contains('on') });
    cameraGO = r.camera; play = r.play;
    playerGO = U.createGameObject('Player');                       // 与预制体里的对象同名
    const sp0 = SL.findSpawn(r.data, 0.7, 1.6);                    // 自动找不卡墙的出生点
    playerGO.transform.localPosition.set(sp0.x, sp0.y, 0);
    window.__spawn = sp0;
    const sr = playerGO.AddComponent(U.SpriteRenderer);            // 占位外观（真实精灵待接入）
    SL.quadSprite(70, 160, '#e8e8f2').then(sp => { sr.sprite = sp; sr.sortingOrder = 10; });
    playerGO.AddComponent(GP.PlayerActionController);              // 移植后的控制器（真实 moveSpeed=9）
    cameraGO.AddComponent(CameraFollow).target = playerGO;

    // 教程序列：UITutorialController + StoryE23（结构对齐原作，词条为已核实的 13 条）
    const tutGO = U.createGameObject('Tutorial');
    tutGO.AddComponent(GT.UITutorialController);
    const storyGO = U.createGameObject('StoryE23');
    const story = storyGO.AddComponent(GT.StoryE23);
    story.loader = SL; story.playBounds = play;
    document.getElementById('stat').textContent =
      `${name} · 精灵 ${r.data.sprites.length - r.helperHidden} · 碰撞盒 ${r.data.colliders.length} · 贴图 ${r.textures}` +
      ` · 出生 (${sp0.x.toFixed(1)}, ${sp0.y.toFixed(1)})`;
  }

  class CameraFollow extends U.MonoBehaviour {
    Update() {
      if (!this.target) return;
      const t = this.target.transform.localPosition, c = this.transform.localPosition;
      const k = Math.min(1, U.Time.deltaTime * 6);
      this.transform.localPosition.set(c.x + (t.x - c.x) * k, c.y + (t.y + 1 - c.y) * k, c.z);
    }
  }

  /* 屏幕错误浮层：任何异常都直接显示在页面上（便于远程排查） */
  function showError(msg) { report('ERROR ' + msg.slice(0, 110));
    let el = document.getElementById('err');
    if (!el) {
      el = document.createElement('div'); el.id = 'err';
      el.style.cssText = 'position:fixed;left:8px;right:8px;top:8px;z-index:30;background:rgba(60,10,14,.92);' +
        'border:1px solid #a04040;border-radius:8px;padding:8px 12px;color:#ffd9d9;font:12px/1.5 monospace;white-space:pre-wrap;max-height:40vh;overflow:auto';
      document.body.appendChild(el);
    }
    el.textContent = String(el.textContent || '') + msg + '\n';
    el.style.display = 'block';
  }
  addEventListener('error', e => showError('✗ ' + (e.message || 'error') + '  @' + (e.filename || '').split('/').pop() + ':' + e.lineno));
  addEventListener('unhandledrejection', e => showError('✗ Promise: ' + (e.reason && (e.reason.stack || e.reason.message) || e.reason)));

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
    const dsel = document.getElementById('difficulty');
    if (dsel) {
      dsel.value = String(window.GameConfig.difficulty);
      dsel.onchange = () => {
        window.GameConfig.difficulty = parseInt(dsel.value, 10) || 0;
        const p = playerGO && playerGO.GetComponent(GP.PlayerActionController);
        if (p) p.difficulty = window.GameConfig.difficulty;
      };
    }

    document.getElementById('btnDev').onclick = () => {
      const bar = document.querySelector('.bar'), hint = document.getElementById('hint');
      const show = bar.hasAttribute('hidden');
      show ? (bar.removeAttribute('hidden'), hint.removeAttribute('hidden')) : (bar.setAttribute('hidden',''), hint.setAttribute('hidden',''));
      document.getElementById('btnDev').style.opacity = show ? '1' : '.55';
    };
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

    // 首屏操作提示（5 秒后淡出，不常驻）
    (() => {
      const t = document.createElement('div');
      t.style.cssText = 'position:fixed;left:50%;top:14px;transform:translateX(-50%);z-index:9;' +
        'background:rgba(12,12,18,.8);border:1px solid #33506a;border-radius:8px;padding:6px 12px;color:#cfe3f2;' +
        'transition:opacity .6s;pointer-events:none';
      t.textContent = 'A/D 移动 · J 轻攻 · I 重攻 · K 跳跃 · L 闪避 · O 裁决 · ⚙ 打开调试面板';
      document.body.appendChild(t);
      setTimeout(() => { t.style.opacity = '0'; setTimeout(() => t.remove && t.remove(), 800); }, 5000);
    })();

    try { await bootScene(SCENES[0]); } catch (e) { showError('✗ 场景装载失败: ' + (e && e.stack || e)); }

    // 自动诊断：3 秒后检查是否真的有内容被画出来（贴图失败/场景为空都要报出来）
    setTimeout(() => {
      const st = renderer.stats, U2 = window.Unity;
      const objs = U2.SceneManager.active.gameObjects.length;
      const sprites = U2.SceneManager.active.gameObjects.filter(g => g.GetComponent(U2.SpriteRenderer) && g.GetComponent(U2.SpriteRenderer).sprite).length;
      const diag = document.getElementById('diag');
      report(`boot objs=${objs} sprites=${sprites} drawn=${st.drawn} fps=${(st.fps || 0).toFixed(0)}`);
      const bad = (objs < 5 || sprites === 0 || st.drawn === 0);
      diag.hidden = !bad;
      diag.textContent = `构建 ${window.__BUILD} · 对象 ${objs} · 有图精灵 ${sprites} · 本帧绘制 ${st.drawn}`
        + `\n贴图缓存 ${window.SceneLoader ? 'ok' : '缺失'}`
        + (bad ? '\n→ 若长期如此：请把本行发我（或看顶部红色错误）' : '');
    }, 3000);

    // 每帧包一层 try/catch：单帧异常不再杀死整个循环
    let __n = 0;
    U.PlayerLoop.start(() => {
      try { updateAxis(); renderer.draw(cameraGO); if (PROBE && (++__n % 120 === 0)) report(`alive frames=${U.Time.frameCount} drawn=${renderer.stats.drawn}`); }
      catch (e) { if (!window.__drawErr) { window.__drawErr = 1; showError('✗ 渲染异常: ' + (e && e.stack || e)); } }
    });
  });
})();
