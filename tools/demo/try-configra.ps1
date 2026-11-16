<#
.SYNOPSIS
  Configra in a test V2pn on this computer: the demo of Configra's site with its pack from partners/configra, before
  Configra is deployed.

.DESCRIPTION
  Starts Configra's demo site (branch claude/v2pn-partner, in a worktree under %TEMP%) on http://127.0.0.1:32620/,
  hidden, and opens a test V2pn on the screen with Configra's demo subscription offered through a v2pn://add link.

  The pack is partners/configra with two changes for this computer: its API and cabinet point at the demo, and its
  listing names 127.0.0.1 and localhost for 30 days. It is published (tools/partner.cs publish --local) with a test
  root key made here, never the real one, and served as a releases page on http://127.0.0.1:32621/ (hidden). The test
  V2pn is a Debug build of this tree that trusts the test root and asks that page for releases: V2pn takes a partner
  over plain http from this computer only in a development build.

  The demo's servers are made up (example.com): the look and the plan work, connecting does not.
  Nothing touches the V2pn you use: the test copy keeps its own data and other ports and leaves the system proxy alone.

.EXAMPLE
  tools/demo/try-configra.ps1            # build, publish, start everything
  tools/demo/try-configra.ps1 -NoBuild   # restart quickly with the last builds
  tools/demo/try-configra.ps1 -Fresh     # as a computer that has never seen Configra
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
$releasesPidFile = Join-Path $Dir 'releases.pid'
$port = 32620
$origin = "http://127.0.0.1:$port"
$releases = 'http://127.0.0.1:32621'
$branch = 'claude/v2pn-partner'
$worktree = Join-Path $env:TEMP 'configra-v2pn-wt'

function Stop-Demo {
    foreach ($file in $pidFile, $releasesPidFile) {
        if (Test-Path $file) {
            Stop-Process -Id ([int](Get-Content $file)) -Force -ErrorAction SilentlyContinue
            Remove-Item $file -Force
        }
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

# The test root: the private half signs the index, the public half goes into the build.
$rootPem = Join-Path $Dir 'root.pem'
$rootPubFile = Join-Path $Dir 'root.pub'
if (-not (Test-Path $rootPem)) {
    $gen = Join-Path $Dir 'new-key.cs'
    Set-Content -Encoding utf8 $gen 'using System.Security.Cryptography; using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256); System.IO.File.WriteAllText(args[0], k.ExportPkcs8PrivateKeyPem()); System.Console.WriteLine(System.Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));'
    $out = & $dotnet run $gen -- $rootPem
    if ($LASTEXITCODE -ne 0) { throw 'the test root key was not made' }
    ($out | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim() | Set-Content -Encoding ascii $rootPubFile
}
$rootPub = (Get-Content $rootPubFile -Raw).Trim()

$dist = Join-Path $worktree 'web\dist'
if (-not $NoBuild -or -not (Test-Path (Join-Path $dist 'index.html')) -or -not (Test-Path (Join-Path $Dir 'build\V2pn.exe'))) {
    Write-Host "2/5 building Configra's site and the test V2pn (a few minutes the first time)"
    Push-Location $worktree
    npm run build:web *> (Join-Path $Dir 'web-build.log')
    $webOk = $LASTEXITCODE -eq 0
    Pop-Location
    if (-not $webOk) { throw "Configra's site did not build; see $Dir\web-build.log" }
    $project = Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj'
    & $dotnet build $project -c Debug -nologo -v q "-p:V2pnPartnerKeys=$rootPub" "-p:V2pnUpdateSources=$releases" -o (Join-Path $Dir 'build')
    if ($LASTEXITCODE -ne 0) { throw 'V2pn did not build' }
    # The shared obj folder now holds the test key and source; build once more so the normal Debug build has the real ones.
    & $dotnet build $project -c Debug -nologo -v q | Out-Null
}

Write-Host '3/5 publishing partners/configra for 127.0.0.1 with the test root'
$site = Join-Path $Dir 'site'
if (Test-Path $site) { Remove-Item -Recurse -Force $site }
Copy-Item -Recurse $dist $site
$partners = Join-Path $Dir 'partners'
$pack = Join-Path $partners 'configra'
if (Test-Path $partners) { Remove-Item -Recurse -Force $partners }
New-Item -ItemType Directory -Force $partners | Out-Null
Copy-Item -Recurse (Join-Path $repo 'partners\configra') $pack
$utf8 = New-Object Text.UTF8Encoding $false
$packFile = Join-Path $pack 'partner.json'
[IO.File]::WriteAllText($packFile, ((Get-Content $packFile -Raw -Encoding utf8) -replace 'https://angleford\.co', $origin), $utf8)
$listingFile = Join-Path $pack 'listing.json'
$listing = Get-Content $listingFile -Raw -Encoding utf8 | ConvertFrom-Json
$listing.domains = @('127.0.0.1', 'localhost')
$listing.until = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
[IO.File]::WriteAllText($listingFile, ($listing | ConvertTo-Json), $utf8)
$download = Join-Path $Dir 'releases\latest\download'
& $dotnet run $partnerTool -- publish --partners $partners --out $download --root-key $rootPem --local | Out-Null
# 3: the app's real keys do not take the test root, as expected.
if ($LASTEXITCODE -notin 0, 3 -or -not (Test-Path (Join-Path $download 'partner-index.json'))) { throw 'publish failed' }

Write-Host "4/5 starting Configra's demo on $origin/ and the releases page on $releases/ (hidden)"
& $sandbox -Dest $app -Stop | Out-Null
Stop-Demo
$page = Start-Process python -ArgumentList @('-I', '-m', 'http.server', '32621', '--bind', '127.0.0.1', '--directory', (Join-Path $Dir 'releases')) `
    -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Dir 'releases.log') -RedirectStandardError (Join-Path $Dir 'releases.err')
$page.Id | Set-Content $releasesPidFile
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
    try { $web.DownloadString("$origin/") | Out-Null; $web.DownloadString("$releases/latest/download/partner-index.json") | Out-Null; $up = $true; break } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $up) { throw "the demo or the releases page did not start; see $Dir\demo.err and $Dir\releases.err" }

Write-Host '5/5 opening the test V2pn'
$liveBin = Join-Path $env:LOCALAPPDATA 'Programs\V2pn\bin'
# The cores come from the installed V2pn, read only; the test copy keeps its own data between runs.
& $sandbox -From (Join-Path $Dir 'build') -Dest $app -CoresFrom (Split-Path $liveBin) -KeepData -Partner configra | Out-Null
$gui = Join-Path $app 'guiConfigs'
if ($Fresh) {
    Remove-Item -Recurse -Force (Join-Path $gui 'partners') -ErrorAction SilentlyContinue
}
$link = 'v2pn://add?url=' + [Uri]::EscapeDataString("$origin/s/demo-token") + '&name=Configra'
if ($Hidden) { & $sandbox -Dest $app -Launch -AppArgs $link | Out-Null }
else { & $sandbox -Dest $app -Launch -Visible -AppArgs $link | Out-Null }
Write-Host ''
Write-Host "Test V2pn is open (folder $app). Add the Configra subscription it offers."
Write-Host 'Stop everything: tools/demo/try-configra.ps1 -Stop'
