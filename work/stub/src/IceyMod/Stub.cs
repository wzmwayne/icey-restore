using System;
using System.IO;
using System.Reflection;

namespace IceyMod {
    /// <summary>
    /// 由 Cecil 注入到 Assembly-CSharp.dll 的引导桩（纯 BCL，不依赖 UnityEngine）。
    /// 第一步：找到并加载 IceyModHost.dll；第二步：调用 IceyModHost.Bootstrap.Init()。
    /// 宿主若因引擎尚未就绪而失败，下次挂钩会重试；所有过程写日志到 SD 卡与应用私有目录。
    /// </summary>
    public static class Bootstrap {
        private static bool _done;
        private static MethodInfo _mi;

        private static void Log(string s) {
            try { string d = "/sdcard/" + ModRoot(); System.IO.Directory.CreateDirectory(d); File.AppendAllText(d + "/icey_stub.log", s + "\n"); } catch (Exception) { }
            try { File.AppendAllText(DataRoot() + "/icey_stub.log", s + "\n"); } catch (Exception) { }
        }

        private static string Msg(Exception e) {
            if (e == null) return "-";
            Exception inner = e.InnerException;
            if (inner != null) return e.GetType().Name + ": " + e.Message + " >> " + inner.GetType().Name + ": " + inner.Message;
            return e.GetType().Name + ": " + e.Message;
        }

        private const string MAIN_PKG = "com.fantablade.icey";
        private static string[] _paths;

        private static string PkgName() {
            try {
                string d = UnityEngine.Application.persistentDataPath;
                if (!string.IsNullOrEmpty(d)) {
                    string[] parts = d.Replace('\\', '/').Split('/');
                    for (int i = 0; i < parts.Length - 1; i++) if (parts[i] == "data") return parts[i + 1];
                    if (parts.Length >= 2) return parts[parts.Length - 2];
                }
            } catch (Exception) { }
            return MAIN_PKG;
        }
        private static string ModRoot() { return IsMainPkg() ? "ICEYMods" : "ICEYModsCo"; }
        private static bool IsMainPkg() { return PkgName() == MAIN_PKG; }
        private static string DataRoot() {
            try { string d = UnityEngine.Application.persistentDataPath; if (!string.IsNullOrEmpty(d)) return d; } catch (Exception) { }
            return "/data/data/" + PkgName() + "/files";
        }

        /** 每个包各用各的目录（共存版不与主版共用）。 */
        private static void BuildPaths() {
            string pkg = PkgName();
            System.Collections.Generic.List<string> l = new System.Collections.Generic.List<string>();
            if (IsMainPkg()) {
                l.Add("/sdcard/ICEYMods/IceyModHost.dll");
                l.Add("/storage/emulated/0/ICEYMods/IceyModHost.dll");
            } else {
                l.Add("/sdcard/ICEYModsCo/IceyModHost.dll");
                l.Add("/storage/emulated/0/ICEYModsCo/IceyModHost.dll");
                l.Add(DataRoot() + "/mods/IceyModHost.dll");
            }
            l.Add(DataRoot() + "/mods/IceyModHost.dll");
            l.Add("/sdcard/Android/data/" + pkg + "/files/mods/IceyModHost.dll");
            l.Add("/storage/emulated/0/Android/data/" + pkg + "/files/mods/IceyModHost.dll");
            _paths = l.ToArray();
        }

        private static string PathOf(int i) {
            if (_paths == null) BuildPaths();
            return i < _paths.Length ? _paths[i] : null;
        }

        private static MethodInfo Fetch(Assembly a) {
            if (a == null) return null;
            Type t = a.GetType("IceyModHost.Bootstrap");
            if (t == null) return null;
            return t.GetMethod("Init");
        }

        private static void Ensure() {
            Log("stub: Ensure 开始");
            for (int i = 0; i < 4; i++) {
                if (_mi != null) return;
                string p = PathOf(i);
                if (p == null) continue;
                try {
                    byte[] b = File.ReadAllBytes(p);
                    _mi = Fetch(Assembly.Load(b));
                    if (_mi != null) { Log("stub: 以 bytes 方式加载成功 " + p); return; }
                    Log("stub: bytes 加载后未找到 IceyModHost.Bootstrap " + p);
                } catch (Exception e) {
                    Log("stub: bytes 失败 " + p + " :: " + Msg(e));
                }
                try {
                    _mi = Fetch(Assembly.LoadFrom(p));
                    if (_mi != null) { Log("stub: 以 LoadFrom 方式加载成功 " + p); return; }
                } catch (Exception e2) {
                    Log("stub: LoadFrom 失败 " + p + " :: " + Msg(e2));
                }
            }
            try {
                _mi = Fetch(Assembly.Load("IceyModHost"));
                if (_mi != null) { Log("stub: 使用 APK 内置宿主 DLL"); return; }
                Log("stub: 内置宿主 DLL 中未找到入口");
            } catch (Exception e3) {
                Log("stub: 内置宿主加载失败 :: " + Msg(e3));
            }
        }

        private static int _dbgFlag = -1;

        /** 调试模式开关：由文件控制（/sdcard/ICEYMods/debug_mode）。内容为空/1/true/on/yes = 开；0/false/off = 关。 */
        public static bool DebugFlag() {
            if (_dbgFlag >= 0) return _dbgFlag == 1;
            bool on = false;
            System.Collections.Generic.List<string> pl = new System.Collections.Generic.List<string>();
            if (IsMainPkg()) {                                        // 主版沿用原共享位置
                pl.Add("/sdcard/ICEYMods/debug_mode");
                pl.Add("/storage/emulated/0/ICEYMods/debug_mode");
                pl.Add(DataRoot() + "/debug_mode");
            } else {                                                  // 共存版只看自己的目录
                pl.Add("/sdcard/ICEYModsCo/debug_mode");
                pl.Add("/storage/emulated/0/ICEYModsCo/debug_mode");
                pl.Add(DataRoot() + "/debug_mode");
            }
            string[] ps = pl.ToArray();
            for (int i = 0; i < ps.Length; i++) {
                try {
                    if (!System.IO.File.Exists(ps[i])) continue;
                    string t = "";
                    try { t = System.IO.File.ReadAllText(ps[i]).Trim().ToLowerInvariant(); } catch (Exception) { }
                    on = (t.Length == 0 || t == "1" || t == "true" || t == "on" || t == "yes");
                    Log("调试模式文件 " + ps[i] + " → " + (on ? "开" : "关"));
                    break;
                } catch (Exception) { }
            }
            if (!on) Log("未找到 debug_mode 文件，按发布模式运行");
            _dbgFlag = on ? 1 : 0;
            return on;
        }

        public static void Init() {
            if (_done) return;
            try {
                if (_mi == null) Ensure();
                if (_mi == null) { Log("stub: 找不到宿主，放弃（下次挂钩会重试）"); return; }
                _mi.Invoke(null, null);
                _done = true;
                Log("stub: 宿主 Init 调用成功，引导完成");
            } catch (Exception e) {
                Log("stub: 宿主 Init 抛异常(将重试) :: " + Msg(e));
            }
        }
    }
}
