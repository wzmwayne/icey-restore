package com.icey.shell.mod;

/**
 * Mod 接口（API v1）。外部 mod 以 dex/jar 形式放在
 * /sdcard/Android/data/com.fantablade.icey/files/mods/ 并在同目录 mods.txt 中登记，
 * 外壳会在生命周期各阶段回调实现类。
 */
public interface IceyMod {
    void onBeforeUnity(ModContext ctx);
    void onAfterUnity(ModContext ctx);
    void onResume(ModContext ctx);
    void onPause(ModContext ctx);
    void onDestroy(ModContext ctx);
}
