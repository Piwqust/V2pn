"""Serves a made-up subscription with N servers, for load and performance tests.

Usage: python tools/fake_sub.py <count> [port]   (port 0 picks a free one; the port is printed)
GET /sub returns <count> vless:// links, base64-encoded as panels send them. The addresses are
in TEST-NET ranges (RFC 5737), so nothing real is ever contacted.
"""

import base64
import sys
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

COUNTRIES = [
    ("DE", "Germany"), ("NL", "Netherlands"), ("FI", "Finland"), ("SE", "Sweden"), ("FR", "France"),
    ("GB", "United Kingdom"), ("US", "United States"), ("JP", "Japan"), ("SG", "Singapore"),
    ("TR", "Turkey"), ("KZ", "Kazakhstan"), ("PL", "Poland"), ("LV", "Latvia"), ("EE", "Estonia"),
    ("CH", "Switzerland"), ("AT", "Austria"), ("CA", "Canada"), ("AE", "United Arab Emirates"),
]


def flag(code):
    return "".join(chr(0x1F1E6 + ord(c) - ord("A")) for c in code)


def body(count):
    nets = ["192.0.2", "198.51.100", "203.0.113"]
    links = []
    for i in range(count):
        code, name = COUNTRIES[i % len(COUNTRIES)]
        host = "%s.%d" % (nets[i % 3], 1 + (i // 3) % 254)
        remark = "%s %s %d" % (flag(code), name, i + 1)
        links.append(
            "vless://%s@%s:443?encryption=none&security=reality&sni=www.example.com&fp=chrome"
            "&pbk=Z84J2IelR9ch3k8VtlVhhs5ycBUlXA7wHBWcBrjqnAw&sid=6ba85179e30d4fc2&type=tcp&flow=xtls-rprx-vision#%s"
            % (uuid.uuid5(uuid.NAMESPACE_DNS, str(i)), host, remark.replace(" ", "%20"))
        )
    return base64.b64encode("\n".join(links).encode()).decode().encode()


class Handler(BaseHTTPRequestHandler):
    payload = b""

    def log_message(self, fmt, *args):
        pass

    def do_GET(self):
        if self.path.split("?")[0] != "/sub":
            self.send_response(404)
            self.end_headers()
            return
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(self.payload)))
        self.end_headers()
        self.wfile.write(self.payload)


def main():
    Handler.payload = body(int(sys.argv[1]))
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print("listening on %d" % server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
