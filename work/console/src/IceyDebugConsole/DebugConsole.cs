using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using IceyModHost;
using UnityEngine;

namespace IceyDebugConsole {

    /// <summary>内置调试台（mod）：预设数值锁定 / 反射浏览 / 场景跳转 / 存档 / 日志 / WebUI。</summary>
    public class DebugConsole : IIceyMod {
        // ---------------- 玩家数值预设 ----------------
        private const string P = "GameWorld.PlayerManager.Attribute";
        private static readonly string[] PKeys = { "atk", "hp", "maxhp", "energy", "maxenergy", "flash", "flashlvl", "flashcd", "speed", "charge", "execute", "endure" };
        private static readonly string[] PLabels = { "攻击力", "当前血量", "血量上限", "闪避能量", "闪避能量上限", "闪避次数", "闪避等级", "闪避冷却", "移动速度", "蓄力时间", "处决次数上限", "蓄力承伤上限" };
        private static readonly string[] PSfx = {
            ".baseAtk", ".currentHP", ".maxHP", ".currentEnergy", ".maxEnergy",
            ".currentFlashTimes", ".flashLevel", ".FlashCd", ".moveSpeed", ".maxChargeTime",
            ".maxExecuteNum", ".maxChargeEndureDamage" };
        private static readonly string[] PAlts = { P, "R.Player.Attribute", "PlayerAttribute" };
        private static readonly bool[] PIsFloat = { false, false, false, false, false, false, false, true, true, true, false, false };

        private ModContext _ctx;
        private bool _show;
        private Rect _ball = new Rect(0, 0, 0, 0);
        private Rect _win = new Rect(0, 0, 0, 0);
        private int _tab;
        private string _search = "";
        private Type _selType;
        private string _pendingPath = "", _pendingValue = "";
        private string _prefsMsg = "", _logFilter = "";
        private List<PrefsUtil.Pref> _prefs;
        private float _pubTimer;
        private string _confirmKey; private float _cfAt;
        private Vector2 _moveDir; private float _moveSpeed = 6f;
        private bool _freezeOnOpen; private float _savedScale = -1f;
        private int _moveTicks;
        private string _logDump;
        private float _logAt;
        private readonly string[] _edit = new string[16];
        private readonly bool[] _edited = new bool[16];
        private readonly ModGui.ScrollState _scPreset = ModGui.NewScroll();
        private readonly ModGui.ScrollState _scLock = ModGui.NewScroll();
        private readonly ModGui.ScrollState _scBrowse = ModGui.NewScroll();
        private readonly ModGui.ScrollState _scPrefs = ModGui.NewScroll();
        private readonly ModGui.ScrollState _scScene = ModGui.NewScroll();
        private readonly ModGui.ScrollState _scLog = ModGui.NewScroll();
        private static Font _cjk;
        private static bool _fontTried;

        private static readonly string[] Tabs = { "预设", "浏览", "敌人", "传送", "移动", "存档", "解锁", "奖杯", "日志", "Web", "设置" };

        public void OnLoad(ModContext ctx) {
            _ctx = ctx;
            ctx.OnCommand(Handle);
            ctx.RegisterWebRoute("debug");
            ctx.Log("调试台已加载：右下角小球开关；预设含攻击/血量/闪能量等 12 项");
            try { _prefs = PrefsUtil.Load(); } catch (Exception e) { ctx.Log("读取存档失败: " + e.Message); }
            Publish();
        }

        /** 统一设置速度：时间缩放 + 音频音调 + 视频播放速率。 */
        private void SetSpeed(float scale) {
            Time.timeScale = scale;
            ApplyMediaRate(scale);
            _appliedRate = scale;
            _ctx.Log("速度设为 " + scale.ToString("0.##") + "x（含音频/视频）");
        }

        /** 把速度同步到音频与视频（游戏音频管理器可能覆盖，故周期重设）。 */
        private void ApplyMediaRate(float scale) {
            try {
                AudioSource[] srcs = (AudioSource[])UnityEngine.Object.FindObjectsOfType(typeof(AudioSource));
                for (int i = 0; i < srcs.Length; i++) { try { srcs[i].pitch = scale; } catch (Exception) { } }
            } catch (Exception) { }
            try {
                Type mp = Reflect.TypeByFullName("RenderHeads.Media.AVProVideo.MediaPlayer");
                if (mp == null) mp = Reflect.TypeByFullName("MediaPlayer");
                if (mp != null) {
                    UnityEngine.Object[] players = UnityEngine.Object.FindObjectsOfType(mp);
                    for (int i = 0; i < players.Length; i++) {
                        PropertyInfo pr = mp.GetProperty("PlaybackRate", ANY);
                        if (pr != null && pr.CanWrite) { pr.SetValue(players[i], Convert.ChangeType(scale, pr.PropertyType), null); }
                    }
                }
            } catch (Exception) { }
        }

        public void OnUpdate(ModContext ctx) {
            _pubTimer += Time.unscaledDeltaTime;
            if (_pubTimer > 2f) { _pubTimer = 0f; Publish(); }
            ApplyMove();
            HandleFreeze();
            // 速度同步：时间被外部改动或需要持续压制音频时重设
            _rateAt += Time.unscaledDeltaTime;
            if (Mathf.Abs(Time.timeScale - _appliedRate) > 0.001f || (_appliedRate != 1f && _rateAt > 0.5f)) {
                _rateAt = 0f;
                ApplyMediaRate(Time.timeScale);
                _appliedRate = Time.timeScale;
            }
            if (IceyModHost.ModConsoleBridge.ToggleRequested) { IceyModHost.ModConsoleBridge.ToggleRequested = false; _show = !_show; _ctx.Log("面板 " + (_show ? "已打开" : "已关闭")); }
            if (IceyModHost.ModConsoleBridge.ShowRequested) { IceyModHost.ModConsoleBridge.ShowRequested = false; _show = true; }
            if (IceyModHost.ModConsoleBridge.HideRequested) { IceyModHost.ModConsoleBridge.HideRequested = false; _show = false; }
        }

        /** 强制移动：按未缩放时间推进（暂停/变速时也生效）。 */
        private void ApplyMove() {
            if (_moveDir == Vector2.zero) return;
            try {
                object t; Type h; string m, err;
                if (!Reflect.Resolve("R.Player.Transform", out t, out h, out m, out err)) return;
                Transform tr = t as Transform;
                if (tr == null) return;
                Vector3 p = tr.position;
                float step = _moveSpeed * Time.unscaledDeltaTime;
                p.x += _moveDir.x * step; p.y += _moveDir.y * step;
                tr.position = p;
                try { Rigidbody2D rb = tr.GetComponent<Rigidbody2D>(); if (rb != null) rb.position = new Vector2(p.x, p.y); } catch (Exception) { }
                if ((_moveTicks++ % 30) == 0) HostLog.Info(string.Format("强制移动 {0} → ({1:0.0}, {2:0.0})", _moveDir, p.x, p.y));
            } catch (Exception e) { HostLog.Warn("移动失败: " + e.Message); }
        }

        /** 打开菜单时冻结游戏时间（可开关，关闭窗口自动恢复）。 */
        private void HandleFreeze() {
            if (_freezeOnOpen && _show) {
                if (Time.timeScale != 0f) { _savedScale = Time.timeScale; Time.timeScale = 0f; }
            } else if (_savedScale >= 0f) {
                Time.timeScale = _savedScale > 0f ? _savedScale : 1f;
                _savedScale = -1f;
            }
        }

        /** 二次确认：第一次点击进入“待确认”，4 秒内再点一次才执行。 */
        private bool Confirm(string key, string what) {
            if (_confirmKey == key && Time.unscaledTime - _cfAt < 4f) { _confirmKey = null; return true; }
            _confirmKey = key; _cfAt = Time.unscaledTime;
            _ctx.Log("⚠ 再点一次以确认：" + what);
            return false;
        }

        private string ConfirmLabel(string key, string label) {
            return _confirmKey == key ? ("确认 " + label + " ?") : label;
        }

        /** 可能影响存档的操作统一走这里。 */
        private bool Risky(string key, string what) { return Confirm(key, what); }

        public void OnUnload(ModContext ctx) { }

        public void OnGui(ModContext ctx) {
            EnsureFont();
            if (ModGui.Ball(ref _ball, "调试")) _show = !_show;
            if (!_show) return;
            ModGui.Window(0x1CE7, ref _win, "ICEY 调试台 · " + SceneUtil.Current + " · " + Fps() + "fps", DrawWindow);
        }

        private static void EnsureFont() {
            if (_cjk != null || _fontTried) return;
            _fontTried = true;
            try {
                _cjk = Font.CreateDynamicFontFromOSFont(new string[] { "Noto Sans CJK SC", "Droid Sans Fallback", "Source Han Sans CN", "sans-serif" }, 14);
                if (_cjk != null) {
                    ModGui.SetupSkin(_cjk, 14);          // 统一字号/内边距，避免文字被裁剪
                    HostLog.Info("中文字体已加载: " + _cjk.name);
                } else {
                    HostLog.Warn("未找到系统中文字体：界面文字可能显示为方框");
                }
            } catch (Exception e) { HostLog.Warn("字体加载失败: " + e.Message); }
        }

        private static string Fps() {
            try { return (Time.smoothDeltaTime > 0f ? (1f / Time.smoothDeltaTime) : 0f).ToString("0", CultureInfo.InvariantCulture); }
            catch (Exception) { return "0"; }
        }

        /** 列表高度：以窗口实际高度为基准，保证窗口缩小时内容仍完整可见。 */
        private static float ListH(float frac) {
            float avail = Mathf.Max(160f, ModGui.WinH - 170f);
            return Mathf.Clamp(avail * frac * 1.6f, 160f, avail);
        }

        private float _lastVW, _lastVH;

        private ModGui.ScrollState _scOuter = ModGui.NewScroll();
        private float _pageX, _pageY, _pageW;
        private string _scaleText;
        private bool _spawnOpen;
        private string _spawnSearch = "";
        private string _spawnMsg = "";
        private int _selEnemy = -1;
        private string _hpEdit = "";
        private static string[] _etypeNames;
        private static int[] _etypeValues;
        private float _appliedRate = 1f, _rateAt;
        private static readonly Dictionary<int, string> _trophyNames = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> _trophyDetails = new Dictionary<int, string>();
        private float _trophyAt;
        private int _trophyTotal = -1, _trophyUnlocked = -1, _trophyOpen = -1, _trophySel = -1;
        private string _trophyMsg = "";
        private Rect _outerView, _bodyView;
        private static readonly float[] _pageH = new float[16];

        private float NextRow(float h) { float y = _pageY; _pageY += h; return y; }
        private void PLabel(string text, float h) { GUI.Label(new Rect(_pageX, NextRow(h), _pageW, h - 2f), text); }
        private bool PButton(string text, float w, float h) { return GUI.Button(new Rect(_pageX, NextRow(h + 4f), w, h), text); }

        /** 页面内嵌列表：登记（含外层偏移的）命中矩形 + 处理拖动，返回内容坐标矩形。 */
        private Rect InnerRect(float viewH, float contentH, ModGui.ScrollState st) {
            Rect r = new Rect(_pageX, _pageY, _pageW, viewH);
            _pageY += viewH + 6f;
            // 三种可能的坐标空间都试（内容坐标 / 窗口坐标 / 视区坐标），命中哪个用哪个
            Rect hit = _innerHit;
            if (Event.current != null && (Event.current.type == EventType.MouseDown || hit.width <= 0f)) {
                hit = InnerHitRect(r);
            }
            if (hit.width <= 0f) hit = r;
            ModGui.RegisterInner(hit);
            st.Handle(r, contentH, hit, true);   // 内层列表优先级最高
            return r;
        }

        private Rect _innerHit;

        /** 依次尝试内容坐标 / 窗口坐标 / 视区坐标，返回命中的那个矩形（都不中则返回零宽矩形）。 */
        private Rect InnerHitRect(Rect r) {
            if (ModGui.Inside(r)) return r;
            Rect w = new Rect(_outerView.x + r.x - _scOuter.Pos.x, _outerView.y + r.y - _scOuter.Pos.y, r.width, r.height);
            if (ModGui.Inside(w)) return w;
            Rect v = new Rect(_outerView.x + r.x, _outerView.y + r.y, r.width, r.height);
            if (ModGui.Inside(v)) return v;
            if (Event.current != null && Event.current.type == EventType.MouseDown) {
                Vector2 m = ModGui.RawMouse();
                HostLog.Info(string.Format("[内层未命中] raw=({0:0},{1:0}) 内容=({2:0},{3:0},{4:0},{5:0}) 屏=({6:0},{7:0})",
                    m.x, m.y, r.x, r.y, r.width, r.height, _outerView.x, _outerView.y));
            }
            return new Rect(0f, 0f, 0f, 0f);
        }

        private static Rect ListRow(Rect view, int i, float h) { return new Rect(2f, 2f + i * (h + 2f), view.width - 20f, h); }

        /** 外层整体滚动：显式矩形 + GUI.BeginScrollView（与内层列表同一套已验证的画法）。 */
        private void DrawScrolled(int tab) {
            float h = Mathf.Max(120f, ModGui.WinH - 206f);
            Event ev = Event.current;
            Rect view;
            if (ev == null || ev.type == EventType.Layout || ev.type == EventType.Repaint || _bodyView.width <= 1f) {
                view = GUILayoutUtility.GetRect(10f, h, GUILayout.ExpandWidth(true));
                if (view.width > 1f) _bodyView = view;          // 事件帧 GetRect 会返回退化矩形（宽 1），必须缓存
                else if (_bodyView.width > 1f) view = _bodyView;
            } else {
                view = _bodyView;                               // 事件帧沿用 Layout/Repaint 帧的矩形
            }
            _outerView = view;
            float c = Mathf.Max(h, _pageH[tab]);
            try { _scOuter.Handle(view, c); } catch (Exception e) { ReportGuiError("外层拖动", e); }
            Vector2 sp = GUI.BeginScrollView(view, _scOuter.Pos, new Rect(0f, 0f, view.width - 16f, c));
            _scOuter.SetPos(sp);
            _pageX = 2f; _pageY = 0f; _pageW = view.width - 22f;
            try { DrawTabBody(tab); } catch (Exception e) { ReportGuiError("页面 " + tab, e); }
            _pageH[tab] = _pageY + 14f;
            GUI.EndScrollView();
        }

        private void DrawTabBody(int tab) {
            switch (tab) {
                case 0: TabPresets(); break;
                case 1: TabBrowse(); break;
                case 2: TabEnemies(); break;
                case 3: TabTeleport(); break;
                case 4: TabMove(); break;
                case 5: TabPrefs(); break;
                case 6: TabUnlock(); break;
                case 7: TabTrophy(); break;
                case 8: TabLog(); break;
                case 9: TabWeb(); break;
                default: TabSettings(); break;
            }
        }

        private float _guiErrAt;

        private void ReportGuiError(string where, Exception e) {
            if (Time.unscaledTime - _guiErrAt < 2f) return;
            _guiErrAt = Time.unscaledTime;
            string st = e.StackTrace ?? "";
            if (st.Length > 300) st = st.Substring(0, 300);
            HostLog.Warn("界面异常[" + where + "]: " + e.GetType().Name + ": " + e.Message + " ⏎ " + st.Replace('\n', ' '));
        }

        /** 各页内容高度估算（手动偏移滚动需要知道内容长度）。 */
        private float ContentHeightUnused(int tab) {
            switch (tab) {
                case 0: return PSfx.Length * 58f + 30f;
                case 1: return 320f + ListH(0.45f);
                case 2: return 150f + ListH(0.45f);
                case 3: return 200f + ListH(0.45f);
                case 4: return 470f;
                case 5: return 230f + ListH(0.4f);
                case 6: return 570f + ListH(0.3f);
                case 7: return 470f + ListH(0.9f);
                case 8: return 530f;
                default: return 700f;
            }
        }


        private void DrawWindow(int id) {
            // 固定区①：最顶部可见拖动条（不随页面滚动）
            Rect bar = new Rect(0f, 0f, Mathf.Max(120f, ModGui.WinW - 16f), 26f);
            ModGui.Fill(bar, new Color(0.25f, 0.6f, 1f, 0.32f));
            ModGui.Fill(new Rect(bar.x, bar.yMax - 2f, bar.width, 2f), new Color(0.45f, 0.8f, 1f, 0.95f));
            GUI.Label(new Rect(8f, 3f, bar.width - 12f, 20f), "≡ 拖动此处移动窗口");
            GUI.DragWindow(bar);
            GUI.DragWindow(new Rect(0f, -ModGui.TitleH, ModGui.WinW, ModGui.TitleH));
            if (true) {
                GUILayout.Label(string.Format("页面区 view={0:0}x{1:0} 内容高={2:0} 偏移={3:0} 窗口={4:0}x{5:0} 缩放={6:0.##}",
                    Mathf.Max(10f, ModGui.WinW - 30f), Mathf.Max(100f, ModGui.WinH - 200f), _pageH[_tab], _scOuter.Pos.y, ModGui.WinW, ModGui.WinH, ModGui.Scale));
            }
            if (_win.width <= 0f || _lastVW != ModGui.VW || _lastVH != ModGui.VH) {
                _lastVW = ModGui.VW; _lastVH = ModGui.VH;
                Rect fit = ModGui.FitWindow();
                _win.width = fit.width; _win.height = fit.height;
                if (_win.x <= 0f && _win.y <= 0f) { _win.x = fit.x; _win.y = fit.y; }
            }
            // 常驻调速行：不管在哪个标签页都能一键回调速度
            GUILayout.BeginHorizontal();
            GUILayout.Label("时间流速 " + Time.timeScale.ToString("0.##") + "x", GUILayout.Width(100));
            if (GUILayout.Button(Time.timeScale == 0f ? "继续" : "暂停", GUILayout.Width(48))) SetSpeed(Time.timeScale == 0f ? 1f : 0f);
            if (GUILayout.Button("0.3x", GUILayout.Width(44))) SetSpeed(0.3f);
            if (GUILayout.Button("0.5x", GUILayout.Width(44))) SetSpeed(0.5f);
            if (GUILayout.Button("1x", GUILayout.Width(40))) SetSpeed(1f);
            if (GUILayout.Button("2x", GUILayout.Width(40))) SetSpeed(2f);
            if (GUILayout.Button("3x", GUILayout.Width(40))) SetSpeed(3f);
            if (GUILayout.Button("5x", GUILayout.Width(40))) SetSpeed(5f);
            GUILayout.EndHorizontal();
            _tab = GUILayout.Toolbar(_tab, Tabs, GUILayout.Height(30));
            GUILayout.Space(3);
            DrawScrolled(_tab);
            GUILayout.Space(2);
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        // ================= 预设 =================
        private void TabPresets() {
            PLabel("输入框填目标值 → 点「写入」生效（不再自动同步）", 24f);
            for (int i = 0; i < PSfx.Length; i++) {
                float y = NextRow(58f);
                bool locked = IsPinned(BestPath(i));
                GUI.Label(new Rect(_pageX, y, 118f, 26f), PLabels[i]);
                GUI.Label(new Rect(_pageX + 120f, y, 118f, 26f), ReadPathCached(i));
                if (GUI.Button(new Rect(_pageX + 240f, y, 74f, 26f), locked ? "已锁定" : "锁定")) ToggleLock(i);
                if (_edit[i] == null) _edit[i] = "";
                _edit[i] = GUI.TextField(new Rect(_pageX, y + 29f, 150f, 26f), _edit[i]);
                if (GUI.Button(new Rect(_pageX + 154f, y + 29f, 44f, 26f), "-10")) Bump(i, -10);
                if (GUI.Button(new Rect(_pageX + 202f, y + 29f, 36f, 26f), "-1")) Bump(i, -1);
                if (GUI.Button(new Rect(_pageX + 242f, y + 29f, 36f, 26f), "+1")) Bump(i, 1);
                if (GUI.Button(new Rect(_pageX + 282f, y + 29f, 44f, 26f), "+10")) Bump(i, 10);
                if (GUI.Button(new Rect(_pageX + 330f, y + 29f, 62f, 26f), "写入")) ApplyPreset(i, _edit[i]);
            }
            if (PButton("一键回满（全部恢复）", 170f, 30f)) Recover();
            if (PButton("解除全部锁定", 140f, 30f)) { Pin.Items.Clear(); _ctx.Log("已解除全部锁定"); }
            PLabel("锁定项 " + Pin.Items.Count + " 个", 26f);
            for (int i = 0; i < Pin.Items.Count; i++) {
                float y = NextRow(26f);
                Pin.Item it = Pin.Items[i];
                GUI.Label(new Rect(_pageX, y, _pageW - 84f, 24f), it.Path);
                if (GUI.Button(new Rect(_pageX + _pageW - 80f, y, 78f, 24f), "解锁")) { Pin.Remove(it.Path); break; }
            }
        }

        private readonly string[] _valStr = new string[16];
        private float _valAt = -9f;
        private static readonly string[] _best = new string[16];
        private static readonly float[] _bestAt = new float[16];
        private static readonly string[] _err = new string[16];

        /** 依次尝试候选路径，返回第一个能解析成功的；失败时记录原因。 */
        private static string BestPath(int i) {
            string cached = _best[i];
            if (cached != null && Time.unscaledTime - _bestAt[i] < 2f) return cached;
            string lastErr = null;
            for (int k = 0; k < PAlts.Length; k++) {
                string path = PAlts[k] + PSfx[i];
                object v; Type h; string m, err;
                if (Reflect.Resolve(path, out v, out h, out m, out err)) {
                    _best[i] = path; _bestAt[i] = Time.unscaledTime; _err[i] = null;
                    return path;
                }
                lastErr = err;
            }
            _best[i] = null; _bestAt[i] = Time.unscaledTime; _err[i] = lastErr;
            return PAlts[0] + PSfx[i];
        }

        /** 预设值：每 0.4 秒统一刷新一次，OnGUI 两次回调共用，避免每帧重复反射。 */
        private string ReadPathCached(int i) {
            if (Time.unscaledTime - _valAt > 0.4f) {
                for (int k = 0; k < _valStr.Length; k++) _valStr[k] = ReadPath(k);
                _valAt = Time.unscaledTime;
            }
            return _valStr[i];
        }

        private static string ReadPath(int i) {
            try {
                string path = BestPath(i);
                object v; Type h; string m, err;
                if (Reflect.Resolve(path, out v, out h, out m, out err)) return Reflect.Format(v);
                _err[i] = err;
                return "(" + Short(err) + ")";
            } catch (Exception e) { return "(错误)"; }
        }

        private List<Type> _foundCache;
        private string _foundFor;
        private float _foundAt;

        private List<Type> SearchTypes() {
            if (_search.Length < 2) { _foundCache = new List<Type>(); _foundFor = _search; return _foundCache; }
            if (_foundFor == _search && Time.unscaledTime - _foundAt < 0.6f) return _foundCache;
            _foundCache = Reflect.Find(_search, 60);
            _foundFor = _search; _foundAt = Time.unscaledTime;
            return _foundCache;
        }

        private static string Short(string err) {
            if (err == null) return "未找到";
            if (err.StartsWith("无实例")) return "无实例";
            if (err.StartsWith("未找到类型")) return "类型缺失";
            if (err.StartsWith("成员不存在")) return "无此成员";
            return err.Length > 10 ? err.Substring(0, 10) : err;
        }

        private static string ReadPath(string path) {
            try {
                object v; Type h; string m, err;
                if (Reflect.Resolve(path, out v, out h, out m, out err)) return Reflect.Format(v);
                return "(未找到)";
            } catch (Exception e) { return "(错误)"; }
        }

        private static bool IsPinned(string path) {
            for (int i = 0; i < Pin.Items.Count; i++) if (Pin.Items[i].Path == path) return Pin.Items[i].Locked;
            return false;
        }

        private void Bump(int i, int delta) {
            string cur = ReadPathCached(i);
            float f;
            if (!float.TryParse(cur, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { _ctx.Log("当前值不可解析: " + cur); return; }
            _edit[i] = (f + delta).ToString(PIsFloat[i] ? "0.###" : "0", CultureInfo.InvariantCulture);
        }

        private void ApplyPreset(int i, string val) { SetPreset(PKeys[i], val); }

        private void SetPreset(string key, string val) {
            int i = IndexOfKey(key);
            if (i < 0) { _ctx.Log("未知预设: " + key); return; }
            string err;
            bool ok = Reflect.Set(BestPath(i), val, out err);
            _ctx.Log((ok ? "已写入 " : "写入失败 ") + PLabels[i] + " = " + val + (ok ? "" : (" (" + err + ")")));
            Publish();
        }

        private void ToggleLock(int i) {
            string path = BestPath(i);
            if (IsPinned(path)) { _ctx.Log(Pin.Remove(path)); }
            else { _ctx.Log(Pin.Add(path, null, true)); }
            Publish();
        }

        private void ToggleLockKey(string key, bool wantLock) {
            int i = IndexOfKey(key);
            if (i < 0) { _ctx.Log("未知预设: " + key); return; }
            bool locked = IsPinned(BestPath(i));
            if (wantLock && !locked) ToggleLock(i);
            else if (!wantLock && locked) ToggleLock(i);
        }

        private static int IndexOfKey(string key) {
            for (int i = 0; i < PKeys.Length; i++) if (PKeys[i] == key) return i;
            return -1;
        }

        private void Recover() {
            try {
                object attr; Type h; string m, err;
                if (!Reflect.Resolve(P, out attr, out h, out m, out err) || attr == null) { _ctx.Log("找不到 PlayerAttribute: " + err); return; }
                MethodInfo mi = attr.GetType().GetMethod("AllAttributeRecovery");
                if (mi == null) { _ctx.Log("没有 AllAttributeRecovery 方法"); return; }
                mi.Invoke(attr, null);
                _ctx.Log("已回满全部属性（含血量与闪能量）");
            } catch (Exception e) { _ctx.Log("回满失败: " + e.Message); }
            Publish();
        }

        // ================= 浏览 =================
        private void TabBrowse() {
            if (_search == null) _search = "";
            float y = NextRow(30f);
            GUI.Label(new Rect(_pageX, y, 44f, 26f), "搜索");
            string ns = GUI.TextField(new Rect(_pageX + 46f, y, _pageW - 46f, 26f), _search);
            if (ns != _search) { _search = ns; _selType = null; }
            PLabel("直接锁定/写值：填 类型.成员（例：R.Player.Attribute.baseAtk）", 24f);
            if (_pendingPath == null) _pendingPath = "";
            y = NextRow(30f);
            _pendingPath = GUI.TextField(new Rect(_pageX, y, _pageW - 60f, 26f), _pendingPath);
            if (GUI.Button(new Rect(_pageX + _pageW - 56f, y, 54f, 26f), "锁定")) _ctx.Log(Pin.Add(_pendingPath, null, true));
            if (_pendingValue == null) _pendingValue = "";
            y = NextRow(30f);
            GUI.Label(new Rect(_pageX, y, 30f, 26f), "值");
            _pendingValue = GUI.TextField(new Rect(_pageX + 32f, y, _pageW - 92f, 26f), _pendingValue);
            if (GUI.Button(new Rect(_pageX + _pageW - 56f, y, 54f, 26f), "写入")) { string err; bool ok = Reflect.Set(_pendingPath, _pendingValue, out err); _ctx.Log(ok ? "已写入" : ("失败: " + err)); }
            List<Type> found = SearchTypes();
            float th = Mathf.Min(140f, Mathf.Max(60f, found.Count * 26f + 6f));
            Rect tv = InnerRect(th, found.Count * 26f + 6f, _scBrowse);
            _scBrowse.SetPos(GUI.BeginScrollView(tv, _scBrowse.Pos, new Rect(0f, 0f, tv.width - 16f, Mathf.Max(tv.height, found.Count * 26f + 6f))));
            for (int i = 0; i < found.Count; i++) {
                if (GUI.Button(ListRow(tv, i, 24f), found[i].FullName)) _selType = found[i];
            }
            GUI.EndScrollView();
            if (_selType != null) {
                PLabel("── " + _selType.FullName, 26f);
                DrawMembers(_selType);
            }
        }

        private void DrawMembers(Type t) {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            List<string> lines = new List<string>();
            List<string> paths = new List<string>();
            try {
                FieldInfo[] fs = t.GetFields(f);
                for (int i = 0; i < fs.Length && i < 60; i++) {
                    if (fs[i].IsLiteral) continue;
                    string val;
                    try { val = fs[i].IsStatic ? Reflect.Format(fs[i].GetValue(null)) : "(实例)"; } catch (Exception) { val = "?"; }
                    lines.Add(fs[i].Name + " : " + fs[i].FieldType.Name + " = " + val);
                    paths.Add(fs[i].IsStatic ? (t.FullName + "." + fs[i].Name) : null);
                }
                PropertyInfo[] ps = t.GetProperties(f);
                for (int i = 0; i < ps.Length && i < 40; i++) {
                    MethodInfo gm = ps[i].GetGetMethod(true);
                    if (gm == null || gm.GetParameters().Length != 0) continue;
                    string val;
                    try { val = gm.IsStatic ? Reflect.Format(ps[i].GetValue(null, null)) : "(实例)"; } catch (Exception) { val = "?"; }
                    lines.Add(ps[i].Name + " : " + ps[i].PropertyType.Name + " = " + val);
                    paths.Add(gm.IsStatic ? (t.FullName + "." + ps[i].Name) : null);
                }
            } catch (Exception e) { lines.Add("读取失败: " + e.Message); paths.Add(null); }
            float lh = Mathf.Min(150f, Mathf.Max(70f, lines.Count * 24f + 6f));
            Rect lr = InnerRect(lh, lines.Count * 24f + 6f, _scBrowse);
            _scBrowse.SetPos(GUI.BeginScrollView(lr, _scBrowse.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, lines.Count * 24f + 6f))));
            for (int i = 0; i < lines.Count; i++) {
                GUI.Label(new Rect(2f, 2f + i * 24f, lr.width - 86f, 22f), lines[i]);
                if (paths[i] != null && GUI.Button(new Rect(lr.width - 80f, 2f + i * 24f, 74f, 22f), "锁定")) _ctx.Log(Pin.Add(paths[i], null, true));
            }
            GUI.EndScrollView();
        }

        // ================= 敌人 =================
        private List<object> _enemyCache = new List<object>();
        private List<string> _enemyRows = new List<string>();
        private float _enemyAt = -9f;

        private void RefreshEnemies() {
            _enemyCache.Clear(); _enemyRows.Clear();
            try {
                object mgr; Type h; string m, err;
                if (!Reflect.Resolve("R.Enemy", out mgr, out h, out m, out err) || mgr == null) { _enemyRows.Add("(找不到 EnemyManager: " + Short(err) + ")"); return; }
                PropertyInfo pi = mgr.GetType().GetProperty("EnemyAttributes", ANY);
                if (pi == null) { _enemyRows.Add("(EnemyManager 没有 EnemyAttributes)"); return; }
                System.Collections.IEnumerable list = pi.GetValue(mgr, null) as System.Collections.IEnumerable;
                if (list == null) { _enemyRows.Add("(敌人列表为空)"); return; }
                Vector3 me = PlayerPos();
                foreach (object e in list) {
                    if (e == null) continue;
                    string id = Str(e, "id"), lv = Str(e, "level");
                    string hp = Str(e, "currentHp"), mhp = Str(e, "maxHp");
                    string dist = "";
                    try {
                        Component c = e as Component;
                        if (c != null) dist = Vector3.Distance(c.transform.position, me).ToString("0.0") + "m";
                    } catch (Exception) { }
                    _enemyCache.Add(e);
                    _enemyRows.Add((id ?? "?") + "  Lv" + lv + "  HP " + hp + "/" + mhp + "  " + dist);
                }
                if (_enemyRows.Count == 0) _enemyRows.Add("(当前场景没有敌人)");
            } catch (Exception e2) { _enemyRows.Add("读取失败: " + e2.Message); }
        }

        private static Vector3 PlayerPos() {
            try {
                object p; Type h; string m, err;
                if (Reflect.Resolve("R.Player.Transform", out p, out h, out m, out err) && p is Transform) return ((Transform)p).position;
            } catch (Exception) { }
            return Vector3.zero;
        }

        private static string Str(object o, string member) {
            try {
                Type t = o.GetType();
                FieldInfo fi = t.GetField(member, ANY);
                if (fi != null) return Reflect.Format(fi.GetValue(o));
                PropertyInfo pi = t.GetProperty(member, ANY);
                if (pi != null && pi.CanRead) return Reflect.Format(pi.GetValue(o, null));
            } catch (Exception) { }
            return "?";
        }

        private void KillEnemy(object e) {
            try {
                Type t = e.GetType();
                PropertyInfo pi = t.GetProperty("currentHp", ANY);
                if (pi != null && pi.CanWrite) { pi.SetValue(e, 0, null); _ctx.Log("已击杀 " + Str(e, "id")); return; }
                FieldInfo fi = t.GetField("_currentHp", ANY);
                if (fi != null) { fi.SetValue(e, 0); _ctx.Log("已击杀 " + Str(e, "id")); return; }
                _ctx.Log("找不到血量字段");
            } catch (Exception ex) { _ctx.Log("击杀失败: " + ex.Message); }
        }

        /** 读取 EnemyType 枚举（名称本身就是中文名，值为代号）。 */
        private static void EnsureEnemyTypes() {
            if (_etypeNames != null) return;
            try {
                Type t = Reflect.TypeByFullName("EnemyType");
                if (t == null) { _etypeNames = new string[0]; _etypeValues = new int[0]; return; }
                Array vals = Enum.GetValues(t);
                _etypeNames = Enum.GetNames(t);
                _etypeValues = new int[vals.Length];
                for (int i = 0; i < vals.Length; i++) _etypeValues[i] = Convert.ToInt32(vals.GetValue(i));
            } catch (Exception) { _etypeNames = new string[0]; _etypeValues = new int[0]; }
        }

        /** 生成敌人：EnemyManager.Generate(EnemyType, Vector2?, true, false)，位置取玩家前方。 */
        private void SpawnEnemy(int idx) {
            try {
                EnsureEnemyTypes();
                Type t = Reflect.TypeByFullName("EnemyType");
                if (t == null) { _spawnMsg = "找不到 EnemyType"; return; }
                object typeVal = Enum.ToObject(t, _etypeValues[idx]);
                Vector3 p = Vector3.zero;
                try { Transform tr = PlayerTransform(); if (tr != null) p = tr.position; } catch (Exception) { }
                Vector2 spawn = new Vector2(p.x + 2f, p.y + 0.5f);
                object posArg = (Vector2?)spawn;
                bool ok = CallOn("R.Enemy", "Generate", typeVal, posArg, true, false);
                _spawnMsg = ok ? ("已生成 " + _etypeNames[idx] + "（代号 " + _etypeValues[idx] + "）") : "生成失败（详见日志）";
                _ctx.Log(_spawnMsg);
                _enemyAt = -9f;
            } catch (Exception e) { _spawnMsg = "生成异常: " + e.Message; _ctx.Log(_spawnMsg); }
        }

        /** 修改选中敌人的血量：which 0=当前 1=上限。 */
        private void SetEnemyHp(int which) {
            if (_selEnemy < 0 || _selEnemy >= _enemyCache.Count) { _spawnMsg = "先点一行选中敌人"; return; }
            object e = _enemyCache[_selEnemy];
            int v;
            if (!int.TryParse(_hpEdit, out v)) { _spawnMsg = "血量输入无效: " + _hpEdit; return; }
            try {
                Type t = e.GetType();
                string fld = which == 0 ? "_currentHp" : "maxHp";
                PropertyInfo pr = t.GetProperty(which == 0 ? "currentHp" : "maxHp", ANY);
                if (pr != null && pr.CanWrite) { pr.SetValue(e, v, null); }
                else { FieldInfo fi = t.GetField(fld, ANY); if (fi == null) { _spawnMsg = "找不到血量字段"; return; } fi.SetValue(e, v); }
                _spawnMsg = "已设置 " + Str(e, "id") + (which == 0 ? " 当前血量=" : " 上限=") + v;
                _ctx.Log(_spawnMsg);
                _enemyAt = -9f;
            } catch (Exception ex) { _spawnMsg = "改血失败: " + ex.Message; _ctx.Log(_spawnMsg); }
        }

        private void TabEnemies() {
            if (Time.unscaledTime - _enemyAt > 0.5f && !_spawnOpen) { _enemyAt = Time.unscaledTime; RefreshEnemies(); }
            if (_spawnOpen) {
                EnsureEnemyTypes();
                PLabel("生成敌人：选择要生成的敌人（中文名 + 代号），生成在玩家前方", 24f);
                float y = NextRow(32f);
                if (GUI.Button(new Rect(_pageX, y, 80f, 28f), "返回列表")) { _spawnOpen = false; _spawnMsg = ""; return; }
                if (_spawnSearch == null) _spawnSearch = "";
                GUI.Label(new Rect(_pageX + 86f, y, 40f, 26f), "搜索");
                _spawnSearch = GUI.TextField(new Rect(_pageX + 128f, y, _pageW - 128f, 26f), _spawnSearch);
                if (_spawnMsg.Length > 0) PLabel(_spawnMsg, 24f);
                int total = _etypeNames == null ? 0 : _etypeNames.Length;
                List<int> show = new List<int>();
                string q = _spawnSearch.Trim().ToLowerInvariant();
                for (int i = 0; i < total; i++) {
                    string lab = _etypeNames[i] + " (" + _etypeValues[i] + ")";
                    if (q.Length > 0 && lab.ToLowerInvariant().IndexOf(q) < 0) continue;
                    show.Add(i);
                }
                int n = show.Count;
                float lh = Mathf.Min(150f, Mathf.Max(70f, n * 30f + 6f));
                Rect lr = InnerRect(lh, n * 30f + 6f, _scBrowse);
                _scBrowse.SetPos(GUI.BeginScrollView(lr, _scBrowse.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, n * 30f + 6f))));
                for (int r = 0; r < n; r++) {
                    int idx = show[r];
                    GUI.Label(new Rect(4f, 2f + r * 30f, lr.width - 110f, 26f), _etypeNames[idx] + "  （代号 " + _etypeValues[idx] + "）");
                    if (GUI.Button(new Rect(lr.width - 100f, 2f + r * 30f, 92f, 26f), "生成")) SpawnEnemy(idx);
                }
                GUI.EndScrollView();
                return;
            }
            PLabel("当前敌人 " + _enemyRows.Count + " 个（0.5 秒刷新）", 24f);
            float y2 = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y2, 80f, 28f), "刷新")) _enemyAt = -9f;
            if (GUI.Button(new Rect(_pageX + 86f, y2, 130f, 28f), ConfirmLabel("killall", "一键清屏")) && Risky("killall", "一键清屏（击杀全部敌人）")) { CallOn("R.Enemy", "KillAllEnemy"); _enemyAt = -9f; }
            if (GUI.Button(new Rect(_pageX + 222f, y2, 130f, 28f), "生成敌人…")) { _spawnOpen = true; _spawnMsg = ""; }
            int cnt = _enemyRows.Count;
            float lh2 = Mathf.Min(150f, Mathf.Max(70f, cnt * 28f + 6f));
            Rect lr2 = InnerRect(lh2, cnt * 28f + 6f, _scBrowse);
            _scBrowse.SetPos(GUI.BeginScrollView(lr2, _scBrowse.Pos, new Rect(0f, 0f, lr2.width - 16f, Mathf.Max(lr2.height, cnt * 28f + 6f))));
            for (int i = 0; i < cnt; i++) {
                string label = (i == _selEnemy ? "▶ " : "") + _enemyRows[i];
                if (GUI.Button(new Rect(2f, 2f + i * 28f, lr2.width - 100f, 26f), label)) { _selEnemy = i; _hpEdit = Str(_enemyCache[i], "currentHp"); }
                if (i < _enemyCache.Count && GUI.Button(new Rect(lr2.width - 94f, 2f + i * 28f, 86f, 26f), "击杀")) { KillEnemy(_enemyCache[i]); _enemyAt = -9f; }
            }
            if (cnt == 0) GUI.Label(new Rect(2f, 2f, lr2.width - 20f, 24f), "当前场景没有敌人");
            GUI.EndScrollView();
            PLabel(_selEnemy >= 0 && _selEnemy < _enemyCache.Count ? ("选中: " + Str(_enemyCache[_selEnemy], "id") + "  Lv" + Str(_enemyCache[_selEnemy], "level") + "  HP " + Str(_enemyCache[_selEnemy], "currentHp") + "/" + Str(_enemyCache[_selEnemy], "maxHp")) : "点上面一行选中敌人后可改血量", 24f);
            if (_spawnMsg.Length > 0) PLabel(_spawnMsg, 24f);
            float y3 = NextRow(32f);
            GUI.Label(new Rect(_pageX, y3, 70f, 26f), "血量改为");
            if (_hpEdit == null) _hpEdit = "";
            _hpEdit = GUI.TextField(new Rect(_pageX + 72f, y3, 80f, 26f), _hpEdit);
            if (GUI.Button(new Rect(_pageX + 158f, y3, 84f, 26f), "设当前")) SetEnemyHp(0);
            if (GUI.Button(new Rect(_pageX + 248f, y3, 84f, 26f), "设上限")) SetEnemyHp(1);
            if (GUI.Button(new Rect(_pageX + 338f, y3, 84f, 26f), "回满")) { if (_selEnemy >= 0 && _selEnemy < _enemyCache.Count) { _hpEdit = Str(_enemyCache[_selEnemy], "maxHp"); SetEnemyHp(0); } }
        }

        // ================= 传送 =================
        private string _tpSearch = "";
        private List<int> _tpIndex = new List<int>();
        private List<string> _tpLabel = new List<string>();
        private string _tpFor = null;
        private static Dictionary<int, string> _areaNameCache;
        private static Dictionary<int, string> _sceneNameCache;

        private static Dictionary<int, string> AreaNames() {
            if (_areaNameCache != null) return _areaNameCache;
            _areaNameCache = new Dictionary<int, string>();
            for (int i = 0; i < AreaTable.Index.Length; i++) {
                string nm = AreaTable.Name[i];
                if (!string.IsNullOrEmpty(nm)) _areaNameCache[AreaTable.Index[i]] = nm;
            }
            return _areaNameCache;
        }

        private static Dictionary<int, string> SceneNames2() {
            if (_sceneNameCache != null) return _sceneNameCache;
            _sceneNameCache = new Dictionary<int, string>();
            for (int i = 0; i < AreaTable.Index.Length; i++) _sceneNameCache[AreaTable.Index[i]] = AreaTable.Scene[i];
            return _sceneNameCache;
        }

        private void BuildTeleportList() {
            _tpIndex.Clear(); _tpLabel.Clear();
            Dictionary<int, string> names = AreaNames();
            Dictionary<int, string> scenes = SceneNames2();
            string q = (_tpSearch ?? "").Trim().ToLowerInvariant();
            int count = SceneUtil.Count;
            for (int i = 0; i < count; i++) {
                string scene;
                if (!scenes.TryGetValue(i, out scene) || string.IsNullOrEmpty(scene)) scene = SceneUtil.NameAt(i);
                if (scene == null) continue;
                string zh;
                if (!names.TryGetValue(i, out zh) || string.IsNullOrEmpty(zh)) zh = scene;
                string label = "#" + i + "  " + zh + "   (" + scene + ")";
                if (q.Length > 0 && label.ToLowerInvariant().IndexOf(q) < 0) continue;
                _tpIndex.Add(i); _tpLabel.Add(label);
            }
            _tpFor = _tpSearch;
        }

        private void TabTeleport() {
            float y = NextRow(30f);
            GUI.Label(new Rect(_pageX, y, 70f, 26f), "搜索区域");
            if (_tpSearch == null) _tpSearch = "";
            string q = GUI.TextField(new Rect(_pageX + 72f, y, _pageW - 72f, 26f), _tpSearch);
            if (q != _tpSearch) _tpSearch = q;
            if (_tpFor != _tpSearch) BuildTeleportList();
            y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 150f, 28f), ConfirmLabel("sceneReload", "重载当前场景")) && Risky("sceneReload", "重载当前场景")) SceneUtil.Load(SceneUtil.Current);
            PLabel("当前: " + SceneUtil.Current + "　共 " + SceneUtil.Count + " 个区域", 24f);
            int n = _tpLabel.Count;
            float lh = Mathf.Min(150f, Mathf.Max(70f, n * 30f + 6f));
            Rect lr = InnerRect(lh, n * 30f + 6f, _scScene);
            _scScene.SetPos(GUI.BeginScrollView(lr, _scScene.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, n * 30f + 6f))));
            for (int i = 0; i < n; i++) {
                string tkey = "tp" + _tpIndex[i];
                if (GUI.Button(new Rect(2f, 2f + i * 30f, lr.width - 20f, 28f), ConfirmLabel(tkey, _tpLabel[i]))) {
                    if (Risky(tkey, "传送到 " + _tpLabel[i])) { SceneUtil.LoadIndex(_tpIndex[i]); _ctx.Log("已传送到 " + _tpLabel[i]); }
                }
            }
            if (n == 0) GUI.Label(new Rect(2f, 2f, lr.width - 20f, 24f), "没有匹配的区域");
            GUI.EndScrollView();
        }

        // ================= 强制移动 =================
        private void TabMove() {
            PLabel("强制移动：长按方向键持续位移（不受暂停/变速影响）", 24f);
            Vector3 p = Vector3.zero;
            try { Transform tr = PlayerTransform(); if (tr != null) p = tr.position; } catch (Exception) { }
            PLabel(string.Format("当前位置 ({0:0.0}, {1:0.0})", p.x, p.y), 24f);
            float y = NextRow(32f);
            GUI.Label(new Rect(_pageX, y, 60f, 26f), "速度 " + _moveSpeed.ToString("0.#"));
            _moveSpeed = GUI.HorizontalSlider(new Rect(_pageX + 64f, y + 8f, _pageW - 190f, 20f), _moveSpeed, 1f, 30f);
            if (GUI.Button(new Rect(_pageX + _pageW - 120f, y, 44f, 26f), "慢2")) _moveSpeed = 2f;
            if (GUI.Button(new Rect(_pageX + _pageW - 72f, y, 44f, 26f), "中6")) _moveSpeed = 6f;
            if (GUI.Button(new Rect(_pageX + _pageW - 24f, y, 24f, 26f), "15")) _moveSpeed = 15f;
            Vector2 d = Vector2.zero;
            float cx = _pageX + _pageW * 0.5f;
            float by = NextRow(60f) + 4f;
            if (GUI.RepeatButton(new Rect(cx - 45f, by, 90f, 52f), "↑")) d.y += 1f;
            by = NextRow(58f);
            if (GUI.RepeatButton(new Rect(cx - 143f, by, 90f, 52f), "←")) d.x -= 1f;
            if (GUI.RepeatButton(new Rect(cx - 45f, by, 90f, 52f), "■")) d = Vector2.zero;
            if (GUI.RepeatButton(new Rect(cx + 53f, by, 90f, 52f), "→")) d.x += 1f;
            by = NextRow(58f);
            if (GUI.RepeatButton(new Rect(cx - 45f, by, 90f, 52f), "↓")) d.y -= 1f;
            _moveDir = d;
            PLabel("■ 为停止；直接改 Transform（会穿墙，调试用）", 24f);
        }

        private static Transform PlayerTransform() {
            try {
                object t; Type h; string m, err;
                if (Reflect.Resolve("R.Player.Transform", out t, out h, out m, out err)) return t as Transform;
            } catch (Exception) { }
            return null;
        }

        // ================= 存档 =================
        private void TabPrefs() {
            PLabel("存档目录: " + PrefsUtil.DataDir, 24f);
            float y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 100f, 28f), "重新读取")) { _prefs = PrefsUtil.Load(); _prefsMsg = "已读取 " + (_prefs == null ? 0 : _prefs.Count) + " 项"; }
            if (GUI.Button(new Rect(_pageX + 106f, y, 190f, 28f), "备份到 /sdcard/ICEYBackup")) _prefsMsg = PrefsUtil.Backup();
            if (_prefsMsg != null && _prefsMsg.Length > 0) PLabel(_prefsMsg, 24f);
            int n = _prefs == null ? 0 : _prefs.Count;
            PLabel("共 " + n + " 项，改完点「写」即存", 24f);
            float lh = Mathf.Min(150f, Mathf.Max(70f, n * 28f + 6f));
            Rect lr = InnerRect(lh, n * 28f + 6f, _scBrowse);
            _scBrowse.SetPos(GUI.BeginScrollView(lr, _scBrowse.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, n * 28f + 6f))));
            for (int i = 0; i < n; i++) {
                PrefsUtil.Pref pr = _prefs[i];
                GUI.Label(new Rect(2f, 2f + i * 28f, 150f, 26f), pr.Key);
                pr.Value = GUI.TextField(new Rect(154f, 2f + i * 28f, lr.width - 216f, 26f), pr.Value);
                if (GUI.Button(new Rect(lr.width - 56f, 2f + i * 28f, 50f, 26f), "写")) _prefsMsg = PrefsUtil.Apply(pr);
            }
            GUI.EndScrollView();
        }

        // ================= 解锁（通用调用） =================
        private static List<string> _unlockSigs;
        private static List<MethodInfo> _unlockMethods;
        private static List<Type> _unlockOwners;
        private static float _unlockAt;
        private int _selMethod = -1;
        private string _argText = "";
        private bool _confirmReset;
        private float _confirmAt;

        private const BindingFlags ANY = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private static MethodInfo Pick(Type t, string name, object[] args) {
            MethodInfo[] ms;
            try { ms = t.GetMethods(ANY); } catch (Exception) { return null; }
            for (int i = 0; i < ms.Length; i++) {
                MethodInfo m = ms[i];
                if (m.Name != name) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                bool ok = true;
                for (int j = 0; j < ps.Length; j++) {
                    if (args[j] == null) continue;
                    if (ps[j].ParameterType == typeof(object)) continue;
                    if (!ps[j].ParameterType.IsInstanceOfType(args[j])) { ok = false; break; }
                }
                if (ok) return m;
            }
            return null;
        }

        private static string ErrMsg(Exception e) {
            Exception inner = e.InnerException;
            return inner != null ? (inner.GetType().Name + ": " + inner.Message) : (e.GetType().Name + ": " + e.Message);
        }

        /** 在某个类型上调用静态/实例方法。 */
        private bool CallStatic(string typeName, string method, params object[] args) {
            Type t = Reflect.TypeByFullName(typeName);
            if (t == null) { _ctx.Log("找不到类型 " + typeName); return false; }
            MethodInfo m = Pick(t, method, args);
            if (m == null) { _ctx.Log("找不到方法 " + typeName + "." + method); return false; }
            return InvokeOn(t, m, args);
        }

        /** 在路径解析出的对象上调用方法（如 R.Trophy.AwardAllTrophies / R.GameData.Enhancement.SetAllTo）。 */
        private bool CallOn(string path, string method, params object[] args) {
            object obj; Type holder; string member, err;
            if (!Reflect.Resolve(path, out obj, out holder, out member, out err) || obj == null) {
                _ctx.Log("定位失败 " + path + " :: " + err);
                return false;
            }
            MethodInfo m = Pick(obj.GetType(), method, args);
            if (m == null) { _ctx.Log("找不到方法 " + obj.GetType().Name + "." + method); return false; }
            return InvokeOn(obj.GetType(), m, args, obj);
        }

        private bool InvokeOn(Type t, MethodInfo m, object[] args) { return InvokeOn(t, m, args, null); }

        private bool InvokeOn(Type t, MethodInfo m, object[] args, object target) {
            try {
                if (!m.IsStatic && target == null) target = Reflect.Singleton(t);
                if (!m.IsStatic && target == null) { _ctx.Log("无实例: " + t.Name); return false; }
                object ret = m.Invoke(m.IsStatic ? null : target, args);
                if (ret is System.Collections.IEnumerator) { try { _ctx.Host.StartCoroutine((System.Collections.IEnumerator)ret); _ctx.Log("已启动协程 " + m.Name); } catch (Exception) { } }
                _ctx.Log("OK " + t.Name + "." + m.Name + (ret == null ? "" : (" → " + Reflect.Format(ret))));
                return true;
            } catch (Exception e) { _ctx.Log("调用失败 " + m.Name + " :: " + ErrMsg(e)); return false; }
        }

        private bool SetPath(string path, string value) {
            string err;
            bool ok = Reflect.Set(path, value, out err);
            _ctx.Log((ok ? "已写入 " : "写入失败 ") + path + " = " + value + (ok ? "" : (" :: " + err)));
            return ok;
        }

        /** 落盘：先写 PlayerPrefs 区块，再存 GameData。 */
        private void SaveGame() {
            CallOn("PlayerDataMgr.Instance", "PersistSectionData");
            CallOn("R.GameData", "Save", true);
        }

        private void EnsureUnlockList() {
            if (_unlockSigs != null && Time.unscaledTime - _unlockAt < 10f) return;
            _unlockAt = Time.unscaledTime;
            _unlockSigs = new List<string>();
            _unlockMethods = new List<MethodInfo>();
            _unlockOwners = new List<Type>();
            string[] keys = { "Unlock", "Achievement", "Trophy", "Chip", "Enhance", "Equipment", "Money", "Coin", "Recover", "Invincible", "Cheat", "Difficulty" };
            List<Type> seen = new List<Type>();
            for (int k = 0; k < keys.Length && _unlockSigs.Count < 60; k++) {
                List<Type> ts = Reflect.Find(keys[k], 60);
                for (int i = 0; i < ts.Count && _unlockSigs.Count < 60; i++) {
                    if (seen.Contains(ts[i])) continue;
                    seen.Add(ts[i]);
                    MethodInfo[] ms;
                    try { ms = ts[i].GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly); }
                    catch (Exception) { continue; }
                    for (int j = 0; j < ms.Length && _unlockSigs.Count < 60; j++) {
                        MethodInfo m = ms[j];
                        if (m.GetParameters().Length > 2) continue;
                        string n = m.Name;
                        if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") || n.StartsWith("remove_")) continue;
                        bool hit = false;
                        for (int q = 0; q < keys.Length && !hit; q++) if (n.ToLowerInvariant().IndexOf(keys[q].ToLowerInvariant()) >= 0) hit = true;
                        if (!hit) continue;
                        _unlockSigs.Add((m.IsStatic ? "static " : "") + ts[i].Name + "." + n + "(" + ParamSig(m) + ")");
                        _unlockMethods.Add(m); _unlockOwners.Add(ts[i]);
                    }
                }
            }
            HostLog.Info("解锁页候选方法 " + _unlockSigs.Count + " 个（已缓存）");
        }

        private void TabUnlock() {
            PLabel("一键操作（改完点「保存存档」才会落盘）", 24f);
            float y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 140f, 28f), ConfirmLabel("enh3", "技能/强化全满(3)")) && Risky("enh3", "把全部技能/强化设为满级")) CallOn("R.GameData.Enhancement", "SetAllTo", 3);
            if (GUI.Button(new Rect(_pageX + 146f, y, 80f, 28f), "全 2 级")) CallOn("R.GameData.Enhancement", "SetAllTo", 2);
            if (GUI.Button(new Rect(_pageX + 232f, y, 80f, 28f), "全 1 级")) CallOn("R.GameData.Enhancement", "SetAllTo", 1);
            if (GUI.Button(new Rect(_pageX + 318f, y, 104f, 28f), "恢复默认强化")) CallOn("R.GameData.Enhancement", "SetToDefault");
            y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 114f, 28f), "金钱 99999")) SetPath("R.GameData.Equipment.CoinNum", "99999");
            if (GUI.Button(new Rect(_pageX + 120f, y, 150f, 28f), "奖杯与成就 →「奖杯」页")) { _tab = 7; }
            y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 110f, 28f), "金钱 +10000")) SetPath("R.GameData.Equipment.CoinNum", (ReadNum("R.GameData.Equipment.CoinNum") + 10000).ToString("0"));
            if (GUI.Button(new Rect(_pageX + 116f, y, 70f, 28f), "难度 0")) SetPath("R.GameData.Difficulty", "0");
            if (GUI.Button(new Rect(_pageX + 190f, y, 70f, 28f), "难度 1")) SetPath("R.GameData.Difficulty", "1");
            if (GUI.Button(new Rect(_pageX + 264f, y, 70f, 28f), "难度 2")) SetPath("R.GameData.Difficulty", "2");
            y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 100f, 28f), "保存存档")) SaveGame();
            if (GUI.Button(new Rect(_pageX + 106f, y, 100f, 28f), "显示进度")) {
                HostLog.Info("成就数 " + ReadPathStr("SettingData.AchievementInfo") + " / 强化 " + ReadPathStr("R.GameData.Enhancement.Attack")
                    + " / 金钱 " + ReadPathStr("R.GameData.Equipment.CoinNum") + " / 难度 " + ReadPathStr("R.GameData.Difficulty")
                    + " / 场景 " + ReadPathStr("R.GameData.SceneName"));
            }
            if (GUI.Button(new Rect(_pageX + 212f, y, 120f, 28f), _confirmKey == "reset" ? "确认重置存档?" : "重置存档(二次确认)")) {
                if (Confirm("reset", "重置存档")) CallStatic("R", "SaveReset");
            }
            PLabel("关键词搜索（选中后可带参数调用）", 24f);
            EnsureUnlockList();
            int n = _unlockSigs.Count;
            float lh = Mathf.Min(150f, Mathf.Max(70f, n * 28f + 6f));
            Rect lr = InnerRect(lh, n * 28f + 6f, _scBrowse);
            _scBrowse.SetPos(GUI.BeginScrollView(lr, _scBrowse.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, n * 28f + 6f))));
            for (int i = 0; i < n; i++) {
                bool sel = i == _selMethod;
                if (GUI.Button(new Rect(2f, 2f + i * 28f, lr.width - 20f, 26f), (sel ? "▶ " : "") + _unlockSigs[i])) { _selMethod = i; _argText = DefaultArgs(_unlockMethods[i]); }
            }
            GUI.EndScrollView();
            y = NextRow(32f);
            GUI.Label(new Rect(_pageX, y, 44f, 26f), "参数");
            if (_argText == null) _argText = "";
            _argText = GUI.TextField(new Rect(_pageX + 46f, y, _pageW - 150f, 26f), _argText);
            if (GUI.Button(new Rect(_pageX + _pageW - 98f, y, 96f, 26f), "调用选中")) {
                if (_selMethod >= 0) InvokeWithText(_unlockOwners[_selMethod], _unlockMethods[_selMethod], _argText);
                else _ctx.Log("先在列表里选中一条");
            }
        }

        private static string DefaultArgs(MethodInfo m) {
            ParameterInfo[] ps = m.GetParameters();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(','); sb.Append(DefaultArg(ps[i].ParameterType)); }
            return sb.ToString();
        }

        private static string DefaultArg(Type t) {
            if (t == typeof(bool)) return "true";
            if (t == typeof(int) || t == typeof(long)) return "1";
            if (t == typeof(float) || t == typeof(double)) return "1";
            if (t == typeof(string)) return "";
            return "0";
        }

        private void InvokeWithText(Type t, MethodInfo m, string text) {
            ParameterInfo[] ps = m.GetParameters();
            object[] args = new object[ps.Length];
            string[] parts = (text ?? "").Split(',');
            for (int i = 0; i < ps.Length; i++) {
                string v = i < parts.Length ? parts[i].Trim() : DefaultArg(ps[i].ParameterType);
                args[i] = Reflect.ParseValue(v, ps[i].ParameterType);
            }
            InvokeOn(t, m, args);
        }

        private float ReadNum(string path) {
            object v; Type h; string m, err;
            if (!Reflect.Resolve(path, out v, out h, out m, out err)) return 0f;
            float f;
            if (v is bool) return ((bool)v) ? 1f : 0f;
            if (float.TryParse(Reflect.Format(v), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return f;
            return 0f;
        }

        private static string ReadPathStr(string path) {
            try {
                object v; Type h; string m, err;
                if (Reflect.Resolve(path, out v, out h, out m, out err)) return Reflect.Format(v);
                return "(" + Short(err) + ")";
            } catch (Exception) { return "(错误)"; }
        }

        private static string ParamSig(MethodInfo m) {
            ParameterInfo[] ps = m.GetParameters();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(ps[i].ParameterType.Name); }
            return sb.ToString();
        }

        private void InvokeMethod(Type t, MethodInfo m) {
            try {
                object target = m.IsStatic ? null : Reflect.Singleton(t);
                if (!m.IsStatic && target == null) { _ctx.Log("无实例可调用 " + t.Name + "." + m.Name); return; }
                ParameterInfo[] ps = m.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) {
                    Type pt = ps[i].ParameterType;
                    if (pt == typeof(bool)) args[i] = true;
                    else if (pt == typeof(int)) args[i] = 1;
                    else if (pt == typeof(float)) args[i] = 1f;
                    else if (pt == typeof(string)) args[i] = "";
                    else args[i] = null;
                }
                object r = m.Invoke(target, args);
                _ctx.Log("调用 " + t.Name + "." + m.Name + " → " + Reflect.Format(r));
            } catch (Exception e) { _ctx.Log("调用失败 " + m.Name + ": " + e.Message); }
        }

        // ================= 设置 =================
        private static string DebugRoot() { return "/sdcard/" + IceyModHost.ModHostBehaviour.ModRootName(); }
        private static string DebugFile { get { return DebugRoot() + "/debug_mode"; } }
        private static string DebugFileLegacy() {
            if (IceyModHost.ModHostBehaviour.IsMainPkg()) return "/storage/emulated/0/ICEYMods/debug_mode";
            return null;
        }

        private static string FindDebugFile() {
            try { if (System.IO.File.Exists(DebugFile)) return DebugFile; } catch (Exception) { }
            string legacy = DebugFileLegacy();
            try { if (legacy != null && System.IO.File.Exists(legacy)) return legacy; } catch (Exception) { }
            return null;
        }

        private static string DebugFileState() {
            string f = FindDebugFile();
            if (f == null) return "关（无文件）";
            string t = "";
            try { t = System.IO.File.ReadAllText(f).Trim(); } catch (Exception) { }
            bool on = (t.Length == 0 || t == "1" || t.ToLowerInvariant() == "true" || t.ToLowerInvariant() == "on" || t.ToLowerInvariant() == "yes");
            return (on ? "开" : "关") + "（" + f + (t.Length > 0 ? " 内容=" + t : " 空文件") + "）";
        }

        /** 读注入桩的缓存值 → 得知本次进程里游戏实际按哪种模式在跑。 */
        private static string RuntimeDebugState() {
            try {
                Type t = Reflect.TypeByFullName("IceyMod.Bootstrap");
                if (t == null) return "未知（桩未找到）";
                MethodInfo m = t.GetMethod("DebugFlag", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) return "未知（桩无 DebugFlag）";
                object v = m.Invoke(null, null);
                bool on = v is bool && (bool)v;
                return on ? "调试模式（已生效）" : "发布模式";
            } catch (Exception e) { return "读取失败: " + e.Message; }
        }

        /** 打开游戏自带调试面板：必要时先按 Debug 构建的做法实例化 Prefab/Core → DebugGUI。 */
        private void OpenNativeDebug() {
            object ui = null; Type h; string m, err;
            bool hasUi = Reflect.Resolve("UIDebug.Instance", out ui, out h, out m, out err) && ui != null;
            object svc = null;
            try { object s2; if (Reflect.Resolve("SRDebug.Instance", out s2, out h, out m, out err)) svc = s2; } catch (Exception) { }
            _ctx.Log("原版调试：UIDebug 实例=" + (hasUi ? "有" : "无") + "，SRDebug 服务=" + (svc != null ? "有" : "无"));
            if (!hasUi && svc == null) SpawnDebugGui();
            if (CallOn("UIDebug.Instance", "Open")) _ctx.Log("已调用 UIDebug.Instance.Open()（游戏自带调试按钮表）");
            try {
                object s3;
                if (Reflect.Resolve("SRDebug.Instance", out s3, out h, out m, out err) && s3 != null) {
                    MethodInfo mm = Pick(s3.GetType(), "ShowDebugPanel", new object[0]);
                    if (mm == null) {
                        Type dt = Reflect.TypeByFullName("SRDebugger.DefaultTabs");
                        object opt = dt != null ? Enum.ToObject(dt, 1) : null;   // Options 页
                        if (opt != null) mm = Pick(s3.GetType(), "ShowDebugPanel", new object[] { opt });
                    }
                    if (mm != null) { mm.Invoke(s3, mm.GetParameters().Length == 0 ? null : new object[] { Enum.ToObject(Reflect.TypeByFullName("SRDebugger.DefaultTabs"), 1) }); _ctx.Log("已调用 SRDebug.Instance.ShowDebugPanel()"); }
                    else _ctx.Log("SRDebugger 没有 ShowDebugPanel 接口");
                }
            } catch (Exception e) { _ctx.Log("SRDebug 打开失败: " + e.Message); }
        }

        /** 实例化 DebugGUI 预制体（等价于调试构建里 GameSceneInit.Start 做的事）。 */
        private void SpawnDebugGui() {
            try {
                Type assetT = Reflect.TypeByFullName("Asset");
                if (assetT == null) assetT = Reflect.TypeByFullName("Icey.Asset");
                if (assetT == null) { _ctx.Log("找不到 Asset 类型"); return; }
                MethodInfo lm = null;
                foreach (MethodInfo cand in assetT.GetMethods(ANY)) {
                    if (cand.Name != "LoadFromResources" || !cand.IsGenericMethodDefinition) continue;
                    if (cand.GetParameters().Length == 2) { lm = cand; break; }
                }
                if (lm == null) { _ctx.Log("找不到 Asset.LoadFromResources"); return; }
                object go = lm.MakeGenericMethod(typeof(GameObject)).Invoke(null, new object[] { "Prefab/Core", "DebugGUI" });
                if (go == null) { _ctx.Log("Prefab/Core 里没有 DebugGUI 资源"); return; }
                GameObject inst = UnityEngine.Object.Instantiate((GameObject)go);
                UnityEngine.Object.DontDestroyOnLoad(inst);
                _ctx.Log("已实例化原版调试面板宿主 DebugGUI");
            } catch (Exception e) { _ctx.Log("实例化 DebugGUI 失败: " + e.Message); }
        }

        /** 写剧情 mod 的进度文件：pending=1/done=0 即“武装”，下次在开始界面点继续时进入。 */
        private void ArmStory(bool arm) {
            try {
                string f = System.IO.Path.Combine(System.IO.Path.Combine("/sdcard/" + IceyModHost.ModHostBehaviour.ModRootName(), "story"), "progress.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
                System.IO.File.WriteAllText(f, arm ? "pending=1\ndone=0\n" : "pending=0\ndone=0\n");
                _ctx.Log(arm ? ("已武装序章 → " + f + "（剧情模块 2 秒内自动重载；回开始界面点“开始/继续”即进入）")
                             : ("已解除武装/重置 → " + f));
            } catch (Exception e) { _ctx.Log("写进度失败: " + e.Message); }
        }

        private void ToggleDebugFile() {
            try {
                string f = FindDebugFile();
                if (f != null) {
                    System.IO.File.Delete(f);
                    _ctx.Log("已删除 " + f + " → 重启后进入发布模式");
                } else {
                    System.IO.File.WriteAllText(DebugFile, "1");
                    _ctx.Log("已创建 " + DebugFile + " → 重启后进入调试模式");
                }
            } catch (Exception e) { _ctx.Log("写文件失败: " + e.Message); }
        }


        private void TabSettings() {
            PLabel("调试模式（Debug 构建模拟）", 26f);
            PLabel("文件: " + DebugFileState(), 24f);
            PLabel("本次运行: " + RuntimeDebugState(), 24f);
            float y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 230f, 28f), ConfirmLabel("dbgmode", "切换调试模式（写文件，需重启）")) && Risky("dbgmode", "切换调试模式并写入文件")) ToggleDebugFile();
            if (GUI.Button(new Rect(_pageX + 236f, y, 180f, 28f), "打开原版调试页面")) OpenNativeDebug();
            y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 250f, 28f), ConfirmLabel("armstory", "立刻演绎序章（含成就）")) && Risky("armstory", "武装《艾希：续》序章")) ArmStory(true);
            if (GUI.Button(new Rect(_pageX + 256f, y, 160f, 28f), "重置序章进度")) ArmStory(false);
            PLabel("开=新建 /sdcard/ICEYMods/debug_mode；关=删除该文件；重启生效", 24f);
            PLabel("行为设置", 26f);
            bool fz = GUI.Toggle(new Rect(_pageX, NextRow(30f), _pageW, 26f), _freezeOnOpen, " 打开菜单时冻结游戏时间（关闭窗口自动恢复）");
            if (fz != _freezeOnOpen) { _freezeOnOpen = fz; _ctx.Log(fz ? "已开启：打开菜单冻结游戏" : "已关闭冻结"); }
            bool inv = GUI.Toggle(new Rect(_pageX, NextRow(30f), _pageW, 26f), ModGui.InvertScroll, " 滚动反向（列表跟随手指 / 相反）");
            if (inv != ModGui.InvertScroll) ModGui.InvertScroll = inv;
            PLabel("输入屏蔽（防穿透）：" + (InputShield.Blocked ? "生效中" : "空闲"), 24f);
            PLabel("画面与性能", 26f);
            y = NextRow(32f);
            GUI.Label(new Rect(_pageX, y, 64f, 26f), "目标帧率");
            if (GUI.Button(new Rect(_pageX + 66f, y, 46f, 26f), "30")) Application.targetFrameRate = 30;
            if (GUI.Button(new Rect(_pageX + 116f, y, 46f, 26f), "60")) Application.targetFrameRate = 60;
            if (GUI.Button(new Rect(_pageX + 166f, y, 46f, 26f), "90")) Application.targetFrameRate = 90;
            if (GUI.Button(new Rect(_pageX + 216f, y, 52f, 26f), "120")) Application.targetFrameRate = 120;
            if (GUI.Button(new Rect(_pageX + 272f, y, 60f, 26f), "不限")) Application.targetFrameRate = -1;
            GUI.Label(new Rect(_pageX + 338f, y, 80f, 26f), "当前 " + Application.targetFrameRate);
            y = NextRow(32f);
            GUI.Label(new Rect(_pageX, y, 90f, 26f), "界面缩放");
            if (_scaleText == null) _scaleText = ModGui.Scale.ToString("0.##");
            _scaleText = GUI.TextField(new Rect(_pageX + 94f, y, 90f, 26f), _scaleText);
            if (GUI.Button(new Rect(_pageX + 190f, y, 80f, 26f), "应用")) {
                float v;
                if (float.TryParse(_scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) {
                    ModGui.Scale = Mathf.Clamp(v, 1f, 6f);
                    _ctx.Log("界面缩放已应用: " + ModGui.Scale.ToString("0.##"));
                } else _ctx.Log("界面缩放输入无效: " + _scaleText);
            }
            GUI.Label(new Rect(_pageX + 276f, y, 150f, 26f), "当前 " + ModGui.Scale.ToString("0.##") + "（1~6）");
            y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 160f, 28f), "切换全屏 / 窗口")) { try { Screen.fullScreen = !Screen.fullScreen; } catch (Exception) { } }
            if (GUI.Button(new Rect(_pageX + 166f, y, 150f, 28f), "重新校准点击坐标")) { ModGui.EvScale = 1f; ModGui.AutoCalibrate(); _ctx.Log("已重新校准：EvScale=" + ModGui.EvScale); }
            PLabel("调试信息", 26f);
            PLabel("最近触摸: " + ModGui.LastTouch, 24f);
            PLabel(string.Format("屏幕 {0}x{1}　虚拟画布 {2:0}x{3:0}　本次除数 {4:0.##}", Screen.width, Screen.height, ModGui.VW, ModGui.VH, ModGui.EvScale), 24f);
            PLabel(string.Format("页面区 {0}x{1}　内容高 {2:0}　偏移 {3:0}", _outerView.width, _outerView.height, _pageH[_tab], _scOuter.Pos.y), 24f);
            if (PButton("显示模块路由", 150f, 28f)) HostLog.Info("模块路由: " + WebBridge.LocalUrl());
        }

        // ================= 奖杯 =================
        private static int TrophyMax() {
            try {
                Type t = Reflect.TypeByFullName("AchievementManager");
                if (t != null) { FieldInfo f = t.GetField("TrophyMaxCount", ANY); if (f != null) return Convert.ToInt32(f.GetValue(null)); }
            } catch (Exception) { }
            return 34;
        }

        private static object TrophyMgr() {
            try {
                object mgr; Type h; string m, err;
                if (Reflect.Resolve("AchievementManager.Instance", out mgr, out h, out m, out err) && mgr != null) return mgr;
            } catch (Exception) { }
            try { Type t = Reflect.TypeByFullName("AchievementManager"); if (t != null) return Reflect.Singleton(t); } catch (Exception) { }
            return null;
        }

        private static string TrophyName(int id) {
            string cached;
            if (_trophyNames.TryGetValue(id, out cached)) return cached;
            string name = null;
            try {
                object mgr = TrophyMgr();
                if (mgr != null) {
                    MethodInfo g = mgr.GetType().GetMethod("GetAchievementInfo", ANY);
                    if (g != null) {
                        object info = g.Invoke(mgr, new object[] { id });
                        if (info != null) {
                            FieldInfo nf = info.GetType().GetField("Name", ANY);
                            if (nf != null) name = nf.GetValue(info) as string;
                            FieldInfo df = info.GetType().GetField("Detail", ANY);
                            if (df != null) _trophyDetails[id] = df.GetValue(info) as string;
                        }
                    }
                }
            } catch (Exception) { }
            if (string.IsNullOrEmpty(name)) name = "(未找到 #" + id + ")";
            _trophyNames[id] = name;
            return name;
        }

        private static string TrophyDetail(int id) {
            string d;
            return _trophyDetails.TryGetValue(id, out d) ? d : "";
        }

        private static bool TrophyUnlocked(int id) {
            try {
                object mgr = TrophyMgr();
                if (mgr != null) {
                    MethodInfo g = mgr.GetType().GetMethod("GetAchievementUnlockState", ANY);
                    if (g != null) return (bool)g.Invoke(mgr, new object[] { id });
                }
            } catch (Exception) { }
            return false;
        }

        private bool AwardTrophy(int id) {
            try {
                object mgr = TrophyMgr();
                if (mgr == null) { _trophyMsg = "找不到 AchievementManager"; return false; }
                MethodInfo m = mgr.GetType().GetMethod("AwardAchievement", ANY);
                if (m == null) { _trophyMsg = "找不到 AwardAchievement"; return false; }
                object r = m.Invoke(mgr, new object[] { id });
                bool ok = r is bool && (bool)r;
                _trophyMsg = ok ? ("已获得奖杯 #" + id + " " + TrophyName(id)) : ("#id " + id + " 已经是已解锁状态");
                _ctx.Log(_trophyMsg);
                _trophyAt = -9f;
                return ok;
            } catch (Exception e) { _trophyMsg = "授予失败: " + e.Message; _ctx.Log(_trophyMsg); return false; }
        }

        private void AwardAllTrophies() {
            try {
                object mgr = TrophyMgr();
                if (mgr == null) { _trophyMsg = "找不到 AchievementManager"; return; }
                MethodInfo m = mgr.GetType().GetMethod("AwardAll", ANY);
                if (m == null) { _trophyMsg = "找不到 AwardAll"; return; }
                m.Invoke(mgr, null);
                _trophyMsg = "已请求解锁全部奖杯（1.." + (TrophyMax() - 1) + "）";
                _ctx.Log(_trophyMsg);
                _trophyAt = -9f;
            } catch (Exception e) { _trophyMsg = "批量失败: " + e.Message; }
        }

        /** 打开游戏自带的奖杯界面（UITrophyController.Open）。 */
        private void OpenGameTrophyUi() {
            try {
                Type t = Reflect.TypeByFullName("UITrophyController");
                if (t == null) { _trophyMsg = "找不到 UITrophyController"; return; }
                UnityEngine.Object[] arr = Resources.FindObjectsOfTypeAll(t);
                if (arr == null || arr.Length == 0) { _trophyMsg = "当前场景没有奖杯界面实例"; return; }
                MethodInfo op = t.GetMethod("Open", ANY);
                if (op == null) { _trophyMsg = "UITrophyController 没有 Open()"; return; }
                op.Invoke(arr[0], null);
                _trophyMsg = "已调用游戏奖杯界面 Open()（共 " + arr.Length + " 个实例）";
                _ctx.Log(_trophyMsg);
            } catch (Exception e) { _trophyMsg = "打开失败: " + e.Message; _ctx.Log(_trophyMsg); }
        }

        private void TabTrophy() {
            int max = TrophyMax();
            if (Time.unscaledTime - _trophyAt > 1f) {
                _trophyAt = Time.unscaledTime;
                int unl = 0;
                for (int i = 1; i < max; i++) if (TrophyUnlocked(i)) unl++;
                _trophyUnlocked = unl; _trophyTotal = max - 1;
            }
            PLabel("奖杯 " + (_trophyUnlocked < 0 ? "…" : _trophyUnlocked + "/" + _trophyTotal) + "　（1.." + (max - 1) + "，每秒刷新状态）", 24f);
            float y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 150f, 28f), "打开游戏奖杯界面")) OpenGameTrophyUi();
            if (GUI.Button(new Rect(_pageX + 156f, y, 80f, 28f), "刷新")) _trophyAt = -9f;
            if (GUI.Button(new Rect(_pageX + 242f, y, 84f, 28f), "隐藏奖杯")) CallStatic("AchievementManager", "UnlockHastur");
            if (GUI.Button(new Rect(_pageX + 332f, y, 92f, 28f), ConfirmLabel("troAll2", "全部获得")) && Risky("troAll2", "解锁全部奖杯")) AwardAllTrophies();
            if (_trophyMsg.Length > 0) PLabel(_trophyMsg, 24f);
            int n = max - 1;
            float lh = Mathf.Min(160f, Mathf.Max(80f, n * 30f + 6f));
            Rect lr = InnerRect(lh, n * 30f + 6f, _scBrowse);
            GUI.BeginScrollView(lr, _scBrowse.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, n * 30f + 6f)));
            for (int i = 1; i < max; i++) {
                int row = i - 1;
                bool on = TrophyUnlocked(i);
                string label = "#" + i + "  " + TrophyName(i) + (on ? "   ✔ 已解锁" : "");
                if (GUI.Button(new Rect(2f, 2f + row * 30f, lr.width - 100f, 28f), (i == _trophySel ? "▶ " : "") + label)) _trophySel = i;
                if (!on && GUI.Button(new Rect(lr.width - 94f, 2f + row * 30f, 86f, 28f), "获得")) AwardTrophy(i);
            }
            GUI.EndScrollView();
            if (_trophySel > 0 && _trophySel < max) {
                GUILayout.Label("");  // 占位，保持与其它页一致的间距
                PLabel("选中 #" + _trophySel + " " + TrophyName(_trophySel), 24f);
                PLabel(TrophyDetail(_trophySel), 44f);
                if (PButton("获得该奖杯", 120f, 28f)) AwardTrophy(_trophySel);
            }
        }

        // ================= 日志 =================
        private void TabLog() {
            float y = NextRow(32f);
            if (GUI.Button(new Rect(_pageX, y, 90f, 28f), "刷新")) _logDump = null;
            if (GUI.Button(new Rect(_pageX + 96f, y, 90f, 28f), "清空显示")) { _logDump = ""; _logFilter = ""; }
            if (_logFilter == null) _logFilter = "";
            GUI.Label(new Rect(_pageX + 192f, y, 40f, 26f), "过滤");
            _logFilter = GUI.TextField(new Rect(_pageX + 234f, y, _pageW - 234f, 26f), _logFilter);
            if (_logDump == null || Time.unscaledTime - _logAt > 0.8f) { _logDump = HostLog.Dump(300); _logAt = Time.unscaledTime; }
            string[] all = _logDump.Split('\n');
            List<string> lines = new List<string>();
            for (int i = 0; i < all.Length; i++) {
                if (all[i].Length == 0) continue;
                if (_logFilter.Length > 0 && all[i].ToLowerInvariant().IndexOf(_logFilter.ToLowerInvariant()) < 0) continue;
                lines.Add(all[i]);
            }
            int n = lines.Count;
            float lh = Mathf.Min(160f, Mathf.Max(80f, n * 24f + 6f));
            Rect lr = InnerRect(lh, n * 24f + 6f, _scBrowse);
            _scBrowse.SetPos(GUI.BeginScrollView(lr, _scBrowse.Pos, new Rect(0f, 0f, lr.width - 16f, Mathf.Max(lr.height, n * 24f + 6f))));
            for (int i = 0; i < n; i++) GUI.Label(new Rect(2f, 2f + i * 24f, lr.width - 20f, 22f), lines[i]);
            GUI.EndScrollView();
        }

        // ================= Web =================
        private void TabWeb() {
            PLabel("WebUI: " + WebBridge.LocalUrl(), 26f);
            PLabel("手机浏览器打开上面地址即可用网页版调试台；模块页面在 /debug/", 24f);
            PLabel("命令示例（网页命令框同样可用）：", 26f);
            PLabel("atk 999 / hp 500 / maxhp 500 / energy 10 / flash 3", 24f);
            PLabel("lock atk / unlock atk / heal / timeScale 0.5 / pause / resume", 24f);
            PLabel("scene level3 / pin R.Player.Attribute.baseAtk / move 6 / freeze / scroll / ui on", 24f);
            PLabel("presets / probe / prefs / backup", 24f);
        }

        // ================= 命令 =================
        private void Handle(string line) {
            string s = (line ?? "").Trim();
            if (s.Length == 0) return;
            string cmd = s, arg = "";
            int sp = s.IndexOf(' ');
            if (sp > 0) { cmd = s.Substring(0, sp).Trim().ToLowerInvariant(); arg = s.Substring(sp + 1).Trim(); }
            int pi = IndexOfKey(cmd);
            if (pi >= 0) { SetPreset(cmd, arg); return; }
            switch (cmd) {
                case "heal": case "recover": case "all": Recover(); return;
                case "lock": ToggleLockKey(arg.ToLowerInvariant(), true); return;
                case "unlock": ToggleLockKey(arg.ToLowerInvariant(), false); return;
                case "probe": {
                    for (int i = 0; i < PSfx.Length; i++) {
                        for (int k = 0; k < PAlts.Length; k++) {
                            string path = PAlts[k] + PSfx[i];
                            object v; Type h; string m, err;
                            bool ok = Reflect.Resolve(path, out v, out h, out m, out err);
                            HostLog.Info("probe " + (ok ? "OK  " : "FAIL") + " " + path + (ok ? (" = " + Reflect.Format(v)) : (" :: " + err)));
                        }
                    }
                    return;
                }
                case "ui": {
                    if (arg == "on") _show = true; else if (arg == "off") _show = false; else _show = !_show;
                    HostLog.Info("调试面板 " + (_show ? "已打开" : "已关闭"));
                    return;
                }
                case "freeze": {
                    _freezeOnOpen = !_freezeOnOpen;
                    HostLog.Info("打开菜单冻结游戏: " + (_freezeOnOpen ? "开" : "关"));
                    return;
                }
                case "move": {
                    string[] p2 = arg.Split(' ');
                    float v;
                    if (p2.Length == 1 && float.TryParse(p2[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { _moveSpeed = Mathf.Clamp(v, 0.5f, 40f); HostLog.Info("移动速度 " + _moveSpeed); return; }
                    if (arg == "stop") { _moveDir = Vector2.zero; HostLog.Info("停止移动"); return; }
                    HostLog.Info("用法: move <速度> | move stop");
                    return;
                }
                case "scroll": {
                    ModGui.InvertScroll = !ModGui.InvertScroll;
                    HostLog.Info("滚动方向已切换为: " + (ModGui.InvertScroll ? "反向(拖动方向与内容相反)" : "同向(内容跟随手指)"));
                    return;
                }
                case "presets": {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < PSfx.Length; i++) sb.Append(PLabels[i]).Append("=").Append(ReadPath(i)).Append("(").Append(BestPath(i)).Append(")  ");
                    HostLog.Info(sb.ToString());
                    return;
                }
                case "timescale": {
                    float f;
                    if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) { SetSpeed(Mathf.Clamp(f, 0f, 20f)); }
                    return;
                }
                case "pause": SetSpeed(0f); HostLog.Info("已暂停（含音频）"); return;
                case "resume": Time.timeScale = 1f; HostLog.Info("已继续"); return;
                case "scene": {
                    int idx;
                    if (int.TryParse(arg, out idx)) SceneUtil.LoadIndex(idx);
                    else if (arg.Length > 0) SceneUtil.Load(arg);
                    else HostLog.Info("当前场景 " + SceneUtil.Current);
                    return;
                }
                case "reload": SceneUtil.Load(SceneUtil.Current); return;
                case "pin": {
                    int sp2 = arg.LastIndexOf(' ');
                    if (sp2 > 0) HostLog.Info(Pin.Add(arg.Substring(0, sp2).Trim(), arg.Substring(sp2 + 1).Trim(), true));
                    else HostLog.Info(Pin.Add(arg, null, true));
                    return;
                }
                case "unpin": HostLog.Info(Pin.Remove(arg)); return;
                case "set": {
                    int sp2 = arg.IndexOf(' ');
                    if (sp2 <= 0) { HostLog.Warn("用法: set <路径> <值>"); return; }
                    string err; bool ok = Reflect.Set(arg.Substring(0, sp2).Trim(), arg.Substring(sp2 + 1).Trim(), out err);
                    HostLog.Info(ok ? "已写入" : ("写入失败: " + err));
                    return;
                }
                case "call": {
                    int dot = arg.LastIndexOf('.');
                    if (dot > 0) {
                        Type t = Reflect.TypeByFullName(arg.Substring(0, dot));
                        if (t == null) { HostLog.Warn("找不到类型 " + arg.Substring(0, dot)); return; }
                        MethodInfo m = t.GetMethod(arg.Substring(dot + 1), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                        if (m != null) InvokeMethod(t, m); else HostLog.Warn("找不到方法 " + arg);
                    }
                    return;
                }
                case "prefs": _prefs = PrefsUtil.Load(); HostLog.Info("存档项 " + (_prefs == null ? 0 : _prefs.Count)); return;
                case "backup": HostLog.Info(PrefsUtil.Backup()); return;
                case "help":
                    HostLog.Info("预设: atk/hp/maxhp/energy/maxenergy/flash/flashlvl/flashcd/speed/charge/execute/endure <值> | lock <预设> | unlock <预设> | heal | presets");
                    HostLog.Info("通用: timeScale <f> | pause | resume | scene <名|序号> | reload | pin <路径> [值] | unpin <路径> | set <路径> <值> | call <类型.方法> | prefs | backup");
                    return;
                default: HostLog.Warn("未知命令: " + s); return;
            }
        }

        private void Publish() {
            if (_ctx == null) return;
            StringBuilder sb = new StringBuilder("{");
            CultureInfo ci = CultureInfo.InvariantCulture;
            sb.Append("\"timeScale\":").Append(Time.timeScale.ToString("0.###", ci));
            sb.Append(",\"scene\":\"").Append(SceneUtil.Current).Append('"');
            sb.Append(",\"prefs\":").Append(_prefs == null ? 0 : _prefs.Count);
            sb.Append(",\"presets\":[");
            for (int i = 0; i < PSfx.Length; i++) {
                if (i > 0) sb.Append(',');
                sb.Append("{\"key\":\"").Append(PKeys[i]).Append("\",\"label\":\"").Append(PLabels[i])
                  .Append("\",\"value\":\"").Append(Clean(ReadPath(i)))
                  .Append("\",\"locked\":").Append(IsPinned(BestPath(i)) ? "true" : "false")
                  .Append(",\"path\":\"").Append(BestPath(i)).Append("\"}");
            }
            sb.Append("],\"pins\":").Append(Pin.ToJson());
            sb.Append(",\"touch\":\"").Append(Clean(ModGui.LastTouch)).Append("\"");
            sb.Append(",\"win\":\"").Append(ModGui.WinW.ToString("0") + "x" + ModGui.WinH.ToString("0")).Append("\"");
            sb.Append(",\"outerY\":").Append(_scOuter.Pos.y.ToString("0.##", ci));
            sb.Append(",\"contentH\":").Append(_pageH[_tab].ToString("0", ci));
            sb.Append(",\"tab\":").Append(_tab);
            sb.Append(",\"trophyUnlocked\":").Append(_trophyUnlocked).Append(",\"trophyTotal\":").Append(_trophyTotal);
            sb.Append(",\"evScale\":").Append(ModGui.EvScale.ToString("0.##", ci));
            sb.Append(",\"ball\":\"").Append(Clean(_ball.ToString())).Append("\"");
            sb.Append(",\"scenes\":[");
            int max = Mathf.Min(SceneUtil.Count, 80);
            for (int i = 0; i < max; i++) {
                string n = SceneUtil.NameAt(i);
                if (n == null) continue;
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(n).Append('"');
            }
            sb.Append("]}");
            _ctx.Publish(sb.ToString());
        }

        private static string Clean(string s) { return (s ?? "").Replace("\\", "").Replace("\"", ""); }
    }

    /// <summary>PlayerPrefs 读写与存档备份。</summary>
    public static class PrefsUtil {
        public static string DataDir = "/data/data/com.fantablade.icey";
        private static string PrefsFile {
            get {
                string p = DataDir + "/shared_prefs";
                try { if (Directory.Exists(p)) { string[] fs = Directory.GetFiles(p, "*.xml"); if (fs.Length > 0) return fs[0]; } } catch (Exception) { }
                return p + "/com.fantablade.icey.v2.playerprefs.xml";
            }
        }

        public class Pref { public string Key; public string Type; public string Value; public string Edit; }

        public static List<Pref> Load() {
            List<Pref> list = new List<Pref>();
            try {
                string f = PrefsFile;
                if (!File.Exists(f)) return list;
                string xml = File.ReadAllText(f);
                int i = 0;
                while (true) {
                    int s = xml.IndexOf("<", i);
                    if (s < 0) break;
                    int e = xml.IndexOf(">", s);
                    if (e < 0) break;
                    string tag = xml.Substring(s + 1, e - s - 1).Trim();
                    i = e + 1;
                    string name = Attr(tag, "name");
                    if (name == null) continue;
                    string type = tag.Split(' ')[0];
                    int close = xml.IndexOf("</" + type + ">", i);
                    string val = close > i ? xml.Substring(i, close - i) : "";
                    if (close > 0) i = close;
                    Pref p = new Pref();
                    p.Key = name; p.Type = type; p.Value = Unescape(val); p.Edit = p.Value;
                    list.Add(p);
                }
            } catch (Exception e) { HostLog.Warn("解析存档失败: " + e.Message); }
            return list;
        }

        private static string Attr(string tag, string key) {
            int a = tag.IndexOf(key + "=\"");
            if (a < 0) return null;
            a += key.Length + 2;
            int b = tag.IndexOf('"', a);
            return b < 0 ? null : tag.Substring(a, b - a);
        }

        private static string Unescape(string s) {
            return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("\\n", "\n").Replace("\\'", "'");
        }

        public static string Apply(Pref p) {
            try {
                if (p.Type == "int") PlayerPrefs.SetInt(p.Key, int.Parse(p.Edit, CultureInfo.InvariantCulture));
                else if (p.Type == "float") PlayerPrefs.SetFloat(p.Key, float.Parse(p.Edit, CultureInfo.InvariantCulture));
                else PlayerPrefs.SetString(p.Key, p.Edit);
                PlayerPrefs.Save();
                p.Value = p.Edit;
                return "已保存 " + p.Key + " = " + p.Edit;
            } catch (Exception e) { return "保存失败: " + e.Message; }
        }

        public static string Backup() {
            try {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dst = "/sdcard/ICEYBackup/" + stamp;
                Directory.CreateDirectory(dst);
                int n = 0;
                n += CopyDir(DataDir + "/shared_prefs", dst + "/shared_prefs");
                n += CopyDir(DataDir + "/files", dst + "/files");
                return "已备份 " + n + " 个文件到 " + dst;
            } catch (Exception e) { return "备份失败: " + e.Message; }
        }

        private static int CopyDir(string src, string dst) {
            int n = 0;
            try {
                if (!Directory.Exists(src)) return 0;
                Directory.CreateDirectory(dst);
                string[] fs = Directory.GetFiles(src);
                for (int i = 0; i < fs.Length; i++) {
                    try { File.Copy(fs[i], Path.Combine(dst, Path.GetFileName(fs[i])), true); n++; } catch (Exception) { }
                }
                string[] ds = Directory.GetDirectories(src);
                for (int i = 0; i < ds.Length; i++) n += CopyDir(ds[i], Path.Combine(dst, Path.GetFileName(ds[i])));
            } catch (Exception) { }
            return n;
        }
    }
}
