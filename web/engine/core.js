/* ICEY · WEB SLICE —— 类 Unity 运行时核心（Unity-like API，便于原作 C# 逻辑近乎 1:1 移植）
 * 设计原则：命名与语义对齐 UnityEngine（GameObject/Transform/Component/MonoBehaviour/Coroutine/Time/Input/Mathf/Vector2/…），
 *          渲染与物理为最小实现，但接口形状与 Unity 一致，移植原作脚本时只改类型名不改结构。
 */
'use strict';

/* ───────── Mathf / Vector2 / Vector3 ───────── */
const Mathf = {
  PI: Math.PI, Deg2Rad: Math.PI / 180, Rad2Deg: 180 / Math.PI,
  Abs: Math.abs, Sign: Math.sign, Min: Math.min, Max: Math.max, Floor: Math.floor, Ceil: Math.ceil,
  Clamp: (v, a, b) => v < a ? a : (v > b ? b : v),
  Clamp01: v => v < 0 ? 0 : (v > 1 ? 1 : v),
  Lerp: (a, b, t) => a + (b - a) * Mathf.Clamp01(t),
  MoveTowards: (c, t, d) => Math.abs(t - c) <= d ? t : c + Math.sign(t - c) * d,
  Approximately: (a, b) => Math.abs(b - a) < 1e-5,
};

class Vector2 {
  constructor(x = 0, y = 0) { this.x = x; this.y = y; }
  set(x, y) { this.x = x; this.y = y; return this; }
  clone() { return new Vector2(this.x, this.y); }
  add(v) { return new Vector2(this.x + v.x, this.y + v.y); }
  sub(v) { return new Vector2(this.x - v.x, this.y - v.y); }
  mul(s) { return new Vector2(this.x * s, this.y * s); }
  get magnitude() { return Math.hypot(this.x, this.y); }
  get sqrMagnitude() { return this.x * this.x + this.y * this.y; }
  get normalized() { const m = this.magnitude || 1; return new Vector2(this.x / m, this.y / m); }
  static get zero() { return new Vector2(0, 0); }
  static get one() { return new Vector2(1, 1); }
  static get right() { return new Vector2(1, 0); }
  static get up() { return new Vector2(0, 1); }
  static Distance(a, b) { return Math.hypot(a.x - b.x, a.y - b.y); }
  static Lerp(a, b, t) { return new Vector2(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t)); }
}

class Vector3 extends Vector2 {
  constructor(x = 0, y = 0, z = 0) { super(x, y); this.z = z; }
  clone() { return new Vector3(this.x, this.y, this.z); }
  static get zero() { return new Vector3(0, 0, 0); }
  static get one() { return new Vector3(1, 1, 1); }
}

/* ───────── Time ───────── */
const Time = {
  deltaTime: 0, unscaledDeltaTime: 0, time: 0, unscaledTime: 0,
  timeScale: 1, frameCount: 0,
};

/* ───────── Object / Component / Behaviour ───────── */
let __nextId = 1;

class Object {
  constructor(name = '') { this.name = name; this.id = __nextId++; this._destroyed = false; }
  static Instantiate(src, position) {
    const go = new GameObject(src.name + '(Clone)');
    if (src.components) {
      for (const c of src.components) {
        const nc = c.clone();
        if (nc) { nc.gameObject = go; go.components.push(nc); }
      }
    }
    if (position) go.transform.position.set(position.x, position.y);
    SceneManager.active.add(go);
    for (const c of go.components) if (c.Awake) c.Awake();
    return go;
  }
  static Destroy(o) { o && (o._destroyed = true); }
  static DontDestroyOnLoad() { /* 单场景运行时，无需实现 */ }
  static FindObjectsOfType(t) { return SceneManager.active.all(t); }
  static FindObjectOfType(t) { return SceneManager.active.all(t)[0] || null; }
}

class Component {
  constructor() { this.gameObject = null; this.enabled = true; }
  get transform() { return this.gameObject.transform; }
  get name() { return this.gameObject.name; }
  clone() { return Object.assign(Object.create(Object.getPrototypeOf(this)), this, { gameObject: null }); }
}

/** 协程：把生成器包装成逐帧推进的任务（等价 Unity StartCoroutine + yield return null/WaitForSeconds） */
class Coroutine { constructor(gen) { this.gen = gen; this.waitUntil = 0; this.done = false; } }

class YieldInstruction { }
class WaitForSeconds extends YieldInstruction { constructor(s) { super(); this.seconds = s; } }
class WaitForFixedUpdate extends YieldInstruction { }
class WaitForEndOfFrame extends YieldInstruction { }

class MonoBehaviour extends Component {
  constructor() { super(); this._coroutines = []; }
  StartCoroutine(gen) {
    const co = new Coroutine(typeof gen === 'function' ? gen() : gen);
    this._coroutines.push(co);
    tickCoroutine(co);
    return co;
  }
  StopCoroutine(co) { const i = this._coroutines.indexOf(co); if (i >= 0) this._coroutines.splice(i, 1); }
  StopAllCoroutines() { this._coroutines.length = 0; }
  Invoke(fn, t) { const at = Time.time + t; const self = this; const id = setInterval(() => { if (Time.time >= at) { clearInterval(id); fn.call(self); } }, 16); }
  get rigidbody2D() { return this.GetComponent(Rigidbody2D); }
  GetComponent(t) { return this.gameObject.GetComponent(t); }
  GetComponentsInChildren(t) { return this.gameObject.GetComponentsInChildren(t); }
}

class Transform extends Component {
  constructor() {
    super();
    this.position = new Vector3();
    this.localPosition = new Vector3();
    this.localScale = new Vector3(1, 1, 1);
    this.localRotation = 0;          // 2D：绕 Z 弧度
    this.parent = null;
    this.children = [];
  }
  get childCount() { return this.children.length; }
  GetChild(i) { return this.children[i]; }
  SetParent(p) {
    if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1);
    this.parent = p;
    if (p) p.children.push(this);
    return this;
  }
  Translate(x, y) { this.localPosition.x += x; this.localPosition.y += y; return this; }
  /** 世界矩阵（2D 仿射，与导出数据一致） */
  get matrix() {
    const ang = this.localRotation, co = Math.cos(ang), si = Math.sin(ang);
    const sx = this.localScale.x, sy = this.localScale.y;
    const a = co * sx, b = si * sx, c = -si * sy, d = co * sy;
    const p = this.parent ? this.parent.matrix : { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 };
    const m = { a: p.a * a + p.c * b, b: p.b * a + p.d * b, c: p.a * c + p.c * d, d: p.b * c + p.d * d,
                e: p.a * this.localPosition.x + p.c * this.localPosition.y + p.e,
                f: p.b * this.localPosition.x + p.d * this.localPosition.y + p.f };
    this.position.set(m.e, m.f, this.localPosition.z);
    return m;
  }
}

class GameObject extends Object {
  constructor(name = '') {
    super(name);
    this.components = [];
    this.activeSelf = true;
    this.tag = 'Untagged';
    this.layer = 0;
    this._transform = new Transform(); this._transform.gameObject = this;
    this.components.push(this._transform);
  }
  get transform() { return this._transform; }
  get activeInHierarchy() { return this.activeSelf; }
  SetActive(v) { this.activeSelf = !!v; }
  AddComponent(T) {
    const c = new T(); c.gameObject = this; this.components.push(c);
    if (SceneManager.started.has(this) && c.Awake) c.Awake();
    if (SceneManager.started.has(this) && c.Start) c.Start();
    return c;
  }
  GetComponent(T) {
    if (typeof T === 'string') return this.components.find(c => c.constructor.name === T) || null;
    return this.components.find(c => c instanceof T) || null;
  }
  GetComponentsInChildren(T) {
    const out = [];
    const walk = go => { const c = go.GetComponent(T); if (c) out.push(c); go.transform.children.forEach(ch => walk(ch.gameObject)); };
    walk(this); return out;
  }
  static Find(name) { return SceneManager.active.findByName(name); }
  static FindGameObjectsWithTag(tag) { return SceneManager.active.all(null).filter(g => g.tag === tag); }
  static FindGameObjectWithTag(tag) { return GameObject.FindGameObjectsWithTag(tag)[0] || null; }
}

/* ───────── 精灵 / 碰撞 / 刚体 / 相机 / 音频 ───────── */
class Sprite {
  constructor(img, rect, ppu, pivot, name) {
    this.image = img; this.rect = rect; this.pixelsPerUnit = ppu || 100;
    this.pivot = pivot || [0.5, 0.5]; this.name = name || '';
    this.bounds = { size: new Vector2(rect[2] / this.pixelsPerUnit, rect[3] / this.pixelsPerUnit) };
    this.texture = img;
  }
  get width() { return this.rect[2] / this.pixelsPerUnit; }
  get height() { return this.rect[3] / this.pixelsPerUnit; }
}

class SpriteRenderer extends Component {
  constructor() {
    super();
    this.sprite = null; this.color = { r: 1, g: 1, b: 1, a: 1 };
    this.sortingOrder = 0; this.sortingLayerID = 0;
    this.flipX = false; this.flipY = false; this.enabled = true;
  }
}

class Collider2D extends Component {
  constructor() { super(); this.offset = new Vector2(); this.isTrigger = false; }
}
class BoxCollider2D extends Collider2D {
  constructor() { super(); this.size = new Vector2(1, 1); }
  get bounds() {
    const m = this.transform.matrix;
    const sx = Math.hypot(m.a, m.b), sy = Math.hypot(m.c, m.d);
    const w = Math.abs(this.size.x * sx), h = Math.abs(this.size.y * sy);
    return { center: new Vector2(m.e + this.offset.x * sx, m.f + this.offset.y * sy), size: new Vector2(w, h) };
  }
}
class PolygonCollider2D extends Collider2D { constructor() { super(); this.points = []; } }

class Rigidbody2D extends Component {
  constructor() {
    super();
    this.velocity = new Vector2(); this.gravityScale = 1; this.bodyType = Rigidbody2D.Dynamic;
    this.freezeRotation = true; this.mass = 1;
  }
  static get Dynamic() { return 0; }
  static get Kinematic() { return 1; }
  static get Static() { return 2; }
  MovePosition(p) { this.transform.localPosition.set(p.x, p.y, this.transform.localPosition.z); }
}

class Camera extends Component {
  constructor() {
    super();
    this.orthographic = true; this.orthographicSize = 5; this.backgroundColor = '#0b0b0e';
    this.depth = -1; this.main = null;
  }
  static get main() { return SceneManager.mainCamera; }
  ScreenToWorldPoint(x, y, vw, vh) {
    const s = vh / (this.orthographicSize * 2);
    const t = this.transform.matrix;
    return new Vector3(t.e + (x - vw / 2) / s, t.f + (vh / 2 - y) / s, 0);
  }
}

class AudioSource extends Component {
  constructor() { super(); this.clip = null; this.volume = 1; this.loop = false; this._el = null; }
  Play() { if (this.clip) { if (!this._el) { this._el = new Audio(this.clip); this._el.volume = this.volume; } this._el.currentTime = 0; this._el.play().catch(() => { }); } }
  Stop() { this._el && this._el.pause(); }
}

/* ───────── 输入（Unity 风格：GetAxisRaw / GetButtonDown）───────── */
const Input = {
  _down: {}, _prev: {},
  keymap: { left: 'a', right: 'd', up: 'w', down: 's', attack: 'j', heavy: 'i', execute: 'o',
            jump: 'k', dash: 'l', spiral: 'n', flash: 'm', special: 'u', choiceL: 'q', choiceR: 'e' },
  axis: { x: 0, y: 0 }, dir8: -1,
  GetAxisRaw(name) { return name === 'Horizontal' ? this.axis.x : (name === 'Vertical' ? this.axis.y : 0); },
  GetButton(b) { return !!this._down[b]; },
  GetButtonDown(b) { return !!this._down[b] && !this._prev[b]; },
  GetButtonUp(b) { return !this._down[b] && !!this._prev[b]; },
  _snapshot() { for (const k in this._down) this._prev[k] = this._down[k]; },
};

/* ───────── 场景管理器（等价 Unity 的场景 + 组件生命周期）───────── */
const SceneManager = {
  active: null, mainCamera: null, started: new WeakSet(), sprites: [],
  init() {
    this.active = new Scene(this);
    this.active.init();                 // RuntimeInitializeOnLoadMethod 等价
  },
  update() {
    const list = this.active.gameObjects;
    for (const go of list) {
      if (go._destroyed || !go.activeInHierarchy) continue;
      for (const c of go.components) {
        if (!this.started.has(go)) { c.Awake && c.Awake(); }
      }
      if (!this.started.has(go)) { for (const c of go.components) c.Start && c.Start(); this.started.add(go); }
      else for (const c of go.components) c.Update && c.enabled !== false && c.Update();
    }
    for (const go of list) for (const c of go.components) c.LateUpdate && c.enabled !== false && c.LateUpdate();
    // 协程推进
    for (const go of list) for (const c of go.components) {
      if (!(c instanceof MonoBehaviour)) continue;
      for (let i = c._coroutines.length - 1; i >= 0; i--) if (tickCoroutine(c._coroutines[i])) c._coroutines.splice(i, 1);
    }
    // 清理
    for (let i = list.length - 1; i >= 0; i--) if (list[i]._destroyed) list.splice(i, 1);
  }
};

function tickCoroutine(co) {
  if (co.done) return true;
  if (Time.unscaledTime < co.waitUntil) return false;
  const r = co.gen.next();
  if (r.done) { co.done = true; return true; }
  const y = r.value;
  if (y instanceof WaitForSeconds) co.waitUntil = Time.unscaledTime + y.seconds;
  else if (typeof y === 'number') co.waitUntil = Time.unscaledTime + y;
  else co.waitUntil = Time.unscaledTime;
  return false;
}

class Scene {
  constructor(mgr) { this.mgr = mgr; this.gameObjects = []; }
  init() { }
  add(go) { this.gameObjects.push(go); return go; }
  all(T) { return this.gameObjects.filter(g => T ? g.GetComponent(T) : true); }
  findByName(n) { return this.gameObjects.find(g => g.name === n) || null; }
}

/* ───────── 主循环（等价 Unity 的 player loop）───────── */
const PlayerLoop = {
  running: false, _last: 0, maxDelta: 0.05,
  start(onFrame) {
    this.running = true; this._last = performance.now();
    const step = t => {
      if (!this.running) return;
      const dt = Math.min(this.maxDelta, (t - this._last) / 1000); this._last = t;
      Time.deltaTime = dt * Time.timeScale; Time.unscaledDeltaTime = dt;
      Time.time += Time.deltaTime; Time.unscaledTime += dt; Time.frameCount++;
      SceneManager.update();
      Input._snapshot();
      onFrame && onFrame(dt);
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }
};

/* ───────── 导出（浏览器全局；移植时可 as namespace 使用）───────── */
const __export = {
  Mathf, Vector2, Vector3, Time, Object, Component, MonoBehaviour, GameObject, Transform,
  Sprite, SpriteRenderer, Collider2D, BoxCollider2D, PolygonCollider2D, Rigidbody2D,
  Camera, AudioSource, Input, SceneManager, Scene, PlayerLoop,
  WaitForSeconds, WaitForFixedUpdate, WaitForEndOfFrame, Coroutine,
  /** 便捷构造（非 Unity API，供移植层用） */
  createGameObject(name) { const go = new GameObject(name); SceneManager.active.add(go); return go; },
};
if (typeof window !== 'undefined') window.Unity = __export;
if (typeof module !== 'undefined' && module.exports) module.exports = __export;
