/* ICEY · WEB SLICE —— 2D 物理层（AABB，语义对齐 Unity 的 Collider2D/Rigidbody2D 最小子集）
 * 提供：实体碰撞盒收集、地面/墙体求解、单向平台、重叠查询。角色控制器与敌人共用。
 */
'use strict';

(function (root) {
  const U = root.Unity || (typeof require !== 'undefined' ? require('./core.js') : null);

  const Physics2D = {
    gravity: -30,

    /** 收集当前场景中的实体 BoxCollider2D（跳过 trigger / 未启用 / 离群巨体） */
    solids() {
      const out = [];
      for (const go of U.SceneManager.active.gameObjects) {
        if (!go.activeInHierarchy) continue;
        const bc = go.GetComponent(U.BoxCollider2D);
        if (!bc || bc.isTrigger || bc.enabled === false) continue;
        const b = bc.bounds;
        if (Math.abs(b.size.x) > 60 || Math.abs(b.size.y) > 60) continue;   // 相机边界等离群体
        out.push({ go, bc, b });
      }
      return out;
    },

    /** 脚下地面高度（取脚下所有实体顶面最高者） */
    groundY(x, footY, solids) {
      let top = -1e9;
      for (const s of (solids || this.solids())) {
        const b = s.b;
        if (x < b.center.x - b.size.x / 2 - 0.02 || x > b.center.x + b.size.x / 2 + 0.02) continue;
        const t = b.center.y + b.size.y / 2;
        if (t <= footY + 0.6) top = Math.max(top, t);     // 只认脚下（含小台阶容差）
      }
      return top;
    },

    /** 水平移动求解：返回未被阻挡的 x（简单 AABB 推挤 + 可踏上的矮台阶） */
    moveX(x, y, hw, hh, dx, solids) {
      let nx = x + dx;
      for (const s of (solids || this.solids())) {
        const b = s.b;
        const cx = b.center.x, cy = b.center.y, sx = b.size.x / 2, sy = b.size.y / 2;
        if (y + hh <= cy - sy || y - hh >= cy + sy) continue;             // 垂直不重叠
        if (cy + sy - (y - hh) < 0.35) continue;                          // 可踏上的台阶
        // 用“扫掠区间”判定，避免大位移穿透（Unity 侧由连续碰撞检测覆盖）
        const lo = Math.min(x, nx), hi = Math.max(x, nx);
        if (hi + hw > cx - sx && lo - hw < cx + sx) nx = dx > 0 ? cx - sx - hw : cx + sx + hw;
      }
      return nx;
    },

    /** 垂直移动求解：返回 {y, grounded, landedOn} */
    moveY(x, y, hw, hh, dy, solids, oneWayFrom) {
      let ny = y + dy, grounded = false;
      for (const s of (solids || this.solids())) {
        const b = s.b;
        const cx = b.center.x, cy = b.center.y, sx = b.size.x / 2, sy = b.size.y / 2;
        if (x + hw <= cx - sx || x - hw >= cx + sx) continue;             // 水平不重叠
        const top = cy + sy, bottom = cy - sy;
        if (dy <= 0 && y - hh >= top - 0.02 && ny - hh <= top) { ny = top + hh; grounded = true; }
        else if (dy > 0 && y + hh <= bottom + 0.02 && ny + hh >= bottom) { ny = bottom - hh; }
      }
      return { y: ny, grounded };
    },

    /** 区域重叠查询（攻击判定用） */
    overlap(rect, tag) {
      const out = [];
      for (const s of this.solids()) {
        const b = s.b;
        if (Math.abs(b.center.x - rect.x) > (b.size.x + rect.w) / 2) continue;
        if (Math.abs(b.center.y - rect.y) > (b.size.y + rect.h) / 2) continue;
        if (tag && s.go.tag !== tag) continue;
        out.push(s);
      }
      return out;
    },
  };

  const api = { Physics2D };
  if (typeof window !== 'undefined') window.Physics2D = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
