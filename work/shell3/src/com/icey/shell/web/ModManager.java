package com.icey.shell.web;

import android.content.Context;
import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.FileReader;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/** 模块文件管理：列出 / 安装(上传, 支持 zip) / 删除 / 启用停用(mods.txt)。 */
public final class ModManager {

    public static List<File> dirs(Context ctx) {
        List<File> out = new ArrayList<File>();
        try {
            File ext = ctx.getExternalFilesDir(null);
            if (ext != null) out.add(new File(ext, "mods"));
        } catch (Throwable t) { }
        out.add(new File("/sdcard/ICEYMods"));
        out.add(new File("/storage/emulated/0/ICEYMods"));
        return out;
    }

    /** 主目录：优先外部专属目录，其次 /sdcard/ICEYMods（若可写）。 */
    public static File primary(Context ctx) {
        for (File d : dirs(ctx)) {
            try {
                if (d.isDirectory() || d.mkdirs()) {
                    File t = new File(d, ".wtest");
                    FileOutputStream fos = new FileOutputStream(t);
                    fos.write(1); fos.close(); t.delete();
                    return d;
                }
            } catch (Throwable e) { }
        }
        return new File("/sdcard/ICEYMods");
    }

    private static File findDirWith(Context ctx, String name) {
        for (File d : dirs(ctx)) {
            File f = new File(d, name);
            if (f.exists()) return f;
        }
        return null;
    }

    public static String listJson(Context ctx) {
        StringBuilder sb = new StringBuilder("[");
        String txt = readTxt(ctx);
        boolean first = true;
        for (File d : dirs(ctx)) {
            File[] fs = d.listFiles();
            if (fs == null) continue;
            for (File f : fs) {
                if (f.getName().startsWith(".")) continue;
                if (!first) sb.append(',');
                first = false;
                boolean reg = txt.contains(f.getName());
                sb.append("{\"name\":").append(q(f.getName()))
                  .append(",\"dir\":").append(q(d.getAbsolutePath()))
                  .append(",\"size\":").append(f.isDirectory() ? 0 : f.length())
                  .append(",\"dir?\":").append(f.isDirectory())
                  .append(",\"enabled\":").append(reg)
                  .append("}");
            }
        }
        return sb.append(']').toString();
    }

    public static String readTxt(Context ctx) {
        File f = null;
        for (File d : dirs(ctx)) {
            File c = new File(d, "mods.txt");
            if (c.isFile()) { f = c; break; }
        }
        if (f == null) return "";
        StringBuilder sb = new StringBuilder();
        try {
            BufferedReader r = new BufferedReader(new FileReader(f));
            String line;
            while ((line = r.readLine()) != null) sb.append(line).append('\n');
            r.close();
        } catch (Throwable t) { }
        return sb.toString();
    }

    public static void writeTxt(Context ctx, String content) throws IOException {
        File d = primary(ctx);
        d.mkdirs();
        FileOutputStream fos = new FileOutputStream(new File(d, "mods.txt"));
        fos.write(content.getBytes("UTF-8"));
        fos.close();
    }

    /** 上传一个文件；.zip 会解包到同名子目录并合并其中的 mods.txt。 */
    public static String upload(Context ctx, String rawName, byte[] data) throws IOException {
        String name = sanitize(rawName);
        File dir = primary(ctx);
        dir.mkdirs();
        if (name.toLowerCase().endsWith(".zip")) {
            String sub = name.substring(0, name.length() - 4);
            File target = new File(dir, sub);
            target.mkdirs();
            ZipInputStream zis = new ZipInputStream(new ByteArrayInputStream(data));
            ZipEntry e;
            List<String> declared = new ArrayList<String>();
            while ((e = zis.getNextEntry()) != null) {
                String en = sanitize(e.getName());
                if (en.length() == 0) continue;
                File out = new File(target, en);
                if (e.isDirectory()) { out.mkdirs(); continue; }
                out.getParentFile().mkdirs();
                FileOutputStream fos = new FileOutputStream(out);
                copy(zis, fos);
                fos.close();
                if (en.equals("mods.txt")) {
                    BufferedReader r = new BufferedReader(new FileReader(out));
                    String line;
                    while ((line = r.readLine()) != null) declared.add(line);
                    r.close();
                }
            }
            zis.close();
            if (!declared.isEmpty()) {
                StringBuilder sb = new StringBuilder(readTxt(ctx));
                for (String line : declared) {
                    String t = line.trim();
                    if (t.length() == 0 || t.startsWith("#")) { sb.append(t).append('\n'); continue; }
                    int sp = t.indexOf(' ');
                    if (sp > 0) sb.append(sub).append('/').append(t.substring(0, sp)).append(t.substring(sp)).append('\n');
                    else sb.append(t).append('\n');
                }
                writeTxt(ctx, sb.toString());
            }
            return "已解包到 " + sub + "/" + (declared.isEmpty() ? "（未含 mods.txt，请手动登记）" : "（已合并 mods.txt 登记）");
        }
        File out = new File(dir, name);
        FileOutputStream fos = new FileOutputStream(out);
        fos.write(data);
        fos.close();
        return "已上传 " + name + " (" + data.length + " 字节)，请在模块页登记后重启应用";
    }

    public static String delete(Context ctx, String name) throws IOException {
        File f = findDirWith(ctx, sanitize(name));
        if (f == null) return "未找到 " + name;
        deleteRec(f);
        StringBuilder sb = new StringBuilder();
        BufferedReader r = new BufferedReader(new java.io.StringReader(readTxt(ctx)));
        String line;
        while ((line = r.readLine()) != null) {
            if (line.contains(f.getName())) continue;
            sb.append(line).append('\n');
        }
        writeTxt(ctx, sb.toString());
        return "已删除 " + name;
    }

    /** 在 mods.txt 中启用/停用（注释）某模块相关行。 */
    public static String toggle(Context ctx, String name) throws IOException {
        String txt = readTxt(ctx);
        StringBuilder sb = new StringBuilder();
        boolean changed = false, newlyEnabled = false;
        BufferedReader r = new BufferedReader(new java.io.StringReader(txt));
        String line;
        while ((line = r.readLine()) != null) {
            if (!line.contains(name)) { sb.append(line).append('\n'); continue; }
            changed = true;
            if (line.trim().startsWith("#")) { sb.append(line.replaceFirst("#\\s*", "")).append('\n'); newlyEnabled = true; }
            else sb.append("# ").append(line).append('\n');
        }
        if (!changed) {
            // 该文件还没登记：把它当作 dex 登记上去，类名留空 → 提示
            sb.append(name).append(" <类全名>\n");
            writeTxt(ctx, sb.toString());
            return "已在 mods.txt 追加一行，请填写实现 IceyMod 的类全名";
        }
        writeTxt(ctx, sb.toString());
        return newlyEnabled ? "已启用（重启应用生效）" : "已停用（重启应用生效）";
    }

    private static void copy(InputStream in, FileOutputStream out) throws IOException {
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
    }

    private static void deleteRec(File f) {
        if (f.isDirectory()) {
            File[] cs = f.listFiles();
            if (cs != null) for (File c : cs) deleteRec(c);
        }
        f.delete();
    }

    private static String sanitize(String s) {
        if (s == null) return "";
        s = s.replace('\\', '/');
        int slash = s.lastIndexOf('/');
        if (slash >= 0) s = s.substring(slash + 1);
        s = s.replace("..", "_").replace(":", "_");
        return s.trim();
    }

    private static String q(String s) {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder("\"");
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            if (c == '"' || c == '\\') sb.append('\\').append(c);
            else if (c == '\n') sb.append("\\n");
            else if (c < 0x20) sb.append(' ');
            else sb.append(c);
        }
        return sb.append('"').toString();
    }
}
