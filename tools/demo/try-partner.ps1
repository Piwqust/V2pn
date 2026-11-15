<#
.SYNOPSIS
  A V2pn test build with a pretend provider "Kestrel" that uses every provider feature, on this computer.

.DESCRIPTION
  Builds V2pn into a separate folder with a test partner root key (never the real one), signs a Kestrel pack
  for 127.0.0.1 with that key, starts the pretend provider (subscription, pack, API and a local VLESS server
  through Xray, all hidden) and opens the test V2pn on the screen with the Kestrel subscription offered.
  Nothing here touches the V2pn you use: the test copy keeps its own data, other local ports and leaves the
  system proxy alone.

  Kestrel has: its theme (template "split"), buttons (fastest, most stable, for YouTube, renew), server groups
  with purposes and notes, the plan from its API, reviewed routes ("Маршруты Kestrel" in "Что идёт через VPN"),
  reviewed connection settings and core config. -Lock adds closed settings, -HideUrl hides the link as well.

.EXAMPLE
  tools/demo/try-partner.ps1            # build, sign, start everything
  tools/demo/try-partner.ps1 -Lock      # the same with closed settings
  tools/demo/try-partner.ps1 -NoBuild   # restart quickly with the last build
  tools/demo/try-partner.ps1 -Stop      # close the test V2pn and the pretend provider
#>
param(
    [string]$Dir = (Join-Path $env:TEMP 'v2pn-try'),
    [switch]$Lock,
    [switch]$HideUrl,
    [switch]$NoBuild,
    [switch]$Stop
)
# Native tools write notes to stderr (partner.cs warns that the test root is not the real one); each step is
# checked by its result instead.
$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sandbox = Join-Path $repo 'tools\sandbox.ps1'
$partnerTool = Join-Path $repo 'tools\partner.cs'
$demo = Join-Path $repo 'tools\demo'
$dotnet = Join-Path $env:USERPROFILE '.dotnet10\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$app = Join-Path $Dir 'sandbox'
$pidFile = Join-Path $Dir 'provider.pid'

function Stop-Provider {
    if (Test-Path $pidFile) {
        $id = [int](Get-Content $pidFile)
        # The provider script starts Xray as its child; stop both.
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$id" -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        Remove-Item $pidFile -Force
    }
}

if ($Stop) {
    & $sandbox -Dest $app -Stop | Out-Null
    Stop-Provider
    Write-Host 'Test V2pn and the pretend provider are stopped.'
    return
}

New-Item -ItemType Directory -Force $Dir | Out-Null
$keys = Join-Path $Dir 'keys'
New-Item -ItemType Directory -Force $keys | Out-Null

function Get-Key([string]$name) {
    $pem = Join-Path $keys "$name.pem"
    $pub = Join-Path $keys "$name.pub"
    if (-not (Test-Path $pem)) {
        $out = & $dotnet run $partnerTool -- new-key $pem
        if ($LASTEXITCODE -ne 0) { throw "new-key $name failed" }
        ($out | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim() | Set-Content -Encoding ascii $pub
    }
    (Get-Content $pub -Raw).Trim()
}

Write-Host '1/5 test keys'
$rootPub = Get-Key 'root'
$kestrelPub = Get-Key 'kestrel'

if (-not $NoBuild -or -not (Test-Path (Join-Path $Dir 'build\V2pn.exe'))) {
    Write-Host '2/5 building V2pn that trusts the test root (a few minutes the first time)'
    & $dotnet build (Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj') -c Debug -nologo -v q "-p:V2pnPartnerKeys=$rootPub" -o (Join-Path $Dir 'build')
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    # The shared obj folder now holds the test key; build once more so the normal Debug build has the real key.
    & $dotnet build (Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj') -c Debug -nologo -v q | Out-Null
}

Write-Host '3/5 signing the Kestrel pack for 127.0.0.1'
$pack = Join-Path $Dir 'pack'
if (Test-Path $pack) { Remove-Item -Recurse -Force $pack }
New-Item -ItemType Directory -Force $pack | Out-Null
Copy-Item (Join-Path $demo 'partner\*') $pack
$grants = 'edition,account,actions' + $(if ($Lock -or $HideUrl) { ',lock' } else { '' })
& $dotnet run $partnerTool -- cert --partner kestrel --name Kestrel --domains 127.0.0.1 --key $kestrelPub --grants $grants `
    --review (Join-Path $pack 'routing.json') --review (Join-Path $pack 'transport.json') --review (Join-Path $pack 'core.json') `
    --root (Join-Path $keys 'root.pem') --out $pack | Out-Null
if (-not (Test-Path (Join-Path $pack 'cert.json'))) { throw 'cert failed' }
& $dotnet run $partnerTool -- pack $pack --key (Join-Path $keys 'kestrel.pem') | Out-Null
if (-not (Test-Path (Join-Path $pack 'partner.json.sig'))) { throw 'pack signing failed' }

Write-Host '4/5 starting the pretend provider on http://127.0.0.1:18998/ (hidden)'
Stop-Provider
& $sandbox -Dest $app -Stop | Out-Null
$xrayDir = Join-Path $Dir 'xray'
$liveBin = Join-Path $env:LOCALAPPDATA 'Programs\V2pn\bin'
if (-not (Test-Path (Join-Path $xrayDir 'xray.exe'))) {
    New-Item -ItemType Directory -Force $xrayDir | Out-Null
    Copy-Item (Join-Path $liveBin 'xray\*') $xrayDir -Recurse
}
$providerArgs = @('-I', (Join-Path $demo 'demo_partner.py'), $pack, '--xray', (Join-Path $xrayDir 'xray.exe'))
if ($Lock -or $HideUrl) { $providerArgs += '--lock' }
if ($HideUrl) { $providerArgs += '--hide-url' }
$provider = Start-Process python -ArgumentList $providerArgs -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $Dir 'provider.log') -RedirectStandardError (Join-Path $Dir 'provider.err')
$provider.Id | Set-Content $pidFile

Write-Host '5/5 opening the test V2pn'
# The cores come from the installed V2pn, read only; the test copy keeps its own data between runs.
& $sandbox -From (Join-Path $Dir 'build') -Dest $app -CoresFrom (Split-Path $liveBin) -KeepData | Out-Null
& $sandbox -Dest $app -Launch -Visible -AppArgs 'v2pn://add?url=http%3A%2F%2F127.0.0.1%3A18998%2Fsub&name=Kestrel' | Out-Null
Write-Host ''
Write-Host "Test V2pn is open (folder $app). Add the Kestrel subscription it offers."
Write-Host 'Stop everything: tools/demo/try-partner.ps1 -Stop'
