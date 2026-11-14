<#
.SYNOPSIS
  Writes the winget manifest of a published V2pn release (winget install Piwqust.V2pn).

.DESCRIPTION
  Reads the release's release.json (package names, sizes, SHA-256) from GitHub, or from a local
  release folder with -From, and writes the three files winget-pkgs wants into
  dist/winget/<version>/:

    Piwqust.V2pn.yaml                    version
    Piwqust.V2pn.installer.yaml          the zips for x64 and arm64, their hashes
    Piwqust.V2pn.locale.en-US.yaml       name, description, links (plus ru-RU)

  The package is a zip with a portable app inside. ArchiveBinariesDependOnPath keeps winget from
  starting V2pn through a link in another folder: V2pn needs the DLLs next to its exe. In a WinGet
  folder V2pn keeps its data in %LOCALAPPDATA%\V2pn and leaves updates to winget (V2pnWinGet.cs).

  Then check and submit (the first version as a pull request to microsoft/winget-pkgs):

    winget validate --manifest dist/winget/<version>
    wingetcreate submit --token <GitHub token> dist/winget/<version>

  Later versions: .github/workflows/v2pn-winget.yml, when the WINGET_TOKEN secret is set.

.EXAMPLE
  tools/winget-manifest.ps1 -Version 1.0.1
  tools/winget-manifest.ps1 -Version 1.0.1 -From dist/release/1.0.1
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    # A local release folder with release.json; by default the published release on GitHub.
    [string]$From,
    [string]$OutDir,
    [string]$Repository = 'Piwqust/V2pn'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo "dist/winget/$Version" }
$id = 'Piwqust.V2pn'
$schema = '1.10.0'
$releases = "https://github.com/$Repository/releases"

if ($From) {
    $manifest = Get-Content (Join-Path $From 'release.json') -Raw | ConvertFrom-Json
}
else {
    $content = (Invoke-WebRequest -UseBasicParsing -Uri "$releases/download/v$Version/release.json").Content
    if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
    $manifest = $content | ConvertFrom-Json
}
if ($manifest.version -ne $Version) { throw "release.json is for $($manifest.version), not $Version." }

$installers = foreach ($arch in 'x64', 'arm64') {
    $name = "V2pn-windows-$arch.zip"
    $file = $manifest.files | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $file) { throw "Release $Version has no $name." }
    @"
- Architecture: $arch
  InstallerUrl: $releases/download/v$Version/$name
  InstallerSha256: $($file.sha256.ToUpperInvariant())
"@
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$header = '# Created by tools/winget-manifest.ps1'
$files = [ordered]@{
    "$id.yaml" = @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@
    "$id.installer.yaml" = @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: V2pn\V2pn.exe
  PortableCommandAlias: v2pn
ArchiveBinariesDependOnPath: true
MinimumOSVersion: 10.0.17763.0
ReleaseDate: $((Get-Date $manifest.published).ToString('yyyy-MM-dd'))
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: $schema
"@
    "$id.locale.en-US.yaml" = @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Piwqust
PublisherUrl: https://github.com/Piwqust
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: V2pn
PackageUrl: https://github.com/$Repository
License: GPL-3.0
LicenseUrl: https://github.com/$Repository/blob/v2pn/LICENSE
Copyright: Copyright (c) 2026 V2pn contributors. Based on v2rayN, (c) 2017-2026 2dust.
ShortDescription: A simple VPN client built on v2rayN, with the Xray, sing-box and mihomo cores.
Description: |-
  V2pn is a VPN client with a plain interface. It runs v2rayN inside, so the same servers and subscriptions work: VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC, WireGuard.
  It moves servers, subscriptions and settings over from a v2rayN on the same computer.
Moniker: v2pn
Tags:
- proxy
- sing-box
- v2ray
- v2rayn
- vless
- vpn
- xray
ReleaseNotesUrl: $releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@
    "$id.locale.ru-RU.yaml" = @"
$header
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: ru-RU
Publisher: Piwqust
PackageName: V2pn
License: GPL-3.0
ShortDescription: Простой VPN-клиент на основе v2rayN с ядрами Xray, sing-box и mihomo.
Description: |-
  V2pn — VPN-клиент с простым интерфейсом. Внутри работает v2rayN, поэтому подходят те же серверы и подписки: VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC, WireGuard.
  Переносит серверы, подписки и настройки из v2rayN на этом же компьютере.
ReleaseNotesUrl: $releases/tag/v$Version
ManifestType: locale
ManifestVersion: $schema
"@
}
foreach ($name in $files.Keys) {
    # winget-pkgs wants UTF-8 without a BOM and LF line ends.
    [IO.File]::WriteAllText((Join-Path $OutDir $name), ($files[$name] -replace "`r`n", "`n") + "`n", (New-Object Text.UTF8Encoding $false))
}
Write-Host "winget manifest for V2pn $Version`: $OutDir"
