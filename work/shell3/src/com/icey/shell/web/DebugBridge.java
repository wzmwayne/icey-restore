package com.icey.shell.web;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Java 外壳 与 C# 宿主（IceyModHost）之间的桥：
 *  - C# 每秒把游戏状态以 JSON 推上来（setSnapshot）
 *  - WebUI 下发的命令进队列，C# 轮询取走（push / take）
 *  - 日志环形缓冲，供 WebUI 与 C# 共用
 * 命令采用行文本协议，一行一条：<cmd> [参数...]（见 MOD_INTERFACE.md）
 */
public final class DebugBridge {
    private static volatile String snapshot = "{}";
    private static final List<String> CMDS = Collections.synchronizedList(new ArrayList<String>());
    private static final List<String> LOGS = Collections.synchronizedList(new ArrayList<String>());
    private static final Map<String, WebHandler> ROUTES = Collections.synchronizedMap(new LinkedHashMap<String, WebHandler>());
    private static volatile long started = System.currentTimeMillis();
    private static volatile boolean hostReady = false;
    private static volatile int requestCount = 0;
    private static volatile String localUrl = "";

    private DebugBridge() { }

    public static void setSnapshot(String json) { snapshot = json == null ? "{}" : json; }
    public static String getSnapshot() { return snapshot; }
    public static boolean hostReady() { return hostReady; }
    public static void setLocalUrl(String u) { localUrl = u == null ? "" : u; }
    public static String localUrl() { return localUrl; }
    public static void setHostReady(boolean v) { hostReady = v; }
    public static long uptimeMs() { return System.currentTimeMillis() - started; }
    public static int requests() { return requestCount; }
    public static void countRequest() { requestCount++; }

    public static void push(String cmd) {
        if (cmd == null) return;
        cmd = cmd.trim();
        if (cmd.length() == 0) return;
        if (CMDS.size() < 200) CMDS.add(cmd);
    }

    /** C# 每次轮询取走所有待执行命令。 */
    public static String[] take() {
        synchronized (CMDS) {
            String[] out = CMDS.toArray(new String[CMDS.size()]);
            CMDS.clear();
            return out;
        }
    }

    public static void putLog(String line) {
        if (line == null) return;
        synchronized (LOGS) {
            LOGS.add(line);
            while (LOGS.size() > 800) LOGS.remove(0);
        }
    }

    public static String logs(int max) {
        synchronized (LOGS) {
            int from = Math.max(0, LOGS.size() - max);
            StringBuilder sb = new StringBuilder();
            for (int i = from; i < LOGS.size(); i++) sb.append(LOGS.get(i)).append('\n');
            return sb.toString();
        }
    }

    public static void registerRoute(String prefix, WebHandler h) {
        if (prefix == null || prefix.length() == 0 || h == null) return;
        ROUTES.put(prefix, h);
        putLog("[web] 模块注册路由: /" + prefix.replaceAll("^/+", "") + "/");
    }


    /** C# 侧模块只声明前缀（页面用静态文件，命令走 /api/cmd）时使用。 */
    public static void registerRouteName(String prefix) {
        if (prefix == null) return;
        final String p = prefix.replaceAll("^/+", "").replaceAll("/+$", "");
        if (p.length() == 0) return;
        if (ROUTES.containsKey(p)) return;
        ROUTES.put(p, new WebHandler() {
            public Response handle(String method, String path, String query, byte[] body, String contentType) {
                return Response.text(200, "模块 " + p + " 由 C# 侧处理；请通过 /api/cmd 下发命令。");
            }
        });
        putLog("[web] C# 模块声明路由: /" + p + "/");
    }

    public static List<String> prefixes() { return new ArrayList<String>(ROUTES.keySet()); }
    public static WebHandler handler(String prefix) { return ROUTES.get(prefix); }
}
