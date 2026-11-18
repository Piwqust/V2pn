<#
.SYNOPSIS
  Copies a V2pn release to your own server, so installed copies can update from it when GitHub
  does not answer.

.DESCRIPTION
  A mirror is a folder of static files behind any web server (nginx, Caddy, object storage with
  a public URL). It has the layout of a GitHub releases page, which is what V2pn asks for:

    <root>/download/v<version>/<every file of the release>
    <root>/latest/download/release.json
    <root>/latest/download/release.json.sig
    <root>/latest/download/partner-index.json(.sig), partner-<id>.zip (what V2pn reads for a partner's tab)
    <root>/latest/download/partners.json(.sig) (empty, for V2pn 1.2.1 and older)

  The mirror does not have to be trusted: V2pn installs a package only when it matches
  release.json and release.json carries the release key's signature. A mirror that serves
  something else is skipped.

  The script uploads the packages first and switches "latest" last, so a V2pn that asks in the
  middle never sees a manifest whose packages are not there yet.

  To make V2pn use the mirror, add its address to V2pnUpdateSources in
  v2rayN/Directory.Build.targets (after the GitHub one, separated by "|") and release a version:
  copies from that version on know the mirror. docs/V2PN-RELEASES.md has the nginx example.

.EXAMPLE
  # From dist/release/1.0.1, over SSH (needs ssh and scp, which Windows 10+ has):
  tools/publish-mirror.ps1 -Version 1.0.1 -Target deploy@example.com:/var/www/v2pn

  # Take the files from the published GitHub release instead of a local build:
  tools/publish-mirror.ps1 -Version 1.0.1 -Target deploy@example.com:/var/www/v2pn -FromGitHub

  # Only lay the files out in a local folder (to upload some other way):
  tools/publish-mirror.ps1 -Version 1.0.1 -LocalRoot D:\mirror
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    # user@host:/path/of/the/mirror/root
    [string]$Target,
    # Lay the mirror out here instead of (or before) uploading.
    [string]$LocalRoot,
    # Folder with the release files; dist/release/<version> by default.
    [string]$From,
    # Download the files of the published GitHub release with gh instead of using -From.
    [switch]$FromGitHub
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not $Target -and -not $LocalRoot) { throw 'Say where to: -Target user@host:/path or -LocalRoot <folder>.' }
if (-not $From) { $From = Join-Path $repo "dist/release/$Version" }

if ($FromGitHub) {
    $From = Join-Path ([IO.Path]::GetTempPath()) "v2pn-mirror-$Version"
    if (Test-Path $From) { Remove-Item -Recurse -Force $From }
    New-Item -ItemType Directory -Force $From | Out-Null
    Invoke-Checked 'gh' @('release', 'download', "v$Version", '--repo', 'Piwqust/V2pn', '--dir', $From)
}

$files = @(Get-ChildItem $From -File -ErrorAction SilentlyContinue)
foreach ($required in 'release.json', 'release.json.sig') {
    if ($files.Name -notcontains $required) { throw "$From has no $required. Build the release first (tools/release.ps1) or pass -FromGitHub." }
}
$manifest = Get-Content (Join-Path $From 'release.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) { throw "release.json in $From is for $($manifest.version), not $Version." }
foreach ($package in $manifest.files) {
    $file = Join-Path $From $package.name
    if (-not (Test-Path $file)) { throw "$($package.name) is listed in release.json but missing in $From." }
    if ((Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant() -ne $package.sha256) { throw "$($package.name) does not match release.json." }
}
Write-Host "V2pn ${Version}: $($files.Count) files, all packages match the manifest."

if ($LocalRoot) {
    $versionDir = Join-Path $LocalRoot "download/v$Version"
    $latestDir = Join-Path $LocalRoot 'latest/download'
    New-Item -ItemType Directory -Force $versionDir, $latestDir | Out-Null
    Copy-Item -Force $files.FullName $versionDir
    Copy-Item -Force (Join-Path $From 'release.json'), (Join-Path $From 'release.json.sig') $latestDir
    # The partners are read from "latest" too.
    $partnerFiles = @($files | Where-Object { $_.Name -match '^(partners\.json(\.sig)?|partner-index\.json(\.sig)?|partner-[a-z0-9-]+\.zip)$' })
    if ($partnerFiles) { Copy-Item -Force $partnerFiles.FullName $latestDir }
    Write-Host "Laid out in $LocalRoot"
}

if ($Target) {
    if ($Target -notmatch '^(?<login>[^:]+):(?<path>/.+)$') { throw '-Target must look like user@host:/absolute/path' }
    $login = $Matches.login
    $root = $Matches.path.TrimEnd('/')
    Invoke-Checked 'ssh' @($login, "mkdir -p '$root/download/v$Version' '$root/latest/download'")
    Write-Host "Uploading to ${login}:$root/download/v$Version ..."
    Invoke-Checked 'scp' (@('-q') + $files.FullName + @("${login}:$root/download/v$Version/"))
    # "latest" changes only now, with the packages already in place. Both files are copied next to
    # their final names first and then renamed, so the moment in which a V2pn can read a manifest
    # and a signature that do not belong together is two renames long; it would refuse that pair
    # and ask again later.
    Invoke-Checked 'ssh' @($login, "cp '$root/download/v$Version/release.json.sig' '$root/latest/download/release.json.sig.new' && cp '$root/download/v$Version/release.json' '$root/latest/download/release.json.new' && mv '$root/latest/download/release.json.sig.new' '$root/latest/download/release.json.sig' && mv '$root/latest/download/release.json.new' '$root/latest/download/release.json'")
    # The packs first, then the index that names them, renamed into place as release.json is: a V2pn never reads
    # an index whose zips are not there yet, or an index with another index's signature for more than a moment.
    $zips = @($files.Name | Where-Object { $_ -match '^partner-[a-z0-9-]+\.zip$' } | ForEach-Object { "'$root/download/v$Version/$_'" })
    if ($zips) { Invoke-Checked 'ssh' @($login, "cp $($zips -join ' ') '$root/latest/download/'") }
    foreach ($index in 'partner-index.json', 'partners.json') {
        if ($files.Name -notcontains $index) { continue }
        Invoke-Checked 'ssh' @($login, "cp '$root/download/v$Version/$index.sig' '$root/latest/download/$index.sig.new' && cp '$root/download/v$Version/$index' '$root/latest/download/$index.new' && mv '$root/latest/download/$index.sig.new' '$root/latest/download/$index.sig' && mv '$root/latest/download/$index.new' '$root/latest/download/$index'")
    }
    Write-Host "Done. The mirror now serves V2pn $Version."
}
