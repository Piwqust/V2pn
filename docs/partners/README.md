# V2pn for providers

[По-русски](README.ru.md)

V2pn is a free VPN client for Windows and macOS. Your subscription link works in it as it is, with no application. If you want your people to have a tab of your own, with your logo and colours, apply on [v2pn.angleford.co](https://v2pn.angleford.co/providers/). You put the look together in a cabinet on the site, and V2pn's server signs and publishes it. You need no keys, no hosting and no separate build of V2pn.

Someone who came from you sees two tabs in the title bar, as in a browser: **V2pn** and yours, with your mark and name. Your tab has your colour, a card with the plan, your buttons and only your servers. The V2pn tab stays plain V2pn: all of the person's servers, yours included, and nothing from a provider. The connection, the settings and the protection state always belong to V2pn.

## Do nothing

Tier 0, "Subscription". No application: the person pastes your link, and V2pn reads it and the headers you already send for other clients. There is no tab; it is an ordinary subscription.

### Subscription headers

| Header | What V2pn does |
| --- | --- |
| `profile-title` | the subscription's name; send Cyrillic as `base64:<UTF-8 in base64>` |
| `subscription-userinfo` | traffic and term |
| `profile-update-interval` | how often to update the subscription |
| `support-url`, `profile-web-page-url` | the "Support" and "Renew" links |
| `announce`, `announce-url` | an announcement and a "More" button under it |

Header names are case-insensitive. V2pn takes only `https:` and `tg:` links.

### Headers of other apps

The Happ, INCY and v2RayTun headers work too, so a panel set up for them needs no changes.

| Header | What V2pn does |
| --- | --- |
| `sub-info-text`, `sub-info-color`, `sub-info-button-text`, `sub-info-button-link` (Happ) | a banner on the subscription: text up to 200 characters, button up to 25. `red`, `blue`, `green` pick a colour of V2pn's palette, anything else is blue. `sub-info-text: 0` removes the banner |
| `banner-text`, `banner-button-text`, `banner-button-url`, `banner-url`, `premium-url` (INCY) | the same banner when there is no Happ banner |
| `sub-expire`, `sub-expire-button-link` (Happ); `expiry-banner-text`, `expiry-banner-button-text`, `expiry-banner-button-url`, `expiry-banner-url` (INCY) | a renewal reminder: 3 days before the term ends and after. `sub-expire: 0` turns it off |
| `subscription-autoconnect: 1`, `subscription-autoconnect-type: lastused`, `lowestdelay` or `random` | connecting when V2pn starts; the person can turn it off |
| `subscriptions-sort-type` (Happ), `sort-order` (INCY) | the order of servers: by ping, by name or as you send them |
| `subscription-ping-onopen-enabled: 1` | testing the subscription's servers at start |
| `update-always: true` (v2RayTun), `subscription-auto-update-open-enable: 1` (Happ) | updating this subscription at start |
| `new-url`, `new-domain` | moving the subscription to another of your domains from the application; only for people who came from you, and only in HTTP headers |
| `hide-settings`, `hide-url` | locked settings; only at tier 2 with hidden addresses |
| `ping-type`, `check-url-via-proxy` | kept, not applied yet |

V2pn also reads `#key: value` lines at the start of a subscription that lists links, as Happ and INCY do. A header wins over the same line. `new-url`, `new-domain`, the INCY banners and `v2pn-account-token` work only as headers.

What V2pn never takes from headers: routes, fragmentation, noise and other settings that change the way traffic goes, and colour profiles and settings of other apps.

**User-Agent.** V2pn downloads a subscription with `v2rayN/7.25.6 V2pn/1.3.0`: panels pick the format by v2rayN's agent, and V2pn reads the same formats. If the person set their own User-Agent for the subscription, that one goes.

**A whole config.** If a subscription sends a whole Xray, sing-box or Clash config, V2pn runs it after a filter. Servers, routing between them, DNS and balancing stay. The core's API, log files, file paths, TUN and ports not on `127.0.0.1` go. Server links are simpler.

**A Remnawave response rule.** Put it above the rules for `v2rayN`: rules are checked top to bottom.

```json
{
  "name": "V2pn",
  "enabled": true,
  "operator": "AND",
  "conditions": [
    { "headerName": "user-agent", "operator": "CONTAINS", "value": "v2pn", "caseSensitive": false }
  ],
  "responseType": "XRAY_BASE64",
  "responseModifications": {
    "applyHeadersToEnd": true,
    "headers": [
      { "key": "sub-info-color", "value": "green" },
      { "key": "sub-expire", "value": "1" },
      { "key": "subscription-autoconnect", "value": "1" },
      { "key": "subscription-autoconnect-type", "value": "lowestdelay" },
      { "key": "subscriptions-sort-type", "value": "ping" }
    ]
  }
}
```

**Add links.** `v2pn://add?url=<URL-encoded link>&name=<name>` opens V2pn, which asks whether to add the subscription. V2pn also understands Happ (`happ://add/…`), v2RayTun (`v2raytun://import/…`) and Hiddify (`hiddify://import/…`) links. Such a link adds an ordinary subscription, with no tab.

## Apply

1. Sign in on [v2pn.angleford.co/providers/login/](https://v2pn.angleford.co/providers/login/) by email: you get a six-digit code, there is no password. The code lasts 10 minutes. While the site has no mail set up, the page says so and the owner of V2pn passes the code to you.
2. Fill in the [application](https://v2pn.angleford.co/providers/apply/); the fields are in the table below. The draft saves itself.
3. On submit, V2pn's server checks the test link the way V2pn will open it: the address must be public, the answer must fit in 8 seconds and 2 MB. The server reads the headers, counts the servers by protocol and opens a connection to five of them. You see the result line by line. A red line keeps the application a draft: fix it and submit again.
4. The owner of V2pn approves the application with a tier and a term (a year by default), or returns it with the reason.
5. Once approved, the [cabinet](https://v2pn.angleford.co/providers/cabinet/) opens.

| Field | |
| --- | --- |
| Name | what people see: up to 40 characters, no "V2pn" |
| id | for addresses: 2–32 of `a–z`, `0–9` and `-` |
| Domains | 1 to 20, where your subscription links live: `example.com` is that address only, `*.example.com` every address under it. One domain, one provider |
| Test link | a working subscription, https. Only you and the owner see it; it never goes into logs |
| Contact, tier, note | how to reach you; tier 1 or 2 |

## Look

In the cabinet's "Look" section, a live V2pn window with your tab stands on the right, in light and dark.

- **Name**, up to 40 characters.
- **Colours**: one to three, `#RRGGBB`. The first is the brand colour: Connect, links, the subscription card.
- **Background**: none, a gradient of the brand colours, or a picture. With a background your tab shows the Scene: your background in place of the world map, with the plan large on it. Without one it is Accent: V2pn's layout in your colour. One colour is enough for a gradient; V2pn adds a second, lighter one.
- **Pictures**: PNG only, up to 1 MB each, sizes in the table below.
- **Links** "Cabinet" and "Support" (https), when your subscription headers do not send them.
- **At tier 2** also the API address, sign-in by code, hidden server addresses and up to four buttons (see "Tiers and grants").

| Picture | Size |
| --- | --- |
| logo for a dark ground | 2:1 to 8:1, 32–256 px tall, transparent; 340×96 will do |
| logo for a light ground | the same |
| mark for the tab and the card | square, 64–512 px, transparent; 128×128 will do |
| background picture | 640–3200 px a side, at most 3:1 |

V2pn works out every other colour itself: text reads at 4.5:1 or better, buttons and fields at 3:1. A colour that could pass for the green "Protected", the amber "Attention" or the red "Not protected" is turned away from it, and the cabinet says what becomes what. If the text of your page `/p/<id>/` would not read, the look cannot be sent.

The draft saves itself. "Send for publication" queues the look: the owner looks at it and publishes it or returns it with the reason. On publication V2pn's server rebuilds and signs the list of providers and your pack and updates your page. V2pn on your people's computers sees the new look at its next start or within six hours.

## Share

Your page is `https://v2pn.angleford.co/p/<id>/`. With no subscription in the address it is a download page: V2pn for Windows and macOS, your colours and logo, the strong button for the visitor's system.

For your bot, site or email, the same page with the person's subscription after `#sub=`:

```
https://v2pn.angleford.co/p/<id>/#sub=<the subscription link, URL-encoded>
```

The page then shows "Open in V2pn". It opens `v2pn://add?url=…&partner=<id>&name=…`: V2pn adds the subscription and, as its domain is in your application, opens your tab. If V2pn is not installed yet, the person downloads it on the same page, installs it and presses the button again. If they already have the subscription, the button just opens your tab.

A `v2pn://` link cannot go into a Telegram button: bot buttons open only `https://` and `tg://`. That is why the button leads to the page.

Telegram Bot API, `sendMessage` with a button:

```json
{
  "chat_id": 123456789,
  "text": "Your subscription is ready. Press the button on your computer.",
  "reply_markup": {
    "inline_keyboard": [[
      { "text": "Connect in V2pn", "url": "https://v2pn.angleford.co/p/kestrel/#sub=https%3A%2F%2Fsub.kestrel.example%2Fs%2FAbC123" }
    ]]
  }
}
```

The address in your bot's code:

```js
const url = 'https://v2pn.angleford.co/p/kestrel/#sub=' + encodeURIComponent(subscriptionUrl);
```

```python
from urllib.parse import quote
url = "https://v2pn.angleford.co/p/kestrel/#sub=" + quote(subscription_url, safe="")
```

**Why after the hash.** A browser never sends the part of an address after `#` to the server, nor puts it in the Referer. V2pn's server sees only a request for `/p/kestrel/`; the page's script reads the subscription in the person's browser. It is the person's own link: whoever they forward it to gets the subscription too, as with a plain link.

The cabinet's "Share" section has your address, the button's code ready to copy and a field to try the button with a subscription of your own.

## Sign-in by code

Tier 2. The person copies nothing. In V2pn they pick "Sign in with a provider" in the + menu, or "I have a subscription from a provider" at first start, see six digits and a QR code, and confirm in your bot. V2pn gets the person's own link, adds the subscription, updates it, selects its first server and opens your tab. The person connects themselves.

You need the API address in the cabinet (https, on a domain of your application) and "Sign-in by code" turned on. Below, `<api>` is that address with a `/` at the end.

### POST `<api>login/start`

V2pn sends it when the person opens the sign-in and on "New code". Headers `Content-Type: application/json; charset=utf-8` and `User-Agent: V2pn/1.3.0`. Body:

```json
{ "device": "Windows · DESKTOP-7Q2", "app": "V2pn 1.3.0" }
```

`device` is the system and the computer's name, up to 60 characters: your bot says by it who asks to sign in.

The answer is `200` and JSON up to 64 KB:

```json
{ "code": "482913", "token": "q7Vd0yXk3m9TzL2bWn8sHf4cRj6pAe1u", "url": "https://t.me/kestrel_bot?start=v2pn-482913", "expires": 600 }
```

| Field | |
| --- | --- |
| `code` | exactly six digits; not repeated among your codes that have not expired |
| `token` | 32–256 of `A–Z a–z 0–9 - _ . ~`; V2pn asks about the sign-in with it. The person never sees it |
| `url` | optional: the link for the QR code and "Open the bot", https on `t.me` or on your domains, up to 512 characters. Without it V2pn shows only the digits |
| `expires` | how many seconds the code lives, an integer; 600 when missing. V2pn keeps it between 60 and 1800 and counts from the answer |

Any other status, not JSON, or a wrong `code` or `token`: V2pn says the provider does not answer and offers to try again.

### GET `<api>login/poll?token=<token>`

Every 2 seconds while the sign-in is open and the code has not expired. The answer is `200` and JSON:

```json
{ "state": "waiting" }
```
```json
{ "state": "done", "subscription": "https://sub.kestrel.example/s/AbC123", "name": "Kestrel" }
```
```json
{ "state": "expired" }
```

- `waiting`: the person has not confirmed yet. A state V2pn does not know counts as `waiting` too.
- `done`: confirmed. `subscription` is https, up to 2048 characters, no spaces, no user name or password in it, on a domain of your application; V2pn takes no other link and shows an error. `name` is optional, up to 40 characters.
- `expired` or status `404`: the code ran out, you do not know it, or the person pressed "Not me" in the bot. V2pn offers a new code.
- JSON without `state`: an error; the sign-in stops.
- No answer, or a status other than `200` and `404`: V2pn keeps waiting; after five such answers in a row (about 10 seconds) the sign-in stops with an error.

A token gets one `done`. After it V2pn asks no more; forget the token.

### Time limits and the network

- For each request V2pn waits 10 seconds for the connection and 25 for the answer.
- V2pn follows no redirects for `login/start` or `login/poll`: the token is in the address and must not travel to another host.
- If your site does not open directly (it is blocked where the person is), V2pn asks again through the VPN when it is connected, and keeps asking that way.

### In the bot

1. From the QR code the person comes with `/start v2pn-<code>`, for example `/start v2pn-482913`. Take six digits typed by hand too.
2. Say who asks to sign in: "Sign in to V2pn on Windows · DESKTOP-7Q2? Confirm only if this code is on your screen right now." Buttons "Sign in" and "Not me".
3. After "Sign in", give `login/poll` this person's own permanent link, not a shared one. After "Not me", `expired`.

### Security

- Tie a code to the first Telegram account that sent it. Refuse a second account with the same code.
- Limit `login/start` per address, `login/poll` per token, and how many codes one account may try, per account. Otherwise six digits can be guessed.
- Codes and tokens are short-lived and work once.
- `device` is written by software on the person's computer. Escape it in the bot's message (Telegram HTML or Markdown) and use it for nothing but the caption.

The cabinet's "Check the API" button calls your `login/start` once with `device: "V2pn Hub · проверка"` and `login/poll` once. It expects `waiting`, not `404` and not `done`. The check's code simply expires. Configra's bot is a working example.

## What V2pn never does

- Sends you or V2pn's server nothing about the person: not their traffic, other subscriptions or V2pn's measurements. Your server gets the subscription request and, at tier 2, calls to your API. The one exception is `login/start`: the system and the computer's name, when the person opened the sign-in themselves.
- Shows your tab to someone who only pasted your link. The V2pn tab has nothing from a provider.
- Takes from headers anything that changes the way traffic goes.
- Gives a provider the protection state, the connect button, the layout or the font.
- Locks the person out of the essentials, even with hidden addresses: connecting and disconnecting, picking any of your servers, deleting the subscription, going to the V2pn tab, adding their own servers and routes, the kill switch, V2pn's updates.
- Connects by itself after a sign-in by code.
- Follows redirects with a token or sends a token to another domain.
- Holds the person: if you leave or your term ends, the tab closes with a note and the servers work as an ordinary subscription.

## Tiers and grants

| | 0 "Subscription" | 1 "Verified" | 2 "Integration" |
| --- | --- | --- | --- |
| Application | none | yes | yes |
| Subscription headers and other apps' headers | yes | yes | yes |
| A tab with your logo, colours and background | no | yes | yes |
| The page `/p/<id>/` and the bot button | no | yes | yes |
| Moving a subscription to another of your domains (`new-url`) | no | yes | yes |
| API: plan, devices, server health | no | no | yes |
| Sign-in by code | no | no | yes |
| Up to four buttons of your own | no | no | yes |
| Hidden server addresses (`hide-settings`, `hide-url`) | no | no | yes |

Tier 2 can be asked for in the cabinet; the owner decides. Your own routes, fragmentation and extra servers do not go through the site: the owner reads such files by hand (the last section).

### API

All at tier 2, with `<api>` on your domain. An answer is `200` and JSON up to 64 KB; V2pn takes anything else as no answer. Every field is optional. The token is the subscription header `v2pn-account-token`: one per person, up to 200 of `A–Z a–z 0–9 - _ . ~`. V2pn sends it as `Authorization: Bearer <token>` and follows no redirects with it. If your site does not open directly, V2pn asks again through the VPN.

`GET <api>account` with the token: when your tab opens and every 30 minutes while it is open.

```json
{ "plan": "Personal", "status": "ACTIVE", "servers": { "healthy": 11, "total": 14 }, "devices": { "count": 2, "limit": 5 }, "until": "2027-01-31", "note": "Planned works on Sunday night." }
```

`GET <api>status` without the token, every 15 minutes:

```json
{ "incidents": [ { "title": "Germany is down", "text": "Fixing it.", "severity": "major" } ] }
```

With buttons V2pn also asks, with the token:

- `GET <api>servers`, at most every 30 minutes: `{ "servers": [ { "name": "🇩🇪 Germany 2", "group": "Main", "country": "DE", "purpose": ["youtube"], "load": 31, "healthy": true } ] }`. A server is found by its exact name or by `host`; `city`, `priority`, `hidden` and `note` are there too.
- `GET <api>recommend?purpose=youtube`, on a "Server for a task" button: `{ "servers": ["🇳🇱 Netherlands", "🇩🇪 Germany 2"] }`, best first.
- `GET <api>card?id=<button id>`, on a "Card with data" button: `{ "title": "Family plan", "text": "5 devices, unlimited traffic." }`, a title up to 60 characters, text up to 600.

Send only numbers and names, no server addresses or credentials.

### Buttons

A button is a label up to 24 characters, one of V2pn's icons and an action from the list. A provider has no logic of its own in the app.

| Action | What V2pn does |
| --- | --- |
| `connect.best` | connects to the best server: `ping` the lowest delay, `stable` the most answers over the last 10 checks, `load` the lowest load from `servers` |
| `connect.recommend` | asks `recommend` with a purpose (`youtube`, `games`, `mobile`, `whitelist`, `streaming`, `ai`, `torrents`) and connects to the first server that answers; without the API's answer, to the fastest server with that purpose |
| `connect.server`, `connect.group` | to a server by name or address; to the fastest in a group |
| `open.cabinet`, `open.renew`, `open.support` | the links from the subscription headers, else from the look |
| `open.link` | https on your domains or `t.me`, or `tg:` |
| `subscription.update` | updates the subscription |
| `check.services` | checks whether YouTube, Telegram and ChatGPT open |
| `info.card` | shows the card from `card` |

Icons: `bolt`, `shield`, `play`, `game`, `card`, `chat`, `globe`, `star`, `refresh`, `check`, `info`, `gift`.

### Hidden addresses

With them V2pn shows no addresses, ports or keys of your servers, lets no one share a server, show its QR code, export a config or change the subscription's link, and leaves those servers out of backups. The person sees that you locked the settings. Turn it on in the look or with the header `hide-settings: 1`; `hide-url: 1` hides the link itself too. The protection is not absolute: the core's config is on disk. The aim is that addresses cannot be forwarded in two clicks.

## Questions

**Do my people need a separate build of V2pn?** No. Everyone installs the same V2pn; the tab comes from the page's button or a sign-in by code.

**Someone already added my link by hand. How do they get the tab?** Let them press the bot's button. V2pn says the subscription is already there and opens your tab.

**When does the tab go?** When the person deletes your last subscription, when the term ends, or when the owner revokes the provider. V2pn checks the term at start and every hour.

**How fast do look changes arrive?** After publication: at V2pn's next start or within six hours. A pack that did not change is not downloaded again.

**What if v2pn.angleford.co does not open?** V2pn works with the last list of providers and your pack it got, and takes the new ones when the site opens again. Servers work either way.

**I used to hand out a V2pn build with my marker. What about it?** Those copies keep your tab: at the first start of the new version V2pn moves the marker into its settings and deletes the file. There are no new builds or install commands for a provider any more; hand out the button.

**Does V2pn's server see my people's links?** No: in the button the subscription is after `#` and never reaches the server. The server sees only the test link from your application.

**How do I change the name or the domains?** The name in the look; the domains through the owner of V2pn.

## The owner's path: the root signature

Only for partners that need their own routes (`routing.json`), transport (`transport.json`) or extra servers (`core.json`). These files change the way traffic goes, so the owner of V2pn reads each one by hand, and V2pn takes them only from a list signed with the root key on the owner's computer, not with the site's key.

- The partner lives in `partners/<id>/` of the V2pn repository: `listing.json` (id, name, domains, grants, term, contact), `partner.json` ([JSON schema](partner.schema.json)), pictures and the reviewed files. The three files' formats are in the schema, under `$defs`.
- `dotnet run tools/partner.cs -- review partners/<id>` says in plain words what each file does; `dotnet run tools/partner-check.cs -- partners/<id>` checks the folder the way V2pn will read it.
- `dotnet run tools/partner.cs -- publish` signs `partner-index.json` with the root key, and it goes out with V2pn's release on GitHub. Its `reviewed` field holds the SHA-256 of each reviewed file: V2pn does not use a changed file until the owner reviews it again.
- V2pn merges this list with the site's. When an id is in both, the site's list wins, so such a partner must not be on the site.
