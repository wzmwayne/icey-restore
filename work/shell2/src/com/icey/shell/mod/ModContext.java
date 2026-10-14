package com.icey.shell.mod;

import android.app.Activity;
import android.util.Log;
import java.io.File;

/** 传给 mod 的上下文：宿主 Activity、mod/数据目录、可见引擎类的 ClassLoader。 */
public final class ModContext {
    public static final int API_VERSION = 1;
    public final Activity activity;
    public final File modsDir;
    public final File dataDir;
    public final ClassLoader classLoader;

    public ModContext(Activity activity, File modsDir, File dataDir, ClassLoader classLoader) {
        this.activity = activity;
        this.modsDir = modsDir;
        this.dataDir = dataDir;
        this.classLoader = classLoader;
    }

    public int apiVersion() { return API_VERSION; }


    /** Java mod 注册自己的 Web 子路径：/<prefix>/... 的请求会转到 handler。 */
    public void webRegister(String prefix, com.icey.shell.web.WebHandler handler) {
        com.icey.shell.web.DebugBridge.registerRoute(prefix, handler);
    }

    /** 向 WebUI 调试台推送 JSON 状态。 */
    public void publish(String json) { com.icey.shell.web.DebugBridge.setSnapshot(json); }

    public void log(String msg) { Log.i("ICEY-MOD", msg); }

    /** 让 mod 能用宿主 ClassLoader 反射到引擎/游戏类。 */
    public Class<?> findClass(String name) throws ClassNotFoundException {
        return Class.forName(name, true, classLoader);
    }
}
