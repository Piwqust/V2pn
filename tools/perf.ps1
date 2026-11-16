<#
.SYNOPSIS
  Measures a V2pn (or plain v2rayN) build: start time, CPU at rest with the window open and
  minimized, and memory, empty and with many servers.

.DESCRIPTION
  Works the same for V2pn and the original v2rayN, so the two can be compared: nothing depends on
  V2pn's own buttons. The build runs in a sandbox (tools/sandbox.ps1: system proxy untouched, own
  port). Servers come from a local made-up subscription (tools/fake_sub.py, TEST-NET addresses
  only) that the app's own scheduler fetches about a minute after start.

  CPU is "% of one core" averaged over -Seconds, starting -Settle seconds after the state is
  reached. Start time is from launch until the main window shows its content.

.EXAMPLE
  tools/perf.ps1
  tools/perf.ps1 -From <upstream checkout>/v2rayN/v2rayN.Desktop/bin/Release/net10.0 -Label v2rayN
#>
param(
    [string]$From = (Join-Path (Split-Path -Parent $PSScriptRoot) 'v2rayN/v2rayN.Desktop/bin/Release/net10.0'),
    [string]$Label,
    [int]$Servers = 1000,
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'v2pn-perf'),
    [int]$Port = 18780,
    [int]$Seconds = 20,
    [int]$Settle = 10,
    # Only the empty measurements: a quick A/B check of a change.
    [switch]$EmptyOnly,
    # Start times are the median of this many starts: on a busy computer one start can take three times another.
    [int]$Starts = 5
)

$ErrorActionPreference = 'Stop'
# Every V2pn this script starts (and restarts) keeps its windows off the screen; see tools/sandbox.ps1.
$env:V2PN_BACKGROUND = '1'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PerfWin {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@
$AE = [Windows.Automation.AutomationElement]
$dir = Join-Path $WorkDir 'app'
$exeName = @('V2pn.exe', 'v2rayN.exe') | Where-Object { Test-Path (Join-Path $From $_) } | Select-Object -First 1
if (-not $exeName) { throw "Neither V2pn.exe nor v2rayN.exe in $From" }
if (-not $Label) { $Label = [IO.Path]::GetFileNameWithoutExtension($exeName) }
$rows = @()

function Wait-Window([Diagnostics.Process]$p, [int]$seconds = 60) {
    # "Shows its content": the window's UI tree has its controls, not only the frame.
    $cond = New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $win = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
            if ($win) {
                $buttons = $win.FindAll([Windows.Automation.TreeScope]::Descendants,
                    (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
                if ($buttons.Count -ge 5) { return }
            }
        }
        catch {
            # UI Automation fails now and then while the window is still being built; the next look succeeds.
        }
        Start-Sleep -Milliseconds 50
    }
    throw 'The main window did not appear.'
}

function Start-App {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process (Join-Path $dir $exeName) -WorkingDirectory $dir -PassThru
    Wait-Window $p
    $sw.Stop()
    return @{ Process = $p; StartMs = $sw.ElapsedMilliseconds }
}

function Measure-Rest([Diagnostics.Process]$p, [string]$state) {
    Start-Sleep $Settle
    $p.Refresh()
    $cpu0 = $p.TotalProcessorTime
    $t0 = Get-Date
    Start-Sleep $Seconds
    $p.Refresh()
    $cpu = ($p.TotalProcessorTime - $cpu0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds * 100
    Write-Host ("  {0}: CPU {1:N2}%, RAM {2} MB" -f $state, $cpu, [int]($p.WorkingSet64 / 1MB))
    return [pscustomobject]@{
        App          = $Label
        State        = $state
        'CPU %'      = [math]::Round($cpu, 2)
        'RAM MB'     = [int]($p.WorkingSet64 / 1MB)
        'Private MB' = [int]($p.PrivateMemorySize64 / 1MB)
    }
}

function Minimize([Diagnostics.Process]$p) {
    $p.Refresh()
    [void][PerfWin]::ShowWindow($p.MainWindowHandle, 6)
}

function Measure-Starts {
    $times = @()
    for ($i = 0; $i -lt [math]::Max(1, $Starts); $i++) {
        $times += (Start-App).StartMs
        Stop-App
    }
    $sorted = $times | Sort-Object
    return $sorted[[int][math]::Floor($sorted.Count / 2)]
}

function Stop-App { & (Join-Path $PSScriptRoot 'sandbox.ps1') -Dest $dir -Stop | Out-Null }

function Invoke-Sql([string]$db, [string]$sql) {
    # Windows quoting mangles inline python, so the statement goes through a file.
    $py = Join-Path $WorkDir 'sql.py'
    Set-Content -Encoding ascii $py "import sqlite3,sys`nc=sqlite3.connect(sys.argv[1])`nr=c.execute(open(sys.argv[2]).read()).fetchall()`nc.commit()`nprint(r[0][0] if r else '')"
    $sqlFile = Join-Path $WorkDir 'stmt.sql'
    Set-Content -Encoding ascii $sqlFile $sql
    python $py $db $sqlFile
}

Write-Host "== $Label ($exeName)"

# --- empty ------------------------------------------------------------------------------------
& (Join-Path $PSScriptRoot 'sandbox.ps1') -From $From -Dest $dir | Out-Null
# The first start creates the database and caches; measure the next one, as a person sees it daily.
$run = Start-App
Start-Sleep 3
Stop-App
$startEmpty = Measure-Starts
Write-Host "  start, empty: $startEmpty ms (median of $Starts)"
$run = Start-App
$rows += Measure-Rest $run.Process 'empty, window open'
Minimize $run.Process
$rows += Measure-Rest $run.Process 'empty, minimized'
Stop-App

if ($EmptyOnly) {
    $rows | Format-Table -AutoSize | Out-String | Write-Host
    return
}

# --- with servers -----------------------------------------------------------------------------
$sub = Start-Process python -ArgumentList @((Join-Path $PSScriptRoot 'fake_sub.py'), $Servers, $Port) -PassThru -WindowStyle Hidden
try {
    Start-Sleep 1
    $db = Join-Path $dir 'guiConfigs/guiNDB.db'
    # Due at once (never updated), then not again for months: the scheduler fetches it one time.
    Invoke-Sql $db "DELETE FROM SubItem WHERE Id='perf'" | Out-Null
    Invoke-Sql $db "INSERT INTO SubItem (Id, Remarks, Url, Enabled, UserAgent, Sort, AutoUpdateInterval, UpdateTime) VALUES ('perf', 'Perf', 'http://127.0.0.1:$Port/sub', 1, '', 1, 100000, 0)" | Out-Null

    $run = Start-App
    $p = $run.Process
    $count = 0
    $deadline = (Get-Date).AddSeconds(150)
    while ((Get-Date) -lt $deadline) {
        $count = [int](Invoke-Sql $db "SELECT COUNT(*) FROM ProfileItem WHERE Subid='perf'")
        if ($count -ge $Servers) { break }
        Start-Sleep 1
    }
    if ($count -lt $Servers) { throw "Only $count of $Servers servers arrived." }
    $rows += Measure-Rest $p "$count servers, window open"
    Minimize $p
    $rows += Measure-Rest $p "$count servers, minimized"
    Stop-App

    $startFull = Measure-Starts
    Write-Host "  start, $count servers: $startFull ms (median of $Starts)"
}
finally {
    if (-not $sub.HasExited) { $sub.Kill() }
}

$size = [math]::Round(((Get-ChildItem $dir -File | Where-Object { $_.Extension -in '.exe', '.dll' } | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host ''
Write-Host "$Label  start empty: $startEmpty ms, start with $count servers: $startFull ms, app files: $size MB"
$rows | Format-Table -AutoSize | Out-String | Write-Host
