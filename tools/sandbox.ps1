<#
.SYNOPSIS
  Runs a V2pn build in a throwaway portable folder that cannot disturb the V2pn you use.

.DESCRIPTION
  A fresh v2rayN config clears the Windows system proxy on start and on exit, and listens on
  10808. Both would cut off a V2pn that is already running. The sandbox config keeps the
  system proxy untouched, turns TUN off and moves the local ports to -Port.

  The app keeps its data next to the exe, so each sandbox is fully separate. Only one
  sandbox can run at a time: V2pn allows a single instance per user session.

  -Launch starts the app in background mode (V2PN_BACKGROUND=1): its windows open off the screen,
  take no focus and get no taskbar button, so nothing flashes in front of the owner. tools/shot.ps1
  and UI Automation still see them. Pass -Visible to put the windows on the screen.

.EXAMPLE
  tools/sandbox.ps1 -From v2rayN/v2rayN.Desktop/bin/Release/net10.0 -Dest $env:TEMP/v2pn-a -Launch
  tools/sandbox.ps1 -Dest $env:TEMP/v2pn-a -Stop
#>
param(
    # Build output folder or release zip to copy from. Omit to reuse what is already in -Dest.
    [string]$From,
    [Parameter(Mandatory = $true)][string]$Dest,
    [int]$Port = 20808,
    # Copies bin/ (cores and geo files) from this folder, e.g. dist/V2pn-win-x64.
    [string]$CoresFrom,
    [switch]$Launch,
    # Shows the windows on the screen instead of keeping them off it.
    [switch]$Visible,
    # Passed to V2pn on -Launch, e.g. a v2pn://add link to test how links are handled.
    [string[]]$AppArgs,
    [switch]$Stop,
    # Keeps guiConfigs/ when -From refreshes the app files.
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'
$Dest = [IO.Path]::GetFullPath($Dest)

function Stop-Sandbox {
    $procs = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($Dest, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    }
    foreach ($p in $procs) {
        Write-Host "Stopping $($p.ProcessName) ($($p.Id))"
        # CloseMainWindow lets V2pn save its config; the sandbox config never touches the proxy on exit.
        if ($p.MainWindowHandle -ne 0) { [void]$p.CloseMainWindow(); [void]$p.WaitForExit(5000) }
        if (-not $p.HasExited) { $p.Kill(); [void]$p.WaitForExit(3000) }
    }
}

if ($Stop) { Stop-Sandbox; return }

if ($From) {
    Stop-Sandbox
    if ((Test-Path $Dest) -and -not $KeepData) { Remove-Item -Recurse -Force $Dest }
    New-Item -ItemType Directory -Force $Dest | Out-Null
    if ($From.EndsWith('.zip')) {
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ("v2pn-unzip-" + [Guid]::NewGuid())
        Expand-Archive -Path $From -DestinationPath $tmp
        # Release zips hold one top-level folder, as AmazTool expects.
        $inner = Get-ChildItem $tmp -Directory | Select-Object -First 1
        Copy-Item -Recurse -Force (Join-Path $inner.FullName '*') $Dest
        Remove-Item -Recurse -Force $tmp
    }
    else {
        Copy-Item -Recurse -Force (Join-Path $From '*') $Dest -Exclude 'guiConfigs', 'guiLogs', 'guiTemps', 'binConfigs'
    }
    if ($CoresFrom) {
        Copy-Item -Recurse -Force (Join-Path $CoresFrom 'bin') $Dest
    }
}

$configDir = Join-Path $Dest 'guiConfigs'
$configFile = Join-Path $configDir 'guiNConfig.json'
if (-not (Test-Path $configFile)) {
    New-Item -ItemType Directory -Force $configDir | Out-Null
    # Unchanged = 2 in ESysProxyType. Missing sections are filled with defaults on load.
    $config = [ordered]@{
        Inbound         = @(@{ Protocol = 'socks'; LocalPort = $Port; UdpEnabled = $true; SniffingEnabled = $true; RouteOnly = $false })
        SystemProxyItem = @{ SysProxyType = 2 }
        TunModeItem     = @{ EnableTun = $false }
        UiItem          = @{ V2pnProxyMethod = 2; V2pnPreferTun = $false; EnableAutoAdjustMainLvColWidth = $true }
        GuiItem         = @{ AutoRun = $false; DisplayRealTimeSpeed = $true }
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $configFile
    Write-Host "Sandbox config written: system proxy untouched, socks port $Port"
}

if ($Launch) {
    $exe = Join-Path $Dest 'V2pn.exe'
    if (-not (Test-Path $exe)) { throw "V2pn.exe not found in $Dest" }
    $old = $env:V2PN_BACKGROUND
    $env:V2PN_BACKGROUND = if ($Visible) { $null } else { '1' }
    # A shell's HTTP_PROXY (often the live V2pn) would carry the sandbox's own requests, local test sites included.
    $proxyVars = 'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'WS_PROXY', 'WSS_PROXY', 'http_proxy', 'https_proxy', 'all_proxy'
    $savedProxy = @{}
    foreach ($name in $proxyVars) {
        $value = [Environment]::GetEnvironmentVariable($name, 'Process')
        if ($value) { $savedProxy[$name] = $value; [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    }
    try {
        $p = if ($AppArgs) { Start-Process -FilePath $exe -WorkingDirectory $Dest -ArgumentList $AppArgs -PassThru }
             else { Start-Process -FilePath $exe -WorkingDirectory $Dest -PassThru }
    }
    finally {
        $env:V2PN_BACKGROUND = $old
        foreach ($name in $savedProxy.Keys) { [Environment]::SetEnvironmentVariable($name, $savedProxy[$name], 'Process') }
    }
    $mode = if ($Visible) { 'on screen' } else { 'in the background, windows off screen' }
    Write-Host "Started V2pn ($($p.Id)) from $Dest, $mode"
}
