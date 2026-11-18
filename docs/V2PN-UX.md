# V2pn: how the interface works

The core and all of v2rayN's logic stay as they were: updates, subscriptions, cores, rules. V2pn changes how a person sees these features and controls them.

## The main screen: three decisions

1. **The server.** Picked in the list on the left or on the map.
2. **The connection mode.** The "Proxy | VPN" switch under the Connect button.
3. **What goes through the VPN.** The button next to the switch; it opens the routing presets.

The mode and the route can be chosen in advance, without connecting. Connect applies them.

### Proxy or VPN

| | Proxy | VPN (TUN) |
| --- | --- | --- |
| What goes through the server | Browsers and programs that use the system proxy | All traffic of the computer, including games and programs without proxy support |
| Administrator rights | Not needed | Needed (on Windows) |
| What v2rayN does inside | `SysProxyType`: system proxy or PAC | `EnableTun`; the system proxy is off |

What Proxy mode turns on is chosen in Settings → Proxy mode → "On connect": the system proxy, a PAC script, or "Don't change system settings". On the first start the choice is taken from the mode v2rayN had.

If VPN is chosen without administrator rights, V2pn asks first and only then restarts as administrator. VPN mode turns on right after the restart. v2rayN restarted silently in this case and lost the choice.

### What goes through the VPN

Three presets for Russia. They are ordinary v2rayN rule sets with fixed ids (`v2pn-ru-direct`, `v2pn-blocked-only`, `v2pn-all`), so the routing editor and the tray menu see them too.

- **Russian sites direct** (recommended). `.ru`, `.su`, `.рф`, `category-ru`, banks, government services, `ru-available-only-inside` and `geoip:ru` go direct. Everything else goes through the server, including blocked sites in `.ru` (`geosite:ru-blocked` is checked first).
- **Only blocked sites.** `ru-blocked`, YouTube, Discord, Telegram, AI services and Discord voice (UDP 50000–65535) go through the server. Everything else goes direct.
- **Everything through VPN.** Only the local network goes direct.

The "Block ads and trackers" switch adds `geosite:category-ads-all → block` to all three presets.

The Russian presets use tags from the runetfreedom geo files. If the sources are different, picking a preset makes V2pn turn on v2rayN's regional preset "Russia" (geo files, rule templates and DNS), download the files, and only then switch the route. An unknown tag would stop the core.

The presets use the `IPOnDemand` strategy. sing-box used to ignore a rule set's strategy, so `geoip:ru` rules did not match domains. This is fixed in `SingboxRoutingService` and covered by a test.

Your own rule sets are chosen in the same menu, under "Your rules". The full editor opens with "Routing settings…".

## Window size

The main window follows its width (`MainWindow.Adaptive.cs`), from 360 px up:

| Layout | When | What changes |
| --- | --- | --- |
| Full | the map is at least 650 px wide (a 1000 px window with the usual sidebar) | nothing: list, map, tiles, status card |
| No tiles | the map is 470–650 px wide | the tiles hide (their actions are in the ☰ menu); the status card below 600 px drops the speed graph and the Check label |
| One column | the map would be under 470 px (about 800 px of window) | the state, the server, Connect, the mode and the route on top, the route under the mode; the server list below. No map, status card or Appearance button |

- The thresholds count the map's width, so a narrow sidebar keeps the full layout a little longer.
- A one-column window can be as short as its top part; with under about 150 px left for the list, the list hides. The first-run card scrolls instead of being cut off.
- Two-column layouts keep the old minimum height, 560 px, so the status card never runs into the header.
- The arrows button next to the window controls jumps to a 400×640 mini window and back to the last full size; each size is saved (`MainWindow.Mini`, `MainWindow.Full`), and V2pn reopens at whatever size it was closed at.
- Updates, backup and the QR code are in-window dialogs wider than the mini window: the full size opens for them and the mini one comes back after.

## The server list

- **Home:**
  - "Fastest server" by the last test. A result older than 30 minutes (`UiItem.V2pnLastPing`) is retested before connecting: the 12 quickest, the favorites and the recent servers, at most 24; if none answers, all servers. With statistics on, a server with four or more tests and under half answered is skipped;
  - **Favorites:** the first five, "Show all favorites" for the rest;
  - **Recent:** the last three servers you connected through, except those already in favorites;
  - **Fastest now:** the five quickest distinct connections;
  - **Statistics:** the three servers with the most time, or an offer to turn statistics on.
- **Statistics** (`UiItem.V2pnStats`, off by default; tables `ServerUsageItem` and `UsageDayItem` in guiNDB.db, `ServerUsageManager`). Time counts in 15-second ticks while the core runs, the state is not "Unprotected" and the last check did not fail; a gap over two minutes (sleep) is not counted and starts a new session. Every ping result and every connection check counts as a test; a check that failed because the core was down does not. Saved every two minutes and on exit. The "All statistics" view: 14 days of connected time, the most used servers, the ones that answer most often (three tests at least), countries by time; clear and turn off at the bottom.
- **List order:** the header of Countries sorts countries by name (default), ping or use (`V2pnSort`); a subscription's servers and search results by ping (default), name or use (`V2pnServerSort`). By ping means answered servers by delay, then untested, then those that did not answer. By use needs statistics.
- **Countries:** groups with the best ping, expanding to their servers.
- **Subscriptions.** The subscription menu (⋮ or a right click): update, test ping, delete servers that do not answer (after a confirmation), edit, delete. The row shows the plan's traffic and end date when the provider sends `subscription-userinfo`.

## First start

### The walkthrough

A fresh install opens a two-step walkthrough over the window (`MainWindow.Onboarding.cs`). Skip stays available. Back and the main button stay in the footer while a long step scrolls.

1. **Welcome.** The language: English or Русский. Picking the other one restarts V2pn in it, back on this step.
2. **Your servers.** Paste a link, add a subscription, scan a QR code on Windows, or move servers from another app. Partner builds can offer their provider's sign-in. The footer counts the imported servers. As soon as the list is ready, the same step shows the selected server, mode and route, with Connect in the footer.

Choose **Change mode and route** to review the routing presets and pick Proxy or VPN before connecting. V2pn requests VPN permissions when you connect.

The step is saved (`V2pnOnboardingStep`), so a restart it causes comes back to it; `V2pnOnboarded` marks it done or skipped. While it is open, the window under it is disabled, so Tab and screen readers cannot reach Connect behind it. People who already had servers are marked done without seeing it; so is a move from v2rayN, which brings its own routing and settings. A skipped walkthrough leaves the first-run card below.

### The first-run card


With no servers, the map shows an "Add your first server" card instead of the header, the Connect button and the statistics card. It has three actions: "Paste from clipboard" (a subscription and `vless://` links both work), "Add subscription" and "Scan QR code". The list on the left says "No servers yet" and offers to add a subscription. If other VPN apps with servers are on the computer, the card offers to move from them first (see "Moving from other apps" below).

Lower in the same card you choose what goes through the VPN: "Russian sites direct" or "Everything through VPN". On a clean install v2rayN turns on its own set "All except China", so V2pn switches it once to "Everything through VPN": the same in practice, and no files are needed. The Russian mode downloads the runetfreedom site lists only when picked; the progress shows right in the card.

A new subscription updates itself every 12 hours (720 minutes). v2rayN's default is 0, that is, never.

A new install starts in English on every system; the walkthrough offers Russian on its first step, and the other languages are in Appearance.

## Moving from other apps

V2pn looks for other clients at start (in the background) and in Tools → "Move from another app". When it finds some, the first-run card leads with the move: the title becomes "Move your servers to V2pn", and the apps are rows in the same well as the routing modes, each with a coloured tile, its name and what it holds. With several apps each row has a tick (all on) and the button counts them ("Move from 3 apps"); with one, the button names it. More than four apps show three rows and "N more". The ways to add a server by hand move under a divider ("or add a server by hand") as one row of three short buttons. Happ, which V2pn cannot read, is a row without a tick that says to copy its link.

| Client | What V2pn reads |
| --- | --- |
| v2rayN | alone: the whole guiConfigs folder, with routing and settings (a restart); with other apps: its subscriptions and hand-added servers |
| Clash Verge (Rev), Clash Nyanpasu, Clash Party | `profiles.yaml` / `profile.yaml`: remote profiles are subscriptions, local ones are read for their `proxies` |
| Clash for Windows | `~/.config/clash/profiles/list.yml` |
| FlClash, FlClashX | `database.sqlite` or `shared_preferences.json`, and `profiles/<id>.yaml` |
| NekoRay, NekoBox, Throne up to 1.0 | `config/groups/*.json` (a group with a url is a subscription) and `config/profiles/*.json` |
| Throne 1.1+ | `config/throne.db` |
| Hiddify | `db.sqlite` (`profile_entries`) and `configs/<id>.json` |
| v2RayTun | `shared_preferences.json` (`flutter.sub_*`, `flutter.config_*`) |
| Karing | `karing_subscribe.json` (remote subscriptions only) |
| GUI.for.Clash, GUI.for.SingBox | `data/subscribes.yaml` |
| Happ | nothing: it encrypts its data; the menu says to copy the link in Happ |

- Subscriptions are added with the name the person gave them (names the other app made up, like "Remote File", are left for the panel to replace) and download at once, in view in the Subscriptions tab.
- Servers added by hand become share links and go through the same importer as a pasted link (`V2pnForeignLinks`). A server V2pn already has (same kind, address, port and secret) is skipped; so is a subscription with the same link.
- Other apps' files are only read; SQLite databases are copied to guiTemps first, so a running app is never touched.
- Portable copies are found next to running processes and a few levels into Downloads, Desktop, Documents, Programs and the user folder. "Choose the app's folder…" covers the rest.
- A Clash subscription becomes separate servers instead of one mihomo profile; proxies a link can't hold fall back to the whole-config profile.

Code: `ServiceLib/V2pn/V2pnForeignImport.cs` (finding and adding), `V2pnForeignReaders.cs` (one reader per client), `V2pnForeignLinks.cs` (Clash, sing-box and NekoRay servers to links), `v2rayN.Desktop/Views/MainWindow.Import.cs` (card and menu).

## Connection state

- Until a server is picked, V2pn says "Unprotected", whatever proxy mode is saved: the core is not running, and "Proxy on port N" would not be true.
- The main window, mini window and tray use the result for the current connection. "Proxy connected" applies to apps that follow the system proxy. "Local proxy on port N" applies to apps pointed at `127.0.0.1:N`. The route is shown separately.
- V2pn verifies the selected server with the running Xray or sing-box configuration through an authenticated loopback listener. The check follows the server outbound and leaves application routing rules intact. Full custom configurations show that verification is unavailable.
- Bundled `generate_204` endpoints must return HTTP 204. Custom HTTP check URLs can return any successful 2xx response. HTTP errors, redirects and timeouts have separate results and can be retried.
- A timeout or an unreachable check address also offers a search in the selected country. If none responds, other countries in the subscription can be checked. The app asks before switching and checks the new connection afterwards.
- The speed and session traffic come from the cores in kilobytes per one-second sample; the card turns them into bytes.

## Processor use

The world map is several thousand vector outlines. Everything animated on top of it used to repaint it too: during a connection check two small spinners took two thirds of a core (143 Hz monitor). Now the country layer is cached as a bitmap (`BitmapCache` in `MapView`), and the spinners step 24 times per turn (`StepEasing` in `Motion.cs`) instead of every frame: 7.6 % of a core during a connection check instead of 67 %.

## Menus and words

- Three ways lead into settings, all to one window: Settings in the ☰ menu, the Settings tile on the map, and <kbd>Ctrl</kbd>+<kbd>,</kbd> (<kbd>⌘</kbd>+<kbd>,</kbd> on a Mac). Routing rules, DNS, hotkeys and the config template live in windows of their own; they open from "More settings" at the bottom of the navigation, over the Settings window, which stays open. Backup, moving from v2rayN, the data folder, restarting as administrator, the Microsoft Store exemptions, the regional presets and clearing statistics are in the ☰ menu → "Tools".
- "Help" holds the version, "V2pn website", "Report a problem" and "Built on v2rayN". The websites of a dozen cores that v2rayN listed mean nothing to most people.
- Subscriptions in the menu: "Update all subscriptions", "Update through the VPN" (if the provider's site is blocked), "All subscriptions…". The "Current subscription" submenu is hidden: every subscription in the list on the left has its own buttons.
- Questions name the action: "Delete", "Quit", "Restart" instead of "Confirm"; a delete button is red. Settings windows save with "Save", a new subscription with "Add".
- v2rayN's generic "Operation successful" shows as "Done".

## What could come next

- Registering `v2rayn://install-sub`, `clash://install-config`, `hiddify://import` and `happ://add` links, with a confirmation of the source.
- A domain check: show which way a site goes under the current route, like Diversion Rule Detect in Karing.
- Split tunnelling by program in VPN mode (`process` rules), with a program list in the style of Mullvad.
