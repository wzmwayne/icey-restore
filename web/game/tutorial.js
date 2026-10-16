/* ICEY · WEB SLICE —— 教程序列移植（结构对齐原作 StoryE23P1..P16 + UITutorialController）
 * 反编译依据：
 *   UITutorialController{_generalTutorialWidgit,_generalLabel,_tutorialWidgets[],_videoRenderer,_playVideo} → Show(i)/Hide(i)/Hide()/PlayVideo
 *   StoryE23 系列在 OnDestroy/OnBattle 时调用 Hide(0..7)，P9 持有 airBot/dao/pao 三个敌人 + EnemyAttribute + 位置 + 左右门，
 *   P16 持有 choice/_question/_yes/_no + 两侧 BattleZoneGate（即剧情里的「是/否」选择点）。
 * 词条：13 条（ui/tutorial/* 与 mobile/tutorial/*），与 UI 预制体 413b55c4… 中挂载的提示对象一一对应。
 */
'use strict';

(function (root) {
  const U = root.Unity, P = root.Physics2D.Physics2D;

  /* ── 教程词条（已核实；顺序按预制体内提示对象与 StoryE23 的 Hide 索引） ── */
  const KEYS = [
    'ui/tutorial/move', 'ui/tutorial/attack', 'ui/tutorial/heavyAttack', 'ui/tutorial/jump',
    'ui/tutorial/flash', 'ui/tutorial/armor', 'ui/tutorial/warning', 'mobile/tutorial/back2I',
    'ui/tutorial/breakAmmor', 'mobile/tutorial/charging', 'mobile/tutorial/excute',
    'mobile/tutorial/flashAttack', 'mobile/tutorial/ukemi',
  ];
  const TEXT = {
    'ui/tutorial/move': '左右移动',
    'ui/tutorial/attack': '轻攻击',
    'ui/tutorial/heavyAttack': '重攻击',
    'ui/tutorial/jump': '跳跃',
    'ui/tutorial/flash': '消耗 energy 进行冲刺，非战斗状态下能量恢复速度加快',
    'ui/tutorial/armor': '护盾，存在时霸体且降低所受伤害，失效后，在一定时间内不受攻击可以恢复',
    'ui/tutorial/warning': '看到 ! 注意敌人的反击',
    'ui/tutorial/breakAmmor': '连续的重攻击可以击破敌人的防御',
    'mobile/tutorial/back2I': '使用 □ ，现可回到泛用型 I™ 系统接入界面',
    'mobile/tutorial/charging': '当能量晶体出现时，长按 □ 吸收，松开释放全屏攻击',
    'mobile/tutorial/excute': '在虚弱的敌人旁使用重攻击捏碎核心，回复自身血量及护盾',
    'mobile/tutorial/flashAttack': '在受到敌人攻击瞬间使用 R1 ，对其造成大量伤害',
    'mobile/tutorial/ukemi': '无护盾状态下在空中遭受攻击产生硬直，可以按 × 受身起跳',
  };

  class UITutorialController extends U.MonoBehaviour {
    constructor() {
      super();
      this._generalTutorialWidgit = null; this._generalLabel = null;
      this._tutorialWidgets = KEYS.slice(); this._videoRenderer = null; this._playVideo = null;
      this.current = -1;
    }
    _el() {
      let el = document.getElementById('tut');
      if (!el) {
        el = document.createElement('div'); el.id = 'tut';
        el.innerHTML = '<b id="tut-k"></b><span id="tut-zh"></span><i id="tut-n"></i>';
        document.body.appendChild(el);
      }
      return el;
    }
    localize(s, keymap) {
      return String(s).replace(/□/g, (keymap.attack || 'j').toUpperCase())
                      .replace(/×/g, (keymap.jump || 'k').toUpperCase())
                      .replace(/R1/g, (keymap.heavy || 'i').toUpperCase())
                      .replace(/energy/g, '能量');
    }
    Show(index, keymap) {
      const key = KEYS[index] || null;
      this.current = index;
      const el = this._el();
      document.getElementById('tut-k').textContent = '教程 · ' + (key ? key.split('/').pop() : '?');
      document.getElementById('tut-zh').textContent = this.localize(TEXT[key] || '', keymap || {});
      document.getElementById('tut-n').textContent = (index + 1) + ' / ' + KEYS.length;
      el.classList.add('show');
    }
    Hide(index) {
      if (index === undefined) { this.current = -1; const el = document.getElementById('tut'); el && el.classList.remove('show'); return; }
      if (this.current === index) this.Hide();
    }
    PlayVideo(n) {
      const v = document.getElementById('vid');
      if (!v) return;
      v.hidden = false;
      const vp = document.getElementById('vidp');
      vp.src = 'media/tutorial/' + n + '.mp4';
      vp.play().catch(() => { });
    }
  }

  /* ── 敌人：按 P9 的三个对象 + 真实攻击参数驱动（airBot 数值取自 Enemy_AirRobot.json） ── */
  class EnemyAttribute extends U.MonoBehaviour {
    constructor() { super(); this.hp = 100; this.maxHp = 100; this.speed = 3.0; this.type = 'Enemy'; }
  }
  class SimpleEnemy extends U.MonoBehaviour {
    constructor() { super(); this.baseX = 0; this.dir = -1; this.range = 3; this.speed = 3.0; this.hp = 100; }
    Start() {
      this.baseX = this.transform.localPosition.x;
      const sr = this.GetComponent(U.SpriteRenderer);
      if (sr) sr.sortingOrder = 5;
    }
    Update() {
      const dt = U.Time.deltaTime, t = this.transform.localPosition;
      t.x += this.dir * this.speed * dt;
      if (Math.abs(t.x - this.baseX) > this.range) { this.dir *= -1; const sr = this.GetComponent(U.SpriteRenderer); if (sr) sr.flipX = this.dir > 0; }
    }
  }

  /** 生成敌人（对应 StoryE23P9 的 airBot/dao/pao） */
  async function spawnEnemies(loader, list) {
    const out = [];
    for (const e of list) {
      const go = U.createGameObject(e.name);
      go.transform.localPosition.set(e.x, e.y, 0);
      const sr = go.AddComponent(U.SpriteRenderer);
      sr.sortingOrder = 5;
      const sp = await loader.quadSprite(48, 96, e.color || '#8a6fb0');
      sr.sprite = sp;
      const ea = go.AddComponent(EnemyAttribute);
      ea.type = e.name; ea.speed = e.speed || 3.0;            // 真实数值来自 Enemy_*.json 的 xSpeed
      const en = go.AddComponent(SimpleEnemy);
      en.speed = ea.speed;
      out.push(go);
    }
    return out;
  }

  /* ── StoryE23 序列（步骤与条件按反编译结构；条件用“玩家行为”替代原作动画事件） ── */
  /* 阶段与提示索引严格对齐反编译结果（UITutorialController.Hide(idx) 的调用点）：
       P1 → 0（开门）        P2 → 1,2（轻/重攻击）
       P3 → 3（跳跃）        P4 → 4（闪避，OnBattle 时也 Hide）
       P5 → 5（护盾）        P6 → （无索引，isFirst）
       P7 → 6（危险预警）     P8 → 7（回到 I™ 界面）
       P9 → 生成 airBot/dao/pao（三个敌人对象 + EnemyAttribute + 位置 + 左右门）
       P10..P14 → 8..12（破防/能量吸收/处决/闪现攻击/受身）
       P16 → choice（_question/_yes/_no + 两侧 BattleZoneGate） */
  const STEPS = [
    { id: 'P1',  prompt: 0,  when: (p) => Math.abs(U.Input.GetAxisRaw('Horizontal')) > 0.1 },
    { id: 'P2a', prompt: 1,  when: (p) => U.Input.GetButtonDown('attack') },
    { id: 'P2b', prompt: 2,  when: (p) => U.Input.GetButtonDown('heavy') },
    { id: 'P3',  prompt: 3,  when: (p) => U.Input.GetButtonDown('jump') },
    { id: 'P4',  prompt: 4,  when: (p) => U.Input.GetButtonDown('dash') },
    { id: 'P5',  prompt: 5,  when: (p) => U.Input.GetButtonDown('execute') },
    { id: 'P7',  prompt: 6,  when: (p) => p.claymore && p.claymore.combo >= 2 },
    { id: 'P8',  prompt: 7,  when: (p) => p.stateTime > 2 },
    { id: 'P9',  prompt: 8,  enemies: true, when: (p) => p.tutElapsed > 3 },
    { id: 'P10', prompt: 9,  when: (p) => p.tutElapsed > 4 },
    { id: 'P12', prompt: 10, when: (p) => U.Input.GetButtonDown('execute') },
    { id: 'P13', prompt: 11, when: (p) => U.Input.GetButtonDown('heavy') && U.Input.GetButtonDown('dash') },
    { id: 'P14', prompt: 12, when: (p) => U.Input.GetButtonDown('jump') && U.Input.GetButtonDown('dash') },
  ];

  class StoryE23 extends U.MonoBehaviour {
    constructor() {
      super();
      this.isFirst = true; this.index = 0; this.tutElapsed = 0; this.enemiesSpawned = false;
      this.choice = -1; this.showChoice = false;
    }
    Start() {
      this.tut = this.gameObject.GetComponent(UITutorialController) || U.SceneManager.active.gameObjects
        .map(g => g.GetComponent(UITutorialController)).find(Boolean);
      this.tut.Show(0, U.Input.keymap);
      this.index = 0;
    }
    Update() {
      const dt = U.Time.deltaTime; this.tutElapsed += dt;
      if (this.showChoice) {
        if (U.Input.GetButtonDown('choiceL')) { this.choice = 0; this.showChoice = false; this._choice('否'); }
        if (U.Input.GetButtonDown('choiceR')) { this.choice = 1; this.showChoice = false; this._choice('是'); }
        return;
      }
      const step = STEPS[this.index];
      if (!step) return;
      if (!this.player && root.GamePlayer) {
        this.player = U.SceneManager.active.gameObjects
          .map(g => g.GetComponent(root.GamePlayer.PlayerActionController)).find(Boolean) || null;
      }
      const player = this.player;
      if (!player) return;
      if (step.enemies && !this.enemiesSpawned) {
        this.enemiesSpawned = true;
        const play = this.playBounds || { minX: 0, maxX: 20 };
        spawnEnemies(this.loader || root.SceneLoader, [
          { name: 'airBot', x: play.minX + 14, y: 3.5, speed: 3.0, color: '#7f6bb0' },   // Enemy_AirRobot.AirAtk1.xSpeed = 3.0
          { name: 'dao', x: play.minX + 20, y: 1.6, speed: 5.0, color: '#a06a6a' },      // AtkPram_* xSpeed 5.0
          { name: 'pao', x: play.minX + 26, y: 1.6, speed: 5.0, color: '#6a8aa0' },
        ]);
      }
      if (step.when(player)) {
        this.tut.Hide(step.prompt);
        this.index++;
        const nx = STEPS[this.index];
        if (nx) this.tut.Show(nx.prompt, U.Input.keymap);
        else this.enterChoice();
      }
      if (this.tutElapsed > 20 && !this.showChoice && this.index >= STEPS.length) this.enterChoice();
    }
    /** 对应 StoryE23P16：是/否选择点（原作由 _question/_yes/_no 的 TextMesh 呈现） */
    enterChoice() {
      if (this.showChoice) return;
      this.showChoice = true;
      const el = this.tut._el();
      document.getElementById('tut-k').textContent = '选择';
      document.getElementById('tut-zh').textContent = '（测试目的）要现在通过这段回忆吗？   Q = 否    E = 是';
      document.getElementById('tut-n').textContent = 'P16';
      el.classList.add('show');
    }
    _choice(v) {
      const el = document.getElementById('tut'); el && el.classList.remove('show');
      U.HostLog && U.HostLog(v);
      // 原作 P16 用两侧 BattleZoneGate 控制通路；此处仅记录选择结果
      this.lastChoice = v;
    }
  }

  const api = { UITutorialController, StoryE23, SimpleEnemy, EnemyAttribute, KEYS, TEXT, STEPS, spawnEnemies };
  if (typeof window !== 'undefined') window.GameTutorial = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
