#!/usr/bin/env python3
"""静态服务（开发用）：对 html/js/css/json 强制 no-store，避免浏览器拿旧文件。"""
import http.server, socketserver, sys, os
os.chdir(os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../"))

class H(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        p = self.path.split('?')[0]
        if p.endswith(('.html', '.js', '.mjs', '.css', '.json')) or p.endswith('/'):
            self.send_header('Cache-Control', 'no-store, must-revalidate')
            self.send_header('Pragma', 'no-cache')
        super().end_headers()
    def log_message(self, fmt, *a):
        sys.stderr.write("%s %s\n" % (self.log_date_time_string(), fmt % a))

port = int(sys.argv[1]) if len(sys.argv) > 1 else 8000
socketserver.ThreadingTCPServer.allow_reuse_address = True
with socketserver.ThreadingTCPServer(("0.0.0.0", port), H) as httpd:
    print(f"serving on :{port} (no-store for html/js)", flush=True)
    httpd.serve_forever()
