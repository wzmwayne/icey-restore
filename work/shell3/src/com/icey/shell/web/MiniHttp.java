package com.icey.shell.web;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** 极简 HTTP/1.1 服务器（无第三方依赖，支持 GET/POST/DELETE/PUT 与二进制上传）。 */
public final class MiniHttp {

    public interface Handler { Response handle(Request req); }

    public static final class Request {
        public final String method, path, query, contentType;
        public final byte[] body;
        public final Map<String, String> headers = new HashMap<String, String>();
        Request(String method, String path, String query, String contentType, byte[] body) {
            this.method = method; this.path = path; this.query = query;
            this.contentType = contentType; this.body = body;
        }
        public String header(String name) {
            String v = headers.get(name.toLowerCase());
            return v == null ? "" : v;
        }
    }

    private final ServerSocket server;
    private final Map<String, Handler> exact = new HashMap<String, Handler>();
    private final List<String> prefixes = new ArrayList<String>();
    private final Map<String, Handler> prefixHandlers = new HashMap<String, Handler>();
    private final ExecutorService pool = Executors.newFixedThreadPool(4);
    private volatile boolean running = true;

    public MiniHttp(int port) throws IOException {
        server = new ServerSocket();
        server.setReuseAddress(true);
        server.bind(new InetSocketAddress(port));
    }

    public int port() { return server.getLocalPort(); }

    public void addRoute(String path, Handler h) { exact.put(path, h); }

    public void addPrefix(String prefix, Handler h) {
        if (!prefix.endsWith("/")) prefix = prefix + "/";
        prefixes.add(prefix); prefixHandlers.put(prefix, h);
    }

    /** 接受连接（静态命名类，避开 d8 对嵌套匿名类的处理缺陷）。 */
    private static final class Acceptor implements Runnable {
        private final MiniHttp http;
        Acceptor(MiniHttp h) { http = h; }
        public void run() {
            while (http.running) {
                try {
                    Socket s = http.server.accept();
                    http.pool.execute(new Conn(http, s));
                } catch (Throwable e) {
                    if (http.running) { try { Thread.sleep(50); } catch (InterruptedException ie) { } }
                }
            }
        }
    }

    private static final class Conn implements Runnable {
        private final MiniHttp http;
        private final Socket socket;
        Conn(MiniHttp h, Socket s) { http = h; socket = s; }
        public void run() { http.handle(socket); }
    }

    public void start() {
        Thread t = new Thread(new Acceptor(this), "icey-web");
        t.setDaemon(true);
        t.start();
    }

    public void stop() {
        running = false;
        try { server.close(); } catch (IOException e) { }
        pool.shutdownNow();
    }

    void handle(Socket s) {
        try {
            s.setSoTimeout(20000);
            InputStream in = s.getInputStream();
            OutputStream out = s.getOutputStream();
            String head = readHead(in);
            if (head == null) { s.close(); return; }
            String[] lines = head.split("\r\n");
            String[] first = lines[0].split(" ");
            if (first.length < 3) { s.close(); return; }
            String method = first[0], target = first[1];
            Map<String, String> hs = new HashMap<String, String>();
            for (int i = 1; i < lines.length; i++) {
                int c = lines[i].indexOf(':');
                if (c > 0) hs.put(lines[i].substring(0, c).trim().toLowerCase(), lines[i].substring(c + 1).trim());
            }
            int len = 0;
            try { len = Integer.parseInt(hs.containsKey("content-length") ? hs.get("content-length") : "0"); } catch (Throwable e) { }
            byte[] body = new byte[len];
            int got = 0;
            while (got < len) {
                int r = in.read(body, got, len - got);
                if (r < 0) break;
                got += r;
            }
            String path = target, query = "";
            int q = target.indexOf('?');
            if (q >= 0) { path = target.substring(0, q); query = target.substring(q + 1); }
            Request req = new Request(method, path, query, hs.get("content-type"), body);
            req.headers.putAll(hs);
            Response resp = dispatch(req);
            if (resp == null) resp = Response.err(404, "not found");
            write(out, resp);
            out.flush();
            s.close();
        } catch (Throwable e) {
            try { s.close(); } catch (Throwable ignore) { }
        }
    }

    private Response dispatch(Request req) {
        Handler h = exact.get(req.path);
        if (h != null) { try { return h.handle(req); } catch (Throwable e) { return Response.err(500, "handler error: " + e); } }
        for (int i = 0; i < prefixes.size(); i++) {
            String p = prefixes.get(i);
            if (req.path.startsWith(p) || req.path.equals(p.substring(0, p.length() - 1))) {
                try { return prefixHandlers.get(p).handle(req); } catch (Throwable e) { return Response.err(500, "prefix error: " + e); }
            }
        }
        return Response.err(404, "not found: " + req.path);
    }

    private static String readHead(InputStream in) throws IOException {
        ByteArrayOutputStream bos = new ByteArrayOutputStream();
        int state = 0;
        while (bos.size() < 16384) {
            int b = in.read();
            if (b < 0) return bos.size() == 0 ? null : new String(bos.toByteArray(), "UTF-8");
            bos.write(b);
            if (b == '\r' && (state == 0 || state == 2)) state++;
            else if (b == '\n' && (state == 1 || state == 3)) { state++; if (state == 4) break; }
            else state = 0;
        }
        return new String(bos.toByteArray(), "UTF-8");
    }

    private static void write(OutputStream out, Response r) throws IOException {
        StringBuilder sb = new StringBuilder();
        sb.append("HTTP/1.1 ").append(r.status).append(' ').append(reason(r.status)).append("\r\n");
        sb.append("Content-Type: ").append(r.contentType).append("\r\n");
        sb.append("Content-Length: ").append(r.body.length).append("\r\n");
        sb.append("Cache-Control: no-store\r\n");
        sb.append("Access-Control-Allow-Origin: *\r\n");
        sb.append("Connection: close\r\n\r\n");
        out.write(sb.toString().getBytes("UTF-8"));
        out.write(r.body);
    }

    private static String reason(int st) {
        switch (st) {
            case 200: return "OK";
            case 400: return "Bad Request";
            case 404: return "Not Found";
            case 500: return "Internal Server Error";
            default: return "Status";
        }
    }
}
