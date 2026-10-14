# ICEY 自建外壳 · Mod 框架与调试台（API v2）

## 0. 架构

```
┌─ Java 外壳（APK 内 classes.dex）────────────────────────────┐
│ com.icey.shell.LoaderActivity   启动项：沉浸式 + 起 WebUI + Toast │
│ com.icey.shell.web.{MiniHttp,WebUi,ModManager,DebugBridge}   │
│ com.icey.shell.mod.{IceyMod,ModLoader,ModContext}            │  ← Java mod（dex/jar）
└──────────────────────────┬──────────────────────────────────┘
                           │ DebugBridge（静态方法 + 命令队列 + 状态快照）
┌──────────────────────────┴──────────────────────────────────┐
│ C# 宿主 IceyModHost.dll（由注入 Assembly-CSharp 的桩自动加载）│
│  IIceyMod 生命周期 / ModGui 悬浮窗 / Reflect 反射 / Pin 锁定   │
│  SceneUtil 场景 / HostLog 日志 / WebBridge(Java 桥)           │  ← C# mod（dll）
└─────────────────────────────────────────────────────────────┘
                           │
                  调试台 mod：IceyDebugConsole.dll（本仓库首个 mod）
```

## 1. 安装 / 生效流程

| 步骤 | 说明 |
|---|---|
| 安装 APK | `Download/ICEY_1.1.1_selfcontained.apk`（自包含，含全部游戏数据，无需 OBB） |
| 放 mod | C# mod（.dll）与 Java mod（.dex/.jar）都放 `/sdcard/ICEYMods/`，并在同目录 `mods.txt` 登记 |
| 生效 | **重启应用**（mod 只在启动时加载） |
| WebUI | 启动时 Toast 显示地址，形如 `http://192.168.x.x:8080/`；也可在调试台 Web 页查看 |

`mods.txt` 每行：`<文件名> <类全名>`；`#` 开头为停用（WebUI 模块页可一键切换）。

```
IceyDebugConsole.dll IceyDebugConsole.DebugConsole
```

> `IceyModHost.dll` 由外壳注入的桩自动加载（优先 `/sdcard/ICEYMods/IceyModHost.dll`，回退 APK 内 `Managed/IceyModHost.dll`），**不要**登记到 `mods.txt`。

## 2. C# mod 接口（API v2）

```csharp
namespace IceyModHost {
  public interface IIceyMod {
    void OnLoad(ModContext ctx);    // 启动时
    void OnUpdate(ModContext ctx);  // 每帧
    void OnGui(ModContext ctx);     // IMGUI 绘制（已在缩放矩阵内）
    void OnUnload(ModContext ctx);  // 退出
  }
  public sealed class ModContext {
    public const int ApiVersion = 2;
    public readonly string ModName, ModsDir;
    public readonly MonoBehaviour Host;      // 可 StartCoroutine
    public void Log(string msg);
    public void OnCommand(Action<string> handler);  // 订阅 WebUI 命令行
    public void RegisterWebRoute(string prefix);    // 声明 /<prefix>/（页面用 mods/webui/<prefix>/）
    public void Publish(string json);               // 状态推给 WebUI（快照 modsState.<ModName>）
    public GameObject NewGameObject(string name);
  }
  public static class ModGui { public static float Scale; public static bool Ball(ref Rect,string);
                               public static bool Window(int id, ref Rect, string title, GUI.WindowFunction); }
  public static class Reflect { public static List<Type> Find(string,int); public static bool Resolve/Set(...);
                                public static object Singleton(Type); public static string Format(object); }
  public static class Pin { public static string Add(string path,string value,bool locked); public static string Remove(string path); }
  public static class SceneUtil { public static int Count; public static string Current; public static string NameAt(int);
                                  public static bool Load(string); public static bool LoadIndex(int); }
  public static class HostLog { public static void Info/Warn/Error(string); public static string Dump(int); }
  public static class WebBridge { public static bool Ready; public static string LocalUrl(); }
}
```

编译（目标框架必须与游戏一致：**.NET 2.0 profile**）：

```bash
mcs -target:library -sdk:2 -out=MyMod.dll \
    -r:UnityEngine.dll -r:System.Core.dll -r:IceyModHost.dll MyMod.cs
```

## 3. Java mod 接口

```java
public interface IceyMod { void onBeforeUnity(ModContext); void onAfterUnity(ModContext);
                           void onResume(ModContext); void onPause(ModContext); void onDestroy(ModContext); }
class ModContext { Activity activity; File modsDir, dataDir; ClassLoader classLoader;
                   int apiVersion(); void log(String); Class<?> findClass(String);
                   void webRegister(String prefix, WebHandler h);   // /<prefix>/...
                   void publish(String json); }
```

## 4. 调试台 mod（IceyDebugConsole.dll）

游戏内右下角出现「调试」悬浮球（可拖动），点开为可拖动窗口，分页：

| 分页 | 功能 |
|---|---|
| 时间 | 时间缩放滑条（0.05x–5x）、暂停/继续、0.25/0.5/1/2/5x 快捷键、慢动作练招 0.3x、目标帧率（30/60/90/120/不限）、界面缩放、全屏切换 |
| 数值 | 锁定列表（每帧写回=真·锁定）、按类名搜索 → 列出静态字段/属性并一键锁定、任意成员路径直接锁定/写入 |
| 场景 | 当前场景、全部场景列表一键跳转、重载当前场景 |
| 存档 | 解析 `PlayerPrefs`（键/类型/值）→ 改值写回并 Save；一键备份存档到 `/sdcard/ICEYBackup/<时间戳>/` |
| 解锁 | 按关键词（Unlock/Achievement/AddHp/Invincible…）搜方法并手动调用（不自动批量，避免误伤存档） |
| 日志 | 内存日志（含 Unity 日志），支持过滤 |
| Web | 显示 WebUI 地址与命令示例 |

### 命令协议（WebUI `/api/cmd`、调试台「命令」框同一套）

```
timeScale <f>        变速（0–20）
pause | resume       暂停 / 继续
scene <名字|序号>     跳转场景        reload   重载当前场景
pin <路径> [值]       锁定数值（省略值则取当前值）
unpin <路径>         解除锁定
set <路径> <值>       单次写入
call <类型.方法>      调用方法（0–2 参数自动填默认值）
prefs                重新读取存档项    backup   备份存档
scale <倍数>         界面缩放（宿主内置）  mods / help
```

路径写法：`类型全名.成员`，例：`Time.timeScale`、`GameWorld.PlayerManager.hp`（实例成员会自动 FindObjectOfType / Instance 单例定位）。

## 5. WebUI 端点（Java 外壳，端口 8080-8099 自动选）

| 端点 | 说明 |
|---|---|
| `GET /` | 外壳主页（模块管理 / 调试台 / 日志 / 设备） |
| `GET /api/status` | 宿主是否连接、运行时长、请求数、模块路由 |
| `GET /api/state` | C# 宿主状态快照（场景、timeScale、fps、pins、`modsState.debug`…） |
| `POST /api/cmd` | 下发命令行（见上） |
| `GET /api/log?lines=N` | logcat + 内存日志 |
| `GET /api/mods` | 模块文件列表（含是否登记/启用） |
| `POST /api/mods/upload?name=x` | 上传模块文件（.dll/.dex/.jar/.zip，zip 自动解包并合并 mods.txt） |
| `POST /api/mods/delete?name=x` / `POST /api/mods/toggle?name=x` | 删除 / 启用停用 |
| `GET|POST /api/mods/txt` | 读取/保存 mods.txt |
| `POST /api/restart` | 请求重启应用 |
| `GET /api/device` | 机型/Android 版本/ABI/时间 |
| `GET /<prefix>/…` | 模块自定义：Java mod 用 `webRegister`；C# mod 用 `RegisterWebRoute`，页面放 `<mods>/webui/<prefix>/index.html`（调试台即 `/debug/`） |

## 6. 排错

```bash
logcat -s ICEY-SHELL ICEY-MOD ICEY-WEB Unity AndroidRuntime      # 手机端 Termux 里直接看
```
- 没有「调试」小球 → 宿主 DLL 未加载：检查 `/sdcard/ICEYMods/IceyModHost.dll` 是否存在、logcat 是否有 `[IceyModHost]` 行。
- WebUI 打不开 → Toast 里给了端口；换端口重试，或看 `ICEY-WEB` 日志。
- 改 mod 后无变化 → 必须重启应用。
- 需要完全回退 → `out/legacy/` 里的旧包（不含 C# 宿主）。

## 7. 目录布局

```
/sdcard/ICEYMods/
├── IceyModHost.dll              # C# 宿主（可热更新）
├── IceyDebugConsole.dll         # 调试台 mod
├── mods.txt                     # 登记表（# = 停用）
└── webui/debug/index.html       # 调试台的 Web 页面（/debug/）
/sdcard/ICEYBackup/<时间戳>/      # 调试台备份的存档
```
