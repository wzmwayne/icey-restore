package com.icey.shell;

import android.content.Context;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.View;
import android.widget.Toast;
import com.icey.shell.mod.ModContext;
import com.icey.shell.mod.ModLoader;
import com.icey.shell.web.MiniHttp;
import com.icey.shell.web.WebUi;
import com.unity3d.player.UnityPlayerActivity;
import java.io.File;

/**
 * 自建加载器外壳：只做 沉浸式 + mod 钩子 + 拉起 Unity 引擎入口。
 * 引擎与游戏内容仍来自原始资源（libunity/libmono/libmain + assets/bin/Data + Unity 引擎 Java 类）。
 */
public class LoaderActivity extends UnityPlayerActivity {
    private static final String TAG = "ICEY-SHELL";
    private static MiniHttp sHttp;
    private ModContext mCtx;

    /** 供 C# 侧（AndroidJavaClass）查询 mod 目录，预留 C# mod 的接入点。 */
    public static String getModsDir(Context c) {
        try {
            File ext = c.getExternalFilesDir(null);
            return ext == null ? "" : new File(ext, "mods").getAbsolutePath();
        } catch (Throwable t) { return ""; }
    }


    /** 启动内嵌 WebUI（8080 起找一个可用端口），并 Toast 提示访问地址。 */
    private void startWebUi() {
        if (sHttp != null) return;
        for (int port = 8080; port < 8100; port++) {
            try {
                MiniHttp http = new MiniHttp(port);
                WebUi.install(http, this);
                http.start();
                sHttp = http;
                String ip = localIp();
                String url = "http://" + (ip.length() > 0 ? ip : "127.0.0.1") + ":" + port + "/";
                Log.i(TAG, "WebUI: " + url);
                com.icey.shell.web.DebugBridge.setLocalUrl(url);
                Toast.makeText(this, "ICEY WebUI: " + url, Toast.LENGTH_LONG).show();
                return;
            } catch (Throwable e) {
                // 端口占用则换一个
            }
        }
        Log.w(TAG, "WebUI 启动失败");
        Toast.makeText(this, "WebUI 启动失败（端口 8080-8099 均不可用）", Toast.LENGTH_LONG).show();
    }

    private static String localIp() {
        try {
            java.util.Enumeration<java.net.NetworkInterface> nis = java.net.NetworkInterface.getNetworkInterfaces();
            while (nis.hasMoreElements()) {
                java.net.NetworkInterface ni = nis.nextElement();
                if (!ni.isUp() || ni.isLoopback()) continue;
                java.util.Enumeration<java.net.InetAddress> as = ni.getInetAddresses();
                while (as.hasMoreElements()) {
                    java.net.InetAddress a = as.nextElement();
                    if (!a.isLoopbackAddress() && a instanceof java.net.Inet4Address) return a.getHostAddress();
                }
            }
        } catch (Throwable t) { }
        return "";
    }

    private void immersive() {
        View v = getWindow().getDecorView();
        v.setSystemUiVisibility(Build.VERSION.SDK_INT >= 19 ? 5894 : 1799);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        Log.i(TAG, "shell onCreate, pkg=" + getPackageName() + ", modDir=" + getModsDir(this));
        startWebUi();
        try {
            ModLoader.load(this);
            File dataDir = getFilesDir();
            mCtx = new ModContext(this, ModLoader.modsDir(), dataDir, getClassLoader());
            ModLoader.fireBeforeUnity(mCtx);
        } catch (Throwable t) {
            Log.w(TAG, "mod pre-init failed: " + t);
        }
        super.onCreate(savedInstanceState);          // 引擎入口：创建 UnityPlayer
        try { if (mCtx != null) ModLoader.fireAfterUnity(mCtx); } catch (Throwable t) { Log.w(TAG, "hook: " + t); }
        immersive();
    }

    @Override
    protected void onResume() {
        super.onResume();
        immersive();
        try { if (mCtx != null) ModLoader.fireResume(mCtx); } catch (Throwable t) { Log.w(TAG, "hook: " + t); }
    }

    @Override
    protected void onPause() {
        try { if (mCtx != null) ModLoader.firePause(mCtx); } catch (Throwable t) { Log.w(TAG, "hook: " + t); }
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        try { if (mCtx != null) ModLoader.fireDestroy(mCtx); } catch (Throwable t) { Log.w(TAG, "hook: " + t); }
        super.onDestroy();
    }
}
