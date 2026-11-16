<#
.SYNOPSIS
  A V2pn test build with a pretend provider "Kestrel" that uses every provider feature, on this computer.

.DESCRIPTION
  Builds V2pn into a separate folder that trusts a test partner root key (never the real one) and asks the
  pretend provider for releases, publishes a partner index with Kestrel for 127.0.0.1 signed with that key
  (tools/partner.cs publish --local), starts the pretend provider (subscription, the index and the pack's zip,
  API and a local VLESS server through Xray, all hidden) and opens the test V2pn on the screen with the
  Kestrel subscription offered.
  Nothing here touches the V2pn you use: the test copy keeps its own data, other local ports and leaves the
  system proxy alone.

  Kestrel has: Scene on its gradient, buttons (fastest, most stable, for YouTube, renew), server groups
  with purposes and notes, the plan from its API, reviewed routes ("Маршруты Kestrel" in "Что идёт через VPN"),
  reviewed connection settings and core config. -Lock adds closed settings, -HideUrl hides the link as well.

.EXAMPLE
  tools/demo/try-partner.ps1            # build, publish, start everything
  tools/demo/try-partner.ps1 -Lock      # the same with closed settings
  tools/demo/try-partner.ps1 -NoBuild   # restart quickly with the last build
  tools/demo/try-partner.ps1 -Stop      # close the test V2pn and the pretend provider
  tools/demo/try-partner.ps1 -SignIn    # a fresh test V2pn without the link: sign in with Kestrel by code instead
  tools/demo/try-partner.ps1 -SignIn -Background -LoginNever   # off screen with the debug window, the code never confirmed
#>
param(
    [string]$Dir = (Join-Path $env:TEMP 'v2pn-try'),
    [switch]$Lock,
    [switch]$HideUrl,
    [switch]$NoBuild,
    [switch]$Stop,
    # A fresh test copy (the walkthrough opens) and no v2pn://add link: Kestrel is added by 'Sign in with a provider',
    # which the pretend provider confirms after a few polls of login/poll.
    [switch]$SignIn,
    # login/poll never says "done", for pictures of the code.
    [switch]$LoginNever,
    # Off screen with the debug window (V2PN_DEBUG=1), for scripted checks with UI Automation and tools/shot.ps1.
    [switch]$Background
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

# A P-256 key for the test root: the private half to sign with, the public half for the build.
function Get-Key([string]$name) {
    $pem = Join-Path $keys "$name.pem"
    $pub = Join-Path $keys "$name.pub"
    if (-not (Test-Path $pem)) {
        $gen = Join-Path $keys 'new-key.cs'
        Set-Content -Encoding utf8 $gen 'using System.Security.Cryptography; using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256); System.IO.File.WriteAllText(args[0], k.ExportPkcs8PrivateKeyPem()); System.Console.WriteLine(System.Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()));'
        $out = & $dotnet run $gen -- $pem
        if ($LASTEXITCODE -ne 0) { throw "the test key $name was not made" }
        ($out | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim() | Set-Content -Encoding ascii $pub
    }
    (Get-Content $pub -Raw).Trim()
}

Write-Host '1/5 test root key'
$rootPub = Get-Key 'root'
$releases = 'http://127.0.0.1:18998/releases'

if (-not $NoBuild -or -not (Test-Path (Join-Path $Dir 'build\V2pn.exe'))) {
    Write-Host '2/5 building V2pn that trusts the test root and asks the pretend provider for releases (a few minutes the first time)'
    & $dotnet build (Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj') -c Debug -nologo -v q "-p:V2pnPartnerKeys=$rootPub" "-p:V2pnUpdateSources=$releases" -o (Join-Path $Dir 'build')
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    # The shared obj folder now holds the test key and source; build once more so the normal Debug build has the real ones.
    & $dotnet build (Join-Path $repo 'v2rayN\v2rayN.Desktop\v2rayN.Desktop.csproj') -c Debug -nologo -v q | Out-Null
}

Write-Host '3/5 publishing Kestrel for 127.0.0.1 with the test root'
$partners = Join-Path $Dir 'partners'
$pack = Join-Path $partners 'kestrel'
$published = Join-Path $Dir 'published'
if (Test-Path $partners) { Remove-Item -Recurse -Force $partners }
New-Item -ItemType Directory -Force $pack | Out-Null
Copy-Item (Join-Path $demo 'partner\*') $pack
# The reviewed files' grants put their hashes into the index; lock only when asked for.
$grants = @('edition', 'account', 'actions', 'routing', 'transport', 'core') + $(if ($Lock -or $HideUrl) { @('lock') } else { @() })
$listing = [ordered]@{ id = 'kestrel'; name = 'Kestrel'; domains = @('127.0.0.1'); grants = $grants; until = (Get-Date).AddDays(30).ToString('yyyy-MM-dd') }
[IO.File]::WriteAllText((Join-Path $pack 'listing.json'), ($listing | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
& $dotnet run $partnerTool -- pack $pack | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'pack failed' }
& $dotnet run $partnerTool -- publish --partners $partners --out $published --root-key (Join-Path $keys 'root.pem') --local | Out-Null
# 3: the app's real keys do not take the test root, as expected.
if ($LASTEXITCODE -notin 0, 3 -or -not (Test-Path (Join-Path $published 'partners.json'))) { throw 'publish failed' }

Write-Host '4/5 starting the pretend provider on http://127.0.0.1:18998/ (hidden)'
Stop-Provider
& $sandbox -Dest $app -Stop | Out-Null
$xrayDir = Join-Path $Dir 'xray'
$liveBin = Join-Path $env:LOCALAPPDATA 'Programs\V2pn\bin'
if (-not (Test-Path (Join-Path $xrayDir 'xray.exe'))) {
    New-Item -ItemType Directory -Force $xrayDir | Out-Null
    Copy-Item (Join-Path $liveBin 'xray\*') $xrayDir -Recurse
}
$providerArgs = @('-I', (Join-Path $demo 'demo_partner.py'), $pack, '--published', $published, '--xray', (Join-Path $xrayDir 'xray.exe'))
if ($Lock -or $HideUrl) { $providerArgs += '--lock' }
if ($HideUrl) { $providerArgs += '--hide-url' }
if ($LoginNever) { $providerArgs += '--login-never' }
$provider = Start-Process python -ArgumentList $providerArgs -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $Dir 'provider.log') -RedirectStandardError (Join-Path $Dir 'provider.err')
$provider.Id | Set-Content $pidFile

Write-Host '5/5 opening the test V2pn'
# The cores come from the installed V2pn, read only; the test copy keeps its own data between runs, except for a
# sign-in, which starts from a fresh install.
if ($SignIn) {
    & $sandbox -From (Join-Path $Dir 'build') -Dest $app -CoresFrom (Split-Path $liveBin) | Out-Null
}
else {
    & $sandbox -From (Join-Path $Dir 'build') -Dest $app -CoresFrom (Split-Path $liveBin) -KeepData | Out-Null
}
$launch = @{ Dest = $app; Launch = $true }
if (-not $Background) { $launch.Visible = $true }
if (-not $SignIn) { $launch.AppArgs = 'v2pn://add?url=http%3A%2F%2F127.0.0.1%3A18998%2Fsub&name=Kestrel' }
if ($Background) { $env:V2PN_DEBUG = '1' }
try { & $sandbox @launch | Out-Null } finally { if ($Background) { Remove-Item Env:V2PN_DEBUG } }
Write-Host ''
if ($SignIn) {
    Write-Host "Test V2pn is $(if ($Background) { 'running off screen' } else { 'open' }) (folder $app). Sign in with Kestrel: the walkthrough's 'I have a subscription from a provider', or + then 'Sign in with a provider'."
}
else {
    Write-Host "Test V2pn is open (folder $app). Add the Kestrel subscription it offers."
}
Write-Host 'Stop everything: tools/demo/try-partner.ps1 -Stop'
