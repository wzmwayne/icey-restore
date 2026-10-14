package com.fantablade.icey;

import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.View;
import android.widget.Toast;
import com.unity3d.player.UnityPlayerActivity;
import java.io.File;

/**
 * 自建加载器外壳：只负责 沉浸式设置 + OBB 就位检查 + 拉起 Unity 引擎入口。
 * 引擎与游戏逻辑仍使用原始资源（libunity/libmono/libmain + assets/bin/Data + UnityPlayer 等引擎 Java 类）。
 */
public class IceyShellActivity extends UnityPlayerActivity {
    private static final String TAG = "ICEY-SHELL";

    private void immersive() {
        View v = getWindow().getDecorView();
        v.setSystemUiVisibility(Build.VERSION.SDK_INT >= 19 ? 5894 : 1799);
    }

    private void checkObb() {
        if (Build.VERSION.SDK_INT < 19) return;
        try {
            File dir = getObbDir();
            String[] list = dir.list();
            if (list == null || list.length == 0) {
                Log.w(TAG, "obb missing: " + dir.getAbsolutePath());
                Toast.makeText(this, "未找到 OBB：请将 main.19.com.fantablade.icey.obb 放入 Android/obb/com.fantablade.icey/",
                        Toast.LENGTH_LONG).show();
            } else {
                Log.i(TAG, "obb ok, files=" + list.length);
            }
        } catch (Throwable t) {
            Log.w(TAG, "obb check failed: " + t);
        }
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        Log.i(TAG, "shell onCreate, pkg=" + getPackageName());
        super.onCreate(savedInstanceState);   // 引擎入口：创建 UnityPlayer 并 setContentView
        immersive();
        checkObb();
    }

    @Override
    protected void onResume() {
        super.onResume();
        immersive();
    }
}
