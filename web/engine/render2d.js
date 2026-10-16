/* ICEY · WEB SLICE —— 渲染层（Canvas2D 精灵渲染，接口语义对齐 Unity 的 SpriteRenderer/Camera）
 * 与 core.js 配套：不直接操作场景 JSON，只消费已实例化的组件（SpriteRenderer / Camera / Transform）。
 */
'use strict';

(function (root) {
  const U = root.Unity || (typeof require !== 'undefined' ? require('./core.js') : null);

  class Renderer2D {
    constructor(canvas) {
      this.canvas = canvas;
      this.ctx = canvas.getContext('2d', { alpha: false });
      this.dpr = Math.min(root.devicePixelRatio || 1, 2);
      this.showGizmos = false;      // 显示碰撞盒（调试）
      this.background = '#0b0b0e';
      this.stats = { drawn: 0, culled: 0, fps: 0 };
      this._fpsT = 0; this._fpsN = 0;
      this.resize();
    }

    resize() {
      const w = root.innerWidth || 960, h = root.innerHeight || 540;
      this.canvas.width = Math.floor(w * this.dpr);
      this.canvas.height = Math.floor(h * this.dpr);
      this.vw = this.canvas.width; this.vh = this.canvas.height;
    }

    /** 等价 Unity 的一帧渲染：按 sortingLayerID → sortingOrder → z 排序后绘制 */
    draw(cameraGO) {
      const ctx = this.ctx, W = this.vw, H = this.vh;
      const cam = cameraGO ? cameraGO.GetComponent(U.Camera) : null;
      const size = cam ? cam.orthographicSize : 5;
      const scale = H / (size * 2);
      const cm = cameraGO ? cameraGO.transform.matrix : { e: 0, f: 0 };
      const camX = cm.e, camY = cm.f;

      ctx.fillStyle = this.background;
      ctx.fillRect(0, 0, W, H);

      // 收集可见精灵
      const list = [];
      for (const go of U.SceneManager.active.gameObjects) {
        if (!go.activeInHierarchy) continue;
        const sr = go.GetComponent(U.SpriteRenderer);
        if (!sr || !sr.enabled || !sr.sprite || !sr.sprite.image || !sr.sprite.image.complete) continue;
        const img = sr.sprite.image;
        if (!img.naturalWidth) continue;
        list.push({ go, sr });
      }
      list.sort((a, b) => (a.sr.sortingLayerID - b.sr.sortingLayerID) ||
                          (a.sr.sortingOrder - b.sr.sortingOrder) ||
                          (a.go.transform.localPosition.z - b.go.transform.localPosition.z));

      let drawn = 0, culled = 0;
      for (const { go, sr } of list) {
        const m = go.transform.matrix;
        const sp = sr.sprite;
        const w = sp.width * Math.hypot(m.a, m.b) * scale;
        const h = sp.height * Math.hypot(m.c, m.d) * scale;
        if (w <= 0 || h <= 0) continue;
        const rot = Math.atan2(m.b, m.a);
        const px = W / 2 + (m.e - camX) * scale - sp.pivot[0] * w;
        const py = H / 2 - (m.f - camY) * scale - (1 - sp.pivot[1]) * h;
        if (px + w < -64 || px > W + 64) { culled++; continue; }
        const c = sr.color;
        ctx.globalAlpha = c.a;
        if (rot) {
          ctx.save();
          ctx.translate(px + sp.pivot[0] * w, py + (1 - sp.pivot[1]) * h);
          ctx.rotate(rot);
          if (c.r < 1 || c.g < 1 || c.b < 1) { ctx.filter = `brightness(${((c.r + c.g + c.b) / 3).toFixed(3)})`; }
          ctx.drawImage(sr.sprite.image, sp.rect[0], sp.rect[1], sp.rect[2], sp.rect[3],
            -sp.pivot[0] * w, -(1 - sp.pivot[1]) * h, sr.flipX ? -w : w, sr.flipY ? -h : h);
          ctx.restore();
        } else {
          if (c.r < 1 || c.g < 1 || c.b < 1) { ctx.filter = `brightness(${((c.r + c.g + c.b) / 3).toFixed(3)})`; }
          ctx.drawImage(sr.sprite.image, sp.rect[0], sp.rect[1], sp.rect[2], sp.rect[3],
            px, py, sr.flipX ? -w : w, sr.flipY ? -h : h);
        }
        ctx.filter = 'none';
        drawn++;
      }
      ctx.globalAlpha = 1;

      if (this.showGizmos) this._gizmos(scale, camX, camY, W, H);

      this.stats.drawn = drawn; this.stats.culled = culled;
      const now = (root.performance || Date).now();
      this._fpsN++;
      if (now - this._fpsT > 500) {
        this.stats.fps = this._fpsN * 1000 / (now - this._fpsT);
        this._fpsT = now; this._fpsN = 0;
      }
    }

    _gizmos(scale, camX, camY, W, H) {
      const ctx = this.ctx;
      ctx.lineWidth = 1;
      for (const go of U.SceneManager.active.gameObjects) {
        if (!go.activeInHierarchy) continue;
        const bc = go.GetComponent(U.BoxCollider2D);
        if (!bc) continue;
        const b = bc.bounds;
        const px = W / 2 + (b.center.x - camX) * scale, py = H / 2 - (b.center.y - camY) * scale;
        ctx.strokeStyle = 'rgba(124,196,232,.75)';
        ctx.strokeRect(px - b.size.x * scale / 2, py - b.size.y * scale / 2, b.size.x * scale, b.size.y * scale);
      }
    }

    worldToScreen(x, y, cameraGO) {
      const cam = cameraGO ? cameraGO.GetComponent(U.Camera) : null;
      const size = cam ? cam.orthographicSize : 5;
      const scale = this.vh / (size * 2);
      const cm = cameraGO ? cameraGO.transform.matrix : { e: 0, f: 0 };
      return { x: this.vw / 2 + (x - cm.e) * scale, y: this.vh / 2 - (y - cm.f) * scale, scale };
    }
  }

  const api = { Renderer2D };
  if (typeof window !== 'undefined') window.Render2D = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
