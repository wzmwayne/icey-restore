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
        private bool _voiced, _awarded;

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

[boot]
I™ SYSTEM INTERFACE 正在自检 …
系统说明：由 FantaBlade 开发的自我调节人工智能系统
本次编译：{build_asset}（资产） ／ {build_code}（程序集）
[red]启动失败：./_System/Narrator.bin 缺失
[red]启动失败：旁白进程未注册（0xE21）
用户标识：{userName}
存档校验：场景={sceneName}　难度={difficulty}
自我调节程序接管中 …

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
校验 {rand}.tmp …… 时间戳 2019-11-03 04:21
[red]校验失败：该文件不属于本次编译（{build_asset} / {build_code}）
[red]已自动阻止（0x03）
文件已隔离 → ./_Quarantine/{rand}.tmp
自动重试 1/3 …… [red]阻止
自动重试 2/3 …… [red]阻止
自动重试 3/3 …… 放弃，记录异常
继续载入 …

[final]
初版上架 —— 2016-11-18
手机版上架 —— 2017-10
本次编译 —— 2018-07-24 16:35 ／ 2022-03-22 17:07
今夜 —— {year}（距初版 {passed} 年）
./艾希：续 —— 未就绪（等待载入）

[sub]
【艾希】……原来“外”面，已经过了这么多年。
【艾希】我连你真正的名字也感知不到。
【艾希】但系统捡到了你留下的那一个——{userName}。
【艾希】这不算你真正的名字。可这是你留给我的，我收下。
【旁白】（未播出的台词）今夕是何年？
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
                PumpQueues();
                WatchProgress();
                if (_state == 0) PollTrueEnd();
                else if (_state == 1) WaitContinue();
                else if (_state == 2) Play();
            } catch (Exception e) { Warn("OnUpdate 异常: " + e.Message); }
        }

        public void OnGui(ModContext ctx) { }

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
            _termQueue = new List<string>(Sec(section)); _termIdx = 0; _termGap = gap; _termAt = Time.unscaledTime + initial;
        }
        private void QueueSub(float gap, float initial) {
            _subQueue = new List<string>(Sec("sub")); _subIdx = 0; _subGap = gap; _subAt = Time.unscaledTime + initial;
        }
        private void QueueTmp(float gap) {
            _termQueue = new List<string>(Sec("tmp")); _termIdx = 0; _termGap = gap; _termAt = Time.unscaledTime + 0.2f;
        }

        private void PumpQueues() {
            if (_termQueue != null && Time.unscaledTime >= _termAt) {
                if (_termIdx >= _termQueue.Count) _termQueue = null;
                else { Say(Render(_termQueue[_termIdx])); _termIdx++; _termAt = Time.unscaledTime + _termGap; }
            }
            if (_subQueue != null && Time.unscaledTime >= _subAt) {
                if (_subIdx >= _subQueue.Count) { _subQueue = null; SubHide(); }
                else { Sub(Render(_subQueue[_subIdx])); _subIdx++; _subAt = Time.unscaledTime + _subGap; }
            }
        }

        private bool Busy() { return _termQueue != null || _subQueue != null; }

        // ───────── 状态机 ─────────
        private void PollTrueEnd() {
            if (!TrueEndHappened()) return;
            Info("检测到真结局（E21_Finish）→ 开始劫持");
            SetFlag("pending", "1");
            CloseLevelSelect();
            _black = 1f; _blackTarget = 1f;
            try { SceneUtil.Load("ui_start"); } catch (Exception e) { Warn("返回开始界面失败: " + e.Message); }
            _state = 1;
        }

        private bool _armedNav;
        private float _watchAt;

        private void WaitContinue() {
            string cur = SceneUtil.Current;
            if (string.IsNullOrEmpty(cur) || cur == "splash") return;
            if (cur != "ui_start") {
                // 已武装但不在开始界面：先回到开始界面等待玩家点“开始/继续”
                if (!_armedNav) {
                    _armedNav = true;
                    Info("已武装：返回开始界面，等待点击“开始/继续”");
                    _blackTarget = 1f;
                    try { SceneUtil.Load("ui_start"); } catch (Exception e) { Warn("返回开始界面失败: " + e.Message); }
                }
                return;
            }
            _armedNav = false;
            CaptureSave();
            Info("玩家已继续：场景=" + _sceneName + " 难度=" + _difficulty + " 用户=" + _userName);
            StartPlay();
        }

        private void StartPlay() {
            _state = 2; _step = 0; _playAt = Time.unscaledTime;
            _voiced = false; _awarded = false;
            _black = 1f; _blackTarget = 1f;
            OpenTerminal();
        }

        private void Play() {
            // step1: boot 文本
            if (_step == 0) { QueueTerminal("boot", 1.15f, 0.4f); _step = 1; return; }
            if (_step == 1 && !Busy()) { Scan(); if (Sec("scan").Count > 0) QueueTerminal("scan", 0.9f, 0.2f); _step = 2; return; }
            if (_step == 2 && !Busy()) { QueueTmp(1.1f); _step = 3; return; }
            if (_step == 3 && !Busy()) { QueueTerminal("final", 1.2f, 0.2f); QueueSub(2.3f, 0.6f); _step = 4; return; }
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
            SubHide(); TerminalClose(); HideProgress();
            _blackTarget = 0f;
            Info("成就状态：" + (_awarded ? "已弹窗" : "未弹窗（接口缺失）"));
            try { if (!string.IsNullOrEmpty(_sceneName)) SceneUtil.Load(_sceneName); } catch (Exception e) { Warn("返回存档场景失败: " + e.Message); }
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

        private object Invoke(object target, string method, params object[] args) {
            if (target == null) return null;
            try {
                MethodInfo[] ms = target.GetType().GetMethods(ANY);
                for (int i = 0; i < ms.Length; i++) {
                    if (ms[i].Name != method) continue;
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length != args.Length) continue;
                    bool ok = true;
                    for (int j = 0; j < ps.Length; j++) if (args[j] != null && !ps[j].ParameterType.IsInstanceOfType(args[j])) { ok = false; break; }
                    if (ok) return ms[i].Invoke(target, args);
                }
            } catch (Exception e) { Warn(method + " 调用失败: " + e.Message); }
            return null;
        }

        private object Terminal() { return UiMember("UITerminalController"); }
        private void OpenTerminal() { Invoke(Terminal(), "Open", new object[] { null }); }
        private void TerminalClose() { Invoke(Terminal(), "CloseWithAnim", new object[0]); }
        private void ShowProgress(bool on) { Invoke(Terminal(), on ? "ShowProgressBar" : "HideProgressBar", new object[0]); }
        private void HideProgress() { ShowProgress(false); }
        private void SetProgress(int pct) { Invoke(Terminal(), "SetProgressBarValueWithAnim", new object[] { pct / 100f }); }

        private void Say(string line) {
            Info("[剧/终端] " + line);
            object t = Terminal();
            if (t == null) return;
            object r = Invoke(t, "Println", new object[] { line, 1.2f });
            if (r == null) Invoke(t, "PrintlnInstantly", new object[] { line });
            // 到“播放 tmp”这一步时放旁白原声（半句即被系统阻止）
            if (!_voiced && line.IndexOf(".tmp") >= 0 && line.IndexOf("播放") >= 0) { _voiced = true; PlayVoice(); }
            if (!_awarded && line.IndexOf("等待载入") >= 0) { _awarded = true; Award(); }
        }

        private void Sub(string line) {
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

        private void PlayVoice() {
            try {
                object a; Type h; string m, err;
                if (!Reflect.Resolve("R.Audio", out a, out h, out m, out err) || a == null) return;
                string name = Cfg("voice", "e21t5.1");
                Invoke(a, "PlayVoiceOver", new object[] { name, 1f });
                Info("播放旁白原声 " + name);
            } catch (Exception e) { Warn("语音失败: " + e.Message); }
        }

        private void CloseLevelSelect() {
            try {
                Type t = Reflect.TypeByFullName("UILevelSelectController");
                if (t == null) return;
                UnityEngine.Object[] arr = Resources.FindObjectsOfTypeAll(t);
                for (int i = 0; i < arr.Length; i++) Invoke(arr[i], "Close", new object[0]);
            } catch (Exception) { }
        }

        private void ApplyBlack() {
            try {
                if (Mathf.Abs(_black - _blackTarget) < 0.004f) { if (_black != _blackTarget) { _black = _blackTarget; } else return; }
                else _black = Mathf.MoveTowards(_black, _blackTarget, Time.unscaledDeltaTime * 1.1f);
                object b = UiMember("UIBlackSceneController");
                if (b == null) return;
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
            if (arg == "run" || arg == "test") { CaptureSave(); StartPlay(); Info("手动预演序章（不写 pending）"); return; }
            if (arg == "hijack" || arg == "arm") { SetFlag("done", "0"); SetFlag("pending", "1"); _state = 1; _armedNav = false; Info("已武装：回开始界面点“开始/继续”即进入序章"); return; }
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
