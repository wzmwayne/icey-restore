/* ICEY · WEB SLICE —— 玩家移植（结构对齐原作 PlayerAttribute / PlayerActionController / Claymore）
 * 数值来源（均已反编译核实）：
 *   PlayerAttribute.SetBaseLevelData → moveSpeed = 9, maxEnergy = 10, baseAtk = 40（调试+CheatMode 时 9999）, maxChargeTime = 2.5
 *   maxHP 由强化表 DB.Enhancements["maxHP"].GetEnhanceEffect(等级) 决定，Lv0 返回 0（基值在别处）→ 暂用 150 并标注待核对
 * 结构来源：PlayerActionController{_playerAction,_playerAbilities,_stateMachine,position,openPos,_move}
 *           Claymore{pAttr,attackID,comboID,cirtComboID,airComboID,...,chargeTime,startCharge,...}
 */
'use strict';

(function (root) {
  const U = root.Unity, P = root.Physics2D.Physics2D;

  /* ── PlayerAttribute（字段名与顺序照抄原作） ── */
  class PlayerAttribute extends U.MonoBehaviour {
    constructor() {
      super();
      this.faceDir = 1;
      this.maxHP = 150;            // 待核对：强化表 Lv0 效果为 0，此处先取 Lv1(150)
      this._currentHP = 150;
      this.maxEnergy = 10;
      this._currentEnergy = 10;
      this.moveSpeed = 9;          // 已核实
      this.flashLevel = 0;
      this.currentFlashTimes = 0;
      this.FlashCd = 0.35;         // 待核对
      this.flashFlag = false;
      this.maxChargeTime = 2.5;    // 已核实（与 Claymore 常量一致）
      this.maxChargeEndureDamage = 0;
      this.baseAtk = 40;           // 已核实
    }
    SetBaseLevelData(cheat) {
      this.moveSpeed = 9;
      this.maxEnergy = 10;
      this.baseAtk = cheat ? 9999 : 40;   // 原作：Debug.isDebugBuild && Settings.CheatMode → 9999
      this.maxChargeTime = 2.5;
    }
    get currentHP() { return this._currentHP; }
    set currentHP(v) { this._currentHP = Math.max(0, Math.min(this.maxHP, v)); }
    get currentEnergy() { return this._currentEnergy; }
    set currentEnergy(v) { this._currentEnergy = Math.max(0, Math.min(this.maxEnergy, v)); }
    get Velocity_x() { return this.moveSpeed * this.faceDir; }
  }

  /* ── Claymore：攻击/连段（结构照抄，帧窗待核对） ── */
  class Claymore extends U.MonoBehaviour {
    constructor() {
      super();
      this.pAttr = null;
      this.attackID = 0; this.comboID = 0; this.cirtComboID = 0; this.cirtHoldComboID = 0;
      this.airComboID = 0; this.airCirtComboID = 0; this.airCirtHoldComboID = 0;
      this.continueAttack = false; this.continueAirAttack = false;
      this.canChangeAtkAnim = false; this.canChangeAirAtkAnim = false;
      this.chargeTime = 2.5;       // 已核实
      this.startCharge = false; this.lastFrameTime = 0; this.airRelease = 0;
      this.attackTimer = 0; this.comboWindow = 0; this.combo = 0;
    }
    /** 攻击时序（毫秒 → 秒）。原作由动画事件驱动，这里先用等效窗口，待接真实动画帧后替换 */
    StartAttack() {
      this.attackTimer = 0.30; this.comboWindow = 0.22; this.combo = Math.min(3, this.combo + 1);
      this.continueAttack = false;
    }
    Update() {
      const dt = U.Time.deltaTime;
      if (this.attackTimer > 0) this.attackTimer -= dt;
      if (this.comboWindow > 0) this.comboWindow -= dt; else this.combo = 0;
      if (this.startCharge) this.chargeTime -= dt;
      if (this.pAttr && this.startCharge && this.chargeTime <= 0) { this.startCharge = false; this.chargeTime = 2.5; }
    }
  }

  /* ── PlayerActionController：状态机 + 物理（走 Physics2D） ── */
  const State = { Idle: 0, Run: 1, Jump: 2, Fall: 3, Attack: 4, Dash: 5, Charge: 6 };

  class PlayerActionController extends U.MonoBehaviour {
    constructor() {
      super();
      this._playerAction = null; this._playerAbilities = null; this._stateMachine = null;
      this.position = new U.Vector3(); this.openPos = true; this._move = false;
      this.attr = null; this.claymore = null;
      this.w = 0.7; this.h = 1.6;
      this.vy = 0; this.grounded = false;
      this.state = State.Idle; this.stateTime = 0;
      this.jumpCount = 0; this.dashTime = 0;
      this.jumpSpeed = 13.5;       // 待核对（原作跳跃曲线在动画/角色控制器里）
      this.anim = 'idle';
    }
    get halfW() { return this.w / 2; }
    get halfH() { return this.h / 2; }

    Start() {
      this.attr = this.GetComponent(PlayerAttribute) || (() => {
        const a = this.gameObject.AddComponent(PlayerAttribute); a.SetBaseLevelData(false); return a;
      })();
      this.claymore = this.gameObject.AddComponent(Claymore);
      this.claymore.pAttr = this.attr;
    }

    SetState(s) { if (this.state !== s) { this.state = s; this.stateTime = 0; } }

    Update() {
      const dt = U.Time.deltaTime, t = this.transform.localPosition;
      this.stateTime += dt;
      const ax = U.Input.GetAxisRaw('Horizontal');
      if (ax) this.attr.faceDir = Math.sign(ax);

      // 冲刺（L）：原作 flash 消耗能量
      if (U.Input.GetButtonDown('dash') && this.attr.currentEnergy > 0 && this.dashTime <= 0) {
        this.dashTime = 0.22; this.attr.currentEnergy -= 1; this.SetState(State.Dash);
      }
      if (this.dashTime > 0) this.dashTime -= dt;

      // 攻击（J）：Claymore 处理连段
      if (U.Input.GetButtonDown('attack')) { this.claymore.StartAttack(); this.SetState(State.Attack); }

      // 跳跃（K）
      if (U.Input.GetButtonDown('jump') && this.grounded) { this.vy = this.jumpSpeed; this.grounded = false; this.SetState(State.Jump); }

      // 水平位移：普通走 moveSpeed，冲刺时 3 倍（原作 flash 位移）——待核对倍率
      const speed = this.dashTime > 0 ? this.attr.moveSpeed * 3 : this.attr.moveSpeed;
      const solids = P.solids();
      const nx = P.moveX(t.x, t.y, this.halfW, this.halfH, ax * speed * dt, solids);
      t.x = nx;

      // 垂直：重力 + 落地
      this.vy += P.gravity * dt;
      const r = P.moveY(t.x, t.y, this.halfW, this.halfH, this.vy * dt, solids);
      t.y = r.y;
      if (r.grounded) { this.vy = 0; this.grounded = true; }
      else if (this.vy < 0) this.grounded = false;

      // 状态归类（对应原作 StateMachine 的常见状态）
      if (this.dashTime > 0) this.SetState(State.Dash);
      else if (this.claymore.attackTimer > 0) this.SetState(State.Attack);
      else if (!this.grounded) this.SetState(this.vy > 0 ? State.Jump : State.Fall);
      else this.SetState(Math.abs(ax) > 0.01 ? State.Run : State.Idle);

      // 能量回复（非战斗状态更快；数值待核对）
      if (this.attr.currentEnergy < this.attr.maxEnergy) this.attr.currentEnergy += dt * 1.5;

      // 表现层：占位方块（真实精灵接入后替换）
      const sr = this.GetComponent(U.SpriteRenderer);
      if (sr) { sr.flipX = this.attr.faceDir < 0; }
      this.anim = ['idle', 'run', 'jump', 'fall', 'attack', 'dash', 'charge'][this.state] || 'idle';
    }
  }

  const api = { PlayerAttribute, Claymore, PlayerActionController, State };
  if (typeof window !== 'undefined') window.GamePlayer = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
