# Measures how fast a sandboxed V2pn brings its window up: the first time after a start in the tray, from the tray,
# from the taskbar, by a second start, and after Windows has taken its memory away.
# Reads the V2PN_PERF hook (MainWindow.Debug.cs), which any local build has (a release package only in developer
# mode). For numbers like a release, measure a local publish with ReadyToRun:
#   dotnet publish v2rayN.Desktop -c Release -r win-x64 -p:SelfContained=true -p:PublishReadyToRun=true -o <dir>
# Everything runs in the sandbox, off screen and without the focus.
#   tools/showperf.ps1 -From <publish dir> -Label after
#   tools/showperf.ps1 -From <publish dir> -OnlyFirst      (only the first show after a start in the tray)
param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [string]$From,
    [string]$Dest = (Join-Path $env:TEMP 'v2pn-showperf'),
    [string]$Log = (Join-Path $env:TEMP 'v2pn-showperf-log'),
    [int]$Rounds = 5,
    [int]$Warm = 12,
    # How many times the app is started in the tray and shown for the first time.
    [int]$FirstShows = 3,
    [switch]$KeepData,
    [switch]$OnlyFirst,
    [string]$Label = 'run'
)
$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ShowPerfNative {
    [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@
$sandbox = Join-Path $Repo 'tools\sandbox.ps1'
New-Item -ItemType Directory -Force $Log | Out-Null
Remove-Item -Force (Join-Path $Log '*') -ErrorAction SilentlyContinue
$perfLog = Join-Path $Log 'perf.log'
$exe = Join-Path $Dest 'V2pn.exe'

if ($From) {
    if ($KeepData) { & $sandbox -From $From -Dest $Dest -KeepData | Out-Null }
    else { & $sandbox -From $From -Dest $Dest -CoresFrom (Join-Path $Repo 'dist\V2pn-win-x64') | Out-Null }
}

$script:serial = 0
function Drop([string]$name) { $script:serial++; Set-Content -Path (Join-Path $Log "$name.$($script:serial)") -Value '' -Encoding ascii }
function Lines { if (Test-Path $perfLog) { @(Get-Content $perfLog | Where-Object { $_ -match ' seen=' }) } else { @() } }
function Last([int]$before, [string]$tag) {
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        $l = @(Lines)
        if ($l.Count -gt $before) { $line = $l[-1]; Write-Host ("{0,-22} {1}" -f $tag, ($line -replace 'asked=\d+ freq=\d+ ', '')); return $line }
        Start-Sleep -Milliseconds 100
    }
    Write-Host "$tag  (no line)"; return $null
}
function Median($values) { $s = @($values | Sort-Object); if ($s.Count -eq 0) { return $null }; $s[[int][math]::Floor($s.Count / 2)] }
function Field([string]$line, [string]$name) { if ($line -match "$name=([\d.]+)") { [double]$Matches[1] } }
function Start-Sandbox([string[]]$appArgs) {
    $env:V2PN_PERF = $Log
    try {
        if ($appArgs) { & $sandbox -Dest $Dest -Launch -AppArgs $appArgs | Out-Null }
        else { & $sandbox -Dest $Dest -Launch | Out-Null }
    }
    finally { $env:V2PN_PERF = $null }
    Start-Sleep $Warm
    $p = Get-Process V2pn -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
    if (-not $p) { throw 'sandbox V2pn is not running' }
    return $p
}
function Stop-Sandbox { & $sandbox -Dest $Dest -Stop | Out-Null }

$fg0 = [ShowPerfNative]::GetForegroundWindow()
$result = [ordered]@{}

# --- the first show: started in the tray (as at sign-in), never drawn, then the tray icon is clicked ---
$frames = @(); $settled = @()
for ($i = 0; $i -lt $FirstShows; $i++) {
    $proc = Start-Sandbox @('--tray')
    $n = @(Lines).Count; Drop 'show'
    $line = Last $n 'first show'
    if ($line) { $frames += Field $line 'frame'; $settled += Field $line 'settled' }
    Start-Sleep -Milliseconds 1500
    if ($i -lt $FirstShows - 1 -or $OnlyFirst) { Stop-Sandbox }
}
if ($FirstShows -gt 0) { $result['first show: frame'] = Median $frames; $result['first show: settled'] = Median $settled }
if ($OnlyFirst) {
    Write-Host ''
    Write-Host "== $Label, ms"
    $result.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-24} {1:N0}" -f $_.Key, $_.Value) }
    return
}
if ($FirstShows -le 0) { $proc = Start-Sandbox @() }

# --- tray: hidden -> shown ---
$frames = @(); $settled = @()
for ($i = 0; $i -lt $Rounds; $i++) {
    Drop 'hide'; Start-Sleep -Milliseconds 1500
    $n = @(Lines).Count; Drop 'show'
    $line = Last $n 'tray show'
    if ($line) { $frames += Field $line 'frame'; $settled += Field $line 'settled' }
    Start-Sleep -Milliseconds 800
}
$result['tray: frame'] = Median $frames; $result['tray: settled'] = Median $settled

# --- taskbar: minimized -> restored ---
$frames = @(); $settled = @()
for ($i = 0; $i -lt $Rounds; $i++) {
    Drop 'minimize'; Start-Sleep -Milliseconds 1500
    $n = @(Lines).Count; Drop 'restore'
    $line = Last $n 'taskbar restore'
    if ($line) { $frames += Field $line 'frame'; $settled += Field $line 'settled' }
    Start-Sleep -Milliseconds 800
}
$result['restore: frame'] = Median $frames; $result['restore: settled'] = Median $settled

# --- second start: a pinned icon or shortcut clicked while V2pn sits in the tray ---
$launch = @(); $frames = @()
$env:V2PN_BACKGROUND = '1'
for ($i = 0; $i -lt $Rounds; $i++) {
    Drop 'hide'; Start-Sleep -Milliseconds 1500
    $n = @(Lines).Count
    $t0 = [Diagnostics.Stopwatch]::GetTimestamp()
    $p2 = Start-Process $exe -WorkingDirectory $Dest -PassThru
    $p2.WaitForExit(10000) | Out-Null
    $exitMs = ([Diagnostics.Stopwatch]::GetTimestamp() - $t0) * 1000.0 / [Diagnostics.Stopwatch]::Frequency
    $line = Last $n 'second start'
    if ($line -and $line -match 'asked=(\d+) freq=(\d+)') {
        $before = ([long]$Matches[1] - $t0) * 1000.0 / [long]$Matches[2]
        $launch += $before; $frames += $before + (Field $line 'frame')
        Write-Host ("    new process until the signal: {0:N0} ms, until it exited: {1:N0} ms" -f $before, $exitMs)
    }
    Start-Sleep -Milliseconds 800
}
$env:V2PN_BACKGROUND = $null
$result['second start: signal'] = Median $launch; $result['second start: frame'] = Median $frames

# --- after Windows took the memory: hidden, working set emptied, shown ---
$frames = @(); $settled = @()
for ($i = 0; $i -lt [math]::Max(3, $Rounds - 2); $i++) {
    Drop 'hide'; Start-Sleep -Milliseconds 1500
    $h = [Diagnostics.Process]::GetProcessById($proc.Id)
    [void][ShowPerfNative]::EmptyWorkingSet($h.Handle)
    Start-Sleep -Milliseconds 1500
    $n = @(Lines).Count; Drop 'show'
    $line = Last $n 'tray show, trimmed'
    if ($line) { $frames += Field $line 'frame'; $settled += Field $line 'settled' }
    Start-Sleep -Milliseconds 800
}
$result['trimmed: frame'] = Median $frames; $result['trimmed: settled'] = Median $settled

$fg1 = [ShowPerfNative]::GetForegroundWindow()
$proc.Refresh()
Write-Host ''
Write-Host "== $Label (median of $Rounds), ms"
$result.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-24} {1:N0}" -f $_.Key, $_.Value) }
Write-Host ("  working set {0} MB, private {1} MB" -f [int]($proc.WorkingSet64 / 1MB), [int]($proc.PrivateMemorySize64 / 1MB))
Write-Host ("  foreground window changed: {0}" -f ($fg0 -ne $fg1))
Stop-Sandbox
