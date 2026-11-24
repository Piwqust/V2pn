# V2pn Hub

The server side of V2pn's partners, on `v2pn.angleford.co`: a provider signs up with a login and a password, fills in
one form (name, subscription link, look, special rights), proves that the subscription's domain is its own (a DNS record
or a file), and the partner is live as soon as it is saved. Moderators can take a partner down and bring it back,
approve the special rights, mark a domain proved by hand, reset passwords and see complaints. The hub signs
`partner-index.json` with its own key, builds each partner's `partner-<id>.zip` and page `/p/<id>/`, sends downloads
(`/r/<name>`, `/releases/...`) to GitHub's release files, and counts downloads, update checks and failed updates. The
flow is the owner's of 2026-11-19 (`docs/internal/PROVIDER-HUB.md`, the clickable prototype `docs/prototype.html`).

Node 24 or newer (the server runs 26), no npm packages: `node:http`, `node:sqlite`, `node:crypto`, `node:net`,
`node:tls`, `node:dns`, `node:zlib`, `node:test`.

```
node hub.mjs [serve]                     the service (HUB_HOST:HUB_PORT)
node hub.mjs keygen                      creates the hub's partner key if missing; prints its public half
node hub.mjs owner <login>               makes the account (or promotes it) the owner; prints a new password once
node hub.mjs reset-password <login>      a new password for any account (owners included); prints it once
node hub.mjs import-partner <folder> [--login <login>]
                                         partners/<id> (listing.json, partner.json, PNG pictures, routing.json) as a
                                         live partner with the rights its listing grants approved and its domains
                                         proved; its account is <login> (default: the id), made without a password
                                         when new
node hub.mjs publish                     rebuilds and re-signs the index, the zips and the pages
node --test test/                        the tests (from hub/)
node test/make-csharp-fixture.mjs        rewrites test/fixtures/csharp for V2pnHubFixtureTests.cs
node dev/serve.mjs --fixture --configra  the site and the hub on one local origin, for working on the pages (not
                                         deployed; --unproved leaves the fixture's domains without a proof record,
                                         --downloads fills the moderators' charts)
node dev/shot.mjs <url> <out.png>        a headless screenshot of a page
```

## Settings (environment)

The service reads `/etc/v2pn-hub.env` (`deploy/v2pn-hub.env.example`), the daily backup also
`/etc/v2pn-hub-backup.env` (`deploy/v2pn-hub-backup.env.example`).

| Variable | Default | What |
| --- | --- | --- |
| `HUB_PORT` | `18190` | Port of the service. |
| `HUB_HOST` | `127.0.0.1` | Address it listens on. Keep it local: Caddy is in front. |
| `HUB_DATA` | `/var/lib/v2pn-hub` | `hub.db` (SQLite, WAL), `keys/hub-partner-key.pem` (0600), `uploads/` (pictures by SHA-256), `root-partner-index.json` (the last good root-signed index). |
| `HUB_PUBLIC_DIR` | `/var/www/v2pn.angleford.co` | The site's root. The hub writes `partners/latest/download/` and `p/<id>/` there and nothing else. |
| `HUB_ORIGIN` | `https://v2pn.angleford.co` | The site's origin: links on pages, the CSRF check (`Origin` must equal it), `Secure` on the cookie when https. |
| `HUB_GITHUB_REPO` | `Piwqust/V2pn` | Where `/r/*` and `/releases/*` redirect; the root index's default address follows it. |
| `HUB_CHECK_UA` | `v2rayN/7.25.6 V2pn/1.3.0` | The User-Agent of the subscription check (V2pn's own, `V2pnSubs.SubscriptionUserAgent`, so panels answer with links). |
| `HUB_ROOT_INDEX_URL` | `https://github.com/<repo>/releases/latest/download/partner-index.json` | The index the owner signs with V2pn's root key; its signature is at the same address + `.sig`. |
| `HUB_ROOT_KEYS` | the key of `V2pnPartnerKeys` | Root keys that index may be signed with: base64 SubjectPublicKeyInfo, several split by `\|`. |
| `HUB_ROOT_PARTNERS` | none | Root partners kept whatever the index says: `id=domain,domain; id=domain`. |
| `HUB_GEO_MIRROR` | on | `0` turns off V2pn's own copy of runetfreedom's rule lists (`/geo/runetfreedom/`). |
| `HUB_GEO_SOURCE` | `https://github.com/runetfreedom/russia-v2ray-rules-dat/releases/latest/download` | Where that copy is fetched from (`<file>` and `<file>.sha256sum`). |
| `HUB_BACKUP_DIR` | `/var/backups/v2pn-hub` | Backup: where the daily archives are kept (`deploy/BACKUP.md`). |
| `HUB_BACKUP_KEEP_DAYS` | `14` | Backup: how many days of archives stay. |
| `HUB_BACKUP_REMOTE` | none | Backup: `user@host:path/` the new archive is also copied to with rsync over ssh. |
| `HUB_BACKUP_SSH` | `ssh -o BatchMode=yes` | Backup: the ssh command for that copy. |
| `HUB_BACKUP_AGE_RECIPIENT` | none | Backup: an age public key (`age1…`); the copy that leaves the server is encrypted to it. Without it every run warns that the copy holds the hub's key unencrypted. |

`HUB_NODE`, `HUB_AGE` and `HUB_RSYNC` name the programs the backup script runs (for its tests); the server leaves them
unset.

## What it does

**Accounts.** A login (3–32 of `a-z 0-9 . _ -`, any case, unique) and a password: 8 to 200 characters for a new one
(registration, a change); an account made when 6 were enough still signs in with its own. Passwords are kept as
scrypt (N 2^15, r 8, p 1, a salt each), hashed off the main thread on libuv's pool, two at a time: a sign-in,
registration or change beyond them is turned away (`rate_limited`) instead of queuing, and an unknown login costs the
same hash, so the time of the answer does not tell which logins exist. Roles: `owner` (only from the command line),
`moderator` (the owner adds and removes them), `provider` (everyone who registers). There is no mail and no recovery: a
moderator resets a provider's password, the owner a moderator's, the command line anyone's; the new password is shown
once and the account's sessions end. The session is `hub_s` (random 32 bytes, stored as SHA-256, httpOnly, Secure,
SameSite=Lax, 30 days). Every request that is not GET needs `X-Hub: 1` and `Origin: <HUB_ORIGIN>` (except the update
reports, below).

**Sign-in limits** (`lib/auth.mjs`, in memory: a restart clears them). A network is an IPv4 address or an IPv6 /64; an
IPv6 address also counts toward its /48, so a site cannot walk round a limit from /64 to /64. An hour allows 30
sign-ins from one network and 60 from one /48, 10 registrations from one network and 30 from one /48. Failed sign-ins
close a login to the network they came from after 10, to a /48 after 20, and to everyone after 100 (against guessing
from many networks at once); then even the right password waits for the hour. A network the login signed in from in the
last 30 days is trusted for it: there only that network's own limits apply, so guessing elsewhere cannot keep the owner
or a moderator out, not even at the ceiling.

**The partner.** One per account. Saving (`PUT /api/partner`) publishes it: there is no application and no queue. The
form: name (1–40 characters, never passing for V2pn), the subscription link, 1–3 brand colours, the background (`none`
or `map` for V2pn's map, `gradient` of the brand colours, `image` or `picture` with a backdrop), the layout (`v2pn` or
`stickers`, written to partner.json `theme.layout` when it is not V2pn's own), PNG pictures (`logo` and `logoOnLight`
2:1–8:1, 32–256 px tall; `mark` square 64–512 px; `backdrop` 640–3200 px, at most 3:1; 1 MB each, as
`V2pnPartnerSlots`), support and cabinet links (`t.me/x` gets `https://`), and the special rights. The look is checked
with V2pn's rules and colours (`lib/colors.mjs`, a port of `V2pnThemePalette`), and the page's text must read at 4.5:1.
The id comes from the name on the first save (Cyrillic spelled out, `-2`, `-3`… when taken, reserved or a root
partner's) and never changes. Domains are not typed: the hub checks the link from the server when it is new or
changed, and the link's host becomes the partner's domain (an imported partner keeps its own domains while the link
stays on them). A save is refused when the link fails the check (no servers V2pn runs, not 200, not a domain…) or when
the host is already a live partner's or a root partner's domain. A partner a moderator took down can still be edited;
it stays down.

**Cabinet and support links** (`linkAllowed` in `lib/validate.mjs`; V2pn applies the same rule). An https link without
a user name passes when its host is `t.me` or `telegram.me`, or one of the partner's domains or a name under it, or the
partner's site or a name under that. The site of a domain is reached by walking up from it one label at a time while
the name has at least two labels and is not in `PUBLIC_SUFFIXES` (a short list kept by hand: country second levels
such as `co.uk` `com.ru` `pp.ua`, hosting such as `workers.dev` `github.io`). For `sub.example.com`, `example.com` and
`lk.example.com` pass; for `sub.example.co.uk`, `example.co.uk` passes and `co.uk` does not. A link saved before the
rule that breaks it goes out without it (logged). Buttons (`open.link`) keep V2pn's narrower rule: `t.me` and the
partner's domains.

**Domain proof** (`lib/proof.mjs`). Before a new partner goes live, or a live one moves to another domain, its provider
proves the subscription's domain is its own: a DNS TXT record `_v2pn.<domain>` with the value `v2pn-verify=<token>`,
or a file `https://<domain>/.well-known/v2pn-verify.txt` that contains the token. The token is the account's (32 hex
characters, made when first shown) and the cabinet shows the record and the file. The hub asks DNS first (the
system's resolvers, 2 s a try, 5 s in all; a long value split into pieces is joined) and the file only when the record
is not there (the request rules below: public addresses only, no redirect, 8 s, 4 KB). It checks on save when the
domain is not proved, and on «Проверить» (`POST /api/partner/proof`), 30 checks an hour per account. Until the domain
is proved a new partner is saved as `pending`: out of the index, no page, no complaints; «Проверить» with the record in
place makes it live and publishes it. A live partner is never moved to a domain it has not proved: the save is refused
(`domain_unproved`, with the record to add) and the partner stays as it was. A taken-down partner may move; restored,
it waits for its domain. A proof stays with the account and the domain (`proofs`: how and when). Partners there before
proofs keep their domains: imported ones as proved by the owner, the others as `grandfathered`, live as before, and
their cabinet asks gently for the record. A moderator can mark a domain proved by hand (`POST
/api/mod/partners/<id>/proof`, an audit line). Every proof and every partner that went live on one is in the audit log.

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

**Root partners.** The owner signs some partners on his computer with V2pn's root key (`tools/partner.cs publish`, the
release asset `partner-index.json`). V2pn reads that index next to the hub's but goes by the hub's first
(`V2pnPartnerIndex.Merge`), so a hub partner with a root partner's id, or on one of its domains, would push the root
partner out. The hub keeps them for it: it reads the root index at start and every six hours (half an hour after a
failure) from `HUB_ROOT_INDEX_URL`, checks its signature with `HUB_ROOT_KEYS` as V2pn does, keeps the last good copy
in `HUB_DATA/root-partner-index.json` (read again at start, checked again) and holds on to it when GitHub does not
answer or a file does not verify; `HUB_ROOT_PARTNERS` adds a fixed list. A new partner never gets a root partner's id,
nobody saves on a root partner's domain, and collisions with partners made earlier are logged as warnings. A hub
partner that holds a root partner's id (made before the hub knew it) is taken down at the next publication (at once
when a refresh finds it): kept with its provider's work, out of the index, its page removed (`/p/<id>/` is the root
partner's address), the reason shown to its provider, an audit line and a log warning; it cannot be restored. A
partner imported from the root partner itself (`source` `import`, the same id) is not touched.

**Rule list mirror.** V2pn's Russian ways read runetfreedom's `geoip.dat` and `geosite.dat` (about 18 and 90 MB)
from a GitHub release; where GitHub is blocked or slow, V2pn falls back to the release branch, to this copy and to
jsDelivr (`V2pnGeoMirror`). The hub fetches both files at start and every six hours (half an hour after a failure)
from `HUB_GEO_SOURCE`, checks each against the release's `.sha256sum`, and writes them with their hashes to
`<HUB_PUBLIC_DIR>/geo/runetfreedom/`, which Caddy serves as static files; a file is replaced only when it is whole on
disk and has the release's hash, and an unchanged release costs two small requests. V2pn checks the hash again
against the publisher's own sources, so the copy needs no trust of its own. It takes about 110 MB of disk.

**Moderation** (`/api/mod/*`, owner and moderators): every partner newest first with its login, domains, state (`live`,
`pending`, `down`), how its domain is proved, complaints and the rights waiting; take down (with a reason) and restore;
mark a domain proved; approve or deny rights; complaints and clearing them; password resets; the audit log of every
decision; downloads, update checks and failed updates by day; publish now. The owner alone lists, adds and removes
moderators. A login not taken yet becomes a new moderator account with a password shown once.

**Complaints.** `POST /api/complaints {partner, text?}` without signing in, from the «Пожаловаться» form on a live
partner's page: 10 an hour per network and one a day per network and partner. Only the partner, the text (up to 1000
characters) and the time are kept.

**Publication** (after every save and decision, `node hub.mjs publish`, once a day, and right after start when the
last one is a day old): for every live partner, `partner-<id>.zip` (partner.json, its pictures and an approved
routing.json, laid out as `V2pnPartnerPublisher.Zip`) and `partner-index.json` (`V2pnPartnerPublisher.IndexBytes`'
layout: id, name, domains, grants, reviewed, until, pack size and SHA-256), signed with the hub's key as
`partner-index.json.sig` (DER ECDSA P-256 / SHA-256, base64 and a newline), and the empty `partners.json(.sig)` V2pn's
publisher still writes. Grants: `edition` always, then `account`, `actions`, `lock`, `routing` as far as approved. With
routing, routing.json is an asset of the pack and its SHA-256 is in the listing's `"reviewed": {"routing.json":
"<sha256>"}`, as the owner's root-signed index carries it. Every listing's `until` is 60 days from the day of the
publication, and the index is signed again daily, so a partner lives on while the hub does and drops out of V2pn within
two months if it stops (V2pn caps a hub-signed listing at published + 60 days as well). An imported partner's own term
(its listing's `until`) caps it when it ends sooner, and it drops out when that ends. Files are written atomically into
`HUB_PUBLIC_DIR/partners/latest/download/`; any problem stops the whole publication. Then `HUB_PUBLIC_DIR/p/<id>/`:
`index.html`, `page.js` (no inline script: works under `script-src 'self'`), the logos, the mark and the background; the
page takes the site's window styles (`/assets/site/site.css`), V2pn's flags and map and its font from the site. With
`#sub=<subscription>` in its address the page offers «Открыть в V2pn» (`v2pn://add?url=…&partner=<id>&name=…`); the
part after `#` never reaches the server. A partner taken down drops out of the index (V2pn prunes it) and its page says
it was taken down, in V2pn's colours. A partner waiting for its domain has neither pack nor page.

**Downloads.** `GET /r/<name>` (`windows-x64`, `windows-arm64`, `windows-x64-zip`, `windows-arm64-zip`, `macos-arm64`,
`macos-x64`, `install.ps1`, `install.sh`) answers 302 to `https://github.com/<repo>/releases/latest/download/<asset>`
and is counted per day and name (no IP, no cookie). `GET /releases/latest/download/<file>` and
`/releases/download/v<version>/<file>` answer 302 to the same paths on GitHub, for release file names only, so the hub
can be V2pn's first update source without storing anything.

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

**Health.** `GET /api/health`: `{ok, version, publication}`, `publication` the time of the last one. An outside monitor
should alert when it does not answer or when `publication` is more than two days old (`deploy/BACKUP.md`).

**Timers.** The download, check and failure counters go to the database every minute and at stop; every ten minutes
expired sessions and old counters are dropped, and the daily publication is made when due (retried at the next round
while it fails), with pictures no look uses swept a day after their upload.

Errors are `{"error": {"code", "message", "fields"?, "detail"?}}` with a stable `code` and a Russian `message`. Every
`/api/` answer has `Cache-Control: no-store`. Logs are JSON lines on stdout: method, path (never the query), status,
time; never passwords, cookies, tokens, addresses or subscription links.

**The database.** Numbered migrations (`lib/db.mjs`), each in one transaction at start. Version 2 moved the first
version's data: every provider became a partner with the look it had published; what its tier let the look carry (API
and sign-in by code, buttons, lock) became special rights already approved; a revoked provider is taken down with its
reason; a provider's account takes the provider's id as its login, any other account keeps its email as the login;
nobody has a password until `hub.mjs reset-password` gives one; sessions end; applications, codes, links, the outbox and
the look queue are gone. Version 3 added the update checks (`checks`). Version 4 added domain proof (`accounts.proof_token`,
`proofs`), with the domains of every partner there as proved (`import` or `grandfathered`). Version 5 added the failed
updates (`update_failures`).

## API

| Method and path | Who | Body → answer |
| --- | --- | --- |
| `GET /api/health` | anyone | `{ok, version, publication}` |
| `POST /api/auth/register` | anyone | `{login, password}` → `{account: {login, role}}` + cookie |
| `POST /api/auth/login` | anyone | `{login, password}` → `{account}` + cookie |
| `POST /api/auth/signout` | anyone | |
| `POST /api/auth/password` | signed in | `{password, newPassword}`; the other sessions end |
| `GET /api/me` | signed in | `{account: {login, role}, partner: {id, name, state} \| null}` |
| `GET /api/partner` | signed in | `{partner \| null, proof, look, review: {problems, notes, colours}, pictures, slots}` |
| `PUT /api/partner` | signed in | the form (below) → the same as GET, with `check` when the link was checked and `proofCheck` when the domain was |
| `POST /api/partner/check` | signed in | `{subscription}` → `{check, domain, taken, proof}` |
| `POST /api/partner/proof` | signed in | `{subscription?}` (the saved link without it) → the same as GET, with `proofCheck` and `taken` |
| `POST /api/partner/upload?slot=logo\|logoOnLight\|mark\|backdrop` | signed in | the PNG (`Content-Type: image/png`) → `{picture: {sha256, width, height}}` |
| `GET /api/partner/pictures/<sha256>` | signed in (own) | the PNG |
| `POST /api/partner/login-test` | signed in, with a partner | `{api?}` → `{test: {ok, lines}}` |
| `POST /api/complaints` | anyone | `{partner, text?}` → `{ok}` |
| `POST /api/report/update` | V2pn (no CSRF headers) | `{from, to, os, arch, stage, code}` → `204` (above) |
| `GET /api/mod/overview` | moderator | partners by state, rights waiting, complaints, the last publication or its error |
| `GET /api/mod/partners` | moderator | `{partners: [{id, name, login, domains, state, downReason, complaints, layout, brand, pictures, rights, pending, proof, check, createdAt, …}]}` |
| `GET /api/mod/partners/<id>` | moderator | the same with `look`, `rightDetails`, `complaintList` |
| `POST /api/mod/partners/<id>/down` · `/restore` | moderator | `{reason}` · nothing |
| `POST /api/mod/partners/<id>/proof` | moderator | `{domain?}` (the partner's own without it) → `{partner}` |
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
something approved earlier is in the pack. `proof` is `{domain, token, record: {name, type, value}, file: {url, text},
proved: {how, at} | null}` (`how`: `dns`, `https`, `moderator`, `import`, `grandfathered`); a `proofCheck` is `{domain,
ok, how, dns: {ok, text}, https: {ok, text} | null}`.

## Deployment

Copy `hub/` without `test/`, `docs/` and `dev/` to `/opt/v2pn-hub` (`deploy/` holds the units, the Caddy block and
examples of the settings). Then, as root:

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

`/etc/v2pn-hub.env` (`deploy/v2pn-hub.env.example`, with the root partners' settings commented out):

```sh
HUB_PORT=18190
HUB_DATA=/var/lib/v2pn-hub
HUB_PUBLIC_DIR=/var/www/v2pn.angleford.co
HUB_ORIGIN=https://v2pn.angleford.co
HUB_GITHUB_REPO=Piwqust/V2pn
```

`deploy/v2pn-hub.service` runs the hub as `v2pn-hub` with 256 MB at most, a read-only system and writes only to
`/var/lib/v2pn-hub` and the site's `partners/` and `p/`. An update migrates the database at start (back up `hub.db`
first; the daily backup does). The old `keys/hub-secret` and `keys/admin-passwords.json` are not read any more and may
be deleted.

**Caddy.** `deploy/Caddyfile` is the site's block: the hub's paths (`/api/*` with a 3 MB body limit, `/r/*`,
`/releases/*`) in front of the static files, the site's Content-Security-Policy, and what happens while the hub is down:
`/r/<name>` and the release files go straight to the same files on GitHub (`test/caddy.test.mjs` keeps them in step with
`lib/downloads.mjs`), `/api/` answers 503 in the hub's own JSON, other errors keep their status. It replaces the old block
in `/etc/caddy/Caddyfile`; then `caddy validate --config /etc/caddy/Caddyfile && systemctl reload caddy`.

The site's own upload must leave `partners/` and `p/` alone (no `rsync --delete` over them): the hub owns them, `p/configra/`
included once Configra is imported.

**Backups** (`deploy/BACKUP.md`): `v2pn-hub-backup.timer` runs `deploy/v2pn-hub-backup.sh` daily at about 03:30 UTC: an
online copy of `hub.db` (checked, one file without a WAL), `keys/` and `uploads/` in one archive under
`/var/backups/v2pn-hub/`, kept 14 days, and with `HUB_BACKUP_REMOTE` a copy on another machine, encrypted with age to
`HUB_BACKUP_AGE_RECIPIENT` (install `age` on the server; the private key stays off it). The archive holds the hub's key:
losing `keys/` means a new hub key and a V2pn release that trusts it; with it anyone can sign the partner index as the
hub. `deploy/BACKUP.md` has the setup, the restore (`age -d -i <key>` first for a copy from the other machine) and the
outside monitor for `/api/health`.
