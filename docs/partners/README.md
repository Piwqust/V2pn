# V2pn for providers: partner looks

[По-русски](README.ru.md)

A VPN provider can dress V2pn in its own look for the people who use its subscription: its name and mark and its own theme, assembled from V2pn's parts: a main-screen template, a connect control, colours, a font, a shape, a background and pictures. The connection, the servers, the settings and the state of protection stay V2pn's. People switch between plain V2pn and the provider's look at any time.

This page is for the provider's developers. It describes what to serve, how it is signed and checked, and what V2pn shows.

## Quick start

1. Install V2pn and turn on developer mode: About → updates → "Developer mode".
2. Open the ☰ menu → "Theme builder". Type the name and id, pick colours, a template, the connect button, a font, the background, blocks, pictures and buttons. The theme shows on the right at once, in three window sizes, light and dark. No certificate is needed for this.
3. "Show in V2pn" dresses the app itself in the theme. The title bar marks it "Preview" and removes it from there.
4. "Save pack…" writes a folder with `partner.json` and the pictures.
5. Under "Certificate request", press "Create a key…" and keep `provider-key.pem` to yourself. Type the domains of the subscription and the pack, tick the grants and press "Save request…".
6. Send `request.json` to V2pn's owner. If the pack has `routing.json`, `transport.json` or `core.json`, send them along (section 5).
7. The owner sends back `cert.json` and `cert.json.sig`. Put them into the pack folder.
8. Sign the pack: "Sign with my key" in the builder, or `dotnet run tools/partner.cs -- pack <folder> --key provider-key.pem`.
9. Serve the folder over https on a domain of the certificate, for example `https://example.com/v2pn/`.
10. Add the header `v2pn-partner: https://example.com/v2pn/` to the subscription's answer (section 6).
11. Check it all at once: `dotnet run tools/partner-check.cs -- https://example.com/s/<subscription token>` (section 11).

Without the builder, `tools/partner.cs new-key`, `request` and `pack` do the same (sections 1 and 2).

## What a provider gets

| | Any panel | Partner (certificate from V2pn) |
| --- | --- | --- |
| Subscription name, traffic, term, announcement, support and cabinet buttons (standard headers) | yes | yes |
| Banners, the renewal reminder, connecting at start, server order, updating and testing at start (Happ, INCY and v2RayTun headers) | yes | yes |
| Moving the subscription to another of your domains (`new-url`, `new-domain`) | no | yes |
| `v2pn://add` links from the provider's site or bot | yes | yes, and V2pn says the provider is a confirmed partner |
| The provider's mark on its subscription in V2pn's list | no | yes |
| The provider's look of V2pn: a theme of its own from V2pn's parts, or plain V2pn with its logo | no | yes, with the `edition` grant |
| Plan, server health by the provider's own checks, devices, incidents in the look | no | yes, with the `account` grant and an API |
| A description of the servers (groups, countries, purposes) and up to four buttons of your own | no | yes, with the `actions` grant, which every partner gets |
| Closed settings: server addresses hidden, the link cannot be passed on | no | yes, with the `lock` grant after a manual review |
| Your own routes, fragmentation, extra servers and balancing | no | yes, with the `routing`, `transport`, `core` grants after a manual review of each file |

## How it works

1. The provider's subscription answers with a `v2pn-partner` header: the address of a folder with its signed pack.
2. After every subscription update V2pn downloads `cert.json` and `partner.json` from that folder and checks:
   - `cert.json` is signed with V2pn's partner root key, in its term, not withdrawn;
   - `partner.json` is signed with the key the certificate names;
   - the subscription and the folder are on domains the certificate names;
   - every picture and every reviewed file (`routing.json`, `transport.json`, `core.json`) matches the size and SHA-256 in `partner.json`, and a reviewed file also the hash in the certificate.
3. When everything checks out, V2pn keeps the pack and offers the look once: "Open <provider>'s look" or "Stay in plain V2pn".
4. The person switches looks from the name in the title bar, from the tray menu, with Ctrl+1…9 (⌘ on a Mac) or on the Appearance page, where the look is also tuned (light or dark, accent).

A pack that fails any check is not used; V2pn stays plain and the subscription window says the provider's look did not pass the check. The servers work as usual either way.

## 1. Keys and the certificate

Make a key pair (ECDSA P-256) and keep the private half on your side. In the builder it is "Create a key…"; in a terminal:

```
dotnet run tools/partner.cs -- new-key provider-key.pem
```

or with OpenSSL:

```
openssl ecparam -name prime256v1 -genkey -noout -out provider-key.pem
openssl ec -in provider-key.pem -pubout -outform DER | base64
```

A certificate is issued from a request. The builder writes it ("Save request…"), or:

```
dotnet run tools/partner.cs -- request --id kestrel --name "Kestrel" --domains example.com,*.example.com --key provider-key.pem --grants edition,account --contact "@kestrel_admin" --out request.json
```

Files to review are added with `--review routing.json` (one per file). The request holds:

- a partner id: lowercase letters, digits and hyphens, 2–32 characters (`configra`);
- the name people see, up to 40 characters, without "V2pn" in it;
- the domains your subscription and pack are served from (`example.com`, `*.example.com`);
- the public key (base64 of the DER SubjectPublicKeyInfo, as printed above);
- the grants you need: `edition` (the look), `account` (the API) and, if needed, `lock` (closed settings), `routing`, `transport`, `core` (your routes, transport and core config, section 5); these four after a manual review. Every partner gets `actions` (buttons and the description of servers);
- the SHA-256 of the files to review;
- how to reach you.

The request is signed with your key. That shows V2pn's owner that the private key is yours and that nobody changed the request on the way. The private key itself never goes into the request. Send `request.json` to V2pn's owner, with the files to review attached.

The owner checks the signature, sees who asks for what (`partner.cs cert --from request.json`), reads the files to review and issues the certificate. This is done by hand: there is no bot or website that issues certificates, and there will not be one, because V2pn's root key must not sit on a server.

V2pn 1.0.3 and older refused a certificate with grants it did not know; with the `actions` grant your look does not open there until the person updates V2pn. Newer versions skip unknown grants.

You get `cert.json` and `cert.json.sig` back, valid for one year. Put both into the pack folder as they are.

## 2. The pack

A folder served over https at the address of your `v2pn-partner` header:

```
https://example.com/v2pn/
  cert.json          from V2pn's owner
  cert.json.sig
  partner.json       yours, signed with your key
  partner.json.sig
  logo-light.png     pictures named in partner.json
  logo-dark.png
  mark.png
  hero.png
  routing.json       optional, after a review (section 5)
  transport.json
  core.json
```

`partner.json` ([JSON schema](partner.schema.json)):

```json
{
  "schema": 1,
  "id": "kestrel",
  "name": "Kestrel",
  "brand": {
    "accent": "#2F7BFF",
    "logo": "logo-light.png",
    "logoOnLight": "logo-dark.png",
    "mark": "mark.png"
  },
  "edition": { "variants": ["theme", "signature"], "default": "theme" },
  "theme": {
    "template": "split",
    "connect": "bar",
    "palette": { "brand": ["#2F7BFF", "#00C2A8"], "tone": "cool" },
    "font": "Onest",
    "background": { "type": "gradient", "colors": ["#1E3A8A", "#2F7BFF"] },
    "blocks": ["actions", "plan", "traffic", "banner", "news", "servers"],
    "art": { "hero": "hero.png" }
  },
  "links": { "cabinet": "https://example.com/cabinet", "support": "https://t.me/example_support" },
  "api": "https://example.com/v2pn/api/",
  "assets": [
    { "name": "logo-light.png", "size": 10913, "sha256": "f602…cf08" }
  ]
}
```

| Field | What V2pn does with it |
| --- | --- |
| `id` | must equal the certificate's partner id |
| `name` | shown in the title bar, the switcher and the intro; 40 characters at most, no "V2pn" |
| `brand.accent` | `#RRGGBB`; the look's accent until the person picks another |
| `brand.logo`, `brand.logoOnLight` | the wide logo for dark and light title bars: 2:1 to 8:1, 32–256 px tall, transparent (340×96 works) |
| `brand.mark` | a square mark on a transparent ground for the subscription card: 64–512 px (128×128 works) |
| `edition.variants` | `theme` (your theme) and `signature` (plain V2pn with your logo), in the order the switcher lists them (section 9). Without a list, a pack with a theme opens its theme |
| `edition.default` | the look that opens first |
| `theme` | your theme: template, connect control, colours, font, shape, background, blocks and pictures (section 9) |
| `links.cabinet`, `links.support` | buttons in the look, when the subscription headers have none |
| `api` | your API's base address (with the `account` grant, on a certified domain) |
| `assets` | every picture and every reviewed file with its size and SHA-256; `tools/partner.cs pack` fills it |

A picture that does not fit its slot is not shown there (V2pn shows your name, or nothing) and `tools/partner-check.cs` says which and why; the rest of the pack keeps working.

What a pack does not set, on purpose: sizes, spacing, font files and the interface's colours. The theme picks among V2pn's parts, and V2pn derives every colour of light and dark from the brand colours, checking that text reads (WCAG AA, 4.5:1). These limits are what keeps a theme working at every window size, in light and dark, for every provider.

Limits: `partner.json` up to 64 KB, `cert.json` up to 16 KB, at most 8 pictures, PNG, WebP or JPEG only, 1 MB each, file names without folders. Besides pictures, only `routing.json`, `transport.json` and `core.json`, up to 64 KB each.

Sign `partner.json` after every change, with "Sign with my key" in the builder or with

```
dotnet run tools/partner.cs -- pack <pack folder> --key provider-key.pem
```

or with your own tooling: ECDSA P-256 over SHA-256 of the exact bytes of `partner.json`, the DER signature in base64 in `partner.json.sig`. Serve the files byte for byte (no re-encoding of line ends); V2pn checks the bytes it receives. The builder and `partner.cs pack` write the same bytes of `partner.json`.

## 3. Servers and buttons

With the `actions` grant (every partner gets it) the pack describes the servers of your subscription and sets up to four buttons of your own. A button is a label, an icon and an action from V2pn's catalogue: no logic of yours runs in the app. The server is chosen either by V2pn from its own measurements or by your API.

### Describing servers

```json
"servers": [
  { "name": "🇩🇪 Germany 2", "group": "Main", "country": "DE", "city": "Berlin",
    "purpose": ["youtube"], "priority": 5, "note": "Fast route to Europe" },
  { "host": "nl1.example.com", "group": "Gaming", "purpose": ["games"] },
  { "name": "Reserve", "hidden": true }
]
```

| Field | What V2pn does with it |
| --- | --- |
| `name` or `host` | finds the server of your subscription: by its exact name (as in the link) or by its address; one of the two is required |
| `group` | up to 24 characters; a header in the subscription's server list and in the look. Groups come in the order they first appear in the pack; servers without one come last, under "Other" |
| `country` | ISO 3166 alpha-2 code: the flag and the place on the map instead of a guess from the name |
| `city` | up to 32 characters, on a line under the name |
| `purpose` | what the server is for: `youtube`, `games`, `mobile`, `whitelist`, `streaming`, `ai`, `torrents`. V2pn names them in the person's language as chips on the server's row; other values are left out |
| `priority` | an integer from −1000 to 1000; between equal measurements the higher one is picked |
| `hidden` | `true`: the server is not listed or offered. Only a recommendation from your API can connect to it |
| `note` | up to 60 characters, on a line under the name |

A field over its limit is not shown (there is no cut-off line), and `partner-check` says which one. At most 300 entries.

### Buttons

```json
"actions": [
  { "id": "fast",   "label": "Fastest",     "icon": "bolt",   "do": "connect.best", "strategy": "ping" },
  { "id": "stable", "label": "Most stable", "icon": "shield", "do": "connect.best", "strategy": "stable", "scope": { "group": "main" } },
  { "id": "yt",     "label": "For YouTube", "icon": "play",   "do": "connect.recommend", "purpose": "youtube" },
  { "id": "renew",  "label": "Renew",       "icon": "card",   "do": "open.renew" }
]
```

`id`: lowercase letters, digits, `-` and `_`, up to 32 characters; `label`: up to 24 characters; `icon`: one of V2pn's icons, `bolt`, `shield`, `play`, `game`, `card`, `chat`, `globe`, `star`, `refresh`, `check`, `info`, `gift` (with another the button has none).

| `do` | What V2pn does |
| --- | --- |
| `connect.best` | tests the servers and connects to the best by `strategy`: `ping`, the lowest delay now; `stable`, the most answers over V2pn's last 10 checks, then the lowest median, then the smallest spread; `load`, the lowest load from `GET servers` (by ping without load numbers). `scope` narrows the choice: `group`, `purpose` or a list of `servers` (names or addresses) |
| `connect.recommend` | asks `GET <api>recommend?purpose=<purpose>` and connects to the first server of the answer that is in the subscription and answers. When the API does not answer or none of them does, to the fastest server with that purpose. Needs the API |
| `connect.server` | to the server `server` (a name or an address) |
| `connect.group` | to the fastest server of the group `group` |
| `open.cabinet`, `open.renew`, `open.support` | opens the cabinet, renewal or support: the links of the subscription's headers, else the pack's `links` |
| `open.link` | opens `url`: https on a certified domain or `t.me`, or `tg:` |
| `subscription.update` | updates the subscription |
| `check.services` | checks whether YouTube, Telegram and ChatGPT open through the current connection and shows the result |
| `info.card` | shows the card from `GET <api>card?id=<button id>` in a frame with your name. Needs the API |

Where the buttons are: in plain V2pn under the connect button while a server of your subscription is chosen, and in your look (in a theme, the `actions` block), at any window width. At most four are on screen: V2pn takes the first four it can run and leaves out an unknown action, a button missing a field it needs, or one that needs the API when there is none. After connecting, V2pn says where and why: "Germany 2: the fastest", "Netherlands: by <provider>'s check". Pressing the same button again within 3 seconds does nothing.

## 4. Closed settings

With the `lock` grant a provider can close the settings of its subscription, as in Happ and INCY: the `hide-settings: 1` header or `"lock": true` in the pack. The grant is given after a manual review: explain what you protect your servers from. Without it the header and the field do nothing; otherwise any subscription could hide where the person's traffic goes.

For the servers of that subscription V2pn then:

- does not show addresses, ports or keys (in the list, the table, the connection card, search by address);
- does not open the server editor and does not share it, copy its link, show a QR code, export its config or move it to another subscription;
- leaves these servers out of backups (a subscription update brings them back after a restore);
- does not let the subscription's link, User-Agent, headers and conversion be changed, and keeps your values for connecting at start and the server order;
- says on the subscription's card and in its window: "Settings closed by <name>".

`hide-url: 1` (also only with the `lock` grant) hides the subscription's link itself.

What the person always keeps: connecting and disconnecting, any server of the subscription, deleting the subscription, plain V2pn, their own servers and routing next to yours, the kill switch and V2pn's updates.

The protection is not absolute: the core's config is on disk and an experienced person can read it. The aim is the same as Happ's: addresses cannot be passed on in two clicks.

## 5. Reviewed files: routes, transport, core config

Three files of the pack change how traffic goes, so V2pn uses them only after a manual review. The owner of V2pn reads the file and issues a certificate with the right to it: the certificate's `reviewed` field holds the file's SHA-256. Change the file and V2pn stops using it until a new certificate with the new hash arrives; the subscription window says "<provider> routes changed after the review and are not used". The rest of the pack works as before.

| File | Grant | What it does |
| --- | --- | --- |
| `routing.json` | `routing` | your mode in the "What goes through the VPN" menu |
| `transport.json` | `transport` | fragmentation and noise when connecting to your servers |
| `core.json` | `core` | extra servers, chains, balancing and rules to them, for your servers |

The files sit in the pack folder next to the pictures and are listed in `assets` with their size and SHA-256, like the pictures (`tools/partner.cs pack` adds them). The names are exactly these, each file up to 64 KB. All three work only with the servers of your subscription; other subscriptions and the person's own servers never see them.

```json
"reviewed": {
  "routing.json": "16732e2a91e3d657874fa65783b5f98f8ff09caa505c174325eaaeb833187e74",
  "transport.json": "7fa0471883888fa10c85491b3a5fd116d72193e98ebde400f76fec3a81fa4760"
}
```

### Routes: routing.json

```json
{
  "name": "YouTube, Telegram and AI through the VPN",
  "rules": [
    { "domains": ["geosite:youtube", "geosite:telegram", "domain:chatgpt.com"], "ips": ["geoip:telegram"], "to": "proxy" },
    { "domains": ["domain:gosuslugi.ru", "domain:sberbank.ru"], "to": "direct" },
    { "domains": ["geosite:category-ads-all"], "to": "block" }
  ],
  "default": "direct"
}
```

| Field | |
| --- | --- |
| `name` | the mode's caption in the menu, up to 40 characters (optional) |
| `rules` | up to 50 rules in order, up to 500 entries in a rule and 2000 in all |
| `rules[].domains` | `example.com` or `domain:example.com` (the site and its subdomains), `full:` (this address only), `keyword:` (3 characters or more), `geosite:` (a list from the geo files) |
| `rules[].ips` | an address, a network (`91.108.4.0/22`) or `geoip:` |
| `rules[].to` | `proxy`: through your server, `direct`: past the VPN, `block`: never opens |
| `default` | where everything else goes: `proxy` or `direct` |

No regular expressions: the reviewer has to read every entry. V2pn puts "local network direct" first itself. `block` may not be broad: no `geoip:`, no whole zone (`domain:ru`), no network wider than /16, no local network. `geosite:` and `geoip:` lists come from the runetfreedom geo files, as for V2pn's own Russian modes, and are downloaded when the person picks your mode; a tag missing there stops the core.

In V2pn it is one more mode of "What goes through the VPN", "<provider> routes", while a server of your subscription is selected. V2pn says once that the mode is there but never turns it on by itself: the person picks it. When the partnership ends (the subscription is deleted, the certificate expires or is revoked) or the file changes after the review, the mode goes away, and if it was selected, V2pn switches to "Everything through VPN".

### Transport: transport.json

```json
{
  "fragment": { "packets": "tlshello", "length": "100-200", "interval": "10-20" },
  "noise": [ { "rand": "10-20", "delay": "10-16" } ]
}
```

| Field | |
| --- | --- |
| `fragment.packets` | `tlshello` or a range of packets from 1 to 10 (`1-3`) |
| `fragment.length` | piece size in bytes, 1 to 1500 |
| `fragment.interval` | pause between pieces in milliseconds, 0 to 200 |
| `noise` | up to 8 entries: `rand`, random bytes (1 to 1024), `delay`, a pause in milliseconds (0 to 200) |

- Xray: fragmentation on your TLS and REALITY servers that connect directly (REALITY gets `packets` 1-3, `tlshello` does not work with it); noise on mKCP and Hysteria servers.
- sing-box: its TLS fragmentation is turned on, it sizes the pieces itself; it has no noise.
- For your servers this replaces the fragmentation from the person's settings; other connections do not change.
- `mux` is not taken: Xray's and sing-box's are different protocols, and both need support on the server.

### Core config: core.json

```json
{
  "core": "xray",
  "outbounds": [
    { "tag": "ai-us", "protocol": "vless",
      "settings": { "vnext": [ { "address": "us.example.com", "port": 443, "users": [ { "id": "…", "encryption": "none" } ] } ] },
      "streamSettings": { "network": "raw", "security": "tls", "tlsSettings": { "serverName": "us.example.com" } } }
  ],
  "balancers": [ { "tag": "ai", "selector": ["ai-us", "proxy"], "strategy": "leastPing" } ],
  "observatory": { "probeUrl": "https://www.gstatic.com/generate_204", "probeInterval": "5m" },
  "rules": [ { "domains": ["geosite:openai", "domain:claude.ai"], "to": "ai" } ]
}
```

| Field | |
| --- | --- |
| `core` | `xray` or `sing-box`: the file goes only into configs of this core |
| `outbounds` | up to 8 outbounds in the core's format. Xray: `vless`, `vmess`, `trojan`, `shadowsocks`, `socks`, `http`, `hysteria`, `wireguard`; chain them with `streamSettings.sockopt.dialerProxy`. sing-box: the same plus `hysteria2`, `tuic`, `anytls`, and `urltest` to pick the fastest; chain them with `detour` |
| `dialer` | the tag of the outbound every server of your subscription connects through (optional) |
| `balancers` | Xray only, up to 4: `selector` is your tags or `proxy`, `strategy` is `random`, `roundRobin`, `leastPing` or `leastLoad` |
| `observatory` | Xray only: `probeUrl` (https) and `probeInterval` (10s to 1h) for `leastPing` and `leastLoad` |
| `rules` | up to 32 rules with `domains` and `ips` as in `routing.json`; `to` is your outbound, balancer or `proxy` |
| `allowTop` | sing-box only: `ntp`, `certificate`, what a subscription that sends a whole sing-box config may keep (V2pn still takes files out of them) |

- Your tags get the prefix `partner-<id>-`, so they never meet V2pn's. `proxy` is the server of your subscription the person connected to.
- Every outbound passes V2pn's subscription filter unchanged: no files, listening ports, API, `tor`, `reverse` or own `domain_resolver`. Otherwise the file is refused.
- The rules come after all of the person's own rules (pinned programs, the rules of the chosen mode, rules the person added to your routes mode) and only before the mode's catch-all default. So a site the person sends direct or blocks stays that way, whatever your rules say. They lead only to your outbounds and balancers: what goes direct or is blocked is `routing.json`'s business. Local network addresses are not allowed in them.
- When `outbounds` speak TLS and you sent `transport.json`, V2pn fragments them too.
- The person can turn off "<provider> connection settings" in the subscription window: then V2pn uses neither `transport.json` nor `core.json`.

### How to get a review

1. Check the files yourself: `dotnet run tools/partner.cs -- review <pack folder>` says in plain words what each of them does (every rule, every setting, every outbound, blocks and everything that touches DNS), and `tools/partner-check.cs` checks the format and the filter.
2. Send the files to the owner of V2pn with a short description: what they do and why.
3. The owner reads the files and issues the certificate: `partner.cs cert … --review routing.json --review transport.json --review core.json`.
4. The right lasts as long as the certificate, one year, then the review is repeated. Any change to a file means a new review and a new certificate.

What the reviewer looks at:

- the routes do not send banks, Gosuslugi and mail to your servers while the person expects them to go direct;
- DNS does not go to your server unencrypted;
- nothing is blocked that would open without the VPN;
- the transport works with your servers and does not touch other connections;
- the core config holds only what the request describes.

If the rules are broken, the certificate is revoked through the release channel, as usual.

Compatibility: V2pn up to 1.0.3 refuses a certificate with the `routing`, `transport` or `core` grants and a pack whose `assets` list these files. There the look does not open until the person updates V2pn.

## 6. Headers of the subscription

| Header | |
| --- | --- |
| `v2pn-partner: https://example.com/v2pn/` | the pack folder: https, ends with `/`, no query, on a certified domain |
| `v2pn-account-token: <token>` | optional; per subscription, up to 200 characters of `A–Z a–z 0–9 - _ . ~`, sent to your API as a bearer token |
| `profile-title`, `subscription-userinfo`, `profile-update-interval`, `support-url`, `profile-web-page-url`, `announce` | the standard ones; V2pn shows them in plain V2pn and in your look |

Other clients ignore the V2pn headers.

When a subscription answers with a whole Xray, sing-box or Clash config instead of server links, V2pn runs it only through a filter. The servers, the routing between them, DNS and balancing stay, and local ports listen on `127.0.0.1` only. These are taken out:

- the core's API, stats and an external control panel;
- logs to files;
- paths to any files on the computer;
- TUN, which V2pn turns on itself;
- Xray reverse, and sing-box tor and tailscale.

V2pn does not run a config for other cores from a subscription. Server links are the simplest thing to send.

### Headers of Happ, INCY and v2RayTun

V2pn also reads the headers that providers already send to Happ, INCY and v2RayTun, so a panel set up for them works in V2pn with no changes. These work for any subscription, without a certificate, except moving a subscription. Header names are case-insensitive. Text may be plain or `base64:<UTF-8 in base64>` (use base64 for Cyrillic). Links must be `https:` or `tg:`; any other link is dropped.

| Header | What V2pn does |
| --- | --- |
| `announce-url` | a "More" button under the announcement (`announce`) |
| `sub-info-text`, `sub-info-color`, `sub-info-button-text`, `sub-info-button-link` (Happ) | a banner under the subscription in the list, above its servers, in the subscription window and in your look: up to 200 characters, a button up to 25. `red`, `blue` or `green` pick V2pn's own red, blue or green; anything else is blue. `sub-info-text: 0` removes the banner. |
| `banner-text`, `banner-button-text`, `banner-button-url` or `banner-url`, `premium-url` (INCY) | the same banner when Happ's is not sent. The button leads to `banner-button-url`, else `banner-url`, else `premium-url`. INCY's colours (`banner-bg-color` and the rest) are ignored. |
| `sub-expire`, `sub-expire-button-link` (Happ); `expiry-banner-text`, `expiry-banner-button-text`, `expiry-banner-button-url` or `expiry-banner-url` (INCY) | the renewal reminder, shown in place of the banner from 3 days before the end in `subscription-userinfo` and after it. Your text when you send `expiry-banner-text`, else V2pn's: "Your subscription ends in 2 days", "Your subscription has ended". The "Renew" button leads to your renewal link, else `profile-web-page-url`, else there is no button. V2pn shows the reminder without `sub-expire` too; `sub-expire: 0` turns it off. |
| `subscription-autoconnect: 1`, `subscription-autoconnect-type: lastused`, `lowestdelay` or `random` | connects when V2pn starts switched off: to the last server used from this subscription, to the fastest one (V2pn tests them first and takes the quickest that answers), or to a random one. It applies when the chosen server is from this subscription, or when none is chosen and this is the first subscription. A V2pn left connected connects as before, and a server the person added by hand stays their choice. The person can turn it off for the subscription. |
| `subscriptions-sort-type: ping`, `alphabet` or `without` (Happ); `sort-order: ping`, `name` or `none` (INCY) | the order of this subscription's servers: fastest first, by name, or as you send them. The person can pick another order. |
| `subscription-ping-onopen-enabled: 1` | tests this subscription's servers when V2pn starts |
| `update-always: true` (v2RayTun), `subscription-auto-update-open-enable: 1` (Happ) | updates this subscription when V2pn starts. Only this one: Happ updates every subscription. |
| `new-url`, `new-domain` | moves the subscription to a new address, only for a V2pn partner whose certificate names the new host, and only in HTTP headers of a response with servers. https only; `new-url` wins over `new-domain`, and a bad `new-url` does not fall back to it; `new-domain` keeps the port, path and query; a move to the same host or to an address another subscription has is ignored. Without a certificate the headers are ignored: otherwise anyone answering on the old address could take the subscription away. The person's log says where it moved. |
| `ping-type`, `check-url-via-proxy` | read and kept, not used yet: V2pn tests every server through the server itself, to the test address the person set |
| `hide-settings`, `hide-url` | closed settings of the subscription, only for a partner with the `lock` grant (section 4); without it V2pn only keeps them |

**In the body.** As Happ and INCY do, V2pn reads `#key: value` lines at the top of a subscription that is a list of links, plain or base64:

```
#profile-title: base64:S2VzdHJlbCBWUE4=
#sub-info-text: base64:0KHQutC40LTQutCwIDMwJSDQvdCwINCz0L7QtCDQtNC+INC/0Y/RgtC90LjRhtGL
#subscription-autoconnect: 1
vless://…
```

A header wins over the same key in the body. Only from headers: `new-url`, `new-domain`, the INCY banners and the V2pn headers. A whole Xray, sing-box or Clash config has no body metadata.

**Not taken.** `routing`, fragmentation, noise, fronting and other settings that change traffic: they need V2pn's manual review (they will come as separate grants). Colour profiles, `change-user-agent` and the rest of the app settings of other clients are ignored.

**V2pn's User-Agent.** V2pn downloads a subscription as `v2rayN/7.25.6 V2pn/1.0.3`: v2rayN's agent first, because panels pick the format by it and V2pn reads the same formats, then V2pn's own name and version. A subscription that the person gave its own User-Agent sends that one instead.

**A Remnawave response rule.** Rules are checked from the top and the first match wins, so put this one above the rules that match `v2rayN`. Leave out `v2pn-partner` if you are not a V2pn partner; `v2pn-account-token` is per person and comes from your panel, not from a rule.

```json
{
  "name": "V2pn",
  "description": "Banners and defaults for V2pn",
  "enabled": true,
  "operator": "AND",
  "conditions": [
    { "headerName": "user-agent", "operator": "CONTAINS", "value": "v2pn", "caseSensitive": false }
  ],
  "responseType": "XRAY_BASE64",
  "responseModifications": {
    "applyHeadersToEnd": true,
    "headers": [
      { "key": "v2pn-partner", "value": "https://example.com/v2pn/" },
      { "key": "announce-url", "value": "https://example.com/news" },
      { "key": "sub-info-text", "value": "base64:RGlzY291bnQ6IDMwJSBvZmYgYSB5ZWFyIHVudGlsIEZyaWRheQ==" },
      { "key": "sub-info-color", "value": "green" },
      { "key": "sub-info-button-text", "value": "base64:QnV5" },
      { "key": "sub-info-button-link", "value": "https://t.me/example_bot" },
      { "key": "sub-expire", "value": "1" },
      { "key": "sub-expire-button-link", "value": "https://example.com/renew" },
      { "key": "subscription-autoconnect", "value": "1" },
      { "key": "subscription-autoconnect-type", "value": "lowestdelay" },
      { "key": "subscriptions-sort-type", "value": "ping" }
    ]
  }
}
```

## 7. Links

`v2pn://add?url=<subscription link, URL-encoded>&name=<name>` opens V2pn and asks before adding the subscription. V2pn also takes Happ (`happ://add/<link>`), v2RayTun (`v2raytun://import/<link>`) and Hiddify (`hiddify://import/<link>`) links. A link to a subscription V2pn already has only says so. On Windows the `v2pn` scheme is registered by the installed app; on macOS the app bundle claims it.

## 8. The API (optional)

With the `account` grant and an `api` address on a certified domain, V2pn asks:

`GET <api>account` with `Authorization: Bearer <v2pn-account-token>`, when the look opens and every 30 minutes while it is on screen:

```json
{
  "plan": "Personal",
  "status": "ACTIVE",
  "servers": { "healthy": 11, "total": 14 },
  "devices": { "count": 2, "limit": 5 },
  "until": "2027-01-31",
  "note": "Maintenance on Sunday night."
}
```

`GET <api>status`, without a token, every 15 minutes:

```json
{ "incidents": [ { "title": "Germany is down", "text": "We are on it.", "severity": "major" } ] }
```

For buttons and the description of servers (the `actions` grant) V2pn also asks, always with the subscription's token when it has one:

`GET <api>servers`, as often as `account`: when the look opens or a server of your subscription is chosen, at most every 30 minutes. The same fields as the pack's `servers`, plus `load` (0–100) and `healthy`. The API's values win over the pack's, field by field:

```json
{ "servers": [ { "name": "🇩🇪 Germany 2", "load": 31, "healthy": true }, { "host": "nl1.example.com", "load": 90, "healthy": false } ] }
```

`GET <api>recommend?purpose=youtube`, when a `connect.recommend` button is pressed: servers in order, best first (names or addresses, up to 50):

```json
{ "servers": ["🇳🇱 Netherlands", "🇩🇪 Germany 2"] }
```

`GET <api>card?id=plan`, when an `info.card` button with that `id` is pressed: a title of up to 60 characters and plain text of up to 600; line breaks are kept, there is no markup:

```json
{ "title": "Family plan", "text": "5 devices, unlimited traffic.\nPaid until 31 January 2027." }
```

Every field is optional; V2pn reads what it knows and ignores the rest. Answers are JSON up to 64 KB with status 200; anything else is treated as no answer. V2pn does not follow redirects with the token. When your site is blocked where the person is, V2pn asks again through the VPN. `servers.healthy`/`total` replace V2pn's own ping count in the look ("11 of 14 by <provider>'s check"). Send counts, never server addresses or credentials.

## 9. Looks: signature and theme

A partner has two looks:

| Look | What it is |
| --- | --- |
| `signature` | plain V2pn with your logo beside V2pn's in the title bar |
| `theme` | your theme: the main screen assembled from V2pn's parts in your colours |

Configra's five looks (`signature`, `warm`, `cabinet`, `studio`, `ascii`) are an edition of its own that V2pn's team wrote for Configra. Of these, other partners get `signature` only. A pack that names the others gets its theme instead, or `signature` without a theme. If you need a look the templates do not have, V2pn's team can write an edition of your own under an agreement.

### The theme

The theme is the `theme` field of `partner.json` (an example is in section 2). You pick the parts; V2pn assembles the screen and draws it at every window width, in light and dark. The easiest way to put a theme together is the builder (quick start above): it writes this field and shows the result at once.

| Field | Values | Default |
| --- | --- | --- |
| `template` | `center`, `half`, `split`, `list`, `map`, `compact` (below) | `center` |
| `connect` | `round` button, `capsule`, wide `bar`, `toggle` switch | each template has its own |
| `palette.brand` | 1–3 colours, `#RRGGBB`; the first is the accent and the brand area's colour | `brand.accent` |
| `palette.tone` | `warm`, `neutral`, `cool`: the tint of the ground and the cards | `neutral` |
| `font` | one of V2pn's fonts (below) | Geist |
| `corners` | `sharp`, `soft`, `round` | `soft` |
| `density` | `normal`, `compact` | `normal` |
| `background` | `{"type": "solid"}`: the first brand colour; `{"type": "gradient", "colors": [2 to 4 colours]}`; `{"type": "image", "art": "<a picture of the pack>"}` | `solid` |
| `blocks` | the order of the blocks: `plan`, `traffic`, `servers`, `actions`, `banner`, `news`, `art` | `actions`, `banner`, `plan`, `traffic`, `news`, `art`, `servers` |
| `art.hero`, `art.banner` | pictures for the template's slots | none |

Templates:

| Template | The main screen | Blocks | Pictures |
| --- | --- | --- | --- |
| `center` | a big connect button in the middle of the brand area, the place and the state under it; blocks and servers in a column on the right | all | `hero`, `banner` |
| `half` | the top in the brand colour or picture with the place and the button; blocks and servers below | all | `hero`, `banner` |
| `split` | a brand column on the left: the state, the button, plan and traffic; servers and the other blocks on the right | all | `hero`, `banner` |
| `list` | groups as in settings: the connection is the first row, then the server, the mode and the route, then the blocks | no `art` | none |
| `map` | V2pn's map across the window with panels over it | no `news`, no `art` | none |
| `compact` | one tall card: made for a small window, centred in a large one | no `art` | `hero` |

In a narrow window every template folds into one column: the state and the button first, then the blocks in your order. The server list takes the room that is left.

Blocks:

| Block | What it shows |
| --- | --- |
| `plan` | the plan, devices and term (from the subscription's headers or the API) |
| `traffic` | traffic used, with a bar |
| `servers` | the server list with search, filters, ping, update and "+" |
| `actions` | your buttons (section 3) |
| `banner` | the subscription's banner and an incident notice |
| `news` | the panel's announcement with its link |
| `art` | the wide picture `art.banner` |

A block shows when there is something for it. The server list is always there: without `servers` in `blocks`, V2pn puts it last. A block the template has no place for is left out.

Picture slots:

| Slot | Size |
| --- | --- |
| `art.hero` | an illustration in the brand area: 200–1600 px a side, at most 2:1. A cut-out on a transparent ground stands as it is; a picture with its own ground is framed |
| `art.banner` | the `art` block's wide picture: 400–2400 px wide, 2:1 to 5:1 |
| `background.art` | the brand area's picture: 640–3200 px a side, at most 3:1 |

The fonts come with V2pn, all with Latin and Cyrillic, under the SIL OFL: Geist, Inter, Manrope, Onest, Golos Text, Montserrat, Rubik, IBM Plex Sans, PT Sans, Raleway, Exo 2, Comfortaa, Unbounded, Spectral, JetBrains Mono. Case, spaces and hyphens do not matter: `golos-text` works too. V2pn takes no font files.

Colours. You give 1–3 brand colours and a tone; V2pn derives the rest for light and dark:
- text reads everywhere by WCAG AA: 4.5:1 for text, 3:1 for buttons and fields;
- where a brand colour becomes text or the ground under text, V2pn makes it just dark or light enough to read;
- in dark, the brand area is a deeper shade of the same hue;
- a colour that could pass for the "protected" green, the "attention" amber or the "unprotected" red is turned away from it;
- over a background picture V2pn lays a scrim strong enough for the text to read whatever the picture holds.

A field of the theme V2pn cannot use (an unknown template, a colour in another format, a font not in the list, a picture that does not fit its slot) falls back to its default, and the theme keeps working. `tools/partner.cs pack` and `tools/partner-check.cs` list what fell back and why. Unknown fields are ignored.

On the Appearance page the person picks light or dark and may swap your accent for one of V2pn's colours; plain V2pn's appearance stays as it is. While the theme is open, V2pn's windows and menus take its colours, font and shape too.

Example themes with pictures are in `tools/demo/themes/` (aurora, ember, pine). `tools/edition-matrix.ps1 -Theme <folder>` shoots a theme on every template, at three window widths, in light and dark.

What a pack cannot change: the state colours and the state pill (green protected, red unprotected), what the connect button does (its shape it can), V2pn's mark and the "on V2pn" note, the switcher back to plain V2pn, the settings and the routing. A provider's own routes are only a separate mode the person picks (section 5).

## 10. Life of a partnership

- **Updates.** Change and re-sign the pack whenever you like; V2pn picks it up at the next subscription update.
- **The term.** A certificate lasts a year. V2pn checks it at start and every hour; when it runs out the look closes with a note, and plain V2pn opens. Ask for a new one a month ahead; `tools/partner-check.cs` warns.
- **Leaving.** Stop sending `v2pn-partner` and the look goes away at the next update. A person who deletes your subscription loses your look with it.
- **Withdrawal.** The owner of V2pn can withdraw a certificate before its term; installed apps learn it from the release channel and close the look.

## 11. Checking

```
dotnet run tools/partner-check.cs -- https://example.com/s/<a real subscription token>
dotnet run tools/partner-check.cs -- --folder ./v2pn --subscription https://example.com/s/<token>
```

It runs V2pn's own checks with the keys built into the app and prints OK or what is wrong at each step: headers, certificate, signatures, domains, pictures, the theme (what it takes, what fell back and why, what V2pn did to the colours), the description of servers and the buttons (what V2pn leaves out and why), closed settings, the reviewed files (format, filter, whether the hash matches the certificate), the API including `servers`, `recommend` and `card` for your buttons.

An example pack with servers, buttons and the three reviewed files, and a local server with a subscription, the pack and the API for previews, are in `tools/demo/partner/` and `tools/demo/demo_partner.py`; example themes are in `tools/demo/themes/`.

To see the theme before publishing and without a certificate, open the pack folder in the builder and press "Show in V2pn". The preview is marked in the title bar, unsigned, stays on this computer and is gone after a restart.

To try the whole way with a subscription, ask V2pn's owner for a preview certificate for `127.0.0.1`, serve the pack and a test subscription from your computer over http, and turn on developer mode in V2pn (About → updates). V2pn then takes a pack served from `127.0.0.1`.

## Privacy

V2pn sends your servers nothing it would not send any subscription: the pack and the API are asked like the subscription itself, with V2pn's user agent and, for `account`, the token you issued. V2pn never sends the person's traffic, their other subscriptions, the device or anything it measures.
