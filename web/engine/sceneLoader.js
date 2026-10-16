/* ICEY · WEB SLICE —— 场景装载：把 UnityPy 导出的 scene.json 实例化为引擎对象
 * 映射关系：transform 记录 → GameObject/Transform；sprite 记录 → SpriteRenderer(+Sprite)；
 *          collider 记录 → BoxCollider2D；另建一台正交相机（场景内无相机，与 Unity 侧一致：相机来自常驻预制体）。
 */
'use strict';

(function (root) {
  const U = root.Unity || (typeof require !== 'undefined' ? require('./core.js') : null);

  const HELPER_RE = /^(BlackMask|Black|Mask.*|CLOSE|CLOSER|CloseIcon\w*|CrossIcon|Arrow\s*\(\d+\))$/;
  const BG_RE = /^BG\d/i;
  /* 远景/中景层命名约定：xxx__NNNN_<layer>_...，layer ∈ bg|mfar|mfront|sky|cloud */
  const FAR_RE = /__(?:\d+)_(bg|mfar|mfront|sky|cloud|far)/i;

  const texCache = new Map();

  function loadImage(url) {
    if (texCache.has(url)) return texCache.get(url);
    const p = new Promise(res => {
      const img = new Image();
      img.onload = () => res(img);
      img.onerror = () => res(null);
      img.src = url;
    });
    texCache.set(url, p);
    return p;
  }

  function quadSprite(w, h, color) {
    const key = 'dbg:' + w + 'x' + h + ':' + color;
    if (!texCache.has(key)) {
      texCache.set(key, new Promise(res => {
        const cv = document.createElement('canvas');
        cv.width = Math.max(1, Math.round(w)); cv.height = Math.max(1, Math.round(h));
        const c = cv.getContext('2d'); c.fillStyle = color; c.fillRect(0, 0, cv.width, cv.height);
        const img = new Image(); img.onload = () => res(img); img.src = cv.toDataURL();
      }));
    }
    return texCache.get(key).then(img => img ? new U.Sprite(img, [0, 0, img.naturalWidth, img.naturalHeight], 100, [0.5, 0.5], 'dbg') : null);
  }

  /** opts: { hideHelpers=true, dirs: 'scenes/C1L1S1' } */
  async function load(dir, opts = {}) {
    const hideHelpers = opts.hideHelpers !== false;
    const base = dir.endsWith('/') ? dir : dir + '/';
    const data = await (await fetch(base + 'scene.json')).json();

    const scene = U.SceneManager.active;
    const byTransformId = new Map();
    const byGoId = new Map();
    const nameOfGo = new Map();
    for (const s of data.sprites) nameOfGo.set(s.go, s.name || '');

    // 第一遍：建立 GameObject 与 Transform
    for (const t of data.transforms) {
      const nm = nameOfGo.get(t.go) || ('GO_' + t.go);
      const go = new U.GameObject(nm);
      go.transform.localPosition.set(t.pos[0], t.pos[1], t.pos[2]);
      go.transform.localScale.set(t.scale[0], t.scale[1], t.scale[2]);
      go.transform.localRotation = 2 * Math.atan2(t.rot[2] || 0, t.rot[3] || 1);
      scene.add(go);
      byTransformId.set(t.id, go);
      byGoId.set(t.go, go);
    }
    // 第二遍：连父链
    for (const t of data.transforms) {
      if (!t.father) continue;
      const child = byTransformId.get(t.id), parent = byTransformId.get(t.father);
      if (child && parent) child.transform.SetParent(parent.transform);
    }
    // 精灵
    let helperHidden = 0;
    const images = new Map();
    for (const s of data.sprites) {
      const go = byGoId.get(s.go);
      if (!go) continue;
      if (hideHelpers && s.name && HELPER_RE.test(s.name)) { helperHidden++; go.SetActive(false); continue; }
      if (!s.tex) continue;
      const url = base + s.tex.replace(/^\.\.\//, '');
      if (!images.has(url)) images.set(url, loadImage(url));
      const sr = go.AddComponent(U.SpriteRenderer);
      sr.sortingOrder = s.order; sr.sortingLayerID = s.layer;
      sr.flipX = !!s.flipX; sr.flipY = !!s.flipY;
      if (s.color) sr.color = { r: s.color[0], g: s.color[1], b: s.color[2], a: s.color[3] };
      sr._pending = images.get(url).then(img => {
        if (!img) return;
        sr.sprite = new U.Sprite(img, s.rect, s.ppu, s.pivot, s.name);
      });
    }
    // 碰撞盒
    for (const c of data.colliders) {
      const go = byGoId.get(c.go);
      if (!go) continue;
      const bc = go.AddComponent(U.BoxCollider2D);
      bc.size.set(c.size[0], c.size[1]);
      bc.offset.set(c.offset[0], c.offset[1]);
      bc.isTrigger = !!c.isTrigger;
    }
    await Promise.all(Array.from(images.values()));

    // 相机（场景内没有）：默认框可玩区域
    const camGO = new U.GameObject('Main Camera');
    scene.add(camGO);
    const cam = camGO.AddComponent(U.Camera);
    cam.orthographic = true;
    const play = playBounds(data, hideHelpers);
    const vh = (play.maxY - play.minY) / 2 + 2;
    cam.orthographicSize = Math.max(4, Math.min(6.5, vh));      // 平台游戏常用视野区间
    camGO.transform.localPosition.set(play.minX + 6, play.minY + cam.orthographicSize * 0.55, 0);
    U.SceneManager.mainCamera = cam;

    return { data, camera: camGO, play, helperHidden, textures: images.size };
  }

  /** 可玩区域：优先用碰撞盒（真正的可玩几何），无碰撞盒时退回精灵包围盒（排除远景层） */
  function playBounds(data, hideHelpers) {
    const trByGo = new Map(data.transforms.map(t => [t.go, t]));
    let minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9, any = false;
    for (const c of data.colliders) {
      if (c.isTrigger || c.enabled === false) continue;            // 触发器/未启用不参与可玩区域
      if (Math.abs(c.size[0]) > 60 || Math.abs(c.size[1]) > 60) continue;   // 离群巨体（相机边界等）
      const tr = trByGo.get(c.go);
      if (!tr) continue;
      const sx = Math.abs(tr.scale[0] || 1), sy = Math.abs(tr.scale[1] || 1);
      const cx = tr.pos[0] + c.offset[0] * sx, cy = tr.pos[1] + c.offset[1] * sy;
      const w = c.size[0] * sx, h = c.size[1] * sy;
      minX = Math.min(minX, cx - w / 2); maxX = Math.max(maxX, cx + w / 2);
      minY = Math.min(minY, cy - h / 2); maxY = Math.max(maxY, cy + h / 2);
      any = true;
    }
    if (any) return { minX, maxX, minY, maxY, from: 'colliders' };
    for (const s of data.sprites) {
      if (hideHelpers && s.name && HELPER_RE.test(s.name)) continue;
      if (BG_RE.test(s.name || '') || FAR_RE.test(s.name || '')) continue;
      if (!s.rect || !s.ppu) continue;
      // 用精灵的变换世界坐标（导出时已含父链）近似其包围盒
      const tr = data.transforms.find(t => t.go === s.go);
      if (!tr) continue;
      const w = s.rect[2] / s.ppu, h = s.rect[3] / s.ppu;
      const x = tr.pos[0], y = tr.pos[1];      // 局部即世界（这些层无父级缩放）
      minX = Math.min(minX, x - w / 2); maxX = Math.max(maxX, x + w / 2);
      minY = Math.min(minY, y - h / 2); maxY = Math.max(maxY, y + h / 2);
      any = true;
    }
    return any ? { minX, maxX, minY, maxY, from: 'sprites' } : { minX: -10, maxX: 10, minY: -5, maxY: 5, from: 'default' };
  }

  const api = { load, quadSprite, playBounds, HELPER_RE, BG_RE, FAR_RE };
  if (typeof window !== 'undefined') window.SceneLoader = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
