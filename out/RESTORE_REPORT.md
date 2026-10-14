# ICEY（艾希）第三方重打包 → 原始游戏提取与「仿原包」复原报告

生成环境：aarch64 Linux；工具链 apktool 3.0.3 / aapt2 2.19-104.2.0.210C00(aarch64) / smali·baksmali 2.5.2 / javac 21 + d8 8.2.2 / zipalign / apksigner / UnityPy 1.25.4。

---

## 1. 输入分析

四个附件实际只有 **2 个唯一包**（前三个 sha256 完全相同）：

| 附件 | 大小 | sha256(前 8) | 备注 |
|---|---|---|---|
| `com.fantablade.icey_6db52af6.apk` | 355,488,838 | `a62e4e02` | 同一文件 |
| `20231022193508_efdffee8….apk` | 355,488,838 | `a62e4e02` | 同一文件 |
| `com.fantablade.icey.apk` | 355,488,838 | `a62e4e02` | 同一文件 |
| `icey.apk` | 339,997,147 | `79ea816a` | 另一个包 |

| 项目 | 包 A（3 份相同） | 包 B（`icey.apk`） |
|---|---|---|
| 包名 / 版本 | com.fantablade.icey / **19 / 1.1.1** | com.fantablade.icey / **16 / 1.0.5** |
| 应用名 | 艾希 | ICEY |
| Unity 版本 | **5.6.6f2**（个别资源包 5.6.4p1） | **5.6.1f1**（个别 5.6.0p2） |
| 签名者 | `CN=ag, OU=ag, O=ag…`（伪造证书，仅 v1） | `CN=马煜, OU=葫芦侠, O=葫芦侠, L=重庆, ST=石柱县, C=CN` |
| 条目数 | 106（2 个 dex；OBB 内嵌于 assets） | 7,793（1 个 dex；OBB 内容已并入 assets） |
| 重打包来源 | PlayMods（含 `com.playmods.*` 代码） | 葫芦侠（仅重签名 + 整合，无代码注入） |

---

## 2. 原始包形态判定（关键结论）

**官方发行形态 = APK（核心数据）+ APK 扩展文件 OBB**：

- `assets/bin/Data/` 为 Unity 数据目录，官方把它拆成两部分：APK 内只放"引导必需"的核心文件，其余全部放进 OBB。
  - 包 A 内嵌的 OBB 名为 `main.19.com.fantablade.icey.obb`（Play 扩展文件命名 = `main.<versionCode>.<packageName>.obb`），与 versionCode 19 吻合，且是完整可用的 ZIP（8,910 条目）。
  - 包 B 的数据目录 = APK 核心 ∪ OBB 内容（被整合者合并），两者互补、无重叠。
- 数据布局（两个版本一致）：
  - `<32位十六进制>`：按哈希命名的 Unity 序列化文件（资源包），每份配套 `<同名>.resource`（FSB5 音频流数据）——已用"同名文件内确实引用自身 `.resource` 名"验证配对关系；
  - `level0…level167`：场景；`sharedassetsN.assets`：Resources 共享资源；
  - 视频位于 `assets/Movies/ps4/**`（技能/教程/过场 mp4）。
- **`.splitN` 分块是官方构建产物，不是第三方所为**，证据：
  1. 它同时存在于包 A 内嵌的 OBB（官方扩展文件）与包 B 的整合数据中；
  2. 两个来源的"扁平/分块"索引集合几乎完全一致——**扁平 67 个索引完全相同**，分块集合仅差 index 0（该文件在 APK 核心内），且不存在任何索引同时以两种形态出现；
  3. 串接 `.splitN` 后的文件头声明的总长度与实际长度逐一吻合。
  4. 分块规则：>1MB 的 `sharedassetsN.assets`（及 `level158`）按 **1MB** 切块。

---

## 3. 第三方注入物清单

### 包 A（PlayMods，v1.1.1）
| 注入项 | 内容 |
|---|---|
| `classes2.dex`（整份，102 类） | `com.gamein.*`（WelcomeActivity 启动器 + SDK）、`a.a.a.*`（混淆库） |
| `classes.dex` 内新增类 | `com.playmods.net.myxxdialog.MyDialogClass`（弹窗）、`com.unity.vq.e`（伪装 Unity 包名的上报/协议类）、`com.unity.cf.r`（APK 签名校验） |
| `classes.dex` 被改写处 | `FBUnityActivity.onCreate` 首行插入 `e.r(this);`（触发弹窗 + 上报） |
| 上报端点（Base64+移位混淆，已解出） | `api.gooddatagame.com` / `api.comedatagood.com` / `api.godatacome.com` / `api.databecome.com` / `api.joydatago.com`，路径 `/apicenter/<action>/<token>`，硬编码 token `1113a4d40917420d81bb6f20b6a0932f`，上报字段含包名、version_name `2018090518`、version_code |
| 清单 | 新增 `com.gamein.i.a.WelcomeActivity` 抢占 MAIN/LAUNCHER；`<meta-data name="gamein_launcher_activity">`；`application` 增加 `android:networkSecurityConfig`；文件末尾追加 `WRITE/READ_EXTERNAL_STORAGE`；`compileSdkVersion=31`（被 aapt2 重编译的痕迹） |
| 资源 | `res/xml/network_security_config.xml` |
| assets | `assets/sdcard/Android/obb/…/main.19.….obb`（331,281,133 字节的内嵌副本，供外部安装器搬到 OBB 目录） |

### 包 B（葫芦侠，v1.0.5）
- **无任何代码/清单/资源注入**；`com.d.a.a.*`、`com.e.*`、`com.b.a.a.*`、`com.c.a` 等混淆包经引用关系分析均被原始 SDK（TapTap SDK、Tyrantdb 统计、AIDL 设备标识）使用，属原始代码。
- 与官方发行版的差异仅为：**重签名**，以及把 OBB 内容合并进 APK。

---

## 4. 提取与还原过程

1. **解包**：`apktool d --no-assets`（apktool 自带 aapt2 为 x86_64，在本机 SIGILL，改用系统 aarch64 aapt2）。
2. **清理包 A**：
   - 删除 `smali_classes2/`（整份 gamein 包装层）、`smali/com/unity/vq`、`smali/com/unity/cf`、`smali/com/playmods`；
   - 删除 `res/xml/network_security_config.xml` 及 `public.xml` 中对应条目；
   - 补丁 `FBUnityActivity.smali`：移除 `invoke-static {p0}, Lcom/unity/vq/e;->r(Landroid/content/Context;)V`；
   - 重写清单：启动项还原为 `com.fantablade.icey.FBUnityActivity`（MAIN + LAUNCHER + LEANBACK），移除 WelcomeActivity / gamein 元数据 / networkSecurityConfig，写回 `versionCode=19 versionName=1.1.1`。
3. **重建 dex**：`smali a smali/ -o classes.dex` → 254,196 字节，242 个类，注入类 0 个。
4. **重建资源**：aapt2 `compile --legacy` + `link`（`--compile-sdk-version-code 23 --compile-sdk-version-name 6.0-2438415`，与原包 `platformBuildVersion*` 自洽）→ resources.arsc 24,448 字节。
5. **装配**：重建的清单/资源 + 修补 dex + 原包 `lib/**` + 原包 `assets/bin/Data/**`（剔除内嵌 OBB 副本）；
6. **对齐与签名**：`zipalign -p 4` + `apksigner`（v1+v2，新密钥 `out/icey-restored.jks`，口令 `iceyrestore`）。

### 分块还原与完整性验证
- 串接 `.splitN`（按序号）→ 还原 **102 个逻辑文件**（`sharedassetsN.assets`、`level158`）。
- 头部声明长度 vs 实际长度：**v1.1.1 5,421/5,421 通过、0 不符**；**v1.0.5 4,730/4,730 通过、0 不符**。
- UnityPy 全量解析：**10,151 个对象文件全部成功、0 失败**（2 个为无对象文件）；抽样：`level1` 2,995 个对象、`globalgamemanagers` 22 个对象。

---

## 5. 交付物

| 文件 | 大小 | sha256 | 说明 |
|---|---|---|---|
| `ICEY_1.1.1_selfcontained.apk` | 366,714,340 | `2de66ab4…` | **唯一安装包**：自建外壳（含 mod 接口）+ 静态分析得到的精简 Java 集 + 原始 APK 核心 + 原始 OBB 全部内容；单文件自包含，**不需要手动放 OBB** |
| `main.19.com.fantablade.icey.obb` | 331,281,133 | `3b70fdc8…` | 原始 OBB 原样保留（数据来源与对照物，字节未改动） |
| `icey-restored.jks` | 2,708 | `f40bcc6c…` | 签名密钥（RSA2048，口令 `iceyrestore`，别名 `icey`） |
| `extracted/` | — | — | 提取出的原始游戏负载：`shipped/`（原样，含官方 `.splitN`）+ `unity_data/`（分块已还原）+ `apk/`（dex、lib） |
| `mod-example/` | 1,692 | `7a9741f2…` | 示例 mod：`example-mod.dex` + `mods.txt` |
| `MOD_INTERFACE.md` | — | — | mod 接口（API v1）说明 |
| `legacy/` | — | — | 早期用 ag / 葫芦侠 dex 做的产物（存档保留，已不属交付范围） |

`extracted/v1.1.1/`、`extracted/v1.0.5/` 结构：
- `shipped/`：**原样**负载（保留官方 `.splitN` 分块，含 `Managed/*.dll`、`Resources/unity_builtin_extra`、`settings.xml`）；
- `unity_data/`：**逻辑视图**（分块已串接，可直接用 UnityPy / AssetStudio / UABEA 打开）；
- `apk/`：`classes.dex`（v1.1.1 为去注入版）与原生库。

### 安装说明
- 原包形态：先安装 APK，再把 `main.19.com.fantablade.icey.obb` 放到
  `/sdcard/Android/obb/com.fantablade.icey/main.19.com.fantablade.icey.obb`
- 整合/外壳整合形态：直接安装，无需 OBB。
- 外壳 + OBB 形态：安装外壳后放 OBB；缺少时外壳会 Toast 提示目标路径（不阻断启动）。
- 与原版**签名不同**，安装前请卸载设备上同包名的任何版本。

---

## 6. 自建加载器外壳的实现（含 Mod 接口）

目标：入口层完全自建；**不使用 ag / 葫芦侠包里的 Java**；游戏内容只用原始资源。

```
自建：AndroidManifest.xml（启动项 = com.icey.shell.LoaderActivity）
      res/（最小资源：strings/styles + 复用原包图标）
      com.icey.shell.LoaderActivity            ← javac → d8 → baksmali → 并入主 dex
      com.icey.shell.mod.{IceyMod,BaseMod,ModContext,ModLoader}   ← Mod 接口（API v1）
复用：com.unity3d.player.*、bitter.jnibridge.*、org.fmod.*（Unity 引擎 Java，运行必需）
      lib/{armeabi-v7a,x86}/{libunity,libmono,libmain,libAVProLocal}.so
      assets/bin/Data/**（APK 核心 + 全部 OBB 内容）
静态分析确认保留：com.xindong.tyrantdb.*（C# 调 TyrantdbGameTracker）、
      com.RenderHeads.AVProVideo.*（C# 调 AVProMobileVideo，过场视频）、
      com.b.a.* / com.xindong.a.* / android.support.v4 的 6 个类（上述两者的依赖闭包）
```

**Java 精简依据（静态分析，非猜测）**：
- C# 程序集里出现的 Java 类名（UTF-16 字面量）只有：
  `com.unity3d.player.UnityPlayer`、`com.xindong.tyrantdb.TyrantdbGameTracker`（含 `$TGTUserSex/$TGTUserType`）、
  `com.xd.taptapSDK.TapTapUtil/TapTapSDKListener`、`com.RenderHeads.AVProVideo.AVProMobileVideo`。
  其中 TapTap 类**在官方 v1.1.1 的 dex 里本就不存在**（发行方遗留引用），故不补入（更不从葫芦侠包取）。
- 以"引擎类全体 + org.fmod + 上述 C# 引用类"为种子做引用传递闭包 → 115 类；
  实测原有 **243 类 → 保留 115 + 外壳 5 = 120 类**。
- 删除的包：`com.fantablade.*`（发行方 Activity 体系，外壳已取代）、`com.a.a.a.a.*` + `com.android.vending.*` 中的 LVL 主体、
  `android.support.v4/appcompat` 其余 83 类、`android.arch.lifecycle` 14 类，以及全部第三方注入类。
- 原生库核对：`libunity.so`/`libmono.so` 只有 FMOD 音频格式字符串、无 JNI 类名；`libAVProLocal.so` 仅含 `RenderHeads` 字样。
  `org/fmod/**` 虽无 Java 引用，但属 Unity 音频胶水，**保留**（2 个类，代价可忽略、避免静音风险）。

**Mod 接口**：外壳在 `onCreate` 时扫描
`/sdcard/Android/data/com.fantablade.icey/files/mods/`（及 `/sdcard/ICEYMods/`），
读取 `mods.txt`（`<dex文件名> <类全名>`），用 `DexClassLoader` 载入并回调 5 个生命周期钩子
（`onBeforeUnity` / `onAfterUnity` / `onResume` / `onPause` / `onDestroy`）。
无 mod 时空转；任一 mod 异常仅写 `logcat (ICEY-MOD)`，不影响启动。
`LoaderActivity.getModsDir(Context)` 为 C# 侧预留的静态接入点。
详见 `MOD_INTERFACE.md`，附可用的 `mod-example/`。

**其他要点**：单 dex 约束（minSdk 16 时 API<21 只加载 `classes.dex`，故外壳类必须进主 dex，
做法是 `d8 → baksmali → 与引擎类一起 smali 汇编`）；包名保持 `com.fantablade.icey`
（Unity 以 `getPackageName()` 推导 OBB 路径）；编译期用桩类定位 `UnityPlayerActivity` 签名。
复核：最终 dex 120 类，`com/fantablade`/`com.a.a`/`com/gamein`/`com/playmods`/`com/unity.vq`/`android/arch` 均为 0；
badging 启动项 = `com.icey.shell.LoaderActivity`；v1+v2 签名通过。

---

## 7. 未能验证 / 不确定性

1. **无法恢复原始签名**：原包私钥不可得，只能用新密钥重签；这也会让原包内嵌的 Play 许可（LVL）校验必然失败——但原始代码的许可回调仅打印日志（`Allow the user access: …`），不影响进入游戏。
2. **注入物是"删除"而非"用官方原版逐字节还原"**：官方 v1.1.1 dex 不可得；包 A 的 `classes.dex` 本身被改写（新增 3 个类 + 改写 1 行），我们移除了这些痕迹，但无法逐字节比对官方 dex。
3. **包 A 的 OBB 是否被第三方改动无法 100% 排除**：其内部 `.resource`/视频条目时间戳为 1980 且为 store 压缩，Unity 数据为 2018-07-24 且 deflate——判断为官方打包工具的分组行为（`.resource` 为 Unity 流式资源、视频本已压缩），并与包 B 的同族布局一致；但缺少官方 OBB 做逐字节比对。
4. **清单元数据为重建产物**：`platformBuildVersionCode/Name` 与 `compileSdkVersion` 由 aapt2 依 `--compile-sdk-version-code 23` 生成（与原包保留的 `23 / 6.0-2438415` 自洽），与官方二进制清单可能存在细节差异。
5. **未做真机/模拟器实测**：本环境无 Android 运行环境，所有验证均为静态（签名校验、清单解析、dex 类清单、Unity 数据完整性与 UnityPy 可解析性）。真实运行表现需要设备验证。
6. **仅 32 位原生库**（armeabi-v7a / x86，与官方包一致）：纯 64 位设备（如 Pixel 7 及以后）无法安装运行，这不是复原引入的问题。
7. 包 A 清单末尾的 `WRITE/READ_EXTERNAL_STORAGE` 保留未删：它们是 OBB 读取/官方 Unity 分包构建的常见权限（v1.0.5 原版清单亦有），删除反而可能影响 OBB 读取。
8. **Java 精简未做真机验证**：闭包基于静态引用图，删除了 127 个类（发行方 Activity、LVL、support 库主体等）。
   风险点在于**仅由原生层按名反射**调用的类（已核查 libunity/libmono/libAVProLocal 中的类名字符串，
   未发现除 `org/fmod` 之外的线索，而 `org/fmod` 已保留）；若真机上出现 `ClassNotFoundException`，
   可回退 `legacy/ICEY_1.1.1_shell_merged.apk`（含发行方全量类）。
9. **自包含包不需要 OBB**：其 `assets/bin/Data` 已包含 APK 核心 + OBB 全部内容（该形态已被包 B 的发行实践验证）。
   外壳不再对缺失 OBB 报警；设备上若残留同版本 OBB，Unity 会照常挂载，内容一致、无副作用。
10. 早期用 ag/葫芦侠 dex 的产物已移入 `legacy/`（含校验和），仅作回退/对照，不再属于交付范围。

---

## 8. 复现脚本（`work/`）

| 脚本 | 作用 |
|---|---|
| `work/build_restored_v111.py` | 装配 v1.1.1 原包形态 APK |
| `work/build_merged_v111.py` | 装配 v1.1.1 整合形态 APK |
| `work/build_shell.py merged\|core` | 装配自建外壳 APK |
| `work/extract_game.py` | 提取负载、串接 `.splitN`、校验 Unity 头 |
| `work/shell/src/…/IceyShellActivity.java` | 外壳 Activity 源码 |
| `work/A_decoded/` | 包 A 的 apktool 解码树（清单/资源/smali，含全部清理改动） |
| `work/build_selfcontained.py` | 装配自包含包（外壳 + 精简 dex + 原始核心/ OBB 数据） |
| `work/shell2/src/…` | 新外壳与 mod 接口源码（`com.icey.shell.*`） |
| `work/keep_classes.txt` / `drop_classes.txt` | 静态分析得到的保留/删除类清单 |
| `work/lean_smali/` | 精简后的 smali 树 |
