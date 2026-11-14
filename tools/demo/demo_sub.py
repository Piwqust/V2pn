"""Demo data for screenshots: a subscription whose servers really work.

Usage: python tools/demo/demo_sub.py <xray.exe> [sub_port] [server_port]

Starts a local VLESS server (xray, freedom outbound, listening on 127.0.0.1 only) and serves a
subscription "Demo VPN" at http://127.0.0.1:<sub_port>/sub. Every demo server is that one local
server under a city name, so V2pn connects, pings and shows traffic like it would with a provider,
and no real provider's servers or keys appear in a picture. Ctrl+C stops both.

Point a sandbox at it (tools/sandbox.ps1, never the V2pn you use): add the subscription link, or
set the Url of an existing one in guiConfigs/guiNDB.db, then press "Update subscriptions".
For a live speed chart, download through the sandbox's port:
  curl --socks5-hostname 127.0.0.1:20808 -o NUL "http://127.0.0.1:<sub_port>/blob?mb=200&kbps=4000"
Run xray from a folder outside the sandbox: tools/sandbox.ps1 -Stop ends every process under it.
"""

import base64
import json
import os
import subprocess
import sys
import tempfile
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

USER_ID = str(uuid.uuid5(uuid.NAMESPACE_DNS, "v2pn-demo"))

# Flag plus city: V2pn takes the country from the flag and shows the city under it.
SERVERS = [
    ("DE", "Frankfurt"), ("DE", "Berlin"), ("NL", "Amsterdam"), ("NL", "Rotterdam"),
    ("FI", "Helsinki"), ("SE", "Stockholm"), ("GB", "London"), ("FR", "Paris"),
    ("CH", "Zurich"), ("AT", "Vienna"), ("PL", "Warsaw"), ("LV", "Riga"),
    ("US", "New York"), ("US", "Los Angeles"), ("CA", "Toronto"), ("JP", "Tokyo"),
    ("SG", "Singapore"), ("TR", "Istanbul"), ("KZ", "Almaty"),
]


def flag(code):
    return "".join(chr(0x1F1E6 + ord(c) - ord("A")) for c in code)


def links(server_port):
    out = []
    for code, city in SERVERS:
        name = "%s %s" % (flag(code), city)
        out.append(
            "vless://%s@127.0.0.1:%d?encryption=none&security=none&type=tcp#%s"
            % (USER_ID, server_port, name.replace(" ", "%20"))
        )
    return base64.b64encode("\n".join(out).encode()).decode().encode()


def server_config(server_port):
    return {
        "log": {"loglevel": "warning"},
        "inbounds": [{
            "listen": "127.0.0.1",
            "port": server_port,
            "protocol": "vless",
            "settings": {"clients": [{"id": USER_ID}], "decryption": "none"},
            "streamSettings": {"network": "tcp", "security": "none"},
        }],
        "outbounds": [{"protocol": "freedom"}],
    }


class Handler(BaseHTTPRequestHandler):
    payload = b""

    def log_message(self, format, *args):  # noqa: A002 - the base class names it so
        del format, args

    def do_GET(self):
        # /blob?mb=N&kbps=R: N MiB of zeros at about R KiB/s, so a download through the sandbox draws
        # the speed chart. The pace is kept here: a client-side limit starts with a burst.
        if self.path.startswith("/blob"):
            query = dict(p.split("=", 1) for p in self.path.partition("?")[2].split("&") if "=" in p)
            mb = int(query.get("mb", 64))
            kbps = int(query.get("kbps", 0))
            self.send_response(200)
            self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(mb * 1024 * 1024))
            self.end_headers()
            chunk = bytes(64 * 1024)
            try:
                for _ in range(mb * 16):
                    self.wfile.write(chunk)
                    if kbps > 0:
                        time.sleep(64 / kbps)
            except OSError:
                pass
            return
        if self.path.split("?")[0] != "/sub":
            self.send_response(404)
            self.end_headers()
            return
        gib = 1024 ** 3
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        # The provider headers V2pn reads: the name, the traffic used of the plan and its end date.
        self.send_header("profile-title", "base64:" + base64.b64encode(b"Demo VPN").decode())
        self.send_header("subscription-userinfo", "upload=%d; download=%d; total=%d; expire=%d"
                         % (3 * gib, 41 * gib, 200 * gib, int(time.time()) + 23 * 86400))
        self.send_header("profile-update-interval", "12")
        self.send_header("Content-Length", str(len(self.payload)))
        self.end_headers()
        self.wfile.write(self.payload)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    xray = sys.argv[1]
    sub_port = int(sys.argv[2]) if len(sys.argv) > 2 else 18999
    server_port = int(sys.argv[3]) if len(sys.argv) > 3 else 24443

    folder = tempfile.mkdtemp(prefix="v2pn-demo-")
    config = os.path.join(folder, "server.json")
    with open(config, "w", encoding="utf-8") as f:
        json.dump(server_config(server_port), f)
    server = subprocess.Popen([xray, "run", "-c", config])

    Handler.payload = links(server_port)
    httpd = ThreadingHTTPServer(("127.0.0.1", sub_port), Handler)
    print("Subscription: http://127.0.0.1:%d/sub" % sub_port, flush=True)
    print("VLESS server: 127.0.0.1:%d (pid %d)" % (server_port, server.pid), flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.terminate()


if __name__ == "__main__":
    main()
