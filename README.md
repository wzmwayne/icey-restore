# ICEY (艾希) 复原 + 热加载 Mod 框架

从第三方重打包版本中提取原始游戏、复原仿原包，并搭建一套**不需要重装 APK 的热加载 Mod 框架**
（宿主 DLL + 模块 DLL，从 `/sdcard/ICEYMods` 或 APK 内置释放目录加载）。

## 目录结构

| 路径 | 内容 |
|---|---|
| `work/host/src/` | 宿主源码（`IceyModHost.dll`）：模块加载、反射工具 `Reflect`、`ModGui`、锁定 `Pin`、场景工具 `SceneUtil`、输入闸门 `InputShield` |
| `work/console/src/IceyDebugConsole/` | 调试台 Mod 源码（10 个标签页 + 区域中文标注表 `AreaTable.cs`） |
| `work/stub/src/IceyMod/` | 注入桩源码（纯 BCL，负责找宿主并 `Assembly.Load`） |
| `work/tools/` | Cecil 工具：`Inject2`(注入 8 处挂钩)、`PatchDebugFlag`(调试模式文件开关)、反汇编/扫描等 |
| `work/shell*/` | Java 外壳（`LoaderActivity` + 内置 WebUI + 模块管理 + `DebugBridge`）与清单/资源 |
| `work/build_shell_v3.py` | 打包脚本：`python3 build_shell_v3.py <out.apk> [Assembly-CSharp.dll] [embed] [base.apk]` |
| `work/build_selfcontained.py` | 自包含包装配（APK+OBB → 单包） |
| `out/story/` | **剧情全提取**：`ICEY_剧情全提取.md`、`terms_full.tsv`(1262 词条×6 语言)、`areas.tsv` |
| `out/mod/` | 当前 mod 产物（宿主/调试台 DLL、mods.txt、webui 页面） |
| `out/DEBUG_MODE.md` | 文件式调试模式说明（改文件 + 重启即可切换） |
| `out/MOD_INTERFACE.md`、`out/RESTORE_REPORT.md` | Mod 接口规范、复原报告 |
| `dist/` | **分卷压缩包**（大件 APK/OBB，本地生成，用 `cat` 合并） |

## 构建

```bash
M=out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed   # 需先解包游戏（extract_game.py）
# 宿主
mcs -target:library -sdk:2 -out:work/host/IceyModHost.dll -r:$M/UnityEngine.dll -r:$M/System.Core.dll work/host/src/*.cs
# 调试台
mcs -target:library -sdk:2 -out:work/console/IceyDebugConsole.dll -r:$M/UnityEngine.dll -r:$M/System.Core.dll -r:work/host/IceyModHost.dll work/console/src/IceyDebugConsole/*.cs
# 桩 + 注入 + 调试模式改写
mcs -target:library -sdk:2 -out:work/stub/IceyModStub.dll work/stub/src/IceyMod/Stub.cs
mono work/tools/inject2.exe $M/Assembly-CSharp.dll work/stub/IceyModStub.dll work/patched/Assembly-CSharp.v2.dll
mono work/tools/patchdebugflag.exe work/patched/Assembly-CSharp.v2.dll work/patched/Assembly-CSharp.v3.dll
# 打包（embed=内置模块；base=外壳清单/资源）
python3 work/build_shell_v3.py work/v6_unaligned.apk work/patched/Assembly-CSharp.v3.dll embed
zipalign -f 4 work/v6_unaligned.apk work/v6_aligned.apk
apksigner sign --ks out/icey-restored.jks --ks-key-alias icey --ks-pass pass:iceyrestore \
  --v1-signing-enabled true --v2-signing-enabled true --out out/ICEY_1.1.1_embedded.apk work/v6_aligned.apk
```

## 特性

- **热加载**：改 Mod 只需把 DLL 放到 `/sdcard/ICEYMods/` 并重启游戏（无需重装 APK）
- **内置模块**：`embed` 打包会把调试台塞进 APK，首启自动释放到可写目录（新装即用）
- **共存版**：`work/shell_co*`（包名 `com.fantablade.icey.co`，应用名「ICEY 共存版」）可与原版并存
- **文件式调试模式**：`echo 1 > /sdcard/ICEYMods/debug_mode` + 重启 → 打开游戏自带调试面板（含关卡选择）
- **调试台**：预设数值（攻击/血量/闪避能量…可锁定，每 300ms 重设）、敌人（列表/改血/生成，中文名+代号）、
  传送（168 区域中文标注 + 搜索）、强制移动、存档、解锁、日志、WebUI、设置
- **WebUI**：`http://<手机IP>:8080/`（应用内 Java HTTP 服务，含模块页面路由与命令接口）

## 说明

- 本仓库为私人用途：包含第三方游戏的派生数据与自签名密钥，请勿公开传播游戏资源。
- 大体积产物（APK/OBB/解包资源）不入库，见 `dist/` 分卷包说明。
