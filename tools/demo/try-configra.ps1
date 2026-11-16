<#
.SYNOPSIS
  Configra in a test V2pn on this computer: the demo of Configra's site with its real pack, before Configra is deployed.

.DESCRIPTION
  Starts Configra's demo site (branch claude/v2pn-partner, in a worktree under %TEMP%) on http://127.0.0.1:32620/,
  hidden, and opens a test V2pn on the screen with Configra's demo subscription offered through a v2pn://add link.

  The pack is Configra's own (web/public/v2pn), with two changes for this computer: its API and cabinet point at the
  demo, signed again with Configra's key (~/.configra/v2pn-partner-key.pem), and a certificate for 127.0.0.1 and
  localhost for 30 days, issued with the partner root key (~/.v2pn/partner-root-key.pem). V2pn takes a partner over
  plain http from this computer only in a development build, so the test V2pn is a Debug build of this tree.

  The demo's servers are made up (example.com): the looks, the plan, the intro and the switcher work, connecting does not.
  Nothing touches the V2pn you use: the test copy keeps its own data and other ports and leaves the system proxy alone.

.EXAMPLE
  tools/demo/try-configra.ps1            # build, sign, start everything
  tools/demo/try-configra.ps1 -NoBuild   # restart quickly with the last builds
  tools/demo/try-configra.ps1 -Fresh     # as a computer that has never seen Configra: the intro again
  tools/demo/try-configra.ps1 -Stop      # close the test V2pn and the demo
#>
param(
    [string]$Dir = (Join-Path $env:TEMP 'v2pn-configra'),
    [string]$Configra = (Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) 'configra'),
    [switch]$NoBuild,
    [switch]$Fresh,
    # Keeps the test V2pn's windows off the screen (for automated checks).
    [switch]$Hidden,
    [switch]$Stop
)
# Native tools write notes to stderr; each step is checked by its result instead.
$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sandbox = Join-Path $repo 'tools\sandbox.ps1'
$partnerTool = Join-Path $repo 'tools\partner.cs'
$dotnet = Join-Path $env:USERPROFILE '.dotnet10\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$app = Join-Path $Dir 'sandbox'
$pidFile = Join-Path $Dir 'demo.pid'
$port = 32620
$origin = "http://127.0.0.1:$port"
$branch = 'claude/v2pn-partner'
$worktree = Join-Path $env:TEMP 'configra-v2pn-wt'

function Stop-Demo {
    if (Test-Path $pidFile) {
        Stop-Process -Id ([int](Get-Content $pidFile)) -Force -ErrorAction SilentlyContinue
        Remove-Item $pidFile -Force
    }
    # A demo left from an earlier run holds the port; Get-NetTCPConnection does not always see node's listener.
    Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*demo-server.mjs*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

if ($Stop) {
    & $sandbox -Dest $app -Stop | Out-Null
    Stop-Demo
    Write-Host 'Test V2pn and the Configra demo are stopped.'
    return
}

New-Item -ItemType Directory -Force $Dir | Out-Null

Write-Host "1/5 Configra's branch $branch"
if (-not (Test-Path (Join-Path $worktree 'package.json'))) {
    if (-not (Test-Path (Join-Path $Configra '.git'))) { throw "No Configra checkout at $Configra (pass -Configra)." }
    git -C $Configra worktree prune
    git -C $Configra worktree add $worktree $branch
    if ($LASTEXITCODE -ne 0) { throw "git worktree add $branch failed" }
}
# The worktree borrows the main checkout's packages instead of installing its own.
foreach ($sub in @('', 'web')) {
    $link = Join-Path (Join-Path $worktree $sub) 'node_modules'
    $source = Join-Path (Join-Path $Configra $sub) 'node_modules'
    if (-not (Test-Path $link)) {
        if (-not (Test-Path $source)) { throw "No ${source}: run npm install in Configra first." }
        New-Item -ItemType Junction -Path $link -Target $source | Out-Null
    }
}

$dist = Join-Path $worktree 'web\dist'
if (-not $NoBuild -or -not (Test-Path (Join-Path $dist 'index.html'))) {
    Write-Host "2/5 building Configra's site and the test V2pn (a few minutes the first time)"
    Push-Location $worktree
    npm run build:web *> (Join-Path $Dir 'web-build.log')
    $webOk = $LASTEXITCODE -eq 0
    Pop-Location
    if (-not $webOk) { throw "Configra's site did not build; see $Dir\web-build.log" }
    & $dotnet build (Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj') -c Debug -nologo -v q -o (Join-Path $Dir 'build')
    if ($LASTEXITCODE -ne 0) { throw 'V2pn did not build' }
}

Write-Host '3/5 the pack for 127.0.0.1'
$site = Join-Path $Dir 'site'
if (Test-Path $site) { Remove-Item -Recurse -Force $site }
Copy-Item -Recurse $dist $site
$pack = Join-Path $site 'v2pn'
$packFile = Join-Path $pack 'partner.json'
$json = Get-Content $packFile -Raw -Encoding utf8
$json = $json -replace 'https://angleford\.co', $origin
[IO.File]::WriteAllText($packFile, $json, (New-Object Text.UTF8Encoding $false))
Push-Location $worktree
node scripts/v2pn-pack.mjs --dir $pack
$packOk = $LASTEXITCODE -eq 0
Pop-Location
if (-not $packOk) { throw "Configra's pack was not signed (needs ~/.configra/v2pn-partner-key.pem)" }
$configraKey = (Get-Content (Join-Path $pack 'cert.json') -Raw | ConvertFrom-Json).key
& $dotnet run $partnerTool -- cert --partner configra --name Configra --domains 127.0.0.1,localhost --key $configraKey `
    --grants edition,account --days 30 --out $pack
if ($LASTEXITCODE -ne 0) { throw 'the certificate for 127.0.0.1 was not issued (needs ~/.v2pn/partner-root-key.pem)' }

Write-Host "4/5 starting Configra's demo on $origin/ (hidden)"
& $sandbox -Dest $app -Stop | Out-Null
Stop-Demo
$env:CONFIGRA_WEB_DIST = $site
$demo = Start-Process node -ArgumentList 'scripts/demo-server.mjs' -WorkingDirectory $worktree -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $Dir 'demo.log') -RedirectStandardError (Join-Path $Dir 'demo.err')
Remove-Item Env:CONFIGRA_WEB_DIST
$demo.Id | Set-Content $pidFile
$up = $false
# Straight to the demo, past the system proxy that the live V2pn may have set.
$web = New-Object Net.WebClient
$web.Proxy = $null
foreach ($i in 1..30) {
    try { $web.DownloadString("$origin/v2pn/partner.json") | Out-Null; $up = $true; break } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $up) { throw "the demo did not start; see $Dir\demo.err" }

Write-Host '5/5 opening the test V2pn'
$liveBin = Join-Path $env:LOCALAPPDATA 'Programs\V2pn\bin'
# The cores come from the installed V2pn, read only; the test copy keeps its own data between runs.
& $sandbox -From (Join-Path $Dir 'build') -Dest $app -CoresFrom (Split-Path $liveBin) -KeepData | Out-Null
$gui = Join-Path $app 'guiConfigs'
if ($Fresh) {
    Remove-Item -Recurse -Force (Join-Path $gui 'partners') -ErrorAction SilentlyContinue
    Remove-Item -Force (Join-Path $gui 'V2pnEdition.json') -ErrorAction SilentlyContinue
}
$link = 'v2pn://add?url=' + [Uri]::EscapeDataString("$origin/s/demo-token") + '&name=Configra'
if ($Hidden) { & $sandbox -Dest $app -Launch -AppArgs $link | Out-Null }
else { & $sandbox -Dest $app -Launch -Visible -AppArgs $link | Out-Null }
Write-Host ''
Write-Host "Test V2pn is open (folder $app). Add the Configra subscription it offers."
Write-Host 'Stop everything: tools/demo/try-configra.ps1 -Stop'
