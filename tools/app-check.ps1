<#
.SYNOPSIS
  Starts a V2pn build where nobody sees it and checks that it runs; with -Connect, also that it connects and
  carries traffic.

.DESCRIPTION
  Everything runs in a sandbox (tools/sandbox.ps1): its own folder and ports, the system proxy untouched, the windows
  off the screen and without focus (background mode). Nothing is stopped by name: the V2pn and xray in use on this
  computer are left alone.

  The start: V2pn must still run -Seconds (30) after it started, and its log (guiLogs) must hold no exception. A
  picture of its window (tools/shot.ps1, PrintWindow) and the logs go to <Work>/evidence.

  -Connect, before that: xray from the pinned cores (tools/cores.lock.json, checked against its hash) runs a local
  VLESS server and tools/demo/demo_sub.py serves its subscription. The sandbox gets the subscription as a preset
  (v2pn-preset.txt, added at the first start without asking) and control from the command line, and v2pnctl waits
  for the servers, chooses one, sets Proxy mode and the route "everything" and connects. Then curl asks for
  http://demo.v2pn-e2e:<SubPort>/sub through V2pn's SOCKS port. Only the demo server resolves that name, so the
  answer proves the whole way: curl, V2pn, the VLESS server, the local HTTP server. The system proxy must be as it
  was before.

  CI: .github/workflows/v2pn-ci.yml runs the start on every push, v2pn-e2e.yml runs -Connect nightly and on demand.

.EXAMPLE
  tools/release.ps1 -Version 0.0.1 -Arch x64 -NoCores -NoInstaller -UpdateOnly -NoManifest -OutDir $env:TEMP/v2pn-pkg -UpdateSources http://127.0.0.1:9/releases
  tools/app-check.ps1 -From $env:TEMP/v2pn-pkg/V2pn-windows-x64-update.zip
  tools/app-check.ps1 -From $env:TEMP/v2pn-pkg/V2pn-windows-x64-update.zip -Connect
#>
param(
    # The app: a package zip (laid out as an update unpacks it) or a folder with V2pn.exe; -Connect needs v2pnctl.exe in it.
    [Parameter(Mandatory = $true)][string]$From,
    [string]$Work = (Join-Path ([IO.Path]::GetTempPath()) 'v2pn-app-check'),
    [switch]$Connect,
    [int]$Seconds = 30,
    # V2pn's SOCKS port (the HTTP one is next to it): far from 10808, which a V2pn in use has.
    [int]$Port = 28910,
    [int]$SubPort = 28950,
    [int]$ServerPort = 28951,
    # A folder with the cores' bin/; by default the bundle pinned in tools/cores.lock.json.
    [string]$CoresFrom,
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot '_common.ps1')
# v2pnctl writes UTF-8 into a pipe (server names have flags).
try { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false) } catch { }

$From = [IO.Path]::GetFullPath($From)
$Work = [IO.Path]::GetFullPath($Work)
$sandbox = Join-Path $Work 'sandbox'
$evidence = Join-Path $Work 'evidence'
$sandboxScript = Join-Path $PSScriptRoot 'sandbox.ps1'
# Only the demo server resolves it (tools/demo/demo_sub.py, SERVER_ONLY_HOST).
$serverOnlyHost = 'demo.v2pn-e2e'

function Step([string]$Text) { Write-Host "== $Text" }

function Wait-Until([string]$What, [int]$Timeout, [scriptblock]$Test) {
    $deadline = (Get-Date).AddSeconds($Timeout)
    while ($true) {
        $result = & $Test
        if ($result) { return $result }
        if ((Get-Date) -gt $deadline) { throw "Waited $Timeout s for $What." }
        Start-Sleep -Seconds 1
    }
}

# curl.exe (Windows 10 and later) without a proxy from the environment: {Code, Body}.
function Invoke-Curl([string[]]$Arguments) {
    $body = Join-Path $Work 'curl-body.txt'
    Remove-Item -Force $body -ErrorAction SilentlyContinue
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $code = & curl.exe -s --max-time 20 -o $body -w '%{http_code}' @Arguments 2>$null }
    finally { $ErrorActionPreference = $previous }
    $text = if (Test-Path $body) { [IO.File]::ReadAllText($body) } else { '' }
    return [pscustomobject]@{ Code = "$code"; Body = $text }
}

# One v2pnctl command to the sandbox's V2pn: {Code, Ok, Data, Error, Text}.
function Invoke-Ctl {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $text = (& $script:ctl --instance $sandbox --json @args 2>$null) -join "`n"
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    $json = $null
    try { $json = $text | ConvertFrom-Json } catch { }
    return [pscustomobject]@{ Code = $code; Ok = ($json.ok -eq $true); Data = $json.data; Error = $json.error.message; Text = $text }
}

function Assert-Ctl([string]$What, [object]$Answer) {
    if (-not $Answer.Ok) { throw "v2pnctl ${What}: exit $($Answer.Code), $($Answer.Error) $($Answer.Text)" }
    Write-Host "   v2pnctl ${What}: ok"
}

function Get-SandboxProcesses {
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($sandbox, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    }
}

function Get-SystemProxy {
    $key = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue
    return "enable=$($key.ProxyEnable) server=$($key.ProxyServer) pac=$($key.AutoConfigURL)"
}

# A process and every process it started, by id.
function Stop-Tree([int]$Id) {
    foreach ($child in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $Id" -ErrorAction SilentlyContinue)) {
        Stop-Tree ([int]$child.ProcessId)
    }
    Stop-Process -Id $Id -Force -ErrorAction SilentlyContinue
}

$server = $null
$failure = $null
try {
    if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
    New-Item -ItemType Directory -Force $Work, $evidence | Out-Null
    $proxyBefore = Get-SystemProxy

    if ($Connect) {
        if (-not $CoresFrom) {
            Step 'cores pinned in tools/cores.lock.json'
            $target = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
            $cores = Join-Path $Work 'cores'
            Expand-Archive -Path (Get-PinnedCores $target) -DestinationPath $cores
            # The bundle has one top folder with bin/ in it. Outside the sandbox: its -Stop ends everything under it.
            $CoresFrom = (Get-ChildItem $cores -Directory | Select-Object -First 1).FullName
        }
        $xray = Join-Path $CoresFrom 'bin\xray\xray.exe'
        if (-not (Test-Path $xray)) { throw "No xray in $CoresFrom\bin\xray" }

        Step "demo VLESS server on 127.0.0.1:$ServerPort, its subscription on 127.0.0.1:$SubPort"
        $quote = { param($s) '"' + $s + '"' }
        $server = Start-Process -FilePath $Python -WindowStyle Hidden -PassThru `
            -ArgumentList @((& $quote (Join-Path $PSScriptRoot 'demo\demo_sub.py')), (& $quote $xray), $SubPort, $ServerPort) `
            -RedirectStandardOutput (Join-Path $evidence 'demo-server.txt') -RedirectStandardError (Join-Path $evidence 'demo-server-errors.txt')
        $subscription = Wait-Until 'the demo subscription' 30 {
            $r = Invoke-Curl @('--noproxy', '*', "http://127.0.0.1:$SubPort/sub")
            if ($r.Code -eq '200' -and $r.Body.Length -gt 0) { $r.Body }
        }
        Wait-Until 'the demo VLESS server' 30 {
            $tcp = [Net.Sockets.TcpClient]::new()
            try { $tcp.Connect('127.0.0.1', $ServerPort); $true } catch { $false } finally { $tcp.Dispose() }
        } | Out-Null
        # The proof below holds only if this computer cannot resolve the name by itself.
        $direct = Invoke-Curl @('--noproxy', '*', "http://${serverOnlyHost}:$SubPort/sub")
        if ($direct.Code -eq '200') { throw "$serverOnlyHost resolves on this computer; the check would prove nothing." }
    }

    Step "sandbox $sandbox, SOCKS port $Port"
    $sandboxArgs = @{ From = $From; Dest = $sandbox; Port = $Port }
    if ($Connect) { $sandboxArgs.CoresFrom = $CoresFrom; $sandboxArgs.Control = $true }
    & $sandboxScript @sandboxArgs
    if ($Connect) {
        # Added at the first start without asking, as a special build's subscription is (V2pnPreset).
        [IO.File]::WriteAllText((Join-Path $sandbox 'v2pn-preset.txt'), "http://127.0.0.1:$SubPort/sub`n")
        $script:ctl = Join-Path $sandbox 'v2pnctl.exe'
        if (-not (Test-Path $script:ctl)) { throw "No v2pnctl.exe in $From" }
    }

    Step 'V2pn starts in background mode'
    # A V2pn that v2pnctl would start itself stays in background mode too.
    $background = $env:V2PN_BACKGROUND
    $env:V2PN_BACKGROUND = '1'
    $started = Get-Date
    & $sandboxScript -Dest $sandbox -Launch
    $app = Wait-Until 'V2pn to start' 30 { Get-SandboxProcesses | Where-Object { $_.ProcessName -eq 'V2pn' } | Select-Object -First 1 }
    Write-Host "   V2pn $($app.Id)"

    if ($Connect) {
        Step 'v2pnctl: the subscription, a server, Proxy mode, the route, connect'
        Wait-Until 'the control channel' 60 { (Invoke-Ctl status).Ok } | Out-Null
        $servers = @(Wait-Until 'the subscription''s servers' 90 { $s = @((Invoke-Ctl servers).Data); if ($s.Count -gt 0 -and $s[0].id) { , $s } })
        Write-Host "   $($servers.Count) servers"
        Assert-Ctl 'route all' (Invoke-Ctl route all)
        Assert-Ctl 'mode proxy' (Invoke-Ctl mode proxy)
        Assert-Ctl "use $($servers[0].id)" (Invoke-Ctl use $servers[0].id)
        Assert-Ctl 'connect' (Invoke-Ctl connect)
        $status = Wait-Until 'the connection' 60 { $s = (Invoke-Ctl status).Data; if ($s.connected -and $s.settled) { $s } }
        Write-Host "   connected: mode $($status.mode), route $($status.route), core $($status.core), check $($status.check.state)"
        if ($status.mode -ne 'proxy') { throw "V2pn connected in $($status.mode) mode, not proxy." }

        Step "a request through V2pn's SOCKS port reaches the local HTTP server through the VLESS server"
        $through = Invoke-Curl @('--socks5-hostname', "127.0.0.1:$Port", "http://${serverOnlyHost}:$SubPort/sub")
        if ($through.Code -ne '200' -or $through.Body -ne $subscription) {
            throw "Through V2pn: HTTP $($through.Code), $($through.Body.Length) bytes; expected the subscription ($($subscription.Length) bytes)."
        }
        Write-Host "   HTTP 200, $($through.Body.Length) bytes, the same as served"
        $proxyNow = Get-SystemProxy
        if ($proxyNow -ne $proxyBefore) { throw "The system proxy changed: was '$proxyBefore', now '$proxyNow'." }
        Write-Host "   system proxy untouched ($proxyNow)"
        Assert-Ctl 'disconnect' (Invoke-Ctl disconnect)
    }

    $left = $Seconds - ((Get-Date) - $started).TotalSeconds
    if ($left -gt 0) {
        Step "V2pn runs for $Seconds s"
        Start-Sleep -Seconds ([math]::Ceiling($left))
    }
    if (-not (Get-SandboxProcesses | Where-Object { $_.Id -eq $app.Id })) { throw "V2pn is not running $Seconds s after it started." }
    Write-Host "   V2pn $($app.Id) still runs"
    try { & (Join-Path $PSScriptRoot 'shot.ps1') -ProcessId $app.Id -Out (Join-Path $evidence 'main-window.png') } catch { Write-Host "   no picture: $($_.Exception.Message)" }
}
catch {
    $failure = $_
}
finally {
    if (Test-Path $sandbox) { & $sandboxScript -Dest $sandbox -Stop }
    if ($server) { Stop-Tree $server.Id }
    $env:V2PN_BACKGROUND = $background
    if (Test-Path (Join-Path $sandbox 'guiLogs')) { Copy-Item -Recurse -Force (Join-Path $sandbox 'guiLogs') $evidence }
}

# What V2pn logged: an exception anywhere fails the check.
$logs = @(Get-ChildItem (Join-Path $evidence 'guiLogs') -File -Recurse -ErrorAction SilentlyContinue)
$exceptions = @($logs | Select-String -Pattern 'Exception' -SimpleMatch)
if ($failure) { throw $failure }
if ($logs.Count -eq 0) { throw 'V2pn wrote no log.' }
if ($exceptions.Count -gt 0) {
    $exceptions | Select-Object -First 20 | ForEach-Object { Write-Host "   $($_.Filename):$($_.LineNumber): $($_.Line)" }
    throw "V2pn's log has $($exceptions.Count) line(s) with an exception (above; the logs are in $evidence)."
}
Write-Host "Passed. Logs and the picture: $evidence"
