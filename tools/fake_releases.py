"""A stand-in for a V2pn update source (github.com/<repo>/releases or a mirror), for testing
updates without publishing anything.

Serves a folder laid out as <root>/<version>/<files of that release>, the way tools/release.ps1
writes dist/release/<version>/. A V2pn built with -p:V2pnUpdateSources=http://127.0.0.1:<port>/releases
(tools/update-e2e.ps1 does that) looks here instead of GitHub.

  /releases/latest/download/<file>      <file> of the newest version (release.json, release.json.sig)
  /releases/download/v<ver>/<file>      a package of that version

Usage: python tools/fake_releases.py <root> [port]   (port 0 picks a free one; the chosen port is printed)
"""

import os
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def versions(root):
    found = [d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d))]
    return sorted(found, key=lambda v: tuple(int(p) for p in v.split(".")))


class Handler(BaseHTTPRequestHandler):
    root = "."

    def log_message(self, format, *args):
        print("%s %s" % (self.command, self.path))
        sys.stdout.flush()

    def do_GET(self):
        path = self.path.replace("\\", "/").split("?")[0]
        all_versions = versions(self.root)
        version = name = None
        if path.startswith("/releases/latest/download/") and all_versions:
            version, name = all_versions[-1], path[len("/releases/latest/download/"):]
        elif path.startswith("/releases/download/v"):
            version, _, name = path[len("/releases/download/v"):].partition("/")
        if version and name:
            file = os.path.join(self.root, os.path.basename(version), os.path.basename(name))
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
