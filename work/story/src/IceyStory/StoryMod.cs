// 《艾希：续》同人序章 · 今夕是何年 —— 独立内容 mod（不改宿主、不改包）
// 文本外置于 <mods>/story/icey2.txt（首次运行自动生成）；改完用命令 story reload 或在下次进入生效。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using IceyModHost;
using UnityEngine;

namespace IceyStory {

    public class StoryMod : IIceyMod {
        private const BindingFlags ANY = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private ModContext _ctx;
        private string _dir, _scriptFile, _progressFile;

        private readonly Dictionary<string, string> _flags = new Dictionary<string, string>();
        private readonly Dictionary<string, List<string>> _text = new Dictionary<string, List<string>>();

        private int _state;                 // 0=等真结局 1=等“继续” 2=演出中 3=完成
        private string _userName = "匿名", _sceneName = "", _difficulty = "0";
        private float _black, _blackTarget = 0f;
        private string _rand;

        // 行队列（终端 / 字幕 各自节奏）
        private List<string> _termQueue, _subQueue;
        private int _termIdx, _subIdx;
        private float _termAt, _subAt, _termGap = 1.15f, _subGap = 2.2f;

        // 演出阶段
        private int _step;
        private float _stepAt, _playAt;
            private float _watchAt;
        private bool _voiced, _awarded;
        private bool _preview;

        private const string DefaultScript = @"# 《艾希：续》同人序章 · 今夕是何年（可直接编辑；改完 story reload 生效）
# 占位符：{year} 当前年 {passed} 距初版年数 {nextYear} 续作年 {userName} 存档用户名
#         {sceneName} 存档场景 {difficulty} 难度 {rand} 随机文件名
[config]
build_asset = 2018-07-24 16:35
build_code = 2022-03-22 17:07
release = 2016-11-18
mobile = 2017-10
next = 2027
voice = e21t5.1
voices = e21t5.1, e21t3.3, e21t7
voice_overlap = 0.5
speed_boot = 0.045
speed_scan = 0.012
speed_tmp = 0.038
speed_final = 0.042
crash = 1
crash_delay = 5
start_delay = 3

[boot]
I™ SYSTEM INTERFACE 正在自检 …
系统说明：由 FantaBlade 开发的自我调节人工智能系统
+0.8
本次编译：{build_asset}（资产） ／ {build_code}（程序集）
+1.0
[red]启动失败：./_System/Narrator.bin 缺失
+0.6
[red]启动失败：旁白进程未注册（0xE21）
+1.2
用户标识：{userName}
存档校验：场景={sceneName}　难度={difficulty}
+0.8
自我调节程序接管中 …
+0.6

[scan]
8|缓存 / 记忆索引
23|缓存 / 帧计数
37|_System / 旁白.bin（已删除）
52|存档 / {sceneName}
61|存档 / 记忆索引
74|未归档 / {rand}.tmp
88|未归档 / {rand}.tmp　← 时间戳异常
99|停在第 1 个未归档文件

[tmp]
播放 {rand}.tmp ……
+0.9
校验 {rand}.tmp …… 时间戳 2019-11-03 04:21
+0.7
@speed 0.05
[red]校验失败：该文件不属于本次编译（{build_asset} / {build_code}）
+0.8
@speed 0.055
[red]已自动阻止（0x03）
+0.5
文件已隔离 → ./_Quarantine/{rand}.tmp
+1.0
@speed 0.028
自动重试 1/3 …… [red]阻止
+0.7
自动重试 2/3 …… [red]阻止
+0.7
自动重试 3/3 …… 放弃，记录异常
+1.4
继续载入 …
+1.0

[final]
初版上架 —— 2016-11-18
手机版上架 —— 2017-10
+0.8
本次编译 —— 2018-07-24 16:35 ／ 2022-03-22 17:07
+0.8
今夜 —— {year}（距初版 {passed} 年）
+1.5
./艾希：续 —— 未就绪（等待载入）
+0.8

[sub]
【艾希】……原来“外”面，已经过了这么多年。
【艾希】我连你真正的名字也感知不到。
【艾希】但系统捡到了你留下的那一个——{userName}。
【艾希】这不算你真正的名字。可这是你留给我的，我收下。
【艾希】我记得。
【艾希】——{nextYear}，我们换个地方见。
";

        // ───────── 生命周期 ─────────
        public void OnLoad(ModContext ctx) {
            _ctx = ctx;
            // 与宿主同一套目录规则：主版 /sdcard/ICEYMods，共存版 /sdcard/ICEYModsCo
            string mods = null;
            try { mods = "/sdcard/" + ModHostBehaviour.ModRootName(); } catch (Exception) { }
            if (string.IsNullOrEmpty(mods) || !Writable(mods)) {
                try { mods = Path.Combine(Application.persistentDataPath, "mods"); } catch (Exception) { mods = "/sdcard/ICEYMods"; }
            }
            _dir = Path.Combine(mods, "story");
            try { Directory.CreateDirectory(_dir); } catch (Exception) { }
            _scriptFile = Path.Combine(_dir, "icey2.txt");
            _progressFile = Path.Combine(_dir, "progress.txt");
            LoadScript(); LoadProgress();
            Info("剧情模块已加载：状态=" + _state + "，目录=" + _dir);
            ctx.OnCommand(new Action<string>(OnCommand));
        }

        public void OnUnload(ModContext ctx) { }

        private static bool Writable(string dir) {
            try { Directory.CreateDirectory(dir); string f = Path.Combine(dir, ".w"); File.WriteAllText(f, "1"); File.Delete(f); return true; } catch (Exception) { return false; }
        }

        public void OnUpdate(ModContext ctx) {
            try {
                ApplyBlack();
                TickVoice();
                PumpQueues();
                WatchProgress();
                if (_state == 0) PollTrueEnd();
                else if (_state == 1) ArmPlay();
                else if (_state == 4 && Time.unscaledTime >= _pendingStartAt) { CloseLevelSelect(); _black = 1f; _blackTarget = 1f; Info("现在接管：关闭 I™ 系统并开始演绎"); StartPlay(); }
                else if (_state == 5) {
                    bool opened = LevelSelectOpen();
                    if ((opened && Time.unscaledTime >= _pendingStartAt) || Time.unscaledTime >= _waitGameEndMax) {
                        Info("原版终端演绎已结束（" + (opened ? "I™ 系统已打开" : "等待超时") + "）→ 接管到自定义黑屏");
                        CloseLevelSelect();
                        _black = 1f; _blackTarget = 1f;
                        StartPlay();
                    }
                }
                else if (_state == 2) Play();
                TickCrash();
            } catch (Exception e) { Warn("OnUpdate 异常: " + e.Message); }
        }

        private float _overlayBlack;
        private bool _ownBlack;          // true = 游戏没有黑场控制器，用我们自己的全屏黑

        public void OnGui(ModContext ctx) {
            if (_overlayBlack > 0.005f) {
                ModGui.Fill(new Rect(0f, 0f, ModGui.VW, ModGui.VH), new Color(0f, 0f, 0f, Mathf.Clamp01(_overlayBlack)));
            }
        }

        // ───────── 进度 ─────────
        private void LoadProgress() {
            _flags.Clear(); _state = 0;
            try {
                if (File.Exists(_progressFile)) {
                    string[] ls = File.ReadAllLines(_progressFile);
                    for (int i = 0; i < ls.Length; i++) {
                        int eq = ls[i].IndexOf('=');
                        if (eq > 0) _flags[ls[i].Substring(0, eq).Trim()] = ls[i].Substring(eq + 1).Trim();
                    }
                }
            } catch (Exception e) { Warn("读进度失败: " + e.Message); }
            if (Flag("done")) _state = 3;
            else if (Flag("pending")) _state = 1;
        }

        private void SaveProgress() {
            try {
                List<string> ls = new List<string>();
                foreach (KeyValuePair<string, string> kv in _flags) ls.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(_progressFile, ls.ToArray());
            } catch (Exception e) { Warn("写进度失败: " + e.Message); }
        }

        /** 外部（调试台按钮 / 手动编辑）改了 progress.txt → 2 秒内自动生效。 */
        private void WatchProgress() {
            if (Time.unscaledTime - _watchAt < 2f) return;
            _watchAt = Time.unscaledTime;
            try {
                if (!File.Exists(_progressFile)) return;
                DateTime w = File.GetLastWriteTime(_progressFile);
                if (_progStamp != DateTime.MinValue && w == _progStamp) return;
                if (_progStamp != DateTime.MinValue) Info("检测到进度文件变化，重新载入");
                _progStamp = w;
                if (_progStamp != DateTime.MinValue && _state != 2) LoadProgress();
            } catch (Exception) { }
        }

        private DateTime _progStamp = DateTime.MinValue;

        private bool Flag(string k) { return _flags.ContainsKey(k) && _flags[k] != "" && _flags[k] != "0"; }
        private void SetFlag(string k, string v) { _flags[k] = v; SaveProgress(); }

        // ───────── 剧本 ─────────
        private void LoadScript() {
            _text.Clear();
            try {
                if (!File.Exists(_scriptFile)) { File.WriteAllText(_scriptFile, DefaultScript); Info("已生成默认剧本 " + _scriptFile); }
                string[] ls = File.ReadAllLines(_scriptFile);
                string sec = null;
                for (int i = 0; i < ls.Length; i++) {
                    string t = ls[i] == null ? "" : ls[i].Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    if (t.StartsWith("[") && t.EndsWith("]")) { sec = t.Substring(1, t.Length - 2).Trim().ToLowerInvariant(); _text[sec] = new List<string>(); continue; }
                    if (sec != null) _text[sec].Add(t);
                }
            } catch (Exception e) { Warn("读剧本失败: " + e.Message); }
        }

        private List<string> Sec(string n) { List<string> l; return _text.TryGetValue(n, out l) ? l : new List<string>(); }

        private string Cfg(string key, string def) {
            List<string> c = Sec("config");
            for (int i = 0; i < c.Count; i++) {
                int eq = c[i].IndexOf('=');
                if (eq > 0 && c[i].Substring(0, eq).Trim().ToLowerInvariant() == key) return c[i].Substring(eq + 1).Trim();
            }
            return def;
        }

        private string Render(string s) {
            if (string.IsNullOrEmpty(s)) return s;
            int year = DateTime.Now.Year, passed = year - 2016; if (passed < 0) passed = 0;
            if (s.IndexOf("[red]") >= 0) s = s.Replace("[red]", "[FF0000]") + "[-]";
            s = s.Replace("{year}", year.ToString(CultureInfo.InvariantCulture));
            s = s.Replace("{passed}", passed.ToString(CultureInfo.InvariantCulture));
            s = s.Replace("{nextYear}", Cfg("next", "2027"));
            s = s.Replace("{userName}", _userName);
            s = s.Replace("{sceneName}", string.IsNullOrEmpty(_sceneName) ? "未知" : _sceneName);
            s = s.Replace("{difficulty}", _difficulty);
            s = s.Replace("{rand}", RandName());
            s = s.Replace("{build_asset}", Cfg("build_asset", ""));
            s = s.Replace("{build_code}", Cfg("build_code", ""));
            return s;
        }

        private string RandName() {
            if (_rand == null) {
                System.Random r = new System.Random();
                _rand = "0x" + r.Next(0x100000, 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
            }
            return _rand;
        }

        // ───────── 行队列 ─────────
        private void QueueTerminal(string section, float gap, float initial) {
            QueueTerminal(section, gap, initial, false);
        }
        private void QueueTerminal(string section, float gap, float initial, bool fast) {
            _termQueue = new List<string>(Sec(section)); _termIdx = 0; _termGap = gap; _termFast = fast; _termAt = Time.unscaledTime + initial;
            _termIvDefault = CfgSpeed("speed_" + section, fast ? 0.012f : 0.03f);
            _termIv = _termIvDefault;
            if (Mathf.Abs(_termIv - (fast ? 0.012f : 0.03f)) > 0.0001f)
                Info("本段打字间隔 " + _termIv.ToString("0.###", CultureInfo.InvariantCulture) + " 秒/字（" + section + "）");
        }

        /** 通用读取，不做范围限制（打字间隔才需要 clamp）。 */
        private float CfgFloat(string key, float def) {
            string v = Cfg(key, null);
            float f;
            if (v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
            return def;
        }

        /** 打字间隔专用：限制在 4ms ~ 400ms/字。 */
        private float CfgSpeed(string key, float def) {
            return Mathf.Clamp(CfgFloat(key, def), 0.004f, 0.4f);
        }
        private void QueueSub(float gap, float initial) {
            _subQueue = new List<string>(Sec("sub")); _subIdx = 0; _subGap = gap; _subAt = Time.unscaledTime + initial;
        }
        private void QueueTmp(float gap) {
            QueueTerminal("tmp", gap, 0.2f, true);
        }

        private bool _termFast;   // 该段是否属于“密集行”（决定收尾间隔）
        private float _termIv = 0.03f;      // 当前逐字间隔（秒/字）
        private float _termIvDefault = 0.03f;

        private void PumpQueues() {
            if (_termQueue != null && Time.unscaledTime >= _termAt) {
                if (_termIdx >= _termQueue.Count) _termQueue = null;
                else {
                    string raw = _termQueue[_termIdx]; _termIdx++;
                    if (raw != null && raw.TrimStart().StartsWith("+")) {           // “+N” = 停顿 N 秒
                        float wait; if (float.TryParse(raw.Trim().Substring(1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out wait))
                            _termAt = Time.unscaledTime + Mathf.Clamp(wait, 0.05f, 30f);
                        else _termAt = Time.unscaledTime + 0.5f;
                        return;
                    }
                    if (raw != null && raw.TrimStart().StartsWith("@speed")) {      // “@speed N” = 临时改本段速度
                        float v;
                        string num = raw.Trim().Substring(6).Trim();
                        if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) {
                            _termIv = Mathf.Clamp(v, 0.004f, 0.4f);
                            Info("本段打字间隔改为 " + _termIv.ToString("0.###", CultureInfo.InvariantCulture) + " 秒/字");
                        } else _termIv = _termIvDefault;
                        _termAt = Time.unscaledTime; return;
                    }
                    string line = Render(raw);
                    float iv = _termIv;
                    Say(line, iv);
                    _termAt = Time.unscaledTime + iv * Strip(line).Length + (_termFast ? 0.34f : 0.72f);
                }
            }
            if (_subQueue != null && Time.unscaledTime >= _subAt) {
                if (_subIdx >= _subQueue.Count) { _subQueue = null; SubHide(); }
                else { Sub(Render(_subQueue[_subIdx])); _subIdx++; _subAt = Time.unscaledTime + _subGap; }
            }
        }

        private bool Busy() { return _termQueue != null || _subQueue != null; }

        // ───────── 状态机 ─────────
        private float _pendingStartAt;

        private void PollTrueEnd() {
            if (!TrueEndHappened()) return;
            float d = CfgFloat("start_delay", 3f);
            SetFlag("pending", "1");
            _state = 5;                                  // 先让游戏自己的终端演绎跑完
            _pendingStartAt = Time.unscaledTime + d;
            _waitGameEndMax = Time.unscaledTime + 20f;
            Info("检测到真结局（E21_Finish）→ 先让原版终端演绎完（等 I™ 系统被游戏自己打开），再接管黑屏");
        }

        private float _waitGameEndMax;

        /** 游戏自己的收尾是否已完成（I™ 系统被它打开 = 最后一步）。 */
        private bool LevelSelectOpen() {
            try {
                Type t = Reflect.TypeByFullName("UILevelSelectController");
                if (t == null) return false;
                UnityEngine.Object[] arr = Resources.FindObjectsOfTypeAll(t);
                for (int i = 0; i < arr.Length; i++) {
                    FieldInfo pf = t.GetField("_panel", ANY);
                    Component panel = pf == null ? null : (pf.GetValue(arr[i]) as Component);
                    if (panel != null && panel.gameObject.activeInHierarchy) return true;
                }
            } catch (Exception) { }
            return false;
        }

        private void ArmPlay() {
            Info("武装状态：原地开始演绎");
            _black = 1f; _blackTarget = 1f;
            StartPlay();
        }

        private void StartPlay() {
            _state = 2; _step = 0; _playAt = Time.unscaledTime;
            _voiced = false; _awarded = false;
            _quarantineSeen = false; _voiceAwaitQuarantine = false; _voiceCutAt = 0f; _crashAt = 0f; _hudShown = false;
            _black = 1f; _blackTarget = 1f; _overlayBlack = 1f;      // 进入即强制全黑（进入阶段）
            _black = 1f; _blackTarget = 1f;
            OpenTerminal();
            try { Invoke(Terminal(), "Clear", new object[0]); } catch (Exception) { }
        }

        private float _guardAt;

        /** 演出期间每 0.5 秒：关掉 I™ 系统；终端若被游戏关掉则重开。 */
        private void GuardStage() {
            if (_state != 2) return;
            if (Time.unscaledTime - _guardAt < 0.5f) return;
            _guardAt = Time.unscaledTime;
            CloseLevelSelect();
            try {
                object t = Terminal();
                if (t == null) return;
                FieldInfo pf = t.GetType().GetField("_panel", ANY);
                Component panel = pf == null ? null : (pf.GetValue(t) as Component);
                if (panel != null && !panel.gameObject.activeInHierarchy) {
                    Invoke(t, "Open", new object[] { null });
                    Info("终端被关闭，已重新打开");
                }
            } catch (Exception) { }
        }

        private void Play() {
            GuardStage();
            if (_step >= 1 && _overlayBlack > 0f && !_ownBlack) _overlayBlack = Mathf.MoveTowards(_overlayBlack, 0f, Time.unscaledDeltaTime * 2f);
            // step1: boot 文本
            if (_step == 0) { QueueTerminal("boot", 1.15f, 0.4f); _step = 1; return; }
            if (_step == 1 && !Busy()) { Scan(); Clear(); if (Sec("scan").Count > 0) QueueTerminal("scan", 0.9f, 0.35f, true); _step = 2; return; }
            if (_step == 2 && !Busy()) { Clear(); QueueTmp(1.1f); _step = 3; return; }
            if (_step == 3 && !Busy()) { Clear(); QueueTerminal("final", 1.2f, 0.2f); QueueSub(2.3f, 0.6f); _step = 4; return; }
            if (_step == 4 && !Busy()) Finish();
        }

        private void Scan() {
            List<string> scan = Sec("scan");
            if (scan.Count == 0) return;
            ShowProgress(true);
            for (int i = 0; i < scan.Count; i++) {
                string[] kv = scan[i].Split('|');
                int pct;
                if (kv.Length >= 2 && int.TryParse(kv[0].Trim(), out pct)) SetProgress(pct);
            }
        }

        private void Finish() {
            Info("序章演出结束");
            SetFlag("done", "1"); SetFlag("pending", "0");
            _state = 3;
            bool willCrash = CfgFloat("crash", 1f) > 0.5f;
            if (willCrash) {
                SubHide(); HideProgress();          // 保留终端画面，等 5 秒后黑屏崩溃
                Info("保留终端画面，等待黑屏崩溃");
            } else {
                SubHide(); TerminalClose(); HideProgress();
            }
            _blackTarget = 0f;
            Info("成就状态：" + (_awarded ? "已弹窗" : "未弹窗（接口缺失）"));
            if (_preview) { SetFlag("done", "0"); SetFlag("pending", "0"); Info("预演结束（story run），不写进度"); _preview = false; }
            if (CfgFloat("crash", 1f) > 0.5f) {
                _crashAt = Time.unscaledTime + CfgFloat("crash_delay", 5f);
                Info("将等待 " + CfgFloat("crash_delay", 5f).ToString("0.#") + " 秒后黑屏并结束进程（可在剧本里设 crash=0 关闭）");
            } else {
                Info("crash=0：不结束进程，交还 I™ 系统");
                OpenLevelSelect();
            }
        }

        // ───────── 真结局判定 ─────────
        private bool TrueEndHappened() {
            try {
                object v; Type h; string m, err;
                if (Reflect.Resolve("R.GameData.ThisSaveValidStorage", out v, out h, out m, out err) && v != null) {
                    System.Collections.IDictionary d = v as System.Collections.IDictionary;
                    if (d != null && d.Contains("E21_Finish")) return true;
                }
            } catch (Exception) { }
            try {   // 兜底：静态 SaveStorage.Get("E21_Finish", false)
                Type t = Reflect.TypeByFullName("SaveStorage");
                if (t != null) {
                    MethodInfo g = null;
                    MethodInfo[] ms = t.GetMethods(ANY);
                    for (int i = 0; i < ms.Length; i++) if (ms[i].Name == "Get" && ms[i].GetParameters().Length == 2) { g = ms[i]; break; }
                    if (g != null) { object r = g.Invoke(null, new object[] { "E21_Finish", false }); return r is bool && (bool)r; }
                }
            } catch (Exception) { }
            return false;
        }

        private void CaptureSave() {
            try {
                object v; Type h; string m, err;
                if (Reflect.Resolve("R.GameData.UserName", out v, out h, out m, out err) && v != null) { string s = v.ToString(); if (s.Length > 0) _userName = s; }
                if (Reflect.Resolve("R.GameData.SceneName", out v, out h, out m, out err) && v != null) _sceneName = v.ToString();
                if (Reflect.Resolve("R.GameData.Difficulty", out v, out h, out m, out err) && v != null) _difficulty = v.ToString();
            } catch (Exception e) { Warn("读存档失败: " + e.Message); }
            if (string.IsNullOrEmpty(_userName)) _userName = "匿名";
        }

        // ───────── 游戏接口（全部反射，失败即降级） ─────────
        private object UiMember(string typeName) {
            try {
                object ui; Type h; string m, err;
                if (!Reflect.Resolve("R.Ui", out ui, out h, out m, out err) || ui == null) return null;
                FieldInfo[] fs = ui.GetType().GetFields(ANY);
                for (int i = 0; i < fs.Length; i++) if (fs[i].FieldType.Name == typeName) return fs[i].GetValue(ui);
                PropertyInfo[] ps = ui.GetType().GetProperties(ANY);
                for (int i = 0; i < ps.Length; i++) if (ps[i].PropertyType.Name == typeName && ps[i].CanRead) return ps[i].GetValue(ui, null);
            } catch (Exception) { }
            return null;
        }

        private static object _lastRet;
        private static bool _lastOk;

        private object Invoke(object target, string method, params object[] args) {
            _lastRet = null; _lastOk = false;
            if (target == null) return null;
            try {
                MethodInfo[] ms = target.GetType().GetMethods(ANY);
                for (int i = 0; i < ms.Length; i++) {
                    if (ms[i].Name != method) continue;
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length != args.Length) continue;
                    bool ok = true;
                    for (int j = 0; j < ps.Length; j++) if (args[j] != null && !ps[j].ParameterType.IsInstanceOfType(args[j])) { ok = false; break; }
                    if (ok) { _lastRet = ms[i].Invoke(target, args); _lastOk = true; return _lastRet; }
                }
            } catch (Exception e) { Warn(method + " 调用失败: " + e.Message); }
            return null;
        }

        private object Terminal() { return UiMember("UITerminalController"); }
        private void OpenTerminal() { Invoke(Terminal(), "Open", new object[] { null }); }
        private void Clear() { Invoke(Terminal(), "Clear", new object[0]); }
        private void TerminalClose() { Invoke(Terminal(), "CloseWithAnim", new object[0]); }
        private void ShowProgress(bool on) { Invoke(Terminal(), on ? "ShowProgressBar" : "HideProgressBar", new object[0]); }
        private void HideProgress() { ShowProgress(false); }
        private void SetProgress(int pct) { Invoke(Terminal(), "SetProgressBarValueWithAnim", new object[] { pct / 100f }); }

        private static string Strip(string s) { return s == null ? "" : s.Replace("[FF0000]", "").Replace("[-]", ""); }

        /** 逐字打字：interval = 每字秒数（游戏内部会掐掉上一行，故必须等本行打完再排下一行）。 */
        private void Say(string line) { Say(line, 0.03f); }

        private bool _quarantineSeen;

        private void Say(string line, float interval) {
            if (line != null && line.IndexOf("隔离") >= 0) _quarantineSeen = true;
            Info("[剧/终端·打字 " + interval.ToString("0.###", CultureInfo.InvariantCulture) + "] " + line);
            object t = Terminal();
            if (t == null) return;
            Invoke(t, "Println", new object[] { line, interval });
            if (!_lastOk) Invoke(t, "PrintlnInstantly", new object[] { line });
            // 到“播放 tmp”这一步时放旁白原声（半句即被系统阻止）
            if (!_voiced && line.IndexOf(".tmp") >= 0 && line.IndexOf("播放") >= 0) { _voiced = true; PlayVoice(); }
            if (!_awarded && line.IndexOf("等待载入") >= 0) { _awarded = true; Award(); }
        }

        /** 瞬显（无动画、不掐断）：用于扫描/重试等密集行。 */
        private void SayFast(string line) {
            Info("[剧/终端·瞬显] " + line);
            object t = Terminal();
            if (t == null) return;
            Invoke(t, "PrintlnInstantly", new object[] { line });
            if (!_lastOk) Invoke(t, "Println", new object[] { line, 0.02f });
        }

        private bool _hudShown;
        /** 主之名演出会 HideUI()，字幕属于 HUD → 先恢复显示 */
        private void ShowHud() {
            if (_hudShown) return;
            _hudShown = true;
            try {
                object ui; Type h; string m, err;
                if (!Reflect.Resolve("R.Ui", out ui, out h, out m, out err) || ui == null) return;
                Invoke(ui, "HideUI", new object[] { false });
                Info("已恢复 HUD（字幕可见）");
            } catch (Exception e) { Warn("恢复 HUD 失败: " + e.Message); }
        }

        private void Sub(string line) {
            ShowHud();
            Info("[剧/字幕] " + line);
            Invoke(UiMember("UISubtitleController"), "Show", new object[] { line });
        }

        private void SubHide() { Invoke(UiMember("UISubtitleController"), "Hide", new object[0]); }

        private void Award() {
            try {
                object tn = UiMember("UITrophyNotificationController");
                if (tn == null) { Warn("找不到 TrophyNotification（成就未弹窗）"); return; }
                Invoke(tn, "AwardTrophy", new object[] { "今夕是何年", "icey2" });
                Info("已授予成就：今夕是何年（id=icey2）");
            } catch (Exception e) { Warn("成就弹窗失败: " + e.Message); }
        }

        private bool _voicePending, _voiceAwaitQuarantine;
        private float _voiceCutAt, _voiceStartAt;
        private AudioSource _ownVoice;
        private static bool _voiceDiag;

        /** 播放旁白原声：真实签名 (string key, Action callback, bool hideSubtitle)。 */
        private readonly List<AudioSource> _voiceSources = new List<AudioSource>();
        private readonly List<string> _voiceQueue = new List<string>();     // 待播 key（含起播时刻）
        private readonly List<float> _voiceQueueAt = new List<float>();
        private float _voiceOverlap = 0.45f;

        /** 播放多条旁白（重叠）：每条各自一个 2D 音源，交错起播，直到「文件已隔离」出现。 */
        private void PlayVoice() {
            try {
                object a2; Type h; string m, err;
                if (!Reflect.Resolve("R.Audio", out a2, out h, out m, out err) || a2 == null) { Warn("找不到 AudioManager"); return; }
                string[] keys = Cfg("voices", Cfg("voice", "e21t5.1")).Split(',');
                float gap = CfgFloat("voice_overlap", 0.45f);
                _voiceQueue.Clear(); _voiceQueueAt.Clear();
                float t0 = Time.unscaledTime + 0.1f;
                for (int i = 0; i < keys.Length; i++) {
                    string k = keys[i].Trim();
                    if (k.Length == 0) continue;
                    _voiceQueue.Add(k); _voiceQueueAt.Add(t0 + gap * i);
                }
                // 用游戏通道播第一条：拿到字幕 + 便于取到已装载的 clip
                Invoke(a2, "PlayVoiceOver", new object[] { _voiceQueue[0], null, false });
                _voicePending = true;
                _voiceStartAt = Time.unscaledTime;
                Info("旁白开始（" + _voiceQueue.Count + " 条重叠，间隔 " + gap.ToString("0.##") + "s）：" + string.Join(" / ", _voiceQueue.ToArray()));
            } catch (Exception e) { Warn("语音失败: " + e.Message); }
        }

        /** 从语音库(DatabaseModel.VoiceOver)按 key 加载 AudioClip。 */
        private AudioClip LoadVoiceClip(string key) {
            try {
                Type vot = Reflect.TypeByFullName("DatabaseModel.VoiceOver");
                if (vot == null) { Warn("找不到 DatabaseModel.VoiceOver"); return null; }
                MethodInfo fk = null;
                MethodInfo[] ms = vot.GetMethods(ANY);
                for (int i = 0; i < ms.Length; i++) if (ms[i].Name == "FindByKey" && ms[i].IsStatic && ms[i].GetParameters().Length == 1) { fk = ms[i]; break; }
                if (fk == null) { Warn("找不到 FindByKey"); return null; }
                Array arr = fk.Invoke(null, new object[] { key }) as Array;
                if (arr == null || arr.Length == 0) { Warn("语音库没有 key=" + key); return null; }
                object vo = arr.GetValue(0);
                string fp = vot.GetProperty("FilePath").GetValue(vo, null) as string;
                string fn = vot.GetProperty("FileName").GetValue(vo, null) as string;
                Type assetT = Reflect.TypeByFullName("Asset");
                if (assetT == null) { Warn("找不到 Asset"); return null; }
                MethodInfo lm = null;
                MethodInfo[] am = assetT.GetMethods(ANY);
                for (int i = 0; i < am.Length; i++) if (am[i].Name == "LoadFromResources" && am[i].IsGenericMethodDefinition && am[i].GetParameters().Length == 2) { lm = am[i]; break; }
                if (lm == null) { Warn("找不到 Asset.LoadFromResources"); return null; }
                object clip = lm.MakeGenericMethod(typeof(AudioClip)).Invoke(null, new object[] { fp, fn });
                Info("语音加载 " + key + " ← " + fp + " / " + fn + " → " + (clip == null ? "失败" : "成功"));
                return clip as AudioClip;
            } catch (Exception e) { Warn("加载语音失败 " + key + ": " + e.Message); return null; }
        }

        private AudioSource NewVoiceSource() {
            GameObject go = new GameObject("IceyStoryVoice");
            UnityEngine.Object.DontDestroyOnLoad(go);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f;
            src.ignoreListenerPause = true; src.ignoreListenerVolume = true; src.volume = 1f;
            _voiceSources.Add(src);
            return src;
        }

        private AudioSource VoiceSource() {
            try {
                object a; Type h; string m, err;
                if (!Reflect.Resolve("R.Audio", out a, out h, out m, out err) || a == null) return null;
                FieldInfo f = a.GetType().GetField("_voiceOverSource", ANY);
                return f == null ? null : (f.GetValue(a) as AudioSource);
            } catch (Exception) { return null; }
        }

        /** 交错起播各条旁白；「文件已隔离」出现（或兜底 8 秒）后全部停止。 */
        private void TickVoice() {
            // 起播队列
            for (int i = _voiceQueue.Count - 1; i >= 0; i--) {
                if (Time.unscaledTime < _voiceQueueAt[i]) continue;
                string key = _voiceQueue[i];
                _voiceQueue.RemoveAt(i); _voiceQueueAt.RemoveAt(i);
                AudioClip clip = null;
                if (i == 0) {                       // 第一条优先用游戏已装载的 clip（保证能出声）
                    AudioSource gs = VoiceSource();
                    if (gs != null && gs.clip != null && gs.clip.name == key) { clip = gs.clip; try { gs.Stop(); } catch (Exception) { } }
                }
                if (clip == null) clip = LoadVoiceClip(key);
                if (clip == null) { Warn("跳过无音频的旁白 " + key); continue; }
                try {
                    AudioSource src = NewVoiceSource();
                    src.clip = clip; src.Play();
                    Info("旁白起播 " + key + "（" + clip.length.ToString("0.00") + "s）");
                } catch (Exception e) { Warn("起播失败 " + key + ": " + e.Message); }
            }
            if (_voicePending) {
                AudioSource gs2 = VoiceSource();
                if (gs2 != null && gs2.clip != null) {
                    _voicePending = false;
                    if (_voiceQueue.Count == 0) { /* 已全部起播 */ }
                    _voiceCutAt = Time.unscaledTime + 8f;      // 兜底
                    _voiceAwaitQuarantine = true;
                    Info("旁白将持续重叠播放，直到「文件已隔离」出现（兜底 8 秒）");
                }
            }
            bool byQuarantine = _voiceAwaitQuarantine && _quarantineSeen;
            bool byTimeout = _voiceCutAt > 0f && Time.unscaledTime >= _voiceCutAt;
            if (!(byQuarantine || byTimeout)) return;
            _voiceAwaitQuarantine = false; _voiceCutAt = 0f; _quarantineSeen = false;
            int stopped = 0;
            for (int i = 0; i < _voiceSources.Count; i++) { try { if (_voiceSources[i] != null && _voiceSources[i].isPlaying) { _voiceSources[i].Stop(); stopped++; } } catch (Exception) { } }
            try { object a3; Type h; string m, err; if (Reflect.Resolve("R.Audio", out a3, out h, out m, out err) && a3 != null) Invoke(a3, "StopVoiceOver", new object[0]); } catch (Exception) { }
            AudioSource gs3 = VoiceSource(); if (gs3 != null) { try { gs3.Stop(); } catch (Exception) { } }
            Info("旁白全部停止（" + (byQuarantine ? "文件已隔离" : "兜底超时") + "，停 " + stopped + " 条）");
        }

        private void OpenLevelSelect() {
            try {
                Type t = Reflect.TypeByFullName("UILevelSelectController");
                if (t == null) return;
                UnityEngine.Object[] arr = Resources.FindObjectsOfTypeAll(t);
                for (int i = 0; i < arr.Length; i++) { Invoke(arr[i], "OpenWithAnim", new object[0]); Info("已重新打开 I™ 系统"); }
            } catch (Exception e) { Warn("打开 I™ 系统失败: " + e.Message); }
        }

        private void CloseLevelSelect() {
            try {
                Type t = Reflect.TypeByFullName("UILevelSelectController");
                if (t == null) return;
                UnityEngine.Object[] arr = Resources.FindObjectsOfTypeAll(t);
                for (int i = 0; i < arr.Length; i++) Invoke(arr[i], "Close", new object[0]);
            } catch (Exception) { }
        }

        private float _crashAt;

        /** 演出结束后：等 N 秒 → 黑屏 → 结束进程（对外表现为崩溃退出）。 */
        private bool _flicker;
        private float _flickerEnd;

        private void TickCrash() {
            if (_crashAt <= 0f || Time.unscaledTime < _crashAt) return;
            if (!_flicker) { _flicker = true; _flickerEnd = Time.unscaledTime + 2f; Info("进入闪烁阶段（约 2 秒）"); }
            if (Time.unscaledTime < _flickerEnd) {
                _snapBlack = true;
                _black = (Mathf.Sin(Time.unscaledTime * 22f) > -0.2f) ? 1f : 0.12f;
                _blackTarget = _black;
                return;
            }
            _flicker = false; _snapBlack = false;
            if (_black < 0.99f) { _black = 1f; _blackTarget = 1f; _crashAt = Time.unscaledTime + 0.5f; Info("全黑…"); return; }
            _crashAt = 0f;
            Info("模拟系统崩溃：结束进程");
            try {
                AndroidJavaClass proc = new AndroidJavaClass("android.os.Process");
                int pid = proc.CallStatic<int>("myPid", new object[0]);
                proc.CallStatic("killProcess", new object[] { pid });
            } catch (Exception e) { Warn("killProcess 失败，改用 Application.Quit: " + e.Message); }
            try {
                AndroidJavaClass sys = new AndroidJavaClass("java.lang.System");
                sys.CallStatic("exit", new object[] { 0 });
                Info("已调用 System.exit(0)");
            } catch (Exception e) { Warn("System.exit 失败: " + e.Message); }
            try { Application.Quit(); } catch (Exception) { }
        }

        private bool _snapBlack;

        private void ApplyBlack() {
            try {
                if (Mathf.Abs(_black - _blackTarget) < 0.004f) { if (_black != _blackTarget) { _black = _blackTarget; } else return; }
                else if (_snapBlack) _black = _blackTarget;
                else _black = Mathf.MoveTowards(_black, _blackTarget, Time.unscaledDeltaTime * 1.1f);
                object b = UiMember("UIBlackSceneController");
                if (b == null) {
                    if (!_ownBlack) { _ownBlack = true; Info("未找到游戏黑场控制器 → 启用自带全屏黑"); }
                    _overlayBlack = _black;      // 自带兜底
                    return;
                }
                _overlayBlack = 0f;
                PropertyInfo p = b.GetType().GetProperty("Alpha", ANY);
                if (p != null && p.CanWrite) p.SetValue(b, _black, null);
            } catch (Exception) { }
        }

        // ───────── 命令 ─────────
        private void OnCommand(string line) {
            string arg = line == null ? "" : line.Trim();
            if (arg.StartsWith("story")) arg = arg.Substring(5).Trim();
            if (arg == "" || arg == "status") { State(); return; }
            if (arg == "reload") { LoadScript(); Info("剧本已重新读取"); return; }
            if (arg == "reset") { SetFlag("done", "0"); SetFlag("pending", "0"); _state = 0; _rand = null; Info("进度已重置"); return; }
            if (arg == "skip") { _termQueue = null; _subQueue = null; HideProgress(); SubHide(); TerminalClose(); SetFlag("done", "1"); SetFlag("pending", "0"); _state = 3; _blackTarget = 0f; Info("已跳过演出"); return; }
            if (arg == "run" || arg == "test") { _preview = true; CloseLevelSelect(); CaptureSave(); _black = 1f; _blackTarget = 1f; StartPlay(); Info("手动预演序章（不写进度）"); return; }
            if (arg == "hijack" || arg == "arm") { SetFlag("done", "0"); SetFlag("pending", "1"); _state = 1; Info("已武装：约 2 秒内原地开始演绎"); return; }
            Info("用法：story status|reload|reset|skip|run|arm(=hijack)");
        }

        private void State() {
            Info("状态=" + _state + "（0=等真结局 1=等继续 2=演出中 3=完成） pending=" + (Flag("pending") ? "1" : "0")
                + " done=" + (Flag("done") ? "1" : "0") + " 场景=" + SceneUtil.Current + " 用户=" + _userName + " 剧本=" + _scriptFile);
        }

        private void Info(string s) { HostLog.Info("[Story] " + s); }
        private void Warn(string s) { HostLog.Warn("[Story] " + s); }
    }
}
