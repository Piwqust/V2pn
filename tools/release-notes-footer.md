---

## Download

<p>
  <a href="https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-windows-x64.zip"><img src="https://raw.githubusercontent.com/Piwqust/V2pn/v{version}/docs/images/badges/download-windows.svg" width="260" alt="Download for Windows"></a>
  &nbsp;
  <a href="https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-macos-arm64.dmg"><img src="https://raw.githubusercontent.com/Piwqust/V2pn/v{version}/docs/images/badges/download-macos.svg" width="260" alt="Download for macOS"></a>
</p>

| System | File |
| --- | --- |
| Windows 10 and 11, x64 | [V2pn-windows-x64.zip](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-windows-x64.zip) |
| Windows 11 on ARM | [V2pn-windows-arm64.zip](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-windows-arm64.zip) |
| macOS 13.6 or later, Apple silicon (M1 and later) | [V2pn-macos-arm64.dmg](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-macos-arm64.dmg) |
| macOS 13.6 or later, Intel | [V2pn-macos-x64.dmg](https://github.com/Piwqust/V2pn/releases/download/v{version}/V2pn-macos-x64.dmg) |

**Or in one command.** Windows (Terminal): `irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex`. Mac (Terminal): `curl -fsSL https://github.com/Piwqust/V2pn/releases/latest/download/install.sh | bash`. Both install without the first-launch warning and move your servers from v2rayN.

**Already using V2pn?** Nothing to download: it updates itself.

On the first launch of a downloaded file, Windows and macOS warn that they do not know the developer, because the app has no paid signature. How to get past it is in the [README](https://github.com/Piwqust/V2pn#install).

<details>
<summary>The other files in the list below</summary>

They are for the app, not for people. The updater reads `-update.zip`, the macOS `.zip`, `release.json` and `release.json.sig`; `-update.zip` has no cores, so it does not work as a first install. `.sha256` files are checksums: `sha256sum -c V2pn-windows-x64.zip.sha256`. `install.ps1` and `install.sh` are the one-command installers above. `V2pn-<provider>-….zip` are providers' own builds of V2pn, linked from each provider's download page; `partner-index.json`, `partners.json` and `partner-<provider>.zip` are what those builds read.

</details>

<sub>По-русски: [README.ru.md](https://github.com/Piwqust/V2pn/blob/v2pn/README.ru.md). Интерфейс переключается на русский в «Appearance».</sub>
