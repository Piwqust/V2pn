# V2pn for providers: partner looks

[По-русски](README.ru.md)

A VPN provider can dress V2pn in its own look for the people who use its subscription: its name and mark, its colour, and one of five looks V2pn draws. The connection, the servers, the settings and the state of protection stay V2pn's. People switch between plain V2pn and the provider's look at any time.

This page is for the provider's developers. It describes what to serve, how it is signed and checked, and what V2pn shows.

## What a provider gets

| | Any panel | Partner (certificate from V2pn) |
| --- | --- | --- |
| Subscription name, traffic, term, announcement, support and cabinet buttons (standard headers) | yes | yes |
| Banners, the renewal reminder, connecting at start, server order, updating and testing at start (Happ, INCY and v2RayTun headers) | yes | yes |
| Moving the subscription to another of your domains (`new-url`, `new-domain`) | no | yes |
| `v2pn://add` links from the provider's site or bot | yes | yes, and V2pn says the provider is a confirmed partner |
| The provider's mark on its subscription in V2pn's list | no | yes |
| The provider's look of V2pn (five looks, the provider's colour) | no | yes, with the `edition` grant |
| Plan, server health by the provider's own checks, devices, incidents in the look | no | yes, with the `account` grant and an API |

## How it works

1. The provider's subscription answers with a `v2pn-partner` header: the address of a folder with its signed pack.
2. After every subscription update V2pn downloads `cert.json` and `partner.json` from that folder and checks:
   - `cert.json` is signed with V2pn's partner root key, in its term, not withdrawn;
   - `partner.json` is signed with the key the certificate names;
   - the subscription and the folder are on domains the certificate names;
   - every picture matches the size and SHA-256 in `partner.json`.
3. When everything checks out, V2pn keeps the pack and offers the look once: "Open <provider>'s look" or "Stay in plain V2pn".
4. The person switches looks from the name in the title bar, from the tray menu, with Ctrl+1…9 (⌘ on a Mac) or on the Appearance page, where the look is also tuned (light or dark, accent).

A pack that fails any check is not used; V2pn stays plain and the subscription window says the provider's look did not pass the check. The servers work as usual either way.

## 1. Keys and the certificate

Make a key pair (ECDSA P-256) and keep the private half on your side:

```
dotnet run tools/partner.cs -- new-key provider-key.pem
```

or with OpenSSL:

```
openssl ecparam -name prime256v1 -genkey -noout -out provider-key.pem
openssl ec -in provider-key.pem -pubout -outform DER | base64
```

Send the owner of V2pn:

- a partner id: lowercase letters, digits and hyphens, 2–32 characters (`configra`);
- the name people see, up to 40 characters, without "V2pn" in it;
- the domains your subscription and pack are served from (`example.com`, `*.example.com`);
- the public key (base64 of the DER SubjectPublicKeyInfo, as printed above);
- the grants you need: `edition` (the look) and `account` (the API).

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
  lamp.webp
```

`partner.json` ([JSON schema](partner.schema.json)):

```json
{
  "schema": 1,
  "id": "configra",
  "name": "Configra",
  "brand": {
    "accent": "#FF7A2F",
    "logo": "logo-light.png",
    "logoOnLight": "logo-dark.png",
    "mark": "mark.png"
  },
  "edition": {
    "variants": ["cabinet", "signature", "warm", "studio", "ascii"],
    "default": "cabinet",
    "art": { "lamp": "lamp.webp" }
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
| `edition.variants` | which of the five looks you allow, in the order the switcher lists them |
| `edition.default` | the look that opens first |
| `edition.art.lamp` | the picture of the studio look: 200–1600 px a side, at most 2:1; a cut-out on a transparent ground stands on the page, a picture with its own ground is framed like a photo |
| `links.cabinet`, `links.support` | buttons in the look, when the subscription headers have none |
| `api` | your API's base address (with the `account` grant, on a certified domain) |
| `assets` | every picture with its size and SHA-256; `tools/partner.cs pack` fills it |

A picture that does not fit its slot is not shown there (V2pn shows your name, or nothing) and `tools/partner-check.cs` says which and why; the rest of the pack keeps working.

What a pack does not set, on purpose: fonts, corners, spacing and every colour but the accent. Each look has its own type and shape, and V2pn derives the rest of the colours for light and dark, checking that text reads (WCAG AA, 4.5:1). Your accent is used as it is for buttons and marks; where it is text on a light ground, V2pn darkens it just enough to read. An accent close to the "protected" green or the "attention" amber is turned away from them. These limits are what keeps every look working at every window size, in light and dark, for every provider.

Limits: `partner.json` up to 64 KB, `cert.json` up to 16 KB, at most 8 pictures, PNG, WebP or JPEG only, 1 MB each, file names without folders.

Sign `partner.json` after every change:

```
dotnet run tools/partner.cs -- pack <pack folder> --key provider-key.pem
```

or with your own tooling: ECDSA P-256 over SHA-256 of the exact bytes of `partner.json`, the DER signature in base64 in `partner.json.sig`. Serve the files byte for byte (no re-encoding of line ends); V2pn checks the bytes it receives.

## 3. Headers of the subscription

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
| `hide-settings`, `hide-url` | read and kept; they will apply only to partners with the `lock` grant |

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

## 4. Links

`v2pn://add?url=<subscription link, URL-encoded>&name=<name>` opens V2pn and asks before adding the subscription. V2pn also takes Happ (`happ://add/<link>`), v2RayTun (`v2raytun://import/<link>`) and Hiddify (`hiddify://import/<link>`) links. A link to a subscription V2pn already has only says so. On Windows the `v2pn` scheme is registered by the installed app; on macOS the app bundle claims it.

## 5. The API (optional)

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

Every field is optional; V2pn reads what it knows and ignores the rest. Answers are JSON up to 64 KB with status 200; anything else is treated as no answer. V2pn does not follow redirects with the token. When your site is blocked where the person is, V2pn asks again through the VPN. `servers.healthy`/`total` replace V2pn's own ping count in the look ("11 of 14 by <provider>'s check"). Send counts, never server addresses or credentials.

## 6. The looks

| Look | What it is |
| --- | --- |
| `signature` | plain V2pn with your logo beside V2pn's in the title bar |
| `warm` | V2pn's layout in your colours and the Geist type |
| `cabinet` | a personal-cabinet layout: a light card with the place, the state and the server squares; servers and routing below |
| `studio` | a website-like layout: the place in bitmap letters, a capsule with Connect, your picture lit while protected |
| `ascii` | text all the way: a typed map, values in a monospaced table, buttons in brackets |

V2pn draws all five, at every window width, in light and dark. The person can tune each one on the Appearance page; their tuning never changes plain V2pn.

What a pack cannot change: the state colours and the state pill (green protected, red unprotected), the Connect button, V2pn's mark and the "on V2pn" note, the switcher back to plain V2pn, the settings and the routing.

## 7. Life of a partnership

- **Updates.** Change and re-sign the pack whenever you like; V2pn picks it up at the next subscription update.
- **The term.** A certificate lasts a year. V2pn checks it at start and every hour; when it runs out the look closes with a note, and plain V2pn opens. Ask for a new one a month ahead; `tools/partner-check.cs` warns.
- **Leaving.** Stop sending `v2pn-partner` and the look goes away at the next update. A person who deletes your subscription loses your look with it.
- **Withdrawal.** The owner of V2pn can withdraw a certificate before its term; installed apps learn it from the release channel and close the look.

## 8. Checking

```
dotnet run tools/partner-check.cs -- https://example.com/s/<a real subscription token>
dotnet run tools/partner-check.cs -- --folder ./v2pn --subscription https://example.com/s/<token>
```

It runs V2pn's own checks with the keys built into the app and prints OK or what is wrong at each step: headers, certificate, signatures, domains, pictures, the API.

To see the look before publishing, ask V2pn's owner for a preview certificate for `127.0.0.1`, serve the pack and a test subscription from your computer over http, and turn on developer mode in V2pn (About → updates). V2pn then takes a pack served from `127.0.0.1`.

## Privacy

V2pn sends your servers nothing it would not send any subscription: the pack and the API are asked like the subscription itself, with V2pn's user agent and, for `account`, the token you issued. V2pn never sends the person's traffic, their other subscriptions, the device or anything it measures.
