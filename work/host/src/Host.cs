using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace IceyModHost {

    /// <summary>由注入到 Assembly-CSharp 的桩调用（RuntimeInitializeOnLoadMethod 时机）。</summary>
    public static class Bootstrap {
        private static bool _inited;
        public static void Init() {
            if (_inited) return;
            try {
                GameObject go = new GameObject("IceyModHost");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<ModHostBehaviour>();
                _inited = true;
                HostLog.Info("宿主启动成功 API v2");
            } catch (Exception e) {
                // 引擎未就绪时把异常抛回桩，桩会在下一个挂钩处重试
                HostLog.FileLog("宿主启动失败(等待重试): " + e.GetType().Name + ": " + e.Message);
                throw e;
            }
        }
    }


    /// <summary>宿主与调试台 mod 的最小约定：面板开关请求、UI 开关命令。</summary>
    public static class ModConsoleBridge {
        public static bool ToggleRequested;
        public static bool ShowRequested;
        public static bool HideRequested;
    }

    /// <summary>防点击穿透：触摸落在我们界面上时，把游戏输入处理器的状态压成“未按下”（不依赖会抛异常的 InputSetting.Stop）。</summary>
    public static class InputShield {
        private static bool _blocked, _unavailable;
        private static Type _proc;
        private static FieldInfo _fOpen, _fPressed, _fWasPressed, _fJoystick;
        private static readonly List<object> _procs = new List<object>();
        private static bool _savedJoystick = true;

        public static bool Blocked { get { return _blocked; } }

        private static void Resolve() {
            if (_proc != null) return;
            Type core = Reflect.TypeByFullName("Core.Input");
            if (core == null) return;
            _fJoystick = core.GetField("JoystickIsOpen", BindingFlags.Public | BindingFlags.Static);
            Type[] nested = new Type[] { core.GetNestedType("Game", BindingFlags.Public | BindingFlags.NonPublic), core.GetNestedType("UI", BindingFlags.Public | BindingFlags.NonPublic) };
            for (int i = 0; i < nested.Length; i++) {
                Type nt = nested[i];
                if (nt == null) continue;
                FieldInfo[] fs = nt.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int j = 0; j < fs.Length; j++) {
                    if (!typeof(object).IsAssignableFrom(fs[j].FieldType)) continue;
                    if (fs[j].FieldType.Name != "InputButtonProcessor") continue;
                    object v = null;
                    try { v = fs[j].GetValue(null); } catch (Exception) { }
                    if (v == null) continue;
                    _proc = fs[j].FieldType;
                    _procs.Add(v);
                }
            }
            if (_proc != null) {
                _fOpen = _proc.GetField("_isOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _fPressed = _proc.GetField("_isPressed", BindingFlags.NonPublic | BindingFlags.Instance);
                _fWasPressed = _proc.GetField("_wasPressed", BindingFlags.NonPublic | BindingFlags.Instance);
                HostLog.Info("输入闸门就绪：接管 " + _procs.Count + " 个按钮处理器");
            }
        }

        public static void Set(bool block) {
            if (block == _blocked) return;
            _blocked = block;
            if (_unavailable) return;
            try {
                Resolve();
                if (_proc == null) { _unavailable = true; HostLog.Warn("输入闸门不可用：没找到 Core.Input 的按钮处理器（防穿透关闭）"); return; }
                if (block) {
                    if (_fJoystick != null) { try { _savedJoystick = (bool)_fJoystick.GetValue(null); _fJoystick.SetValue(null, false); } catch (Exception) { } }
                }
                for (int i = 0; i < _procs.Count; i++) {
                    object p = _procs[i];
                    if (_fOpen != null) _fOpen.SetValue(p, !block);
                    if (block) {
                        if (_fPressed != null) _fPressed.SetValue(p, false);
                        if (_fWasPressed != null) _fWasPressed.SetValue(p, false);
                    }
                }
                if (!block && _fJoystick != null) { try { _fJoystick.SetValue(null, _savedJoystick); } catch (Exception) { } }
            } catch (Exception e) {
                _unavailable = true;
                HostLog.Warn("输入闸门异常（已停用防穿透）: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    /// <summary>宿主：加载 mod、每帧回调、绘制、与 WebUI 通信。</summary>
    public class ModHostBehaviour : MonoBehaviour {
        public static ModHostBehaviour Instance;
        internal static readonly List<IIceyMod> Mods = new List<IIceyMod>();
        internal static readonly List<ModContext> Contexts = new List<ModContext>();
        internal static string ModsDir = "";

        private bool _loaded;
        private float _tSnapshot, _tCmd, _tPin, _guiErrAt;

        void Awake() {
            Instance = this;
            try { Application.logMessageReceived += OnUnityLog; } catch (Exception) { }
        }

        void Start() {
            try { WebBridge.Init(); } catch (Exception e) { HostLog.Warn("WebBridge: " + e.Message); }
            TryLoadMods();
        }

        private void OnUnityLog(string condition, string stack, LogType type) {
            string tag = type == LogType.Error || type == LogType.Exception ? "E" : (type == LogType.Warning ? "W" : "I");
            WebBridge.PushJavaLog(DateTime.Now.ToString("HH:mm:ss") + " U" + tag + " " + condition);
        }

        public const string MAIN_PKG = "com.fantablade.icey";

        /** 从 persistentDataPath（/…/Android/data/<包名>/files）解析包名；Unity 5.6 没有 bundleIdentifier。 */
        public static string PkgName() {
            try {
                string d = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(d)) {
                    string[] parts = d.Replace('\\', '/').Split('/');
                    for (int i = 0; i < parts.Length - 1; i++) if (parts[i] == "data") return parts[i + 1];
                    if (parts.Length >= 2) return parts[parts.Length - 2];
                }
            } catch (Exception) { }
            return MAIN_PKG;
        }

        public static bool IsMainPkg() { return PkgName() == MAIN_PKG; }

        /** 主版用 /sdcard/ICEYMods；共存版用独立目录 /sdcard/ICEYModsCo（互不共用）。 */
        public static string ModRootName() { return IsMainPkg() ? "ICEYMods" : "ICEYModsCo"; }

        public static List<string> ModDirs() {
            string root = ModRootName();
            List<string> list = new List<string>();
            list.Add("/sdcard/" + root);
            list.Add("/storage/emulated/0/" + root);
            try { list.Add(Path.Combine(Application.persistentDataPath, "mods")); } catch (Exception) { }
            return list;
        }

        private static readonly List<string> SeenDirs = new List<string>();
        private static readonly List<string> LoadedFiles = new List<string>();
        private static readonly List<string> LoadedTypes = new List<string>();

        /** 归一化路径：/sdcard 与 /storage/emulated/0 视为同一处，避免重复扫描。 */
        private static string Canonical(string path) {
            try {
                string p = Path.GetFullPath(path).TrimEnd('/');
                if (p.StartsWith("/sdcard/")) p = "/storage/emulated/0/" + p.Substring(8);
                return p;
            } catch (Exception) { return null; }
        }

        public void TryLoadMods() {
            if (_loaded) return;
            _loaded = true;
            EnsureEmbeddedMods();
            List<string> dirs = ModDirs();
            for (int i = 0; i < dirs.Count; i++) ModsDir = dirs[i];
            try { Reflect.Index(AppDomain.CurrentDomain.GetAssemblies()); } catch (Exception e) { HostLog.Warn("索引类型失败: " + e.Message); }
            for (int d = 0; d < dirs.Count; d++) {
                string dir = dirs[d];
                string key = Canonical(dir);
                if (key == null || SeenDirs.Contains(key)) continue;
                SeenDirs.Add(key);
                try {
                    if (!Directory.Exists(dir)) continue;
                    HostLog.Info("扫描模块目录 " + dir);
                    List<string> entries = ReadManifest(dir);
                    if (entries == null) entries = AllDlls(dir);
                    for (int i = 0; i < entries.Count; i++) LoadEntry(dir, entries[i]);
                } catch (Exception e) {
                    HostLog.Warn("目录处理失败 " + dir + ": " + e.Message);
                }
            }
            HostLog.Info("模块加载完成，共 " + Mods.Count + " 个");
        }

        /// <summary>把 APK 内打包的模块释放到可写目录（新装即用，无需 SD 卡文件）。</summary>
        public static void EnsureEmbeddedMods() {
            try {
                string baseUrl = "jar:file://" + Application.dataPath + "!/assets/bin/Data/Managed/";
                string[] names = { "mods.txt", "IceyDebugConsole.dll", "webui/debug/index.html" };
                List<string> dirs = ModDirs();
                string target = null;
                for (int i = 0; i < dirs.Count; i++) {
                    try { Directory.CreateDirectory(dirs[i]); target = dirs[i]; break; } catch (Exception) { }
                }
                if (target == null) { HostLog.Warn("无可用模块目录，跳过内置模块释放"); return; }
                for (int i = 0; i < names.Length; i++) {
                    string rel = names[i];
                    bool exists = false;
                    for (int d = 0; d < dirs.Count; d++) { try { if (File.Exists(Path.Combine(dirs[d], rel))) { exists = true; break; } } catch (Exception) { } }
                    if (exists) continue;
                    string dst = Path.Combine(target, rel);
                    try {
                        string dn = Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dn)) Directory.CreateDirectory(dn);
                        WWW w = new WWW(baseUrl + rel);
                        float t0 = Time.realtimeSinceStartup;
                        while (!w.isDone && Time.realtimeSinceStartup - t0 < 3f) System.Threading.Thread.Sleep(5);
                        if (!string.IsNullOrEmpty(w.error)) { HostLog.Warn("内置模块释放失败 " + rel + ": " + w.error); continue; }
                        File.WriteAllBytes(dst, w.bytes);
                        HostLog.Info("已从 APK 释放内置模块 " + rel + " → " + dst);
                    } catch (Exception e) { HostLog.Warn("释放内置模块异常 " + rel + ": " + e.Message); }
                }
            } catch (Exception e) { HostLog.Warn("内置模块流程异常: " + e.Message); }
        }

        /// <summary>mods.txt：每行 "x.dll" 或 "x.dll 命名空间.类型"，# 开头为停用。</summary>
        private static List<string> ReadManifest(string dir) {
            string f = Path.Combine(dir, "mods.txt");
            if (!File.Exists(f)) return null;
            List<string> outList = new List<string>();
            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++) {
                string s = lines[i].Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                outList.Add(s);
            }
            return outList;
        }

        private static List<string> AllDlls(string dir) {
            List<string> outList = new List<string>();
            try {
                string[] fs = Directory.GetFiles(dir, "*.dll");
                for (int i = 0; i < fs.Length; i++) outList.Add(Path.GetFileName(fs[i]));
            } catch (Exception) { }
            return outList;
        }

        private static void LoadEntry(string dir, string entry) {
            string file = entry; string onlyType = null;
            int sp = entry.IndexOf(' ');
            if (sp > 0) { file = entry.Substring(0, sp).Trim(); onlyType = entry.Substring(sp + 1).Trim(); }
            if (LoadedFiles.Contains(file)) { HostLog.Info("跳过重复模块文件 " + file); return; }
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) { HostLog.Warn("模块文件不存在: " + path); return; }
            LoadedFiles.Add(file);
            try {
                Assembly asm = Assembly.Load(File.ReadAllBytes(path));
                int n = 0;
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
                for (int i = 0; i < types.Length; i++) {
                    Type t = types[i];
                    if (t == null || t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IIceyMod).IsAssignableFrom(t)) continue;
                    if (onlyType != null && t.FullName != onlyType) continue;
                    if (LoadedTypes.Contains(t.FullName)) { HostLog.Info("跳过重复模块类型 " + t.FullName); continue; }
                    LoadedTypes.Add(t.FullName);
                    IIceyMod mod = (IIceyMod)Activator.CreateInstance(t);
                    ModContext ctx = new ModContext(t.Name, Instance, dir);
                    Mods.Add(mod); Contexts.Add(ctx);
                    mod.OnLoad(ctx);
                    n++;
                    HostLog.Info("已加载模块 " + t.FullName + " (" + file + ")");
                }
                if (n == 0) HostLog.Warn("未找到 IIceyMod 实现: " + file + (onlyType != null ? " / " + onlyType : ""));
            } catch (Exception e) {
                HostLog.Error("模块加载失败 " + file + ": " + e.Message);
            }
        }

        void Update() {
            try {
                if (Input.GetKeyDown(KeyCode.F1)) {
                    ModConsoleBridge.ToggleRequested = true;
                    HostLog.Info("F1：切换调试面板");
                }
            } catch (Exception) { }
            // 锁定值：每 300ms 重设一次（与调试窗口是否打开无关）
            _tPin += Time.unscaledDeltaTime;
            if (_tPin >= 0.3f) { _tPin = 0f; try { Pin.Apply(); } catch (Exception) { } }
            for (int i = 0; i < Mods.Count; i++) {
                try { Mods[i].OnUpdate(Contexts[i]); } catch (Exception e) { HostLog.Warn("OnUpdate 异常 " + e.Message); }
            }
            _tCmd += Time.unscaledDeltaTime;
            if (_tCmd >= 0.25f) { _tCmd = 0; PollCommands(); }
            _tSnapshot += Time.unscaledDeltaTime;
            if (_tSnapshot >= 1f) { _tSnapshot = 0; WebBridge.SetSnapshot(Snapshot()); }
        }

        private void PollCommands() {
            string[] cmds = WebBridge.Take();
            if (cmds == null) return;
            for (int i = 0; i < cmds.Length; i++) Dispatch(cmds[i]);
        }

        public static string Dispatch(string line) {
            string s = (line ?? "").Trim();
            if (s.Length == 0) return "空命令";
            string cmd = s, arg = "";
            int sp = s.IndexOf(' ');
            if (sp > 0) { cmd = s.Substring(0, sp).Trim().ToLowerInvariant(); arg = s.Substring(sp + 1).Trim(); }
            if (cmd == "help") {
                HostLog.Info("命令: help | mods | scale <倍数> | state | 其余命令转交 mod（详见 MOD_INTERFACE.md）");
                return "ok";
            }
            if (cmd == "mods") { HostLog.Info("已加载模块: " + Mods.Count); return "ok"; }
            if (cmd == "scale") {
                float f;
                if (float.TryParse(arg, out f) && f > 0.2f && f < 8f) { ModGui.Scale = f; return "缩放=" + f; }
                return "无效倍数";
            }
            if (cmd == "state") { HostLog.Info(WebBridge.LastSnapshot); return "ok"; }
            for (int i = 0; i < Contexts.Count; i++) if (Contexts[i].Dispatch(s)) return "已转交 " + Contexts[i].ModName;
            HostLog.Warn("未处理命令: " + s);
            return "未处理";
        }


        /// <summary>组装上报给 WebUI 的状态 JSON。</summary>
        private string Snapshot() {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"api\":2");
            sb.Append(",\"scene\":\"").Append(Esc(SceneUtil.Current)).Append('"');
            sb.Append(",\"timeScale\":").Append(Time.timeScale.ToString("0.###", ci));
            sb.Append(",\"fps\":").Append((Time.smoothDeltaTime > 0f ? 1f / Time.smoothDeltaTime : 0f).ToString("0.0", ci));
            sb.Append(",\"scale\":").Append(ModGui.Scale.ToString("0.##", ci));
            sb.Append(",\"uptime\":").Append(Time.realtimeSinceStartup.ToString("0", ci));
            sb.Append(",\"frame\":").Append(Time.frameCount);
            sb.Append(",\"sceneCount\":").Append(SceneUtil.Count);
            sb.Append(",\"mods\":[");
            for (int i = 0; i < Contexts.Count; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(Esc(Contexts[i].ModName)).Append('"'); }
            sb.Append("],\"pins\":").Append(Pin.ToJson());
            sb.Append(",\"modsState\":").Append(WebBridge.ModStatesJson());
            sb.Append('}');
            return sb.ToString();
        }

        private static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        void OnGUI() {
            try {
                ModGui.BeginScaled();
                ModGui.AutoCalibrate();
                ModGui.Rects.Clear();
                ModGui.InnerScrolls.Clear();
                for (int i = 0; i < Mods.Count; i++) {
                    try { Mods[i].OnGui(Contexts[i]); } catch (Exception) { }
                }
                // 触摸落在我们的界面上时屏蔽游戏输入，避免点击穿透
                try { InputShield.Set(ModGui.Interacting()); } catch (Exception) { }
                ModGui.EndScaled();
            } catch (Exception e) {
                if (Time.unscaledTime - _guiErrAt > 5f) { _guiErrAt = Time.unscaledTime; HostLog.Warn("界面绘制异常（已跳过本帧）: " + e.GetType().Name + ": " + e.Message); }
            }
        }

        void OnApplicationQuit() {
            for (int i = 0; i < Mods.Count; i++) { try { Mods[i].OnUnload(Contexts[i]); } catch (Exception) { } }
        }
    }

    /// <summary>C# 侧与 Java 外壳（com.icey.shell.web.DebugBridge）的桥。</summary>
    public static class WebBridge {
        private static AndroidJavaClass _bridge;
        private static readonly Dictionary<string, string> ModStates = new Dictionary<string, string>();
        public static bool Ready;
        public static string LastSnapshot = "{}";

        public static void Init() {
            try {
                _bridge = new AndroidJavaClass("com.icey.shell.web.DebugBridge");
                _bridge.CallStatic("setHostReady", true);
                _bridge.CallStatic("putLog", "C# 宿主已连接");
                Ready = true;
            } catch (Exception e) {
                HostLog.Warn("Java 桥不可用: " + e.Message);
            }
        }

        public static void PushJavaLog(string line) {
            try { if (_bridge != null) _bridge.CallStatic("putLog", line); } catch (Exception) { }
        }


        /** 读取 Java 外壳里 WebUI 的访问地址。 */
        public static string LocalUrl() {
            try { return _bridge == null ? "(Java 桥未连接)" : _bridge.CallStatic<string>("localUrl"); }
            catch (Exception) { return "(不可用)"; }
        }

        public static string[] Take() {
            try { return _bridge == null ? null : _bridge.CallStatic<string[]>("take"); } catch (Exception) { return null; }
        }

        public static void RegisterRoute(string mod, string prefix) {
            try {
                if (_bridge != null) _bridge.CallStatic("registerRouteName", prefix);
                HostLog.Info("模块 " + mod + " 声明 Web 路由 /" + prefix.Replace("/", "") + "/");
            } catch (Exception) { }
        }

        public static void Publish(string mod, string json) { ModStates[mod] = json; }

        public static void SetSnapshot(string json) {
            LastSnapshot = json;
            try { if (_bridge != null) _bridge.CallStatic("setSnapshot", json); } catch (Exception) { }
        }

        internal static string ModStatesJson() {
            StringBuilder sb = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, string> kv in ModStates) {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Esc(kv.Key)).Append("\":").Append(string.IsNullOrEmpty(kv.Value) ? "null" : kv.Value);
            }
            return sb.Append('}').ToString();
        }

        private static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }
}
