<#
.SYNOPSIS
  Pins the cores (Xray, sing-box, mihomo, geo files) that V2pn packages ship with.

.DESCRIPTION
  V2pn packages carry the same core bundle as v2rayN: 2dust/v2rayN-core-bin. A release must not
  pull whatever that repository's master happens to hold at build time, or two builds of one tag
  differ and nobody can say what went out. tools/cores.lock.json records, per system, the commit
  the bundle is taken from and its SHA-256; tools/release.ps1 and tools/package-macos.sh download
  exactly that and refuse anything else.

  Run this script to move the pin to the newest bundle, then commit cores.lock.json.

.EXAMPLE
  tools/update-cores.ps1
  tools/update-cores.ps1 -Commit bc603e1ee3c6eb90f16fe9123f7df80bc02f8435
#>
param(
    # A commit of 2dust/v2rayN-core-bin; the head of master by default.
    [string]$Commit
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$lockFile = Join-Path $PSScriptRoot 'cores.lock.json'
$repoUrl = 'https://github.com/2dust/v2rayN-core-bin'

if (-not $Commit) {
    $head = git ls-remote "$repoUrl.git" refs/heads/master
    if ($LASTEXITCODE -ne 0 -or -not $head) { throw "Could not read the head of $repoUrl" }
    $Commit = ($head -split '\s+')[0]
}
Write-Host "Pinning cores to $repoUrl commit $Commit"

# V2pn target -> the bundle's name in v2rayN-core-bin.
$bundles = [ordered]@{
    'win-x64'   = 'v2rayN-windows-64.zip'
    'win-arm64' = 'v2rayN-windows-arm64.zip'
    'osx-x64'   = 'v2rayN-macos-64.zip'
    'osx-arm64' = 'v2rayN-macos-arm64.zip'
}

$targets = [ordered]@{}
$temp = Join-Path ([IO.Path]::GetTempPath()) "v2pn-cores-$Commit"
New-Item -ItemType Directory -Force $temp | Out-Null
foreach ($target in $bundles.Keys) {
    $name = $bundles[$target]
    $url = "$repoUrl/raw/$Commit/$name"
    $file = Join-Path $temp $name
    if (-not (Test-Path $file)) {
        Write-Host "  $name ..."
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile "$file.part"
        Move-Item -Force "$file.part" $file
    }
    $targets[$target] = [ordered]@{
        url    = $url
        size   = (Get-Item $file).Length
        sha256 = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    }
}

$lock = [ordered]@{
    source  = $repoUrl
    commit  = $Commit
    targets = $targets
}
[IO.File]::WriteAllText($lockFile, (($lock | ConvertTo-Json -Depth 5) -replace "`r`n", "`n") + "`n")
Write-Host "Wrote $lockFile. The bundles are cached in $temp."
