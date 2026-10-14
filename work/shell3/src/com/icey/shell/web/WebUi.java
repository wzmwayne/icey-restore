package com.icey.shell.web;

import android.content.Context;
import android.os.Build;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.List;
import java.util.Locale;

/** 内嵌 WebUI：模块管理 / 调试台(转发给 C# 宿主) / 日志 / 设备信息，并托管各模块自定义子路径。 */
public final class WebUi {

    private static Context APP;

    public static void install(MiniHttp http, Context ctx) {
        APP = ctx.getApplicationContext();
        http.addRoute("/", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return Response.html(PAGE); } });
        http.addRoute("/api/status", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return Response.json(status()); } });
        http.addRoute("/api/state", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return Response.json(DebugBridge.getSnapshot()); } });
        http.addRoute("/api/mods", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return Response.json(ModManager.listJson(APP)); } });
        http.addRoute("/api/mods/upload", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) {
            try { return Response.json("{\"msg\":" + q(ModManager.upload(APP, param(r.query, "name"), r.body)) + "}"); }
            catch (Throwable e) { return Response.err(500, String.valueOf(e)); }
        } });
        http.addRoute("/api/mods/delete", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) {
            try { return Response.json("{\"msg\":" + q(ModManager.delete(APP, param(r.query, "name"))) + "}"); }
            catch (Throwable e) { return Response.err(500, String.valueOf(e)); }
        } });
        http.addRoute("/api/mods/toggle", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) {
            try { return Response.json("{\"msg\":" + q(ModManager.toggle(APP, param(r.query, "name"))) + "}"); }
            catch (Throwable e) { return Response.err(500, String.valueOf(e)); }
        } });
        http.addRoute("/api/mods/txt", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) {
            if ("POST".equals(r.method)) {
                try { ModManager.writeTxt(APP, r.body == null ? "" : new String(r.body, "UTF-8")); return Response.json("{\"msg\":\"mods.txt 已保存，重启应用生效\"}"); }
                catch (Throwable e) { return Response.err(500, String.valueOf(e)); }
            }
            return Response.text(200, ModManager.readTxt(APP));
        } });
        http.addRoute("/api/cmd", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) {
            String c;
            try { c = r.body != null && r.body.length > 0 ? new String(r.body, "UTF-8") : param(r.query, "c"); }
            catch (java.io.UnsupportedEncodingException ue) { c = r.body != null ? new String(r.body) : param(r.query, "c"); }
            DebugBridge.push(c);
            return Response.json("{\"msg\":\"已入队\",\"cmd\":" + q(c) + "}");
        } });
        http.addRoute("/api/log", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) {
            int n = 200;
            try { n = Integer.parseInt(param(r.query, "lines")); } catch (Throwable t) { }
            String mem = DebugBridge.logs(n);
            String lc = logcat(n);
            return Response.text(200, (lc.length() > 0 ? lc : "") + mem);
        } });
        http.addRoute("/api/device", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return Response.json(device()); } });
        http.addRoute("/api/restart", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return Response.json("{\"msg\":" + q(restart()) + "}"); } });
        // 兜底：模块注册的 /<prefix>/... 先匹配，再尝试模块静态目录 <mods>/webui/<prefix>/
        http.addPrefix("/", new MiniHttp.Handler() { public Response handle(MiniHttp.Request r) { return moduleRoute(r); } });
    }

    private static Response moduleRoute(MiniHttp.Request r) {
        String path = r.path.startsWith("/") ? r.path.substring(1) : r.path;
        String seg = path;
        int sl = path.indexOf('/');
        if (sl >= 0) seg = path.substring(0, sl);
        if (seg.length() == 0) return Response.err(404, "not found");
        // 1) Java mod 注册的处理器
        for (String p : DebugBridge.prefixes()) {
            String norm = p.replaceAll("^/+", "").replaceAll("/+$", "");
            if (norm.equals(seg)) {
                WebHandler h = DebugBridge.handler(p);
                if (h != null) {
                    String rest = sl >= 0 ? path.substring(sl) : "/";
                    return h.handle(r.method, rest, r.query, r.body, r.contentType);
                }
            }
        }
        // 2) 模块静态目录
        for (File d : ModManager.dirs(APP)) {
            File f = new File(new File(new File(d, "webui"), seg), sl >= 0 ? path.substring(sl + 1) : "index.html");
            if (f.isFile()) {
                try {
                    byte[] b = readAll(f);
                    return Response.bin(mime(f.getName()), b);
                } catch (Throwable e) { return Response.err(500, String.valueOf(e)); }
            }
        }
        return Response.err(404, "模块路径未注册: /" + seg);
    }

    private static String status() {
        StringBuilder sb = new StringBuilder();
        sb.append("{\"hostReady\":").append(DebugBridge.hostReady())
          .append(",\"uptimeMs\":").append(DebugBridge.uptimeMs())
          .append(",\"requests\":").append(DebugBridge.requests())
          .append(",\"modsDir\":").append(q(ModManager.primary(APP).getAbsolutePath()))
          .append(",\"prefixes\":[");
        List<String> ps = DebugBridge.prefixes();
        for (int i = 0; i < ps.size(); i++) { if (i > 0) sb.append(','); sb.append(q(ps.get(i))); }
        sb.append("],\"device\":").append(device()).append('}');
        return sb.toString();
    }

    private static String device() {
        StringBuilder sb = new StringBuilder("{");
        sb.append("\"model\":").append(q(Build.MODEL)).append(",\"brand\":").append(q(Build.BRAND))
          .append(",\"sdk\":").append(Build.VERSION.SDK_INT)
          .append(",\"release\":").append(q(Build.VERSION.RELEASE))
          .append(",\"abi\":").append(q(Build.CPU_ABI))
          .append(",\"pkg\":").append(q(APP.getPackageName()))
          .append(",\"time\":").append(q(new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(new Date())))
          .append('}');
        return sb.toString();
    }

    private static String restart() {
        try {
            Runtime.getRuntime().exec(new String[] { "/system/bin/am", "start", "-n", APP.getPackageName() + "/com.icey.shell.LoaderActivity" });
            Thread.sleep(400);
            Runtime.getRuntime().exec(new String[] { "/system/bin/am", "force-stop", APP.getPackageName() });
            return "已请求重启（若未自动拉起，请手动点开游戏图标）";
        } catch (Throwable t) {
            return "自动重启失败（" + t + "），请手动重开游戏";
        }
    }

    private static String logcat(int lines) {
        try {
            Process p = Runtime.getRuntime().exec(new String[] { "/system/bin/logcat", "-d", "-t", String.valueOf(lines), "-s", "Unity", "ICEY-SHELL", "ICEY-MOD", "ICEY-WEB", "AndroidRuntime" });
            InputStream in = p.getInputStream();
            java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
            in.close();
            p.waitFor();
            return new String(bos.toByteArray(), "UTF-8");
        } catch (Throwable t) { return ""; }
    }

    private static byte[] readAll(File f) throws IOException {
        FileInputStream in = new FileInputStream(f);
        java.io.ByteArrayOutputStream bos = new java.io.ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) bos.write(buf, 0, n);
        in.close();
        return bos.toByteArray();
    }

    private static String mime(String name) {
        String n = name.toLowerCase(Locale.US);
        if (n.endsWith(".html") || n.endsWith(".htm")) return "text/html; charset=utf-8";
        if (n.endsWith(".js")) return "application/javascript; charset=utf-8";
        if (n.endsWith(".css")) return "text/css; charset=utf-8";
        if (n.endsWith(".json")) return "application/json; charset=utf-8";
        if (n.endsWith(".png")) return "image/png";
        if (n.endsWith(".jpg") || n.endsWith(".jpeg")) return "image/jpeg";
        if (n.endsWith(".svg")) return "image/svg+xml";
        return "application/octet-stream";
    }

    private static String param(String query, String key) {
        if (query == null) return "";
        String[] parts = query.split("&");
        for (String p : parts) {
            int e = p.indexOf('=');
            if (e > 0 && p.substring(0, e).equals(key)) {
                try { return java.net.URLDecoder.decode(p.substring(e + 1), "UTF-8"); } catch (Throwable t) { return p.substring(e + 1); }
            }
        }
        return "";
    }

    private static String q(String s) {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder("\"");
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '"' || c == '\\') sb.append('\\').append(c);
            else if (c == '\n') sb.append("\\n");
            else if (c == '\r') continue;
            else if (c < 0x20) sb.append(' ');
            else sb.append(c);
        }
        return sb.append('"').toString();
    }

    private static final String PAGE = buildPage();

    private static String buildPage() {
        StringBuilder s = new StringBuilder();
        s.append("<!doctype html><html lang=zh><head><meta charset=utf-8>")
         .append("<meta name=viewport content='width=device-width,initial-scale=1'>")
         .append("<title>ICEY Shell</title><style>")
         .append("body{font:14px/1.5 system-ui,-apple-system,'Noto Sans CJK SC',sans-serif;margin:0;background:#14161a;color:#e8eaed}")
         .append("header{padding:10px 14px;background:#1d2026;position:sticky;top:0;display:flex;gap:8px;align-items:center;flex-wrap:wrap}")
         .append("h1{font-size:15px;margin:0 10px 0 0}button{background:#2c313a;color:#e8eaed;border:1px solid #3a4048;border-radius:6px;padding:5px 10px;cursor:pointer}")
         .append("button:hover{background:#39404b}button.on{background:#2e7d32;border-color:#2e7d32}")
         .append("section{display:none;padding:12px 14px}section.on{display:block}")
         .append("table{width:100%;border-collapse:collapse}td,th{border-bottom:1px solid #2a2f38;padding:6px 4px;text-align:left;font-size:13px}")
         .append("code,pre{background:#0f1114;border:1px solid #262b33;border-radius:6px;padding:2px 5px;font-size:12px}")
         .append("pre{padding:8px;overflow:auto;max-height:60vh;white-space:pre-wrap}")
         .append("input,textarea{background:#0f1114;color:#e8eaed;border:1px solid #2a2f38;border-radius:6px;padding:5px;font:inherit}")
         .append("input[type=range]{padding:0;width:100%}.muted{color:#98a0aa}.row{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin:6px 0}")
         .append("</style></head><body>")
         .append("<header><h1>ICEY Shell</h1>")
         .append("<button id=b0 class=on onclick=\"tab(0)\">调试台</button><button id=b1 onclick=\"tab(1)\">模块</button>")
         .append("<button id=b2 onclick=\"tab(2)\">日志</button><button id=b3 onclick=\"tab(3)\">设备</button>")
         .append("<span class=muted id=stat></span></header>")

         .append("<section id=s0 class=on><div class=row><b>宿主状态</b><span id=host class=muted></span></div>")
         .append("<pre id=state>(等待宿主 C# 上报…)</pre>")
         .append("<div class=row><input id=cmd style='flex:1' placeholder=\"命令，如: timeScale 2   |   pin Player.hp   |   scene level3\">")
         .append("<button onclick='send()'>发送</button></div>")
         .append("<div class=muted>命令语法见 MOD_INTERFACE.md；由调试台 mod 解析执行。</div></section>")

         .append("<section id=s1><div class=row><input type=file id=f multiple><button onclick='up()'>上传并安装</button>")
         .append("<span class=muted>支持 .dll/.dex/.zip（zip 会解包）；安装后需重启应用</span></div>")
         .append("<table id=mods></table>")
         .append("<div class=row><b>mods.txt</b><button onclick='saveTxt()'>保存</button></div>")
         .append("<textarea id=txt style='width:100%;height:180px'></textarea></section>")

         .append("<section id=s2><div class=row><button onclick='loadLog()'>刷新日志</button>")
         .append("<label class=muted><input type=checkbox id=auto> 自动刷新</label></div><pre id=log></pre></section>")

         .append("<section id=s3><pre id=dev></pre><div class=row><button onclick='restart()'>重启应用</button></div></section>")

         .append("<script>")
         .append("const $=id=>document.getElementById(id);let T=0;")
         .append("function tab(i){for(let k=0;k<4;k++){$('s'+k).className=k==i?'on':'';$('b'+k).className=k==i?'on':''}T=i;if(i==1)loadMods();if(i==2)loadLog();if(i==3)loadDev()}")
         .append("async function j(u,o){const r=await fetch(u,o);return await r.text()}")
         .append("async function st(){try{const a=JSON.parse(await j('/api/status'));$('stat').textContent='运行'+Math.floor(a.uptimeMs/1000)+'s / 请求'+a.requests+' / 模块路由'+a.prefixes.length;$('host').textContent=a.hostReady?'已连接':'未连接(宿主 DLL 未加载)';dev_=a.device}catch(e){$('stat').textContent='连接失败'}}")
         .append("async function state(){try{$('state').textContent=await j('/api/state')}catch(e){}}")
         .append("async function send(){const c=$('cmd').value.trim();if(!c)return;await j('/api/cmd',{method:'POST',body:c});$('cmd').value='';setTimeout(state,300)}")
         .append("async function loadMods(){const a=JSON.parse(await j('/api/mods'));let h='<tr><th>名称</th><th>位置</th><th>大小</th><th>状态</th><th></th></tr>';for(const m of a){h+='<tr><td>'+m.name+'</td><td class=muted>'+m.dir+'</td><td>'+m.size+'</td><td>'+(m.enabled?'已登记':'未登记')+'</td><td><button onclick=\"tog(\\''+m.name+'\\')\">'+(m.enabled?'停用':'启用')+'</button> <button onclick=\"del(\\''+m.name+'\\')\">删除</button></td></tr>'} $('mods').innerHTML=h;$('txt').value=await j('/api/mods/txt')}")
         .append("async function up(){const f=$('f').files;for(const x of f){await fetch('/api/mods/upload?name='+encodeURIComponent(x.name),{method:'POST',body:x})}alert('上传完成，重启应用生效');loadMods()}")
         .append("async function del(n){if(!confirm('删除 '+n+' ?'))return;await j('/api/mods/delete?name='+encodeURIComponent(n),{method:'POST'});loadMods()}")
         .append("async function tog(n){await j('/api/mods/toggle?name='+encodeURIComponent(n),{method:'POST'});loadMods()}")
         .append("async function saveTxt(){await j('/api/mods/txt',{method:'POST',body:$('txt').value});alert('已保存，重启应用生效')}")
         .append("async function loadLog(){try{$('log').textContent=await j('/api/log?lines=300')}catch(e){}}")
         .append("async function loadDev(){try{$('dev').textContent=JSON.stringify(JSON.parse(await j('/api/device')),null,2)}catch(e){}}")
         .append("async function restart(){await j('/api/restart',{method:'POST'});alert('已请求重启')}")
         .append("setInterval(st,2000);setInterval(state,1000);setInterval(()=>{if($('auto').checked&&T==2)loadLog()},3000);st();state();")
         .append("</script></body></html>");
        return s.toString();
    }
}
