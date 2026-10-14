package example.mod;

import com.icey.shell.mod.BaseMod;
import com.icey.shell.mod.ModContext;

/** 示例 mod：演示钩子、日志与反射入口（不需要时删掉整个 mods.txt 即可）。 */
public class HelloMod extends BaseMod {
    @Override
    public void onAfterUnity(ModContext ctx) {
        ctx.log("HelloMod: Unity 已启动, apiVersion=" + ctx.apiVersion() + ", modsDir=" + ctx.modsDir);
        try {
            Class<?> up = ctx.findClass("com.unity3d.player.UnityPlayer");
            ctx.log("HelloMod: 引擎类可见 -> " + up.getName());
        } catch (Throwable t) {
            ctx.log("HelloMod: 反射失败 " + t);
        }
    }
    @Override
    public void onDestroy(ModContext ctx) { ctx.log("HelloMod: bye"); }
}
