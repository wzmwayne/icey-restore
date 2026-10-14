using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace IceyModHost {

    /// <summary>C# 侧 Mod 接口（API v2）。</summary>
    public interface IIceyMod {
        void OnLoad(ModContext ctx);
        void OnUpdate(ModContext ctx);
        void OnGui(ModContext ctx);
        void OnUnload(ModContext ctx);
    }

    /// <summary>提供给 mod 的上下文与宿主能力。</summary>
    public sealed class ModContext {
        public const int ApiVersion = 2;
        public readonly string ModName;
        public readonly MonoBehaviour Host;
        public readonly string ModsDir;
        private readonly List<Action<string>> _cmdHandlers = new List<Action<string>>();

        internal ModContext(string name, MonoBehaviour host, string modsDir) {
            ModName = name; Host = host; ModsDir = modsDir;
        }

        public void Log(string msg) { HostLog.Info("[" + ModName + "] " + msg); }

        /// <summary>订阅 WebUI 下发的命令行（如 "timeScale 2"）。</summary>
        public void OnCommand(Action<string> handler) { if (handler != null) _cmdHandlers.Add(handler); }

        internal bool Dispatch(string line) {
            bool handled = false;
            for (int i = 0; i < _cmdHandlers.Count; i++) {
                try { _cmdHandlers[i](line); handled = true; } catch (Exception e) { HostLog.Warn("命令处理失败: " + e.Message); }
            }
            return handled;
        }

        /// <summary>把 JSON 状态推给 WebUI（出现在调试台页面）。</summary>
        public void Publish(string json) { WebBridge.Publish(ModName, json); }

        /// <summary>在模块静态目录外，注册一个 Web 前缀（配合 mods/webui/&lt;prefix&gt;/index.html）。</summary>
        public void RegisterWebRoute(string prefix) { WebBridge.RegisterRoute(ModName, prefix); }

        public GameObject NewGameObject(string name) {
            GameObject go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go;
        }
    }

    /// <summary>宿主日志：内存环形缓冲 + logcat + 上报 WebUI。</summary>
    public static class HostLog {
        private static readonly List<string> Buf = new List<string>();
        public static void Info(string s) { Add("I", s); }
        public static void Warn(string s) { Add("W", s); }
        public static void Error(string s) { Add("E", s); }

        /** 直接写文件，便于没有 logcat 权限时排查。 */
        public static void FileLog(string s) {
            string line = DateTime.Now.ToString("HH:mm:ss") + " " + s;
            try { System.IO.File.AppendAllText("/sdcard/ICEYMods/icey_host.log", line + "\n"); } catch (Exception) { }
            try { System.IO.File.AppendAllText("/data/data/com.fantablade.icey/files/icey_host.log", line + "\n"); } catch (Exception) { }
        }

        private static void Add(string level, string s) {
            string line = DateTime.Now.ToString("HH:mm:ss") + " " + level + " " + s;
            lock (Buf) { Buf.Add(line); while (Buf.Count > 500) Buf.RemoveAt(0); }
            FileLog(level + " " + s);
            if (level == "E") Debug.LogError("[IceyModHost] " + s);
            else if (level == "W") Debug.LogWarning("[IceyModHost] " + s);
            else Debug.Log("[IceyModHost] " + s);
            WebBridge.PushJavaLog(line);
        }

        public static string Dump(int max) {
            lock (Buf) {
                StringBuilder sb = new StringBuilder();
                int from = Math.Max(0, Buf.Count - max);
                for (int i = from; i < Buf.Count; i++) sb.Append(Buf[i]).Append('\n');
                return sb.ToString();
            }
        }
    }

    /// <summary>IMGUI 助手：可拖动窗口 + 悬浮球 + 全局缩放（触摸友好）。</summary>
    public static class ModGui {
        public static float Scale = 2.0f;
        private static bool _ballDragging;
        private static bool _ballMoved;
        private static Vector2 _grabPos;
        private static Rect _grabRect;

        public static void BeginScaled() {
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
        }

        public static void EndScaled() {
            GUI.matrix = Matrix4x4.identity;
        }

        public static float VW { get { return Screen.width / Scale; } }
        public static float VH { get { return Screen.height / Scale; } }

        /// <summary>右下角悬浮球：位置式拖动（不用 delta，避免触摸事件 Y 方向相反的问题），返回是否被点击。</summary>
        public static bool Ball(ref Rect r, string label) {
            if (r.width <= 0) { r = new Rect(VW - 62, VH - 62, 54, 54); }
            r.x = Mathf.Clamp(r.x, 0, Mathf.Max(0, VW - r.width));
            r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, VH - r.height));
            Rects.Add(r);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Box(r, label);
            GUI.color = Color.white;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && Inside(r)) {
                _ballDragging = true; _ballMoved = false;
                _grabPos = RawMouse() / _dragScale;      // 按下时的指针位置（与 r 同空间）
                _grabRect = r;                            // 按下时的矩形
                e.Use();
            } else if (e.type == EventType.MouseDrag && _ballDragging) {
                Vector2 cur = RawMouse() / _dragScale;
                r.x = _grabRect.x + (cur.x - _grabPos.x);
                r.y = _grabRect.y + (cur.y - _grabPos.y);
                r.x = Mathf.Clamp(r.x, 0, Mathf.Max(0, VW - r.width));
                r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, VH - r.height));
                if ((cur - _grabPos).magnitude > 8f) _ballMoved = true;
                e.Use();
            } else if (e.type == EventType.MouseUp && _ballDragging) {
                _ballDragging = false;
                e.Use();
                if (!_ballMoved) return true;             // 没怎么移动 = 点击
            }
            return false;
        }

        /// <summary>可拖动窗口；返回是否可见（可见时调用方在返回 true 后绘制内容）。</summary>
        public static bool Window(int id, ref Rect r, string title, GUI.WindowFunction draw) {
            if (r.width <= 0) r = new Rect(12, 12, Mathf.Min(430, VW - 24), Mathf.Min(560, VH - 24));
            r.x = Mathf.Clamp(r.x, 0, Mathf.Max(0, VW - r.width));
            r.y = Mathf.Clamp(r.y, 0, Mathf.Max(0, VH - r.height));
            Rects.Add(r);
            WinW = r.width; WinH = r.height;      // 供各页计算列表高度
            r = GUI.Window(id, r, draw, title);
            WinW = r.width; WinH = r.height;
            return true;
        }

        /// <summary>当前调试窗口的虚拟尺寸（由 Window() 每帧写入，供各页算列表高度，避免内容被裁）。</summary>
        public static float WinW, WinH;

        /// <summary>按屏幕自适应窗口：约 1/6 屏幕面积，并保证不小于内容所需的最小尺寸。</summary>
        public static Rect FitWindow() {
            float w = Mathf.Clamp(VW * 0.34f, 440f, Mathf.Max(440f, VW - 16f));
            float h = Mathf.Clamp(VH * 0.52f, 520f, Mathf.Max(520f, VH - 16f));
            float x = Mathf.Clamp(VW * 0.04f, 4f, Mathf.Max(4f, VW - w - 4f));
            float y = Mathf.Clamp(VH * 0.08f, 4f, Mathf.Max(4f, VH - h - 4f));
            Rect r = new Rect(x, y, w, h);
            WinW = w; WinH = h;
            return r;
        }

        /// <summary>本帧登记的可交互矩形（虚拟坐标），用于防点击穿透。</summary>
        public static readonly List<Rect> Rects = new List<Rect>();
        private static bool _touching;

        /// <summary>本次拖动使用的坐标除数（由按下时命中的空间决定，逐次判定，不全局改）。</summary>
        public static float EvScale = 1f;
        private static float _dragScale = 1f;
        private static string _lastSpace = "-";

        public static Vector2 RawMouse() { return Event.current.mousePosition; }
        public static Vector2 MousePos() { return RawMouse() / EvScale; }
        public static Vector2 MouseDelta() { return Event.current.delta / _dragScale; }

        /// <summary>只用于显示/诊断，不再自动改变除数（触摸=GUI空间、鼠标=屏幕像素会混用）。</summary>
        public static void AutoCalibrate() { }

        /// <summary>把虚拟矩形换算成屏幕像素矩形（用于和屏幕空间的事件坐标比对）。</summary>
        public static Rect ScreenRect(Rect r) {
            try {
                Vector2 p = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
                return new Rect(p.x, p.y, r.width * Scale, r.height * Scale);
            } catch (Exception) { return r; }
        }

        /// <summary>最近一次触摸诊断信息（会显示在设置页并由调试台上报到 WebUI）。</summary>
        public static string LastTouch = "(还没有触摸事件)";
        private static int _touchLogs;

        /// <summary>命中判定：GUI 空间与屏幕像素空间各自判定，命中即算；按下时决定本次拖动除数。</summary>
        public static bool Inside(Rect r) {
            Vector2 m = RawMouse();
            Event e = Event.current;
            bool gui = r.Contains(m);
            bool scr = !gui && ScreenRect(r).Contains(m);
            bool hit = gui || scr;
            if (hit) {
                _lastSpace = gui ? "GUI空间" : "屏幕像素";
                if (e != null && e.type == EventType.MouseDown) { _dragScale = gui ? 1f : Scale; EvScale = _dragScale; }
            }
            if (e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseUp)) {
                string msg = string.Format("t={0} raw=({1:0},{2:0}) 命中={3} 空间={4} rect=({5:0},{6:0},{7:0},{8:0}) 除数={9:0.##}",
                    e.type, m.x, m.y, hit ? "是" : "否", hit ? _lastSpace : "两空间都不在", r.x, r.y, r.width, r.height, _dragScale);
                LastTouch = msg;
                if (_touchLogs < 80) { _touchLogs++; HostLog.Info("[触摸] " + msg); }
            }
            return hit;
        }

        private static Texture2D _white;
        /** 用纯色填充矩形（1x1 白纹理），用于高亮/分隔线。 */
        public static void Fill(Rect r, Color c) {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            Color old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old;
        }

        /// <summary>窗口标题栏高度（供高亮/命中用）。</summary>
        public const float TitleH = 22f;

        /// <summary>本帧内层可拖动列表的矩形（外层滚动据此让出事件，实现“最内层优先”）。</summary>
        public static readonly List<Rect> InnerScrolls = new List<Rect>();
        public static void RegisterInner(Rect r) { InnerScrolls.Add(r); }
        public static bool InInner() {
            for (int i = 0; i < InnerScrolls.Count; i++) if (Inside(InnerScrolls[i])) return true;
            return false;
        }

        /// <summary>当前触摸/点击是否落在我们的界面上（用于屏蔽游戏输入，防穿透）。</summary>
        public static bool Interacting() {
            Event e = Event.current;
            if (e == null) return false;
            bool inside = false;
            for (int i = 0; i < Rects.Count; i++) if (Inside(Rects[i])) { inside = true; break; }
            if (e.type == EventType.MouseDown) _touching = inside;
            if (inside || _touching) {
                if (e.type == EventType.MouseUp && !inside) _touching = false;
                return true;
            }
            if (e.type == EventType.MouseUp) _touching = false;
            return false;
        }

        /// <summary>滚动方向开关：true = 手指向下拖时列表向上回滚（与默认相反）。可用 scroll 命令切换。</summary>
        public static bool InvertScroll = false;

        /// <summary>统一设置 GUI 皮肤（中文字体 + 防止文字被裁剪）。</summary>
        public static void SetupSkin(Font font, int size) {
            if (font == null) return;
            GUIStyle l = new GUIStyle(GUI.skin.label);
            l.font = font; l.fontSize = size; l.fixedHeight = 0f; l.wordWrap = false;
            l.padding = new RectOffset(2, 2, 1, 1); l.clipping = TextClipping.Clip;
            GUI.skin.label = l;

            GUIStyle b = new GUIStyle(GUI.skin.button);
            b.font = font; b.fontSize = size; b.fixedHeight = 0f; b.padding = new RectOffset(5, 5, 4, 4);
            GUI.skin.button = b;

            GUIStyle tf = new GUIStyle(GUI.skin.textField);
            tf.font = font; tf.fontSize = size; tf.fixedHeight = 0f; tf.padding = new RectOffset(4, 4, 3, 3);
            GUI.skin.textField = tf;

            GUIStyle w = new GUIStyle(GUI.skin.window);
            w.font = font; w.fontSize = size; w.padding = new RectOffset(8, 8, 22, 8);
            GUI.skin.window = w;

            GUIStyle bx = new GUIStyle(GUI.skin.box);
            bx.font = font; bx.fontSize = size; bx.padding = new RectOffset(6, 6, 4, 4);
            GUI.skin.box = bx;

            GUIStyle tb = new GUIStyle(GUI.skin.button);
            tb.font = font; tb.fontSize = size;
        }

        /// <summary>滚动状态：支持手指拖动内容滚动（不只是拖滚动条）。</summary>
        public sealed class ScrollState {
            public Vector2 Pos;
            private bool _dragging;
            private bool _moved;
            private Vector2 _grab;
            private float _grabPos;

            public Vector2 Handle(Rect view, float contentHeight) { return Handle(view, contentHeight, view); }

            /** view 用于绘制/限幅；hitRect 用于命中判定（可换算到其它坐标空间）。 */
            public Vector2 Handle(Rect view, float contentHeight, Rect hitRect) { return Handle(view, contentHeight, hitRect, false); }

            /** topLayer=true 表示这是最内层（不再让位给其它列表），false 表示外层（遇到已登记的内层列表就让位）。 */
            public Vector2 Handle(Rect view, float contentHeight, Rect hitRect, bool topLayer) {
                Event e = Event.current;
                float max = Mathf.Max(0f, contentHeight - view.height);
                if (e.type == EventType.MouseDown && Inside(hitRect)) {
                    if (contentHeight >= 0f && contentHeight <= view.height + 1f) { _dragging = false; return Pos; }  // 内容装得下 → 不抢（让外层滚页）
                    if (!topLayer && InInner()) { _dragging = false; return Pos; }   // 外层遇内层让位（内层自己传 topLayer=true）
                    _dragging = true; _moved = false;
                    _grab = RawMouse() / _dragScale;
                    _grabPos = Pos.y;
                } else if (e.type == EventType.MouseDrag && _dragging) {
                    Vector2 cur = RawMouse() / _dragScale;
                    float dy = cur.y - _grab.y;
                    Pos.y = InvertScroll ? (_grabPos + dy) : (_grabPos - dy);
                    Pos.x = 0f;
                    if (Mathf.Abs(dy) > 2f) _moved = true;
                    if (_moved) e.Use();
                } else if (e.type == EventType.MouseUp) { _dragging = false; }
                Pos.y = contentHeight < 0f ? Mathf.Clamp(Pos.y, 0f, 100000f) : Mathf.Clamp(Pos.y, 0f, max);
                return Pos;
            }

            public void SetPos(Vector2 v) { Pos = v; }
            public void ToTop() { Pos = Vector2.zero; }
        }

        public static ScrollState NewScroll() { return new ScrollState(); }

        /// <summary>把 IMGUI 拖动事件（手指拖动）转成滚动，返回内容起始 Y。</summary>
        public static Vector2 ScrollArea(ScrollState st, Rect view, float contentHeight) {
            Vector2 p = st.Handle(view, contentHeight);
            GUI.BeginScrollView(view, p, new Rect(0, 0, view.width - 12, contentHeight));
            return p;
        }

        public static bool Button(string text, params GUILayoutOption[] o) { return GUILayout.Button(text, o); }
        public static void Label(string text) { GUILayout.Label(text); }
        public static string TextField(string text, params GUILayoutOption[] o) { return GUILayout.TextField(text, o); }
    }

    /// <summary>反射工具：按 "Type.Member.SubMember" 取值/赋值/调用（静态与实例均支持，自动找单例实例）。</summary>
    public static class Reflect {
        public static readonly List<Type> GameTypes = new List<Type>();
        private static readonly Dictionary<string, object> Cache = new Dictionary<string, object>();
        private static readonly Dictionary<string, float> CacheAt = new Dictionary<string, float>();
        private const float CacheTtl = 1.0f;   // 未找到的负结果也缓存 1 秒，避免每帧重复扫描/FindObjectOfType

        public static readonly Dictionary<Type, List<MemberInfo>> MemberIndex = new Dictionary<Type, List<MemberInfo>>();

        public static void Index(IEnumerable<Assembly> asms) {
            GameTypes.Clear(); MemberIndex.Clear(); Cache.Clear(); TypeCache.Clear();
            foreach (Assembly a in asms) {
                Type[] ts;
                try { ts = a.GetTypes(); } catch (Exception) { continue; }
                foreach (Type t in ts) GameTypes.Add(t);
            }
            // 反向索引：成员类型 -> 声明该成员的类型（用于自动定位实例）
            for (int i = 0; i < GameTypes.Count; i++) {
                Type t = GameTypes[i];
                if (t == null || t.IsAbstract && t.IsSealed) continue;   // 跳过静态类
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.DeclaredOnly;
                try {
                    FieldInfo[] fs = t.GetFields(f);
                    for (int j = 0; j < fs.Length; j++) Add(MemberType(fs[j].FieldType), fs[j]);
                    PropertyInfo[] ps = t.GetProperties(f);
                    for (int j = 0; j < ps.Length; j++) Add(MemberType(ps[j].PropertyType), ps[j]);
                } catch (Exception) { }
            }
        }

        private static void Add(Type memberType, MemberInfo mi) {
            if (memberType == null) return;
            List<MemberInfo> list;
            if (!MemberIndex.TryGetValue(memberType, out list)) { list = new List<MemberInfo>(); MemberIndex[memberType] = list; }
            if (list.Count < 40) list.Add(mi);
        }

        private static Type MemberType(Type t) { return t; }

        public static void ClearCache() { Cache.Clear(); CacheAt.Clear(); }

        public static List<Type> Find(string needle, int max) {
            List<Type> outList = new List<Type>();
            string n = (needle ?? "").ToLowerInvariant();
            foreach (Type t in GameTypes) {
                if (t.FullName == null) continue;
                if (n.Length == 0 || t.FullName.ToLowerInvariant().Contains(n)) {
                    outList.Add(t);
                    if (outList.Count >= max) break;
                }
            }
            return outList;
        }

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        public static Type TypeByFullName(string fullName) {
            Type cached;
            if (TypeCache.TryGetValue(fullName, out cached)) return cached;
            Type found = null;
            foreach (Type t in GameTypes) if (t.FullName == fullName) { found = t; break; }
            TypeCache[fullName] = found;
            return found;
        }

        /// <summary>解析 "Type.Member" 或 "Type.Member.Sub"，返回 (类型, 值, 成员名)。</summary>
        public static bool Resolve(string path, out object value, out Type holder, out string member, out string err) {
            value = null; holder = null; member = null; err = null;
            if (string.IsNullOrEmpty(path)) { err = "路径为空"; return false; }
            string[] parts = path.Split('.');
            for (int split = parts.Length - 1; split >= 1; split--) {
                string typeName = string.Join(".", parts, 0, split);
                Type t = TypeByFullName(typeName);
                if (t == null) continue;
                object cur = null; Type curType = t; string curName = typeName;
                bool ok = true;
                for (int i = split; i < parts.Length; i++) {
                    string m = parts[i];
                    if (!Step(curType, ref cur, m, out err)) { ok = false; break; }
                    curName = curName + "." + m;
                    curType = cur == null ? null : cur.GetType();
                    if (curType == null) { if (i < parts.Length - 1) { ok = false; err = m + " 为 null"; } break; }
                }
                if (ok) { value = cur; holder = curType; member = parts[parts.Length - 1]; return true; }
            }
            err = "未找到类型/成员: " + path;
            return false;
        }

        private static bool Step(Type type, ref object instance, string memberName, out string err) {
            err = null;
            if (type == null) { err = "类型为空"; return false; }
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            FieldInfo fi = type.GetField(memberName, f);
            if (fi != null) {
                if (fi.IsStatic) { instance = fi.GetValue(null); return true; }
                object target = instance ?? Singleton(type);
                if (target == null) target = StaticBacking(type, memberName);   // 回退：同名静态后备字段
                if (target == null) { err = "无实例: " + type.Name; return false; }
                instance = fi.GetValue(target); return true;
            }
            PropertyInfo pi = type.GetProperty(memberName, f);
            if (pi != null && pi.CanRead) {
                MethodInfo gm = pi.GetGetMethod(true);
                if (gm == null) { err = "不可读: " + memberName; return false; }
                if (gm.IsStatic) { instance = pi.GetValue(null, null); return true; }
                object target = instance ?? Singleton(type);
                if (target == null) target = StaticBacking(type, memberName);
                if (target == null) { err = "无实例: " + type.Name; return false; }
                instance = pi.GetValue(target, null); return true;
            }
            err = "成员不存在: " + type.Name + "." + memberName;
            return false;
        }

        /** 实例属性/字段找不到实例时，尝试静态后备字段：GameData -> _gameData、Settings -> _settings。 */
        private static object StaticBacking(Type type, string memberName) {
            if (memberName == null || memberName.Length == 0) return null;
            string alt = "_" + char.ToLowerInvariant(memberName[0]) + memberName.Substring(1);
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo af = type.GetField(alt, f);
            if (af != null && af.IsStatic) { try { return af.GetValue(null); } catch (Exception) { } }
            PropertyInfo ap = type.GetProperty(alt, f);
            if (ap != null && ap.CanRead) { try { object g = ap.GetGetMethod(true) != null && ap.GetGetMethod(true).IsStatic ? ap.GetValue(null, null) : null; return g; } catch (Exception) { } }
            return null;
        }

        /// <summary>为实例成员找一个可用实例：FindObjectOfType → 静态 Instance/Singleton/Current → 递归查找持有它的对象。</summary>
        public static object Singleton(Type t) { return Singleton(t, 0, new List<Type>()); }

        private static object Singleton(Type t, int depth, List<Type> visiting) {
            if (t == null || depth > 4) return null;
            object cached; float cachedAt;
            if (Cache.TryGetValue(t.FullName, out cached) && CacheAt.TryGetValue(t.FullName, out cachedAt)) {
                if (cached != null && !(cached is UnityEngine.Object && (UnityEngine.Object)cached == null)) return cached;
                if (UnityEngine.Time.realtimeSinceStartup - cachedAt < CacheTtl) return null;   // 负缓存
            }
            object inst = null;
            try {
                if (typeof(Component).IsAssignableFrom(t) || typeof(GameObject).IsAssignableFrom(t))
                    inst = UnityEngine.Object.FindObjectOfType(t);
            } catch (Exception) { }
            if (inst == null) inst = StaticInstance(t);
            if (inst == null && !visiting.Contains(t)) {
                visiting.Add(t);
                List<MemberInfo> holders;
                if (MemberIndex.TryGetValue(t, out holders)) {
                    for (int i = 0; i < holders.Count && inst == null; i++) {
                        MemberInfo mi = holders[i];
                        object ownerInst = null;
                        if (!IsStaticMember(mi)) {
                            Type owner = mi.DeclaringType;
                            if (owner == null || visiting.Contains(owner)) continue;
                            ownerInst = Singleton(owner, depth + 1, visiting);
                            if (ownerInst == null) continue;
                        }
                        try {
                            object v = ReadMember(mi, ownerInst);
                            if (v != null) inst = v;
                        } catch (Exception) { }
                    }
                }
                visiting.Remove(t);
            }
            Cache[t.FullName] = inst;
            CacheAt[t.FullName] = UnityEngine.Time.realtimeSinceStartup;
            return inst;
        }

        private static bool IsStaticMember(MemberInfo mi) {
            FieldInfo fi = mi as FieldInfo;
            if (fi != null) return fi.IsStatic;
            PropertyInfo pi = mi as PropertyInfo;
            if (pi != null) { MethodInfo g = pi.GetGetMethod(true); return g != null && g.IsStatic; }
            return false;
        }

        private static object StaticInstance(Type t) {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            string[] names = new string[] { "Instance", "instance", "Singleton", "Current", "I", "s_Instance", "s_instance", "ME" };
            for (int i = 0; i < names.Length; i++) {
                try {
                    FieldInfo fi = t.GetField(names[i], f);
                    if (fi != null) { object v = fi.GetValue(null); if (v != null) return v; }
                    PropertyInfo pi = t.GetProperty(names[i], f);
                    if (pi != null && pi.CanRead) { object v = pi.GetValue(null, null); if (v != null) return v; }
                } catch (Exception) { }
            }
            return null;
        }

        private static object ReadMember(MemberInfo mi, object target) {
            FieldInfo fi = mi as FieldInfo;
            if (fi != null) return fi.GetValue(fi.IsStatic ? null : target);
            PropertyInfo pi = mi as PropertyInfo;
            if (pi != null) {
                MethodInfo g = pi.GetGetMethod(true);
                if (g == null) return null;
                return pi.GetValue(g.IsStatic ? null : target, null);
            }
            return null;
        }

        private static MemberInfo FindMember(Type t, string name) {
            BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            FieldInfo fi = t.GetField(name, f);
            if (fi != null) return fi;
            PropertyInfo pi = t.GetProperty(name, f);
            if (pi != null && pi.CanRead) return pi;
            return null;
        }

        public static bool Set(string path, string text, out string err) {
            object cur; Type holder; string member;
            if (!Resolve(path, out cur, out holder, out member, out err)) return false;
            // 找到成员本体所在的对象
            string[] parts = path.Split('.');
            for (int split = parts.Length - 1; split >= 1; split--) {
                Type t = TypeByFullName(string.Join(".", parts, 0, split));
                if (t == null) continue;
                object obj = null; Type curType = t;
                bool ok = true;
                for (int i = split; i < parts.Length - 1; i++) {
                    if (!Step(curType, ref obj, parts[i], out err)) { ok = false; break; }
                    curType = obj == null ? null : obj.GetType();
                    if (curType == null) { ok = false; break; }
                }
                if (!ok || curType == null) continue;
                BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
                string last = parts[parts.Length - 1];
                FieldInfo fi = curType.GetField(last, f);
                PropertyInfo pi = curType.GetProperty(last, f);
                object target = null;
                MethodInfo sm = pi == null ? null : pi.GetSetMethod(true);
                if (fi != null && !fi.IsStatic) target = obj ?? Singleton(curType);
                if (pi != null && sm != null && !sm.IsStatic) target = obj ?? Singleton(curType);
                object val = ParseValue(text, fi != null ? fi.FieldType : (pi != null ? pi.PropertyType : typeof(string)));
                if (fi != null) { fi.SetValue(fi.IsStatic ? null : target, val); return true; }
                if (pi != null && sm != null) { pi.SetValue(sm.IsStatic ? null : target, val, null); return true; }
                err = "不可写: " + path;
                return false;
            }
            err = "解析失败: " + path;
            return false;
        }

        public static object ParseValue(string text, Type t) {
            try {
                if (t == typeof(string)) return text;
                if (t == typeof(bool)) return text == "1" || text.ToLowerInvariant() == "true";
                if (t == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
                if (t == typeof(float)) return float.Parse(text, CultureInfo.InvariantCulture);
                if (t == typeof(double)) return double.Parse(text, CultureInfo.InvariantCulture);
                if (t == typeof(long)) return long.Parse(text, CultureInfo.InvariantCulture);
                if (t.IsEnum) return Enum.Parse(t, text, true);
                return Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
            } catch (Exception) { return text; }
        }

        public static string Format(object v) {
            if (v == null) return "null";
            if (v is float) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is double) return ((double)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is UnityEngine.Object) return v.ToString();
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>数值锁定：每帧把设定值写回，实现"锁定"。</summary>
    public static class Pin {
        public class Item { public string Path; public string Value; public bool Locked; public string Note; }
        public static readonly List<Item> Items = new List<Item>();

        public static string Add(string path, string value, bool locked) {
            string err;
            if (value == null) {
                object v; Type h; string m;
                if (Reflect.Resolve(path, out v, out h, out m, out err)) value = Reflect.Format(v);
            }
            for (int i = 0; i < Items.Count; i++) if (Items[i].Path == path) { Items[i].Value = value; Items[i].Locked = locked; return "已更新 " + path; }
            Items.Add(new Item { Path = path, Value = value, Locked = locked });
            if (locked) { Apply(); }
            return "已锁定 " + path + " = " + value;
        }

        public static string Remove(string path) {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Path == path) { Items.RemoveAt(i); return "已解锁 " + path; }
            return "未锁定 " + path;
        }

        public static void Apply() {
            for (int i = 0; i < Items.Count; i++) {
                if (!Items[i].Locked) continue;
                try { string err; Reflect.Set(Items[i].Path, Items[i].Value, out err); } catch (Exception) { }
            }
        }

        public static string ToJson() {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < Items.Count; i++) {
                if (i > 0) sb.Append(',');
                sb.Append("{\"path\":\"").Append(Esc(Items[i].Path)).Append("\",\"value\":\"").Append(Esc(Items[i].Value))
                  .Append("\",\"locked\":").Append(Items[i].Locked ? "true" : "false").Append('}');
            }
            return sb.Append(']').ToString();
        }

        private static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }

    /// <summary>场景工具（Unity 5.6 SceneManager）。</summary>
    public static class SceneUtil {
        public static int Count { get { try { return UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; } catch (Exception) { return 0; } } }
        public static string Current { get { try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; } catch (Exception) { return "?"; } } }
        public static string NameAt(int i) {
            try {
                string p = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                int s = p.LastIndexOf('/'); int d = p.LastIndexOf('.');
                return (s >= 0 && d > s) ? p.Substring(s + 1, d - s - 1) : p;
            } catch (Exception) { return null; }
        }
        public static bool Load(string name) {
            try { UnityEngine.SceneManagement.SceneManager.LoadScene(name); return true; } catch (Exception e) { HostLog.Warn("跳转失败 " + name + ": " + e.Message); return false; }
        }
        public static bool LoadIndex(int i) {
            try { UnityEngine.SceneManagement.SceneManager.LoadScene(i); return true; } catch (Exception e) { HostLog.Warn("跳转失败 #" + i + ": " + e.Message); return false; }
        }
    }
}
