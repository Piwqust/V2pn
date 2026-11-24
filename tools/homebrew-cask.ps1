<#
.SYNOPSIS
  Writes the Homebrew cask of a published V2pn release (brew install --cask piwqust/tap/v2pn).

.DESCRIPTION
  Reads the release's release.json from GitHub, or from a local release folder with -From, and
  writes dist/homebrew/Casks/v2pn.rb: the zips for Apple silicon and Intel with their SHA-256.
  The file belongs in the tap repository Piwqust/homebrew-tap under Casks/; people then install
  with

    brew install --cask piwqust/tap/v2pn

  The cask removes the quarantine flag after installing: V2pn has only an ad-hoc signature (no
  paid Apple Developer ID), and macOS refuses to open a quarantined app without one. "auto_updates"
  tells Homebrew that V2pn replaces itself, so "brew upgrade" leaves it alone. "binary" links
  v2pnctl (V2pn.app/Contents/MacOS/v2pnctl) into Homebrew's bin folder, so it is on the PATH; the
  link follows the app when V2pn updates itself, and goes with "brew uninstall".

  Later versions: .github/workflows/v2pn-homebrew.yml, when the HOMEBREW_TAP_TOKEN secret is set.

.EXAMPLE
  tools/homebrew-cask.ps1 -Version 1.0.1
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$From,
    [string]$OutDir,
    [string]$Repository = 'Piwqust/V2pn'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'dist/homebrew/Casks' }
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

function Hash([string]$arch) {
    $file = $manifest.files | Where-Object { $_.name -eq "V2pn-macos-$arch.zip" } | Select-Object -First 1
    if (-not $file) { throw "Release $Version has no V2pn-macos-$arch.zip." }
    return $file.sha256
}

$cask = @"
# Created by tools/homebrew-cask.ps1 in $Repository.
cask "v2pn" do
  arch arm: "arm64", intel: "x64"

  version "$Version"
  sha256 arm:   "$(Hash 'arm64')",
         intel: "$(Hash 'x64')"

  url "$releases/download/v#{version}/V2pn-macos-#{arch}.zip"
  name "V2pn"
  desc "VPN client built on v2rayN, with the Xray, sing-box and mihomo cores"
  homepage "https://github.com/$Repository"

  livecheck do
    url :url
    strategy :github_latest
  end

  auto_updates true
  depends_on macos: ">= :ventura"

  app "V2pn.app"
  # V2pn's command line and MCP server, on the PATH as Homebrew's link to the app's own copy.
  binary "#{appdir}/V2pn.app/Contents/MacOS/v2pnctl"

  # V2pn has only an ad-hoc signature (no Apple Developer ID): a quarantined copy would not open.
  postflight do
    system_command "/usr/bin/xattr",
                   args: ["-dr", "com.apple.quarantine", "#{appdir}/V2pn.app"]
  end

  zap trash: "~/Library/Application Support/V2pn"
end
"@

New-Item -ItemType Directory -Force $OutDir | Out-Null
$path = Join-Path $OutDir 'v2pn.rb'
[IO.File]::WriteAllText($path, ($cask -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))
Write-Host "Homebrew cask for V2pn $Version`: $path"
