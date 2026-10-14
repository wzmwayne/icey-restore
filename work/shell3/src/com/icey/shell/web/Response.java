package com.icey.shell.web;

import java.io.UnsupportedEncodingException;

/** HTTP 响应。 */
public final class Response {
    public final int status;
    public final String contentType;
    public final byte[] body;

    public Response(int status, String contentType, byte[] body) {
        this.status = status; this.contentType = contentType; this.body = body;
    }

    public static byte[] utf8(String s) {
        try { return s.getBytes("UTF-8"); } catch (UnsupportedEncodingException e) { return s.getBytes(); }
    }

    public static Response text(int status, String s) { return new Response(status, "text/plain; charset=utf-8", utf8(s)); }
    public static Response ok(String s) { return text(200, s); }
    public static Response html(String s) { return new Response(200, "text/html; charset=utf-8", utf8(s)); }
    public static Response json(String s) { return new Response(200, "application/json; charset=utf-8", utf8(s)); }
    public static Response bin(String type, byte[] b) { return new Response(200, type, b); }
    public static Response err(int status, String msg) { return text(status, msg); }
}
