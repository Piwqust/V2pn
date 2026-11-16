# V2pn for providers: your tab in V2pn

[По-русски](README.ru.md)

A VPN provider can have a tab of its own in V2pn for the people who came from it. V2pn is one program for everyone; for those people its title bar has tabs, as in a browser: **V2pn** and the provider's, with its mark and name. The provider's tab puts the whole window into the provider's space: its colour on the connect button, a card with the plan, its own buttons, its servers in the list and, if it likes, its own background behind the connect panel. The connection, the settings and the state of protection stay V2pn's.

The V2pn tab is plain V2pn: all the person's servers, the provider's included, and nothing of any provider. A person who came from no provider has no tabs at all, and a provider's subscription is just a subscription for them: the standard headers and those of Happ, INCY and v2RayTun work, and nothing else does (no look, no buttons, no closed settings, no reviewed files, no API calls).

A person came from you when:

- they downloaded V2pn from your page or installed it with your command (section 12): that copy of V2pn has `v2pn-partner.txt` with your id beside the program, and your tab stays as long as it does;
- or they signed in with you by code (section 9): V2pn records it in its settings, and your tab goes when the person deletes their last subscription from your domains.

Pasting your subscription link by hand makes no tab: it is an ordinary subscription then.

V2pn's owner keeps the partners: every partner's look lives in the V2pn repository, and the owner checks it and publishes it with V2pn's releases, together with the partner's packages (`V2pn-<id>-windows-x64.zip` and so on: V2pn itself with your marker). A provider needs no keys, no certificates and no hosting of its own; it sends its logos and colours to the owner.

## What a provider gets

| | Any panel | Partner, in its tab |
| --- | --- | --- |
| Subscription name, traffic, term, announcement, support and cabinet buttons (standard headers) | yes | yes |
| Banners, the renewal reminder, connecting at start, server order, updating and testing at start (Happ, INCY and v2RayTun headers, section 7) | yes | yes |
| A tab in V2pn and a download page that brings it (section 12) | no | yes |
| `v2pn://add` links from the provider's site or bot | yes | yes, and in your tab V2pn says the provider is a V2pn partner |
| Moving the subscription to another of your domains (`new-url`, `new-domain`) | no | yes |
| The provider's mark on its subscription in V2pn's list | no | yes |
| Accent: your logo, your colour, your subscription card (section 5) | no | yes, every partner |
| Scene: your background behind the connect panel, your plan on it (section 5) | no | yes, when your theme has a background |
| Plan, server health by your own checks, devices, incidents | no | with the `account` grant and an API (section 9) |
| Sign-in by code: the person confirms in your bot and V2pn adds their subscription itself, no link to copy | no | with an API and `"login": true` (section 9) |
| A description of the servers (groups, countries, purposes) and up to four buttons of your own | no | with the `actions` grant (section 3) |
| Closed settings: server addresses hidden, the link cannot be passed on | no | with the `lock` grant, after a review (section 4) |
| Your own routes, fragmentation, extra servers and balancing | no | with the `routing`, `transport`, `core` grants, after a review of each file (section 6) |

## How it works

1. The owner of V2pn keeps every partner in `partners/<id>/` of the V2pn repository: `listing.json` (who the partner is, its subscription domains, its grants, until when) and the pack (`partner.json` with its pictures).
2. With every release, or on its own between releases, the owner publishes them with `tools/partner.cs publish`: `partner-index.json`, the list of all partners signed with V2pn's partner root key, and `partner-<id>.zip` for each partner. With a release, `tools/partner-packages.cs` also makes each partner's packages from V2pn's signed packages: `V2pn-<id>-windows-x64.zip`, `V2pn-<id>-windows-arm64.zip`, `V2pn-<id>-macos-arm64.zip` and `V2pn-<id>-macos-x64.zip`, the same files with `v2pn-partner.txt` holding your id (on macOS inside `V2pn.app/Contents/MacOS`). They sit next to V2pn's installers, on the same release page V2pn takes its updates from.
3. V2pn takes `partner-index.json` only for a person who came from a partner: at start and then at most every six hours, and it checks its signature against the keys built into the app. For anyone else it asks for nothing, except when they open «Sign in with a provider», which needs the list of providers.
4. In your tab, a subscription is yours when the host of its link is one of your domains (`example.com`, or anything under `*.example.com`). No header is needed. In the V2pn tab, and in another partner's tab, your subscriptions are ordinary subscriptions.
5. For a person who came from you, V2pn downloads your zip at once, before any subscription of yours is there, checks it against the size and SHA-256 in `partner-index.json`, unpacks only plain picture and JSON files within their limits, and checks every file against `partner.json`. A pack that fails a check is not used; V2pn keeps the last good one, or shows no tab of yours until a good one comes, and the servers work as usual either way.
6. A click on a tab switches at once, and V2pn opens on the tab the person left. A copy with your marker opens on your tab until the person picks another. Ctrl+Tab and Ctrl+Shift+Tab go through the tabs; in the mini window your tab shows only your mark, with your name as its tooltip.
7. Everything of yours applies only in your tab and only to your subscriptions: the look, the card, the buttons, closed settings, routes, connection settings and API calls. Switching tabs never changes the running connection; your connection settings apply from the next connect.
8. A copy with your marker is ordinary V2pn in V2pn's own folder, with V2pn's settings and start with the system. It updates from the same signed release and keeps `v2pn-partner.txt` through every update.

## How to become a partner

Send the owner of V2pn:

- the name people see, up to 40 characters, without "V2pn" in it;
- the domains your subscription links are on (`example.com`, `*.example.com`);
- a wide logo for a dark ground and one for a light ground (PNG with transparency, 2:1 to 8:1, 32–256 px tall; 340×96 works);
- a square mark for the subscription card (64–512 px, transparent; 128×128 works);
- your brand colour (`#RRGGBB`);
- if you want the Scene: two to four colours for a gradient, or a picture (640–3200 px a side, at most 3:1);
- if you want them: buttons and a description of your servers (section 3), the address of your API (section 9), links to your cabinet and support;
- how to reach you.

You can put all this together on [Build your look](builder/): it shows V2pn's window in your look (Accent and Scene, light and dark, three window sizes) and what `partner-check` will say, and downloads `partner.json` and `listing.json`. The page sends nothing anywhere: you send the files and pictures yourself.

What the owner does: makes `partners/<id>/` from it, checks it with `tools/partner-check.cs partners/<id>`, decides on the grants and publishes it with the next release, or at once on its own. Your look reaches people as section 10 says.

## 1. listing.json

Written by the owner, one per partner; it is not published as it is.

```json
{
  "id": "kestrel",
  "name": "Kestrel",
  "domains": ["kestrel.example", "*.kestrel.example"],
  "grants": ["account", "actions"],
  "until": "2027-11-16",
  "contact": "@kestrel_admin"
}
```

| Field | |
| --- | --- |
| `id` | the folder's name: lowercase letters, digits and hyphens, 2–32 characters |
| `name` | the name people see, up to 40 characters, no "V2pn" |
| `domains` | the subscription domains: `example.com` is that host, `*.example.com` every host under it (not the domain itself). At least a name and a zone, so `*.com` is never possible. Two partners never share a domain |
| `grants` | `account` (the API), `actions` (buttons and the description of servers), `lock` (closed settings), `routing`, `transport`, `core` (reviewed files). `edition`, the look itself, every partner has |
| `until` | the last day of the partnership; after it the look goes away |
| `contact` | how the owner reaches the provider; it stays in the repository and is never published |

## 2. The pack

`partners/<id>/` holds `partner.json` and its files:

```
partners/kestrel/
  listing.json       the owner's
  partner.json       the look
  logo-light.png     pictures named in partner.json
  logo-dark.png
  mark.png
  routing.json       optional, after a review (section 6)
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
  "theme": {
    "palette": { "brand": ["#2F7BFF"] },
    "background": { "type": "gradient", "colors": ["#1E3A8A", "#2F7BFF"] }
  },
  "links": { "cabinet": "https://kestrel.example/cabinet", "support": "https://t.me/kestrel_support" },
  "api": "https://kestrel.example/v2pn/api/",
  "assets": [
    { "name": "logo-light.png", "size": 10913, "sha256": "f602…cf08" }
  ]
}
```

| Field | What V2pn does with it |
| --- | --- |
| `id` | must equal the folder's name and `listing.json` |
| `name` | shown in the subscription window, the card and the notes; 40 characters at most, no "V2pn" |
| `brand.accent` | `#RRGGBB`; the brand colour when `theme.palette.brand` names none |
| `brand.logo`, `brand.logoOnLight` | the wide logo for dark and light grounds (the Scene) |
| `brand.mark` | the square mark on your tab, the subscription card and in V2pn's list |
| `theme` | the brand colour and the Scene's background (section 5) |
| `links.cabinet`, `links.support` | the card's "Renew" and "Support" links, when the subscription headers have none |
| `api` | your API's base address (with the `account` grant, on a domain of the listing) |
| `login` | `true`: V2pn offers to sign in with you by code (section 9). Needs `api` |
| `assets` | every picture and every reviewed file with its size and SHA-256; `tools/partner.cs pack partners/<id>` fills it in |

A picture that does not fit its slot is not shown there (V2pn shows your name, or nothing) and `tools/partner-check.cs` says which and why; the rest of the pack keeps working.

Limits: `partner.json` up to 64 KB; at most 8 pictures, PNG, WebP or JPEG, 1 MB each; besides pictures only `routing.json`, `transport.json` and `core.json`, up to 64 KB each. File names are plain: ASCII letters, digits, `-` and `_`, one extension, no folders. `publish` refuses a folder with any other file, or a `partner.json` whose `assets` are not the folder's files.

What a pack does not set, on purpose: the layout, sizes, spacing, the font, the corners and the interface's colours. V2pn lays the window out itself and derives every colour of light and dark from the brand colour, checking that text reads (WCAG AA, 4.5:1). These limits keep a look working at every window size, in light and dark, for every provider.

The fields of former packs are read without error and not used: `edition` (`variants`, `default`, `art`) and in `theme` `connect`, `window`, `template`, `font`, `corners`, `density`, `blocks`, `art`, `palette.tone`. `tools/partner-check.cs` lists them.

## 3. Servers and buttons

With the `actions` grant the pack describes the servers of your subscription and sets up to four buttons of your own. A button is a label, an icon and an action from V2pn's catalogue: no logic of yours runs in the app. The server is chosen either by V2pn from its own measurements or by your API.

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
| `group` | up to 24 characters; a header in the subscription's server list. Groups come in the order they first appear in the pack; servers without one come last, under "Other" |
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
| `open.link` | opens `url`: https on a domain of the listing or `t.me`, or `tg:` |
| `subscription.update` | updates the subscription |
| `check.services` | checks whether YouTube, Telegram and ChatGPT open through the current connection and shows the result |
| `info.card` | shows the card from `GET <api>card?id=<button id>` in a frame with your name. Needs the API |

The buttons are glass capsules under the connect panel in your tab; in a narrow window they are one row that scrolls. At most four are on screen: V2pn takes the first four it can run and leaves out an unknown action, a button missing a field it needs, or one that needs the API when there is none. After connecting, V2pn says where and why: "Germany 2: the fastest", "Netherlands: by <provider>'s check". Pressing the same button again within 3 seconds does nothing.

## 4. Closed settings

With the `lock` grant a provider can close the settings of its subscription, as in Happ and INCY: the `hide-settings: 1` header or `"lock": true` in the pack. The owner gives the grant after a review: explain what you protect your servers from. Without it the header and the field do nothing; otherwise any subscription could hide where the person's traffic goes.

For the servers of that subscription V2pn then:

- does not show addresses, ports or keys (in the list, the table, the connection card, search by address);
- does not open the server editor and does not share it, copy its link, show a QR code, export its config or move it to another subscription;
- leaves these servers out of backups (a subscription update brings them back after a restore);
- does not let the subscription's link, User-Agent, headers and conversion be changed, and keeps your values for connecting at start and the server order;
- says on the subscription's card and in its window: "Settings closed by <name>".

`hide-url: 1` (also only with the `lock` grant) hides the subscription's link itself.

What the person always keeps: connecting and disconnecting, any server of the subscription, deleting the subscription, the V2pn tab, their own servers and routing next to yours, the kill switch and V2pn's updates. In the V2pn tab nothing is closed: there your subscription is an ordinary one.

The protection is not absolute: the core's config is on disk and an experienced person can read it. The aim is the same as Happ's: addresses cannot be passed on in two clicks.

## 5. The look: Accent and Scene

Your tab has one look, in two forms. The V2pn tab wears neither:

| Form | When | What it is |
| --- | --- | --- |
| Accent | always | V2pn's own layout (the sidebar, the glass connect panel, the status card, the world map behind them) in your brand: your mark and name on your tab in the title bar, the connect button in your colour with black or white text, whichever reads; your subscription's card in the sidebar (the mark, the name, the plan, the days left, devices, the traffic bar, the banner, "Renew" and "Support" in your colour); your buttons under the connect panel |
| Scene | when `theme.background` is a gradient or a picture | Accent with your background in place of the map, and your plan on it rather than on the sidebar card: under the connect panel and your buttons, your logo (in a wide window), the days left in large type, the plan, the traffic, the banner, "Renew" and "Support". Where the stage has no room for the plan, as in the mini window, your background is behind the connect panel and the plan stays on the card, right under it |

Without a plan (no term and no traffic in your headers or API) the Scene shows your name where the days would be, and there is no "Renew". The state colours (protected, unprotected, connecting) are always V2pn's, on their own pill, over any background.

### The theme

The `theme` field of `partner.json` (an example is in section 2):

| Field | Values | Default |
| --- | --- | --- |
| `palette.brand` | 1–3 colours, `#RRGGBB`. The first is your brand colour: the connect button, the links, the card. V2pn does not draw the others today | `brand.accent` |
| `background` | what the Scene stands on: `{"type": "gradient", "colors": [2 to 4 colours]}`, corner to corner at 135°; `{"type": "image", "art": "<a picture of the pack>"}`; `{"type": "solid"}` or no background: Accent only | none |

Picture slots:

| Slot | Size |
| --- | --- |
| `brand.logo` | the logo for a dark ground: 2:1 to 8:1, 32–256 px tall, transparent |
| `brand.logoOnLight` | the same logo for a light ground, the same sizes |
| `brand.mark` | a square mark on a transparent ground: 64–512 px |
| `theme.background.art` | the Scene's picture: 640–3200 px a side, at most 3:1 |

Colours. You give the brand colour (and the background's); V2pn derives the rest for light and dark:
- text reads everywhere by WCAG AA: 4.5:1 for text, 3:1 for buttons and fields;
- where the brand colour becomes text or the ground under text, V2pn makes it just dark or light enough to read;
- a colour that could pass for the "protected" green, the "attention" amber or the "unprotected" red is turned away from it;
- in dark mode a gradient is deepened, so a large area does not glare;
- on the Scene, text is white or near-black, whichever reads on the whole background; over a picture V2pn lays a scrim strong enough for the text to read whatever the picture holds.

A field of the theme V2pn cannot use (a colour in another format, a picture that does not fit its slot) falls back to its default, and the look keeps working; `tools/partner.cs pack` and `tools/partner-check.cs` say what and why. Unknown fields are ignored.

`tools/edition-matrix.ps1 -Partner <id>` shoots the V2pn tab beside yours and your tab in Accent and Scene, in light and dark at three window sizes (`-Theme <folder>` for another theme); without `-Partner` it shoots V2pn for a person who came from no partner, with no tabs and nothing of you. Example themes are in `tools/demo/themes/`.

What a pack cannot change: the layout and the font, the state colours and the state pill, what the connect button does, V2pn's mark beside your logo, the settings and the routing. Your own routes are only a separate mode the person picks (section 6).

## 6. Reviewed files: routes, transport, core config

Three files of the pack change how traffic goes, so V2pn uses them only after a manual review. The owner of V2pn reads the file and adds its grant to `listing.json`; `publish` then puts the file's SHA-256 into `partner-index.json` under `reviewed`. Change the file and V2pn stops using it until the owner reviews it again and publishes the new hash; the subscription window says "<provider> routes changed after the review and are not used". The rest of the pack works as before.

| File | Grant | What it does |
| --- | --- | --- |
| `routing.json` | `routing` | your mode in the "What goes through the VPN" menu |
| `transport.json` | `transport` | fragmentation and noise when connecting to your servers |
| `core.json` | `core` | extra servers, chains, balancing and rules to them, for your servers |

The files sit in the pack folder next to the pictures and are listed in `assets` with their size and SHA-256, like the pictures. The names are exactly these, each file up to 64 KB. All three work only with the servers of your subscription; other subscriptions and the person's own servers never see them.

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

In V2pn it is one more mode of "What goes through the VPN", "<provider> routes", in your tab. V2pn says once that the mode is there but never turns it on by itself: the person picks it. Leaving your tab takes the mode out; if it was on, "Everything through VPN" takes its place. When the partnership ends (the term runs out, the owner takes the partner out) or the file changes after the review, the mode goes away, and if it was selected, V2pn switches to "Everything through VPN".

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

### How a file gets reviewed

1. Send the files to the owner of V2pn with a short description: what they do and why.
2. The owner runs `dotnet run tools/partner.cs -- review partners/<id>`: it says in plain words what each file does (every rule, every setting, every outbound, blocks and everything that touches DNS) and runs V2pn's checks of the format and the filter.
3. If it is fine, the owner adds the file's grant to `listing.json` and publishes. Any change to a file means a new review; the right lasts as long as the partnership and is reviewed again every year.

What the reviewer looks at:

- the routes do not send banks, Gosuslugi and mail to your servers while the person expects them to go direct;
- DNS does not go to your server unencrypted;
- nothing is blocked that would open without the VPN;
- the transport works with your servers and does not touch other connections;
- the core config holds only what you described.

## 7. Headers of the subscription

| Header | |
| --- | --- |
| `v2pn-account-token: <token>` | optional; per subscription, up to 200 characters of `A–Z a–z 0–9 - _ . ~`, sent to your API as a bearer token |
| `profile-title`, `subscription-userinfo`, `profile-update-interval`, `support-url`, `profile-web-page-url`, `announce` | the standard ones; V2pn shows them in plain V2pn and in your look |

Other clients ignore `v2pn-account-token`. The `v2pn-partner` header of earlier versions is no longer read: your subscription's domain is what makes it yours.

When a subscription answers with a whole Xray, sing-box or Clash config instead of server links, V2pn runs it only through a filter. The servers, the routing between them, DNS and balancing stay, and local ports listen on `127.0.0.1` only. These are taken out:

- the core's API, stats and an external control panel;
- logs to files;
- paths to any files on the computer;
- TUN, which V2pn turns on itself;
- Xray reverse, and sing-box tor and tailscale.

V2pn does not run a config for other cores from a subscription. Server links are the simplest thing to send.

### Headers of Happ, INCY and v2RayTun

V2pn also reads the headers that providers already send to Happ, INCY and v2RayTun, so a panel set up for them works in V2pn with no changes. These work for any subscription, partner or not, except moving a subscription. Header names are case-insensitive. Text may be plain or `base64:<UTF-8 in base64>` (use base64 for Cyrillic). Links must be `https:` or `tg:`; any other link is dropped.

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
| `new-url`, `new-domain` | moves the subscription to a new address, only for a V2pn partner whose listing names the new host, and only in HTTP headers of a response with servers. https only; `new-url` wins over `new-domain`, and a bad `new-url` does not fall back to it; `new-domain` keeps the port, path and query; a move to the same host or to an address another subscription has is ignored. For anyone else the headers are ignored: otherwise anyone answering on the old address could take the subscription away. The person's log says where it moved. |
| `ping-type`, `check-url-via-proxy` | read and kept, not used yet: V2pn tests every server through the server itself, to the test address the person set |
| `hide-settings`, `hide-url` | closed settings of the subscription, only for a partner with the `lock` grant (section 4); for anyone else V2pn only keeps them |

**In the body.** As Happ and INCY do, V2pn reads `#key: value` lines at the top of a subscription that is a list of links, plain or base64:

```
#profile-title: base64:S2VzdHJlbCBWUE4=
#sub-info-text: base64:0KHQutC40LTQutCwIDMwJSDQvdCwINCz0L7QtCDQtNC+INC/0Y/RgtC90LjRhtGL
#subscription-autoconnect: 1
vless://…
```

A header wins over the same key in the body. Only from headers: `new-url`, `new-domain`, the INCY banners and `v2pn-account-token`. A whole Xray, sing-box or Clash config has no body metadata.

**Not taken.** `routing`, fragmentation, noise, fronting and other settings that change traffic in headers: they need the owner's review, as reviewed files (section 6). Colour profiles, `change-user-agent` and the rest of the app settings of other clients are ignored.

**V2pn's User-Agent.** V2pn downloads a subscription as `v2rayN/7.25.6 V2pn/1.0.3`: v2rayN's agent first, because panels pick the format by it and V2pn reads the same formats, then V2pn's own name and version. A subscription that the person gave its own User-Agent sends that one instead.

**A Remnawave response rule.** Rules are checked from the top and the first match wins, so put this one above the rules that match `v2rayN`. `v2pn-account-token` is per person and comes from your panel, not from a rule.

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

## 8. Links

`v2pn://add?url=<subscription link, URL-encoded>&name=<name>` opens V2pn and asks before adding the subscription. V2pn also takes Happ (`happ://add/<link>`), v2RayTun (`v2raytun://import/<link>`) and Hiddify (`hiddify://import/<link>`) links. A link to a subscription V2pn already has only says so. On Windows the `v2pn` scheme is registered by the installed app; on macOS the app bundle claims it. A link adds your subscription as an ordinary one; it does not make a tab.

## 9. The API (optional)

With the `account` grant and an `api` address on a domain of the listing, V2pn asks:

`GET <api>account` with `Authorization: Bearer <v2pn-account-token>`, when your tab opens and every 30 minutes while it is on screen:

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

`GET <api>servers`, as often as `account`: when your tab opens or a server of your subscription is chosen, at most every 30 minutes. The same fields as the pack's `servers`, plus `load` (0–100) and `healthy`. The API's values win over the pack's, field by field:

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

### Sign-in by code

With `"login": true` in the pack (and so an `api`), a person adds your subscription without copying a link: «I have a subscription from a provider» in the first-start walkthrough, or «Sign in with a provider» in the add menu («+»). Everyone has both; V2pn asks for the list of providers only when one is opened. In a copy with your marker the walkthrough puts your sign-in first, with your mark, and goes straight to you. A successful sign-in records that the person came from you and opens your tab. V2pn shows your mark and name, the QR code of your bot link, six digits and «Open the bot»; the person confirms in your bot, and V2pn adds the subscription, updates it and selects its first server (it does not connect). The person's own subscription link is all V2pn gets; it is added as a pasted link is.

`POST <api>login/start`, when the sign-in opens and at «New code», with a JSON body:

```json
{ "device": "Windows · DESKTOP-7Q2", "app": "V2pn 1.2.0" }
```

`device` is the operating system and the computer's name (at most 60 characters), for your bot to say what is asking. Answer:

```json
{ "code": "482913", "token": "<opaque, 32 to 256 url-safe characters>", "url": "https://t.me/kestrel_bot?start=v2pn-482913", "expires": 600 }
```

- `code`: exactly six digits, unique among your codes that are still waiting.
- `token`: what V2pn asks with; it stays on that computer, never shown. `A–Z a–z 0–9 - _ . ~`.
- `url`: optional; the QR code and «Open the bot». Only https on `t.me` or on your listed domains; anything else, and V2pn shows only the digits.
- `expires`: seconds the code lives; 600 is usual (V2pn keeps it between 60 and 1800, and counts down from the moment it got the answer).

`GET <api>login/poll?token=<token>`, every 2 seconds while the sign-in is open, until the code expires or the person closes it:

```json
{ "state": "waiting" }
{ "state": "done", "subscription": "https://sub.kestrel.example/s/<the person's link>", "name": "Kestrel" }
{ "state": "expired" }
```

`404` means the same as `expired`; so does the person refusing in the bot. V2pn takes `subscription` only when it is https on your listed domains, without a user name or password; anything else is an error V2pn shows as it is. `name` is optional (up to 40 characters). A token is good for one `done`: forget it once that answer has gone out, and V2pn never asks again after it. A state V2pn does not know counts as `waiting`.

In your bot: `/start v2pn-<code>` (from the QR code) or the six digits typed. Say what is asking («Sign in to V2pn on "Windows · DESKTOP-7Q2"? Confirm only if this code is on your screen now.») with a confirm and a refuse button; on confirm, hand over the person's own stable link (never a shared one). Limit `login/start` per address and `login/poll` per token, bind a code to the first account that asks about it, and limit how many codes one account may try, so codes cannot be guessed. Configra's bot is a worked example.

## 10. How changes reach people

- **A change of the look.** The owner changes `partners/<id>` and publishes. A running V2pn of a person who came from you asks for `partner-index.json` at most every six hours, and at every start; when your zip's hash changed, it downloads the new zip once. A zip whose files did not change keeps its hash, so publishing again makes nobody download it.
- **A new install.** A copy with your marker downloads your pack at its first start, before the person signs in or adds a subscription; after a sign-in by code, V2pn downloads it right away.
- **The term.** The partnership lasts until `until` in `listing.json`. V2pn checks it at start and every hour; when it has passed, your tab closes with a note and the V2pn tab opens. `tools/partner-check.cs` warns a month ahead; the owner extends it and publishes.
- **Leaving.** When the owner takes a partner out of `partners/` and publishes, V2pn closes its tab at the next look at the index, with a note, and no new packages of it are made. A marker stays; if the partner is listed again, its tab comes back.
- **The person leaves.** A person who signed in by code and deletes their last subscription from your domains loses your tab with it. A marker is not undone by deleting subscriptions.
- **V2pn 1.2.1 and older.** These dressed V2pn in a partner's look while its server was selected. Every release now also publishes `partners.json`, signed and empty, which is what they read: they drop the partner they kept and stay plain. Newer V2pn reads `partner-index.json` instead.
- **When something fails.** An index that is not signed with V2pn's key, or is older than the one V2pn has, is ignored; a zip that does not match is not used. V2pn keeps what it had and says why in its log.

## 11. Checking

```
dotnet run tools/partner-check.cs -- partners/<id>
dotnet run tools/partner-check.cs -- https://example.com/s/<a real subscription token>
```

The first checks a partner folder before it is published, offline: `listing.json`, the pack packed as `publish` packs it and read back with V2pn's own reader, the pictures and their slots, Accent or Scene and the colours V2pn derives, the old fields it ignores, the description of servers and the buttons (what V2pn leaves out and why), closed settings, the reviewed files (format, filter, grant).

The second checks a live subscription against the published index, as V2pn sees it in that partner's tab: the subscription's headers, the signature of `partner-index.json`, which partner the link belongs to, the zip and every check above, and the API including `servers`, `recommend` and `card` for your buttons. With `login`, it asks `login/start` once and checks the answer's shape (six digits, the token, the link, the term); it never polls, so no sign-in is completed. `--index partners/out` takes a local publish instead of the release page; `--key <public key>` checks one signed with a test root key.

An example pack with servers, buttons and the three reviewed files, and a local server with a subscription, a releases page and the API for previews, are in `tools/demo/partner/`, `tools/demo/demo_partner.py` and `tools/demo/try-partner.ps1`.

## 12. A download page

```
python tools/partner-page.py partners/<id>
```

Builds a page from the partner's folder that leads its people to V2pn with the partner's tab, into `partners/out/site/<id>/` (`index.html`, `page.js`, the font and the pictures). It carries the partner's logo and colours (the Scene's background as its header), "Download for Windows" and "Download for macOS" for the partner's package in the newest release (`V2pn-<id>-windows-x64.zip`, `V2pn-<id>-macos-arm64.zip` and the ARM and Intel ones: V2pn with your marker; macOS gets a zip, no .dmg), the strong button for the visitor's system, the one-command installers (`$env:V2PN_PARTNER='<id>'; irm …/install.ps1 | iex` on Windows, `curl -fsSL …/install.sh | V2PN_PARTNER=<id> bash` on macOS: they install V2pn where it always goes and add the marker; over an installed V2pn they only add the marker, and the servers and settings stay), three steps (sign-in by code with `login`) and "Open the bot" when `links.support` is on t.me or `--bot` is given. Russian and English (`?lang=en`), light and dark; a phone gets "Send the link to yourself" instead of a download. The colours follow V2pn's rules, and the build stops if any text would read below 4.5:1. The folder can go onto your site as it is: no external scripts or fonts, and the script is a file of its own, so a strict CSP (`script-src 'self'`) serves it. Configra serves it at `/v2pn/download/`.

## Privacy

V2pn takes `partner-index.json` and your pack from V2pn's own release page, not from your site, and only for a person who came from a partner (or who opens the sign-in by code). Your site gets only what any subscription gets: the subscription request and, with an API, its calls with V2pn's user agent and, for `account`, `servers`, `recommend` and `card`, the token you issued. V2pn never sends the person's traffic, their other subscriptions, the device or anything it measures. The one exception is the sign-in by code, when the person opens it: `login/start` gets the operating system and the computer's name, so your bot can name what is asking.
