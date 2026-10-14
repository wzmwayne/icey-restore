package com.icey.shell.mod;

import android.content.Context;
import android.util.Log;
import dalvik.system.DexClassLoader;
import java.io.BufferedReader;
import java.io.File;
import java.io.FileReader;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * 极简 mod 加载器：
 *   1) 扫描 <externalFilesDir>/mods 与 /sdcard/ICEYMods；
 *   2) 读取 mods.txt，每行 "dex文件名 类全名"（# 开头为注释）；
 *   3) 用 DexClassLoader 载入并实例化实现了 IceyMod 的类；
 *   4) 任一 mod 失败只记日志，绝不影响游戏启动。
 */
public final class ModLoader {
    private static final String TAG = "ICEY-MOD";
    private static final List<IceyMod> MODS = new ArrayList<IceyMod>();
    private static final Map<String, DexClassLoader> LOADERS = new HashMap<String, DexClassLoader>();
    private static File sModsDir;
    private static boolean sLoaded;

    private ModLoader() {}

    public static List<IceyMod> mods() { return MODS; }
    public static File modsDir() { return sModsDir; }

    public static List<File> searchDirs(Context ctx) {
        List<File> dirs = new ArrayList<File>();
        try {
            File ext = ctx.getExternalFilesDir(null);
            if (ext != null) dirs.add(new File(ext, "mods"));
        } catch (Throwable t) { Log.w(TAG, "external files dir unavailable: " + t); }
        dirs.add(new File("/sdcard/ICEYMods"));
        dirs.add(new File("/storage/emulated/0/ICEYMods"));
        return dirs;
    }

    public static synchronized void load(Context ctx) {
        if (sLoaded) return;
        sLoaded = true;
        for (File dir : searchDirs(ctx)) {
            try {
                if (!dir.isDirectory()) continue;
                File manifest = new File(dir, "mods.txt");
                if (!manifest.isFile()) continue;
                sModsDir = dir;
                Log.i(TAG, "loading mods from " + dir.getAbsolutePath());
                parse(ctx, dir, manifest);
            } catch (Throwable t) {
                Log.w(TAG, "mod dir failed " + dir + ": " + t);
            }
        }
        Log.i(TAG, "mods loaded: " + MODS.size());
    }

    private static void parse(Context ctx, File dir, File manifest) throws Exception {
        BufferedReader r = new BufferedReader(new FileReader(manifest));
        try {
            String line;
            while ((line = r.readLine()) != null) {
                line = line.trim();
                if (line.length() == 0 || line.charAt(0) == '#') continue;
                String dexName, className;
                int sep = line.indexOf(' ');
                if (sep < 0) { dexName = null; className = line; }
                else { dexName = line.substring(0, sep).trim(); className = line.substring(sep + 1).trim(); }
                // 只处理 Java 侧模块（.dex/.jar）；.dll 交给 C# 宿主
                if (dexName != null) {
                    String low = dexName.toLowerCase();
                    if (!low.endsWith(".dex") && !low.endsWith(".jar") && !low.endsWith(".apk")) continue;
                }
                try {
                    ClassLoader cl = dexName == null ? ModLoader.class.getClassLoader()
                            : loaderFor(ctx, new File(dir, dexName));
                    Class<?> c = Class.forName(className, true, cl);
                    if (!IceyMod.class.isAssignableFrom(c)) { Log.w(TAG, className + " 未实现 IceyMod，跳过"); continue; }
                    MODS.add((IceyMod) c.newInstance());
                    Log.i(TAG, "mod loaded: " + className);
                } catch (Throwable t) {
                    Log.w(TAG, "mod failed " + className + ": " + t);
                }
            }
        } finally { r.close(); }
    }

    private static DexClassLoader loaderFor(Context ctx, File dex) {
        String key = dex.getAbsolutePath();
        DexClassLoader cl = LOADERS.get(key);
        if (cl == null) {
            File opt = new File(ctx.getDir("mods_opt", Context.MODE_PRIVATE), "o");
            opt.mkdirs();
            cl = new DexClassLoader(key, opt.getAbsolutePath(), null, ModLoader.class.getClassLoader());
            LOADERS.put(key, cl);
        }
        return cl;
    }

    public static void fireBeforeUnity(ModContext c) { fire(c, 0); }
    public static void fireAfterUnity(ModContext c)  { fire(c, 1); }
    public static void fireResume(ModContext c)      { fire(c, 2); }
    public static void firePause(ModContext c)       { fire(c, 3); }
    public static void fireDestroy(ModContext c)     { fire(c, 4); }

    private static void fire(ModContext c, int which) {
        for (IceyMod m : new ArrayList<IceyMod>(MODS)) {
            try {
                switch (which) {
                    case 0: m.onBeforeUnity(c); break;
                    case 1: m.onAfterUnity(c); break;
                    case 2: m.onResume(c); break;
                    case 3: m.onPause(c); break;
                    case 4: m.onDestroy(c); break;
                }
            } catch (Throwable t) {
                Log.w(TAG, "mod hook " + which + " failed in " + m.getClass().getName() + ": " + t);
            }
        }
    }
}
