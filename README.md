<p align="center">
  <a href="https://v2pn.angleford.co/"><img src="docs/images/readme/hero.png" width="100%" alt="V2pn. Always two ways. A simple VPN client for your provider's servers on Windows and macOS: the V2pn window connected through Frankfurt in front of the ribbed-glass fork"></a>
</p>

<p align="center">
  <a href="https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-windows-x64-setup.exe"><img src="docs/images/badges/download-windows.svg" width="250" alt="Download for Windows"></a>
  &nbsp;
  <a href="https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-macos-arm64.dmg"><img src="docs/images/badges/download-macos.svg" width="250" alt="Download for macOS"></a>
</p>

<p align="center">
  <sub>
    Windows 10 and 11 · x64 or <a href="https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-windows-arm64-setup.exe">ARM64</a>
    &nbsp;&nbsp;·&nbsp;&nbsp;
    macOS 13.6 or later · Apple silicon or <a href="https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-macos-x64.dmg">Intel</a>
  </sub>
</p>

<p align="center">
  <a href="https://github.com/Piwqust/V2pn/releases/latest"><img src="https://img.shields.io/github/v/release/Piwqust/V2pn?style=flat-square&label=release&labelColor=19191E&color=FF3D1F" alt="Latest release"></a>
  <a href="https://github.com/Piwqust/V2pn/releases"><img src="https://img.shields.io/github/downloads/Piwqust/V2pn/total?style=flat-square&label=downloads&labelColor=19191E&color=FF3D1F" alt="Downloads on GitHub"></a>
  <a href="https://github.com/Piwqust/V2pn/actions/workflows/v2pn-ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Piwqust/V2pn/v2pn-ci.yml?branch=v2pn&style=flat-square&label=build&labelColor=19191E" alt="Build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Piwqust/V2pn?style=flat-square&label=license&labelColor=19191E&color=3A3A44" alt="License: GPL-3.0"></a>
</p>

<p align="center">
  <a href="#install"><b>Install</b></a>
  &nbsp;·&nbsp; <a href="#what-it-does">What it does</a>
  &nbsp;·&nbsp; <a href="#two-ways-out">Two ways out</a>
  &nbsp;·&nbsp; <a href="#moving-from-v2rayn">From v2rayN</a>
  &nbsp;·&nbsp; <a href="CHANGELOG.md">What's new</a>
  &nbsp;·&nbsp; <a href="https://v2pn.angleford.co/">Site</a>
  &nbsp;·&nbsp; <a href="README.ru.md">Русский</a>
</p>

<br>

**V2pn** is a VPN client for the servers your provider gives you: VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC and WireGuard. Inside, it runs [v2rayN](https://github.com/2dust/v2rayN) with the Xray, sing-box and mihomo cores, so every server and subscription that works in v2rayN works here. On top, it is one window: pick a country, choose Proxy or VPN, decide what goes through the VPN, press **Connect**.

> [!NOTE]
> V2pn does not sell or provide servers. You need a subscription link from a VPN provider, or a server of your own.

## What it does

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/readme/countries.png" alt="The Countries tab with pings, and the map zoomed to Germany with the pin in Frankfurt, 39 ms">
      <h3>Every country, one click</h3>
      Servers are grouped by country, with live ping. The map zooms to the country and pins the city. Favorites, recent servers and the fastest one sit on the Home tab; numbered series like “AUTO 1/34” fold into one row.
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/readme/status.png" alt="Local proxy on port 20808, Germany, Frankfurt, Disconnect, and the status card with Works · 39 ms">
      <h3>A status you can trust</h3>
      The status turns green only after a real connection check. Ping, speed and session traffic are live. When a server stops answering, V2pn offers another one from the same country.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/readme/kill-switch.png" alt="The status going from VPN connected to Internet blocked and back, beside the Kill switch settings">
      <h3>Kill switch <sup><kbd>1.4</kbd></sup></h3>
      When the connection drops, the internet stays closed until V2pn reconnects or you press Disconnect. On Windows, programs you list, such as Claude, reach the internet only through V2pn and are cut off while it is off. Off by default.
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/readme/bypass.png" alt="The Blocking bypass window: Nothing is blocked, and the network checks, each marked works">
      <h3>Blocking bypass <sup><kbd>1.4</kbd></sup></h3>
      Says in one sentence what your network blocks: the whole internet, a whitelist, the server's address, downloads that freeze after the first ~16 KB. Then tries ways to split the TLS handshake and keeps the one that works, for each network. Off by default.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/readme/subscriptions.png" alt="A subscription with 44 GB of 200 GB used, valid until 13 December 2026">
      <h3>Subscriptions that keep your choices</h3>
      Traffic left and the plan's end date are in the list. Updating a subscription keeps your favorites, recent servers and pings.
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/readme/appearance.png" alt="The Appearance panel with seven palettes and accent colours">
      <h3>Make it yours</h3>
      Light or dark, seven palettes, any accent colour, sharp or round corners. Changes show up at once.
    </td>
  </tr>
</table>

## Two ways out

<p align="center">
  <img src="docs/images/readme/two-ways.svg" width="100%" alt="The V2pn mark as a road seen from above: you at the bottom; Russian sites such as gosuslugi.ru, sberbank.ru and ya.ru leave by the left road directly; youtube.com, chatgpt.com, discord.com and everything else leave by the right road, behind ribbed glass, through your server">
</p>

The mark is a road seen from above: one way in, two ways out. Two choices under the **Connect** button decide which way your traffic takes.

**What goes through the VPN.** Change it at any time, connected or not.

| Preset | Direct | Through your server |
| --- | --- | --- |
| **Russian sites direct** · recommended | Banks, Gosuslugi and `.ru` sites | Everything else, blocked `.ru` sites too |
| **Only blocked sites** | Everything else | Blocked sites, YouTube, Discord, Telegram and AI services |
| **Everything through VPN** | — | All traffic; some banks and Gosuslugi may refuse a foreign IP |

An ad and tracker blocker is one switch in the same menu and works with all three. Your own rule sets are there too; no rule editor is needed for the presets.

**Proxy or VPN.** Proxy turns on the system proxy: browsers and programs that use it go through the server. VPN routes all traffic of the computer, games included; it needs administrator rights on Windows and your password on macOS.

## Install

### Windows

Open Terminal (<kbd>Win</kbd>+<kbd>X</kbd> → Terminal) and run:

```powershell
irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex
```

The script downloads the newest version, checks it against the release's list of files, installs it to `%LOCALAPPDATA%\Programs\V2pn`, adds Start menu and desktop shortcuts and starts V2pn. No administrator rights, no SmartScreen window. If v2rayN is on the computer, V2pn takes its servers and settings right away.

<details>
<summary><b>Install by hand, or a portable copy</b></summary>
<br>

1. Download [V2pn-windows-x64-setup.exe](https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-windows-x64-setup.exe) ([ARM64](https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-windows-arm64-setup.exe) for Windows on ARM).
2. Run it. Windows may show “Windows protected your PC”, because the app has no paid developer signature: press **More info**, then **Run anyway**.
3. The installer puts V2pn into `%LOCALAPPDATA%\Programs\V2pn` for your Windows user, with no administrator rights, adds it to the Start menu (and to the desktop, if you leave that ticked) and offers to take your servers from v2rayN.

**Portable:** [V2pn-windows-x64.zip](https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-windows-x64.zip) ([ARM64](https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-windows-arm64.zip)) is the same app. Unpack it into a folder you can write to (not `Program Files`: settings and servers are kept next to the program) and run `V2pn.exe`.

.NET does not need to be installed; it is inside.

</details>

### macOS

Open Terminal (Applications → Utilities → Terminal) and run:

```bash
curl -fsSL https://github.com/Piwqust/V2pn/releases/latest/download/install.sh | bash
```

The script picks the build for your Mac, checks it against the release's list of files, puts V2pn into Applications and opens it, without the “unverified developer” warning. If v2rayN is on the Mac, V2pn takes its servers and settings right away.

<details>
<summary><b>Install by hand</b></summary>
<br>

1. Download the build for your Mac: [Apple silicon](https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-macos-arm64.dmg) (M1 and later) or [Intel](https://github.com/Piwqust/V2pn/releases/latest/download/V2pn-macos-x64.dmg). Not sure which? Apple menu  → About This Mac: “Chip Apple M…” is Apple silicon, “Processor Intel” is Intel.
2. Open the `.dmg` and drag V2pn to Applications.
3. Start V2pn. macOS says it cannot verify the developer: the app is not signed with a paid Apple certificate. Open System Settings → Privacy & Security, scroll down and press **Open Anyway** next to V2pn. This is needed once.

If there is no **Open Anyway**, run `xattr -cr /Applications/V2pn.app` in Terminal and start V2pn again.

</details>

<details>
<summary><b>On a Mac</b></summary>
<br>

<p align="center"><img src="docs/images/macos.png" width="640" alt="V2pn on macOS: the first-run window"></p>

- V2pn has an icon in the menu bar and can keep running there with the window closed: Settings → General → “Hide to tray when closing the window”. <kbd>⌘</kbd>+<kbd>Q</kbd> quits.
- VPN mode asks for your Mac password: it creates a network interface, which macOS allows only to an administrator.
- Settings and servers are in `~/Library/Application Support/V2pn`.
- The macOS build is new: an automated check builds it and starts it on a Mac, but few people have used it on real machines yet. If something does not work, [open an issue](https://github.com/Piwqust/V2pn/issues/new/choose).

</details>

## First connection

1. Copy the subscription link from your VPN provider, or a server link such as `vless://…`.
2. In V2pn press **Paste from clipboard** (<kbd>Ctrl</kbd>+<kbd>V</kbd>, on a Mac <kbd>⌘</kbd>+<kbd>V</kbd>).
3. Pick a server in the list or on the map and press **Connect**.

At the first start V2pn asks for the language (English or Russian) and walks you through these steps. If your provider's bot or site has a **Connect in V2pn** button, press it instead: V2pn adds the subscription itself.

> [!TIP]
> **For providers.** A provider who works with V2pn gets a tab of their own in the title bar, with their mark, colours, plan and only their servers. One form, no keys, no hosting, no separate build: [v2pn.angleford.co/providers](https://v2pn.angleford.co/providers/) · [the guide](docs/partners/README.md).

**From the terminal and AI assistants.** `v2pnctl` comes with V2pn, and the Windows installer, winget and Homebrew put it on the PATH: `v2pnctl status`, `v2pnctl servers`, `v2pnctl use Frankfurt`, `v2pnctl connect`, `v2pnctl route all`, `v2pnctl --help` for the rest. `v2pnctl mcp` is an MCP server for Claude Code, Claude Desktop, Cursor and the like: `claude mcp add v2pn -- v2pnctl mcp`. It is off until you turn it on in Settings → General → “Control from the command line and AI”, as read only or with control; V2pn shows every change it makes and never gives out links, addresses or keys.

## Moving from v2rayN

V2pn keeps its data in the same files as v2rayN, so moving takes one button. When a v2rayN with servers is on the computer, V2pn offers **Move from v2rayN** at its first start: servers, subscriptions, routing and settings. Later it is in the ☰ menu → **Tools** → **Move from v2rayN…**. The one-command install moves them by itself.

- v2rayN is closed and no longer starts with the system. Its own files stay as they are, so you can go back to it.
- What V2pn had before the move is kept in `guiTemps\V2pnImportBackup-<date>`.
- Cores V2pn lacks are taken from v2rayN's folder.
- On Windows V2pn finds v2rayN by its sign-in entry, a running copy, the WinGet and Scoop folders, `%LOCALAPPDATA%\v2rayN`, and in Downloads, Desktop, Documents and Program Files.
- A v2rayN running as administrator cannot be closed by V2pn; it then asks you to quit v2rayN from its tray icon.

### How V2pn differs from v2rayN

- **The status tells the truth.** “Connected” appears after a successful connection check, not after the button is pressed. If the core dies, V2pn restarts it; after five failures in a row it disconnects and says why.
- **The internet stays after exit.** The system proxy is set only while the core runs and is cleared on exit, shutdown and a crash. A proxy set by another program is left alone.
- **A busy port is named.** If another program holds the port, V2pn does not start on top of it and says so.
- **Settings in plain words.** The everyday options fit on one page. Routing rules, DNS, cores and custom configs are still there, under “More settings” and “Advanced settings”.
- **It updates itself,** with signed packages and a rollback (see below).

## Updates

Every six hours V2pn checks for a new version, downloads it in the background and installs it at the next start or when you press **Restart and update**. A running connection is never cut by an update. Automatic installation can be turned off in the Updates window.

- Every release is signed with a key whose public half is built into the app. V2pn will not install a package without that signature, a changed one or an incomplete one, wherever it was downloaded from.
- If the new version does not start, the installer puts the previous one back.
- Settings, servers and cores are not touched by an update.
- Cores (Xray, sing-box, mihomo) and site lists are updated separately in the same window.

What exactly is checked before installing: [docs/V2PN-RELEASES.md](docs/V2PN-RELEASES.md#what-exactly-is-checked).

<details>
<summary><b>Keyboard</b></summary>
<br>

| | Windows | macOS |
| --- | --- | --- |
| Paste a server or subscription | <kbd>Ctrl</kbd>+<kbd>V</kbd> | <kbd>⌘</kbd>+<kbd>V</kbd> |
| Search servers | <kbd>Ctrl</kbd>+<kbd>F</kbd> | <kbd>⌘</kbd>+<kbd>F</kbd> |
| Home, Countries, Subscriptions | <kbd>Ctrl</kbd>+<kbd>1</kbd> … <kbd>3</kbd> | <kbd>⌘</kbd>+<kbd>1</kbd> … <kbd>3</kbd> |
| Add the selected server to favorites | <kbd>Ctrl</kbd>+<kbd>B</kbd> | <kbd>⌘</kbd>+<kbd>B</kbd> |
| Test ping of all servers | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd> | <kbd>⌘</kbd>+<kbd>Shift</kbd>+<kbd>R</kbd> |
| Scan a QR code on the screen | <kbd>Ctrl</kbd>+<kbd>S</kbd> | |
| Settings | <kbd>Ctrl</kbd>+<kbd>,</kbd> | <kbd>⌘</kbd>+<kbd>,</kbd> |
| Restart the connection | <kbd>F5</kbd> | <kbd>F5</kbd> |

</details>

<details>
<summary><b>Uninstall</b></summary>
<br>

- **Windows, installed with the setup:** Settings → Apps → Installed apps → V2pn. It asks whether to keep your servers and settings for a later install.
- **Windows, installed with the command or unpacked from the zip:** quit V2pn from its tray icon, then delete `%LOCALAPPDATA%\Programs\V2pn` (or the folder you unpacked it to) and its shortcuts. Settings and servers are inside that folder.
- **macOS:** quit V2pn, move it from Applications to the Bin, and delete `~/Library/Application Support/V2pn`.

</details>

## Building from source

You need the [.NET 10 SDK](https://dot.net).

```bash
git clone --recursive https://github.com/Piwqust/V2pn.git
dotnet build V2pn/v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release
dotnet test --project V2pn/v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Release
```

The sources live in the `v2rayN/` folder: the name is kept from the original so that its updates merge without conflicts. For the same reason the root holds v2rayN's `package-*.sh` scripts; V2pn does not use them.

A build from source shows its version with `-dev` and does not update itself. Release packages are made by `tools/release.ps1` (Windows) and `tools/package-macos.sh` (macOS); the screenshots come from a sandbox with the demo subscription in `tools/demo`.

| Developer docs | |
| --- | --- |
| [docs/V2PN-UX.md](docs/V2PN-UX.md) | How the interface is built and why |
| [docs/V2PN-RELEASES.md](docs/V2PN-RELEASES.md) | Releases, updates, the mirror, merging v2rayN |
| [docs/partners/README.md](docs/partners/README.md) | Provider tabs: the form, the headers V2pn reads, the partner file |
| [CHANGELOG.md](CHANGELOG.md) | What changed in each version, with pictures |

## License and credits

[GPL-3.0](LICENSE), the same as v2rayN. V2pn is a fork of [2dust/v2rayN](https://github.com/2dust/v2rayN): connecting, protocols, subscriptions and core handling are the work of its authors. Cores: [Xray-core](https://github.com/XTLS/Xray-core), [sing-box](https://github.com/SagerNet/sing-box), [mihomo](https://github.com/MetaCubeX/mihomo). Icons: [Lucide](https://lucide.dev); the Apple logo on the download button is from [Simple Icons](https://simpleicons.org).

Found a security problem? See [SECURITY.md](SECURITY.md). Something does not work? [Open an issue](https://github.com/Piwqust/V2pn/issues/new/choose).

<br>

<p align="center">
  <a href="https://v2pn.angleford.co/">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="docs/images/readme/lockup-dark.svg">
      <img src="docs/images/readme/lockup-light.svg" height="28" alt="V2pn">
    </picture>
  </a>
  <br>
  <sub>Always two ways.</sub>
</p>
