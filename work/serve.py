#!/usr/bin/env python3
"""静态文件服务器（支持断点续传 Range / 多线程），用于分发复原产物与提取的原始游戏数据。

用法: python3 work/serve.py [目录] [端口]
"""
import os
import re
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer


class RangeFile:
    """把文件对象包装成只暴露 [start, end] 区间，供 copyfileobj 使用。"""

    def __init__(self, fp, remaining):
        self.fp = fp
        self.remaining = remaining

    def read(self, n=-1):
        if self.remaining <= 0:
            return b''
        if n < 0 or n > self.remaining:
            n = self.remaining
        data = self.fp.read(n)
        self.remaining -= len(data)
        return data

    def close(self):
        self.fp.close()


class Handler(SimpleHTTPRequestHandler):
    server_version = 'icey-serve/1.0'
    protocol_version = 'HTTP/1.1'

    def end_headers(self):
        if not getattr(self, '_range_advertised', False):
            self.send_header('Accept-Ranges', 'bytes')
        super().end_headers()

    def send_head(self):
        rng = self.headers.get('Range')
        if not rng:
            return super().send_head()
        path = self.translate_path(self.path)
        if os.path.isdir(path):
            return super().send_head()
        try:
            fp = open(path, 'rb')
        except OSError:
            self.send_error(404, 'File not found')
            return None
        try:
            size = os.fstat(fp.fileno()).st_size
            m = re.fullmatch(r'bytes=(\d*)-(\d*)', rng.strip())
            if not m or (not m.group(1) and not m.group(2)):
                fp.close()
                return super().send_head()
            if not m.group(1):                      # bytes=-N  末尾 N 字节
                start = max(0, size - int(m.group(2)))
                end = size - 1
            else:
                start = int(m.group(1))
                end = int(m.group(2)) if m.group(2) else size - 1
            if start >= size or start > end:
                self.send_response(416)
                self.send_header('Content-Range', 'bytes */%d' % size)
                self.send_header('Content-Length', '0')
                self.end_headers()
                fp.close()
                return None
            end = min(end, size - 1)
            self.send_response(206)
            self.send_header('Content-Type', self.guess_type(path))
            self.send_header('Accept-Ranges', 'bytes')
            self.send_header('Content-Range', 'bytes %d-%d/%d' % (start, end, size))
            self.send_header('Content-Length', str(end - start + 1))
            self.send_header('Last-Modified', self.date_time_string(os.fstat(fp.fileno()).st_mtime))
            self._range_advertised = True
            self.end_headers()
            fp.seek(start)
            return RangeFile(fp, end - start + 1)
        except Exception:
            fp.close()
            raise

    def log_message(self, fmt, *args):
        sys.stdout.write('%s - %s\n' % (self.address_string(), fmt % args))
        sys.stdout.flush()


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else '.')
    port = int(sys.argv[2] if len(sys.argv) > 2 else 8000)
    os.chdir(root)
    httpd = ThreadingHTTPServer(('0.0.0.0', port), Handler)
    httpd.daemon_threads = True
    print('serving %s at http://0.0.0.0:%d/  (root=%s)' % (root, port, root), flush=True)
    httpd.serve_forever()


if __name__ == '__main__':
    main()
