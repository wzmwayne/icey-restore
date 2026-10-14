# 文件控制调试模式（v5 及以后）

## 原理
`UnityEngine.Debug.isDebugBuild` 是原生 InternalCall，托管层改不了。本包把**游戏程序集里 7 处调用**改写为调用注入桩的 `IceyMod.Bootstrap::DebugFlag()`，该函数启动时读文件决定返回值：

| 调用点 | 被打开的功能 |
|---|---|
| `GameSceneInit.Start` | 启动时自动创建游戏自带调试面板（关卡选择/难度/血宫/$+100/强化全满…） |
| `UIKeyInput.UpdateInput` | UI 上的「调试」按钮 |
| `PlayerAttribute.SetBaseLevelData` | 作弊模式 → 攻击力 9999 |
| `GameInfoDebugGUI.GUIStart` | FPS 显示页 |
| `LogManager.Awake` | 更详细日志 |
| `SRDebugService..ctor` / `StandardSystemInformationService.CreateDefaultSet` | SRDebugger 开发构建分支 |

## 用法（改文件 + 重启应用，不需要重装）

开：
```bash
echo 1 > /sdcard/ICEYMods/debug_mode      # 内容为空/1/true/on/yes 都算开
```
关：
```bash
rm /sdcard/ICEYMods/debug_mode            # 或写 0/false/off
```
然后**重启游戏**（该值在进程启动时读取一次）。

查找顺序：`/sdcard/ICEYMods/debug_mode` → `/storage/emulated/0/ICEYMods/debug_mode` → `/data/data/com.fantablade.icey/files/debug_mode`

## 注意
- 开启后会出现游戏自带的调试 UI/更详细日志，属预期；删除文件重启即完全恢复发布模式。
- 存档不受影响（与调试开关无关）。
