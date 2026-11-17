# V2pn Hub

The server side of V2pn's providers, on `v2pn.angleford.co`: providers sign in by an email code, apply, get their
test subscription checked from the server, and once the owner approves them, design their tab's look in a cabinet.
The hub signs `partner-index.json` with its own key, builds each provider's `partner-<id>.zip` and page `/p/<id>/`,
and sends downloads (`/r/<name>`, `/releases/...`) to GitHub's release files.

Node 24 or newer (the server runs 26), no npm packages: `node:http`, `node:sqlite`, `node:crypto`, `node:net`,
`node:tls`, `node:dns`, `node:zlib`, `node:test`.

```
node hub.mjs [serve]                     the service (HUB_HOST:HUB_PORT)
node hub.mjs keygen                      creates the hub's partner key if missing; prints its public half
node hub.mjs admin-link <email>          a one-time sign-in link for an owner (HUB_ADMINS), valid 15 minutes
node hub.mjs import-partner <folder> [--tier 1|2] [--email <email>]
                                         partners/<id> (listing.json, partner.json, PNG pictures) as an approved
                                         provider with its look published; --email lets that address into its cabinet
node hub.mjs publish                     rebuilds and re-signs the index, the zips and the pages
node --test test/                        the tests (from hub/)
node test/make-csharp-fixture.mjs        rewrites test/fixtures/csharp for V2pnHubFixtureTests.cs
node dev/serve.mjs --fixture --configra  the site and the hub on one local origin, for working on the pages (not deployed)
node dev/walk.mjs …                      a headless walk of the provider's whole way, with screenshots (site/README.md)
```

## Settings (environment)

| Variable | Default | What |
| --- | --- | --- |
| `HUB_PORT` | `18190` | Port of the service. |
| `HUB_HOST` | `127.0.0.1` | Address it listens on. Keep it local: Caddy is in front. |
| `HUB_DATA` | `/var/lib/v2pn-hub` | `hub.db` (SQLite, WAL), `keys/` (`hub-partner-key.pem`, `hub-secret`, both 0600), `uploads/` (providers' PNGs by SHA-256). |
| `HUB_PUBLIC_DIR` | `/var/www/v2pn.angleford.co` | The site's root. The hub writes `partners/latest/download/` and `p/<id>/` there and nothing else. |
| `HUB_ORIGIN` | `https://v2pn.angleford.co` | The site's origin: links in mail and pages, the CSRF check (`Origin` must equal it), `Secure` on the cookie when https. |
| `HUB_ADMINS` | (none) | The owner's emails, comma-separated. They see `/api/admin/*`. |
| `HUB_SMTP_URL` | (none) | `smtps://user:pass@host:465` (TLS) or `smtp://user:pass@host:587` (STARTTLS when offered; a password is never sent without TLS). URL-encode `@` and `:` in the user and password. Unset: **no-mail mode**: codes are not sent, they wait in the admin's outbox (`GET /api/admin/outbox`) until they expire. |
| `HUB_MAIL_FROM` | `V2pn <no-reply@<origin host>>` | The From of the code mails. |
| `HUB_GITHUB_REPO` | `Piwqust/V2pn` | Where `/r/*` and `/releases/*` redirect. |
| `HUB_CHECK_UA` | `v2rayN/7.25.6 V2pn/1.3.0` | The User-Agent of the subscription check (V2pn's own, `V2pnSubs.SubscriptionUserAgent`, so panels answer with links). |

## What it does

**Sign-in.** `POST /api/auth/code {email}` makes a 6-digit code (10 minutes, 5 tries, stored as an HMAC) and mails it,
or puts it into the outbox. Limits: 10 requests an hour per address, 5 codes an hour per email and one per 30 s, 300 an
hour in all. `POST /api/auth/verify {email, code}` sets `hub_s` (random 32 bytes, stored as SHA-256, httpOnly, Secure,
SameSite=Lax, 30 days). The owner's first sign-in: `node hub.mjs admin-link <email>` prints
`<origin>/api/auth/admin-link?token=…`; opening it signs in and leads to `/admin/`. Every request that is not GET needs
`X-Hub: 1` and `Origin: <HUB_ORIGIN>`.

**Applications.** One per account: name, id (2–32 of `a-z0-9-`, not reserved), subscription domains (1–20,
`example.com` or `*.example.com`), test subscription link (https), contact, wanted tier (1 or 2), note. States:
draft → checking → submitted → approved | rejected (with the reason) → draft again for a new try. Submitting runs the
check; a check with a failing line keeps the application a draft (`check_failed`, the result in `detail`).

**The check** (`POST /api/check {url, domains}` and on submit). The hub resolves the host itself, refuses private,
loopback, link-local, CGNAT, multicast and reserved addresses (v4 and v6, mapped forms, NAT64, 6to4; one bad answer
refuses the name), connects to the checked address with the original Host and SNI, follows at most 3 redirects to https
with the same checks, gives up after 8 s or 2 MB. It reads every header V2pn reads (`profile-title` with `base64:`,
`subscription-userinfo`, `support-url`, `profile-web-page-url`, `announce`, `profile-update-interval`, the Happ, INCY and
v2RayTun ones), the `#key: value` lines of a link list, and the body (base64 or plain links, Xray JSON, sing-box JSON,
Clash YAML), counts servers by protocol and which ones V2pn runs, and opens a TCP connection to up to 5 of them (public
addresses, 3 s each, 3 at a time). The result is one line per finding: `ok` / `warn` / `fail`, a short Russian text and
the raw values. The test link is shown only to its provider and the owner, and never logged.

**The owner** (`/api/admin/*`): applications with their checks, approve with a tier (and a term, a year by default) or
reject with a reason, change tier or term, revoke and restore a provider, the queue of looks waiting for publication
(publish or return with a reason), the audit log of every decision, downloads by day, the outbox, publish now.

**The cabinet** (`/api/cabinet*`, after approval): the look: name, 1–3 brand colours, background (none, a gradient of
the brand colours, or a picture), PNG pictures (`logo` and `logoOnLight` 2:1–8:1, 32–256 px tall; `mark` square
64–512 px; `backdrop` 640–3200 px, at most 3:1; 1 MB each, as `V2pnPartnerSlots`), cabinet and support links; at tier 2
the API address, sign-in by code, lock, and up to four buttons (`V2pnPartnerCatalog` rules). It is checked with V2pn's
rules and colours (`lib/colors.mjs`, a port of `V2pnThemePalette`), and the provider page's text must read
at 4.5:1. Saving keeps a draft; "send" queues it; the owner's approval publishes it. `POST /api/cabinet/login-test`
calls the provider's `login/start` and one `login/poll` under the same SSRF rules.

**Publication** (after every decision, `node hub.mjs publish`, and once a day so ended terms drop out): for every
active provider with a published look, `partner-<id>.zip` (partner.json and its pictures, laid out as
`V2pnPartnerPublisher.Zip`) and `partner-index.json` (`V2pnPartnerPublisher.IndexBytes`' layout: id, name, domains,
grants, until, pack size and SHA-256), signed with the hub's key as `partner-index.json.sig` (DER ECDSA P-256 / SHA-256,
base64 and a newline), and the empty `partners.json(.sig)` V2pn's publisher still writes. Grants: `edition` always; at
tier 2 `account` (with an API), `actions` (with buttons), `lock`. Never `routing`, `transport` or `core`: V2pn takes
those only from the index the owner signs with the root key on his PC. Files are written atomically into
`HUB_PUBLIC_DIR/partners/latest/download/`; any problem stops the whole publication. Then `HUB_PUBLIC_DIR/p/<id>/`:
`index.html`, `page.js` (no inline script: works under `script-src 'self'`), the logos, the mark and the background;
the page takes the site's window styles (`/assets/site/site.css`), V2pn's flags and map and its font from the site. With
`#sub=<subscription>` in its address the page offers «Открыть в V2pn» (`v2pn://add?url=…&partner=<id>&name=…`); the
part after `#` never reaches the server.

**Downloads.** `GET /r/<name>` (`windows-x64`, `windows-arm64`, `macos-arm64`, `macos-x64`, `install.ps1`, `install.sh`)
answers 302 to `https://github.com/<repo>/releases/latest/download/<asset>` and is counted per day and name (no IP, no
cookie). `GET /releases/latest/download/<file>` and `/releases/download/v<version>/<file>` answer 302 to the same paths
on GitHub, for release file names only, so the hub can be V2pn's first update source without storing anything.

**Health.** `GET /api/health`.

Errors are `{"error": {"code", "message", "fields"?, "detail"?}}` with a stable `code` and a Russian `message`. Every
`/api/` answer has `Cache-Control: no-store`. Logs are JSON lines on stdout: method, path (never the query), status,
time; never codes, cookies, tokens or subscription links.

## API

| Method and path | Who | |
| --- | --- | --- |
| `GET /api/health` | anyone | `{ok, version, mail: smtp/outbox, publication}` |
| `POST /api/auth/code` `{email}` | anyone | `{delivery: mail/outbox}` |
| `POST /api/auth/verify` `{email, code}` | anyone | sets the cookie; `{account}` |
| `POST /api/auth/signout` | anyone | |
| `GET /api/auth/admin-link?token=` | the link | sets the cookie, 302 to `/admin/` |
| `GET /api/me` | signed in | `{account, application, provider}` |
| `GET /api/application` · `PUT /api/application` | signed in | the own application; PUT saves a draft |
| `POST /api/application/submit` | signed in | check and submit |
| `POST /api/check` `{url, domains}` | signed in | a check without saving (20 an hour) |
| `GET /api/cabinet` | provider | provider, look (draft, queued, published), review (problems, notes, colours), share links, pictures |
| `PUT /api/cabinet/look` · `POST /api/cabinet/look/submit` | provider | save the draft · send for publication |
| `POST /api/cabinet/upload?slot=logo|logoOnLight|mark|backdrop` | provider | body: the PNG (`Content-Type: image/png`) |
| `GET /api/cabinet/pictures/<sha256>` | provider (own), owner | the PNG |
| `POST /api/cabinet/login-test` `{api?}` | provider, tier 2 | `{ok, lines}` |
| `POST /api/cabinet/tier-request` | provider | asks for tier 2 |
| `GET /api/admin/overview` | owner | counts, last publication or its error, mail mode |
| `GET /api/admin/applications[?state=]` · `GET /api/admin/applications/<n>` | owner | with checks, emails, test links |
| `POST /api/admin/applications/<n>/approve` `{tier, until?}` · `/reject` `{reason}` | owner | |
| `GET /api/admin/providers` | owner | |
| `POST /api/admin/providers/<id>/tier` `{tier}` · `/until` `{until}` · `/revoke` `{reason}` · `/restore` | owner | |
| `GET /api/admin/queue` · `POST /api/admin/queue/<id>/approve` · `/return` `{reason}` | owner | looks waiting |
| `GET /api/admin/audit[?limit=]` · `/downloads[?days=]` · `/outbox` | owner | |
| `POST /api/admin/publish` | owner | |
| `GET /r/<name>`, `GET /releases/...` | anyone | 302 to GitHub |

## Deployment

Copy `hub/` without `test/`, `docs/` and `dev/` to `/opt/v2pn-hub` (`deploy/` holds the unit and an example of the settings). Then, as root:

```sh
useradd --system --home /var/lib/v2pn-hub --shell /usr/sbin/nologin v2pn-hub
install -d -o v2pn-hub -g v2pn-hub -m 0700 /var/lib/v2pn-hub
install -d -o v2pn-hub -g v2pn-hub -m 0755 /var/www/v2pn.angleford.co/partners /var/www/v2pn.angleford.co/p
install -m 0640 -o root -g v2pn-hub /opt/v2pn-hub/deploy/v2pn-hub.env.example /etc/v2pn-hub.env   # then fill it in
hub() { sudo -u v2pn-hub bash -c "set -a; . /etc/v2pn-hub.env; set +a; node /opt/v2pn-hub/hub.mjs $*"; }
hub keygen                                    # prints the public key for V2pnPartnerKeys
hub import-partner /tmp/configra --email <Configra's email>   # a copy of partners/configra
cp /opt/v2pn-hub/deploy/v2pn-hub.service /etc/systemd/system/ && systemctl daemon-reload && systemctl enable --now v2pn-hub
hub admin-link <owner email>                  # open the link within 15 minutes
```

`/etc/v2pn-hub.env`:

```sh
HUB_PORT=18190
HUB_DATA=/var/lib/v2pn-hub
HUB_PUBLIC_DIR=/var/www/v2pn.angleford.co
HUB_ORIGIN=https://v2pn.angleford.co
HUB_ADMINS=owner@example.com
HUB_GITHUB_REPO=Piwqust/V2pn
# HUB_SMTP_URL=smtps://user%40example.com:password@smtp.example.com:465
# HUB_MAIL_FROM="V2pn <no-reply@angleford.co>"
```

`/etc/systemd/system/v2pn-hub.service`:

```ini
[Unit]
Description=V2pn Hub
After=network-online.target
Wants=network-online.target

[Service]
User=v2pn-hub
Group=v2pn-hub
EnvironmentFile=/etc/v2pn-hub.env
WorkingDirectory=/opt/v2pn-hub
ExecStart=/usr/bin/env node --max-old-space-size=160 /opt/v2pn-hub/hub.mjs serve
Restart=on-failure
RestartSec=5
TimeoutStopSec=20
UMask=0022
MemoryMax=256M
NoNewPrivileges=true
CapabilityBoundingSet=
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
PrivateDevices=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictNamespaces=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
LockPersonality=true
SystemCallArchitectures=native
ReadWritePaths=/var/lib/v2pn-hub /var/www/v2pn.angleford.co/partners /var/www/v2pn.angleford.co/p

[Install]
WantedBy=multi-user.target
```

Caddy (the site's block, with the hub's paths in front of the static files):

```
v2pn.angleford.co {
	root * /var/www/v2pn.angleford.co
	@hub path /api/* /r/* /releases/*
	handle @hub {
		request_body {
			max_size 3MB
		}
		reverse_proxy 127.0.0.1:18190
	}
	handle {
		file_server
	}
	header {
		Content-Security-Policy "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'"
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
	}
	@partners path /partners/*
	header @partners Cache-Control "no-cache"
	@static path /assets/*
	header @static Cache-Control "public, max-age=86400"
}
```

The site's own upload must leave `partners/` and `p/` alone (no `rsync --delete` over them): the hub owns them, `p/configra/`
included once Configra is imported.

Backups: `/var/lib/v2pn-hub` (the database with `sqlite3 hub.db ".backup …"`, `keys/`, `uploads/`). Losing `keys/` means a
new hub key and a V2pn release that trusts it.
