"""A V2pn partner on this computer, for previews and screenshots: subscription, published index and API.

Usage: python tools/demo/demo_partner.py <pack folder> --published <folder> [--xray <xray.exe>] [--port 18998]
                                         [--server-port 24444] [--lock] [--hide-url] [--tls-server]

Serves at http://127.0.0.1:<port>/:
  /sub             the subscription "Kestrel" (one VLESS link per server of the pack's partner.json, all to one local
                   server so V2pn connects and pings for real), with the headers a partner sends: profile-title,
                   subscription-userinfo, v2pn-account-token, and with --lock hide-settings: 1 (--hide-url adds
                   hide-url: 1). --tls-server adds "🇺🇸 Нью-Йорк (TLS)", a VLESS link with TLS to us.kestrel.example
                   that does not answer: its generated config shows the partner's transport (transport.json fragment),
                   which V2pn puts on TLS and REALITY servers only.
  /releases/latest/download/<file>
                   a releases page with what tools/partner.cs publish wrote into --published: partners.json, its
                   .sig and partner-kestrel.zip. V2pn takes the partner from there.
  /v2pn/api/...    the API: account, status, servers, recommend?purpose=, card?id= (bearer token for all but status).

The index must be published with a test root key and --local (listing.json naming 127.0.0.1), and the V2pn build must
trust that key and ask this page for releases; tools/demo/try-partner.ps1 does all of it:

  dotnet build ... -p:V2pnPartnerKeys=<test root public key> -p:V2pnUpdateSources=http://127.0.0.1:18998/releases
  dotnet run tools/partner.cs -- publish --partners <scratch>/partners --out <scratch>/published --root-key <scratch>/root.pem --local

Never publish with the real root key for this. With --xray the script starts a local VLESS server (127.0.0.1 only,
freedom outbound) without a window; run xray from a folder outside the sandbox. Ctrl+C stops both.
"""

import argparse
import base64
import json
import os
import subprocess
import sys
import tempfile
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, quote, unquote, urlparse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demo_sub import USER_ID, server_config  # noqa: E402

TOKEN = "demo-token-kestrel"

# What the partner's own monitoring says now: load in percent and whether the server works.
LIVE = {
    "🇩🇪 Германия 1": {"load": 72, "healthy": True},
    "🇩🇪 Германия 2": {"load": 31, "healthy": True},
    "🇳🇱 Нидерланды": {"load": 18, "healthy": True},
    "🇫🇮 Финляндия": {"load": 55, "healthy": True},
    "🇹🇷 Турция": {"load": 40, "healthy": True},
    "🇸🇪 Швеция": {"load": 90, "healthy": False},
}

RECOMMEND = {
    "youtube": ["🇳🇱 Нидерланды", "🇩🇪 Германия 2"],
    "games": ["🇹🇷 Турция", "🇫🇮 Финляндия"],
}

CARDS = {
    "plan": {"title": "Тариф «Семья»", "text": "5 устройств, без ограничения трафика.\nОплачено до 31 января 2027."},
}


def server_names(pack_folder):
    with open(os.path.join(pack_folder, "partner.json"), encoding="utf-8") as f:
        pack = json.load(f)
    names = [s["name"] for s in pack.get("servers", []) if s.get("name")]
    return names or ["🇩🇪 Германия 1"]


class Handler(BaseHTTPRequestHandler):
    pack = ""
    published = ""
    port = 0
    server_port = 0
    lock = False
    hide_url = False
    tls_server = False
    names = []

    def log_message(self, format, *args):  # noqa: A002 - the base class names it so
        del format, args

    def send_json(self, body, status=200):
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def authorized(self):
        return self.headers.get("Authorization", "") == "Bearer " + TOKEN

    def do_GET(self):
        url = urlparse(self.path)
        path = unquote(url.path)
        query = parse_qs(url.query)
        if path == "/sub":
            return self.subscription()
        if path.startswith("/v2pn/api/"):
            return self.api(path[len("/v2pn/api/"):], query)
        if path.startswith("/releases/latest/download/"):
            name = os.path.basename(path)
            file = os.path.join(self.published, name)
            if name and os.path.isfile(file):
                with open(file, "rb") as f:
                    data = f.read()
                self.send_response(200)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
                return None
        self.send_response(404)
        self.end_headers()
        return None

    def subscription(self):
        body = "\n".join(
            "vless://%s@127.0.0.1:%d?encryption=none&security=none&type=tcp#%s" % (USER_ID, self.server_port, quote(name))
            for name in self.names
        )
        if self.tls_server:
            body += "\nvless://%s@us.kestrel.example:443?encryption=none&security=tls&sni=us.kestrel.example&fp=chrome&type=tcp#%s" % (
                USER_ID, quote("🇺🇸 Нью-Йорк (TLS)"))
        data = base64.b64encode(body.encode("utf-8"))
        gib = 1024 ** 3
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("profile-title", "base64:" + base64.b64encode("Kestrel".encode()).decode())
        self.send_header("subscription-userinfo", "upload=%d; download=%d; total=%d; expire=%d"
                         % (2 * gib, 27 * gib, 100 * gib, int(time.time()) + 40 * 86400))
        self.send_header("profile-update-interval", "12")
        self.send_header("profile-web-page-url", "http://127.0.0.1:%d/cabinet" % self.port)
        self.send_header("v2pn-account-token", TOKEN)
        if self.lock:
            self.send_header("hide-settings", "1")
        if self.hide_url:
            self.send_header("hide-url", "1")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def api(self, name, query):
        if name == "status":
            return self.send_json({"incidents": []})
        if not self.authorized():
            return self.send_json({"error": "token"}, 401)
        if name == "account":
            return self.send_json({"plan": "Семья", "status": "ACTIVE", "servers": {"healthy": 5, "total": 6},
                                   "devices": {"count": 2, "limit": 5}, "until": "2027-01-31"})
        if name == "servers":
            return self.send_json({"servers": [dict(name=n, **v) for n, v in LIVE.items()]})
        if name == "recommend":
            purpose = (query.get("purpose") or [""])[0]
            return self.send_json({"servers": RECOMMEND.get(purpose, [])})
        if name == "card":
            card = CARDS.get((query.get("id") or [""])[0])
            return self.send_json(card) if card else self.send_json({"error": "no card"}, 404)
        return self.send_json({"error": "unknown"}, 404)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("pack")
    parser.add_argument("--published", required=True)
    parser.add_argument("--xray")
    parser.add_argument("--port", type=int, default=18998)
    parser.add_argument("--server-port", type=int, default=24444)
    parser.add_argument("--lock", action="store_true")
    parser.add_argument("--hide-url", action="store_true")
    parser.add_argument("--tls-server", action="store_true")
    args = parser.parse_args()

    server = None
    if args.xray:
        folder = tempfile.mkdtemp(prefix="v2pn-demo-partner-")
        config = os.path.join(folder, "server.json")
        with open(config, "w", encoding="utf-8") as f:
            json.dump(server_config(args.server_port), f)
        flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        server = subprocess.Popen([args.xray, "run", "-c", config], creationflags=flags,
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    Handler.pack = os.path.abspath(args.pack)
    Handler.published = os.path.abspath(args.published)
    Handler.port = args.port
    Handler.server_port = args.server_port
    Handler.lock = args.lock
    Handler.hide_url = args.hide_url
    Handler.tls_server = args.tls_server
    Handler.names = server_names(Handler.pack)
    httpd = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print("Subscription: http://127.0.0.1:%d/sub" % args.port, flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        if server:
            server.terminate()


if __name__ == "__main__":
    main()
