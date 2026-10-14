package com.icey.shell.web;

/** 模块自定义 Web 接口：注册前缀后，/<prefix>/... 的请求会转到这里。 */
public interface WebHandler {
    Response handle(String method, String path, String query, byte[] body, String contentType);
}
