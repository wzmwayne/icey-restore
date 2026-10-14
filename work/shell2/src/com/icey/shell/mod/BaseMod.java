package com.icey.shell.mod;

/** 便捷基类：全部钩子为空实现，mod 只需覆盖关心的那几个。 */
public abstract class BaseMod implements IceyMod {
    public void onBeforeUnity(ModContext ctx) {}
    public void onAfterUnity(ModContext ctx) {}
    public void onResume(ModContext ctx) {}
    public void onPause(ModContext ctx) {}
    public void onDestroy(ModContext ctx) {}
}
