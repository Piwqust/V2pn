"""A stand-in for github.com/<repo>/releases, for testing V2pn updates without publishing.

Serves packages from a folder laid out as <root>/<version>/V2pn-windows-x64.zip(.sha256).
Point a V2pn at it with the environment variable V2PN_RELEASES_URL=http://127.0.0.1:<port>/releases.

  /releases/latest                      302 to /releases/tag/v<newest>, as GitHub does
  /releases/download/v<ver>/<file>      the package or its checksum
  /releases/api                         the JSON list the beta channel reads

Usage: python tools/fake_releases.py <root> [port]   (port 0 picks a free one; the chosen port is printed)
"""

import json
import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def versions(root):
    found = [d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d))]
    return sorted(found, key=lambda v: tuple(int(p) for p in v.split(".")))


class Handler(BaseHTTPRequestHandler):
    root = "."

    def log_message(self, fmt, *args):
        sys.stdout.write("%s %s\n" % (self.command, self.path))
        sys.stdout.flush()

    def do_GET(self):
        path = self.path.replace("\\", "/").split("?")[0]
        all_versions = versions(self.root)
        if path == "/releases/latest" and all_versions:
            self.send_response(302)
            self.send_header("Location", "http://%s/releases/tag/v%s" % (self.headers["Host"], all_versions[-1]))
            self.end_headers()
            return
        if path == "/releases/api":
            body = json.dumps([
                {"tag_name": "v" + v, "prerelease": False, "body": "V2pn " + v}
                for v in reversed(all_versions)
            ]).encode()
            self.reply(200, body, "application/json")
            return
        prefix = "/releases/download/v"
        if path.startswith(prefix):
            version, _, name = path[len(prefix):].partition("/")
            file = os.path.join(self.root, version, os.path.basename(name))
            if os.path.isfile(file):
                with open(file, "rb") as f:
                    self.reply(200, f.read(), "application/octet-stream")
                return
        self.reply(404, b"Not Found", "text/plain")

    def do_HEAD(self):
        self.do_GET()

    def reply(self, code, body, content_type):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)


def main():
    Handler.root = os.path.abspath(sys.argv[1])
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print("listening on %d" % server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
