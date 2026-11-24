# V2pn Hub

The server side of V2pn's partners, on `v2pn.angleford.co`: a provider signs up with a login and a password, fills in
one form (name, subscription link, look, special rights), and the partner is live as soon as it is saved. Moderators
can take a partner down and bring it back, approve the special rights, reset passwords and see complaints. The hub
signs `partner-index.json` with its own key, builds each partner's `partner-<id>.zip` and page `/p/<id>/`, and sends
downloads (`/r/<name>`, `/releases/...`) to GitHub's release files. The flow is the owner's of 2026-11-19
(`docs/internal/PROVIDER-HUB.md`, the clickable prototype `docs/prototype.html`).

Node 24 or newer (the server runs 26), no npm packages: `node:http`, `node:sqlite`, `node:crypto`, `node:net`,
`node:tls`, `node:dns`, `node:zlib`, `node:test`.

```
node hub.mjs [serve]                     the service (HUB_HOST:HUB_PORT)
node hub.mjs keygen                      creates the hub's partner key if missing; prints its public half
node hub.mjs owner <login>               makes the account (or promotes it) the owner; prints a new password once
node hub.mjs reset-password <login>      a new password for any account (owners included); prints it once
node hub.mjs import-partner <folder> [--login <login>]
                                         partners/<id> (listing.json, partner.json, PNG pictures, routing.json) as a
                                         live partner with the rights its listing grants approved; its account is
                                         <login> (default: the id), made without a password when new
node hub.mjs publish                     rebuilds and re-signs the index, the zips and the pages
node --test test/                        the tests (from hub/)
node test/make-csharp-fixture.mjs        rewrites test/fixtures/csharp for V2pnHubFixtureTests.cs
node dev/serve.mjs --fixture --configra  the site and the hub on one local origin, for working on the pages (not deployed)
node dev/shot.mjs <url> <out.png>        a headless screenshot of a page
```

## Settings (environment)

| Variable | Default | What |
| --- | --- | --- |
| `HUB_PORT` | `18190` | Port of the service. |
| `HUB_HOST` | `127.0.0.1` | Address it listens on. Keep it local: Caddy is in front. |
| `HUB_DATA` | `/var/lib/v2pn-hub` | `hub.db` (SQLite, WAL), `keys/hub-partner-key.pem` (0600), `uploads/` (pictures by SHA-256). |
| `HUB_PUBLIC_DIR` | `/var/www/v2pn.angleford.co` | The site's root. The hub writes `partners/latest/download/` and `p/<id>/` there and nothing else. |
| `HUB_ORIGIN` | `https://v2pn.angleford.co` | The site's origin: links on pages, the CSRF check (`Origin` must equal it), `Secure` on the cookie when https. |
| `HUB_GITHUB_REPO` | `Piwqust/V2pn` | Where `/r/*` and `/releases/*` redirect. |
| `HUB_CHECK_UA` | `v2rayN/7.25.6 V2pn/1.3.0` | The User-Agent of the subscription check (V2pn's own, `V2pnSubs.SubscriptionUserAgent`, so panels answer with links). |

## What it does

**Accounts.** A login (3–32 of `a-z 0-9 . _ -`, any case, unique) and a password of 6 to 200 characters, kept as
scrypt (N 2^15, r 8, p 1, a salt each). Roles: `owner` (only from the command line), `moderator` (the owner adds and
removes them), `provider` (everyone who registers). There is no mail and no recovery: a moderator resets a provider's
password, the owner a moderator's, the command line anyone's; the new password is shown once and the account's
sessions end. The session is `hub_s` (random 32 bytes, stored as SHA-256, httpOnly, Secure, SameSite=Lax, 30 days).
Limits: 10 registrations and 30 sign-ins an hour per address, 10 failed sign-ins an hour per login (then even the right
password waits). Every request that is not GET needs `X-Hub: 1` and `Origin: <HUB_ORIGIN>`.

**The partner.** One per account. Saving (`PUT /api/partner`) publishes it: there is no application and no queue. The
form: name (1–40 characters, never passing for V2pn), the subscription link, 1–3 brand colours, the background (`none`
or `map` for V2pn's map, `gradient` of the brand colours, `image` or `picture` with a backdrop), the layout (`v2pn` or
`stickers`, written to partner.json `theme.layout` when it is not V2pn's own), PNG pictures (`logo` and `logoOnLight`
2:1–8:1, 32–256 px tall; `mark` square 64–512 px; `backdrop` 640–3200 px, at most 3:1; 1 MB each, as
`V2pnPartnerSlots`), support and cabinet links (`t.me/x` gets `https://`), and the special rights. The look is checked
with V2pn's rules and colours (`lib/colors.mjs`, a port of `V2pnThemePalette`), and the page's text must read at 4.5:1.
The id comes from the name on the first save (Cyrillic spelled out, `-2`, `-3`… when taken or reserved) and never
changes. Domains are not typed: the hub checks the link from the server when it is new or changed, and the link's host
becomes the partner's domain (an imported partner keeps its own domains while the link stays on them). A save is
refused when the link fails the check (no servers V2pn runs, not 200, not a domain…) or when the host is already a live
partner's domain. A partner a moderator took down can still be edited; it stays down.

**The check** (on save and `POST /api/partner/check`). The hub resolves the host itself, refuses private, loopback,
link-local, CGNAT, multicast and reserved addresses (v4 and v6, mapped forms, NAT64, 6to4; one bad answer refuses the
name), connects to the checked address with the original Host and SNI, follows at most 3 redirects to https with the
same checks, gives up after 8 s or 2 MB. It reads every header V2pn reads (`profile-title` with `base64:`,
`subscription-userinfo`, `support-url`, `profile-web-page-url`, `announce`, `profile-update-interval`, the Happ, INCY and
v2RayTun ones), the `#key: value` lines of a link list, and the body (base64 or plain links, Xray JSON, sing-box JSON,
Clash YAML), counts servers by protocol and which ones V2pn runs, and opens a TCP connection to up to 5 of them (public
addresses, 3 s each, 3 at a time). The result is one line per finding: `ok` / `warn` / `fail`, a short Russian text and
the raw values. 20 checks an hour per account, two at a time. The link is shown to its provider only, never to
moderators, and never logged.

**Special rights.** They change how V2pn works for the partner's people, so a moderator approves them; the rest of the
partner is live meanwhile. `account`: `{api, login}`, the API for live account data and sign-in by code (https, on the
partner's domains). `actions`: up to 4 buttons (`V2pnPartnerCatalog` rules; `info.card` and `connect.recommend` go out
only with an approved API). `lock`: `true`, the server addresses and settings hidden. `routing`: the provider's rules,
as `{name?, default: proxy|direct, rules: [{domains?, ips?, to: proxy|direct|block}]}` or as lines
`domain:ru, geoip:ru → direct` (`default → direct` sets the rest), checked as `V2pnRoutingPreset.Parse` checks
routing.json (`lib/routing.mjs`: no regular expressions, at most 50 rules and 2000 entries, block never takes a whole
country, zone, the local network or a network wider than /16). The request keeps its content and a SHA-256 of it (for
routing, the hash of routing.json's bytes); a moderator approves or denies that very hash. When the provider changes the
content it waits again, and the pack keeps what was approved before; a right no longer asked for leaves the pack; denying
an approved right takes it back. `transport` and `core` are never offered: V2pn takes them only from the index the owner
signs with the root key.

**Moderation** (`/api/mod/*`, owner and moderators): every partner newest first with its login, domains, state,
complaints and the rights waiting; take down (with a reason) and restore; approve or deny rights; complaints and
clearing them; password resets; the audit log of every decision; downloads and update checks by day; publish now. The owner alone lists,
adds and removes moderators. A login not taken yet becomes a new moderator account with a password shown once.

**Complaints.** `POST /api/complaints {partner, text?}` without signing in, from the «Пожаловаться» form on the
partner's page: 10 an hour per address and one a day per address and partner. Only the partner, the text (up to 1000
characters) and the time are kept.

**Publication** (after every save and decision, `node hub.mjs publish`, and once a day): for every live partner,
`partner-<id>.zip` (partner.json, its pictures and an approved routing.json, laid out as `V2pnPartnerPublisher.Zip`) and
`partner-index.json` (`V2pnPartnerPublisher.IndexBytes`' layout: id, name, domains, grants, reviewed, until, pack size
and SHA-256), signed with the hub's key as `partner-index.json.sig` (DER ECDSA P-256 / SHA-256, base64 and a newline),
and the empty `partners.json(.sig)` V2pn's publisher still writes. Grants: `edition` always, then `account`, `actions`,
`lock`, `routing` as far as approved. With routing, routing.json is an asset of the pack and its SHA-256 is in the
listing's `"reviewed": {"routing.json": "<sha256>"}`, as the owner's root-signed index carries it. A partner made on the
hub has no term: its `until` is a year from the day of publication, renewed daily; an imported one keeps its listing's
`until` and drops out when it ends. Files are written atomically into `HUB_PUBLIC_DIR/partners/latest/download/`; any
problem stops the whole publication. Then `HUB_PUBLIC_DIR/p/<id>/`: `index.html`, `page.js` (no inline script: works
under `script-src 'self'`), the logos, the mark and the background; the page takes the site's window styles
(`/assets/site/site.css`), V2pn's flags and map and its font from the site. With `#sub=<subscription>` in its address
the page offers «Открыть в V2pn» (`v2pn://add?url=…&partner=<id>&name=…`); the part after `#` never reaches the server.
A partner taken down drops out of the index (V2pn prunes it) and its page says it was taken down, in V2pn's colours.

**Downloads.** `GET /r/<name>` (`windows-x64`, `windows-arm64`, `macos-arm64`, `macos-x64`, `install.ps1`, `install.sh`)
answers 302 to `https://github.com/<repo>/releases/latest/download/<asset>` and is counted per day and name (no IP, no
cookie). `GET /releases/latest/download/<file>` and `/releases/download/v<version>/<file>` answer 302 to the same paths
on GitHub, for release file names only, so the hub can be V2pn's first update source without storing anything.

**Update checks.** A running V2pn asks for `/releases/latest/download/release.json` shortly after its start and every
six hours after, naming itself `V2pn/<version>` in the User-Agent. Each such request is counted per day and version (no
IP, no cookie, no id of the copy): the moderators' chart of how many copies are in use and on which versions. These are
requests, not people: one computer gives one to four or more a day. A local build counts as `<version>-local`; any
other User-Agent is redirected and not counted; a day keeps 40 versions, the rest add up as `other`. A check that
falls back to GitHub (the hub did not answer) is not seen.

**Update failure reports.** A V2pn whose person turned the reports on (off by default) tells the hub when an update
failed: `POST /api/report/update` with `Content-Type: application/json` and at most 1 KB:

```json
{"from": "1.4.2", "to": "1.4.3", "os": "windows", "arch": "x64", "stage": "verify", "code": "bad_signature"}
```

All six fields, nothing else: `from` and `to` versions like `1.4.3` or `1.5.0-beta.2` (digits `a.b.c`, an optional
`-` suffix of `0-9A-Za-z.-`, 32 characters at most), `os` `windows` or `macos`, `arch` `x64` or `arm64`, `stage` one of
`download` `verify` `stage` `swap` `start` `rollback`, `code` 1 to 40 of `a-z 0-9 _ -`. The answer is `204` with no
body; `422` names the bad fields (`{"error": {"code": "invalid", "fields": {...}}}`), `415` another content type, `413`
a larger body, `429` past 20 reports an hour from one IPv4 address or IPv6 /64 (refused ones count too). No cookie, no
`X-Hub`, no `Origin`, no CORS: the app is not a browser, and a web page cannot send JSON here without a preflight the
hub never answers. Only counts are kept: by day, `to`, `os`, `arch`, `stage` and `code` (not `from`, no address, no
User-Agent, no id of the copy); a day keeps 200 kinds, the rest add up with `to` and `code` as `other`. Moderators see
them as «Сбои обновлений» under the update checks.

**Health.** `GET /api/health`.

Errors are `{"error": {"code", "message", "fields"?, "detail"?}}` with a stable `code` and a Russian `message`. Every
`/api/` answer has `Cache-Control: no-store`. Logs are JSON lines on stdout: method, path (never the query), status,
time; never passwords, cookies, tokens or subscription links.

**The database.** Numbered migrations (`lib/db.mjs`). Version 2 moved the first version's data: every provider became a
partner with the look it had published; what its tier let the look carry (API and sign-in by code, buttons, lock) became
special rights already approved; a revoked provider is taken down with its reason; a provider's account takes the
provider's id as its login, any other account keeps its email as the login; nobody has a password until
`hub.mjs reset-password` gives one; sessions end; applications, codes, links, the outbox and the look queue are gone.

## API

| Method and path | Who | Body → answer |
| --- | --- | --- |
| `GET /api/health` | anyone | `{ok, version, publication}` |
| `POST /api/auth/register` | anyone | `{login, password}` → `{account: {login, role}}` + cookie |
| `POST /api/auth/login` | anyone | `{login, password}` → `{account}` + cookie |
| `POST /api/auth/signout` | anyone | |
| `POST /api/auth/password` | signed in | `{password, newPassword}`; the other sessions end |
| `GET /api/me` | signed in | `{account: {login, role}, partner: {id, name, state} \| null}` |
| `GET /api/partner` | signed in | `{partner \| null, look, review: {problems, notes, colours}, pictures, slots}` |
| `PUT /api/partner` | signed in | the form (below) → the same as GET, with `check` when the link was checked |
| `POST /api/partner/check` | signed in | `{subscription}` → `{check, domain, taken}` |
| `POST /api/partner/upload?slot=logo\|logoOnLight\|mark\|backdrop` | signed in | the PNG (`Content-Type: image/png`) → `{picture: {sha256, width, height}}` |
| `GET /api/partner/pictures/<sha256>` | signed in (own) | the PNG |
| `POST /api/partner/login-test` | signed in, with a partner | `{api?}` → `{test: {ok, lines}}` |
| `POST /api/complaints` | anyone | `{partner, text?}` → `{ok}` |
| `POST /api/report/update` | V2pn (no CSRF headers) | `{from, to, os, arch, stage, code}` → `204` (above) |
| `GET /api/mod/overview` | moderator | partners by state, rights waiting, complaints, the last publication or its error |
| `GET /api/mod/partners` | moderator | `{partners: [{id, name, login, domains, state, downReason, complaints, layout, brand, pictures, rights, pending, check, createdAt, …}]}` |
| `GET /api/mod/partners/<id>` | moderator | the same with `look`, `rightDetails`, `complaintList` |
| `POST /api/mod/partners/<id>/down` · `/restore` | moderator | `{reason}` · nothing |
| `GET /api/mod/rights` | moderator | the requests waiting: `{partner, partnerName, login, right, state, content, hash, text?, approved?}` |
| `POST /api/mod/partners/<id>/rights/<right>/approve` · `/deny` | moderator | `{hash}` · `{hash, reason}`; 409 when the request changed |
| `GET /api/mod/partners/<id>/complaints` · `POST …/complaints/clear` | moderator | |
| `POST /api/mod/accounts/<login>/password` | moderator (providers), owner (moderators too) | `{login, password}` |
| `GET /api/mod/pictures/<sha256>` | moderator | the PNG |
| `GET /api/mod/audit[?limit=]` · `/downloads[?days=]` · `POST /api/mod/publish` | moderator | `downloads` answers `{downloads, checks, failures}` |
| `GET /api/mod/moderators` · `POST /api/mod/moderators` · `POST /api/mod/moderators/<login>/remove` | owner | `{login}` → `{moderator: {login, role, password \| null}}` |
| `GET /r/<name>`, `GET /releases/...` | anyone | 302 to GitHub |

The form of `PUT /api/partner`: `{name, subscription, brand: ["#RRGGBB"], background, layout, logo, logoOnLight, mark,
backdrop (picture hashes or null), links: {support, cabinet}, rights: {account?, actions?, lock?, routing?}}`. Leaving
out `subscription` keeps the saved link; leaving out `rights` keeps the rights asked before. A right in the answer is
`{state: pending|approved|denied, content, hash, live, reason, text?, approved?, approvedText?}`, where `live` says
something approved earlier is in the pack.

## Deployment

Copy `hub/` without `test/`, `docs/` and `dev/` to `/opt/v2pn-hub` (`deploy/` holds the unit and an example of the settings). Then, as root:

```sh
useradd --system --home /var/lib/v2pn-hub --shell /usr/sbin/nologin v2pn-hub
install -d -o v2pn-hub -g v2pn-hub -m 0700 /var/lib/v2pn-hub
install -d -o v2pn-hub -g v2pn-hub -m 0755 /var/www/v2pn.angleford.co/partners /var/www/v2pn.angleford.co/p
install -m 0640 -o root -g v2pn-hub /opt/v2pn-hub/deploy/v2pn-hub.env.example /etc/v2pn-hub.env   # then fill it in
hub() { sudo -u v2pn-hub bash -c "set -a; . /etc/v2pn-hub.env; set +a; node /opt/v2pn-hub/hub.mjs $*"; }
hub keygen                                    # prints the public key for V2pnPartnerKeys
hub import-partner /tmp/configra              # a copy of partners/configra; its account is "configra"
cp /opt/v2pn-hub/deploy/v2pn-hub.service /etc/systemd/system/ && systemctl daemon-reload && systemctl enable --now v2pn-hub
hub owner <login>                             # prints the owner's password once
hub reset-password configra                   # Configra's password, to hand over
```

An update over a version 1 database migrates it at start (back up `hub.db` first). The old `keys/hub-secret` and
`keys/admin-passwords.json` are not read any more and may be deleted.

`/etc/v2pn-hub.env`:

```sh
HUB_PORT=18190
HUB_DATA=/var/lib/v2pn-hub
HUB_PUBLIC_DIR=/var/www/v2pn.angleford.co
HUB_ORIGIN=https://v2pn.angleford.co
HUB_GITHUB_REPO=Piwqust/V2pn
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
