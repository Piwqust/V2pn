<#
.SYNOPSIS
  Measures a V2pn build: start time, CPU at rest (window open and minimized) and memory, empty
  and with many servers.

.DESCRIPTION
  Runs the build in a sandbox (tools/sandbox.ps1, system proxy untouched), loads -Servers servers
  from a local made-up subscription (tools/fake_sub.py, TEST-NET addresses only) and prints one
  table. CPU is "% of one core" averaged over -Seconds.

.EXAMPLE
  tools/perf.ps1
  tools/perf.ps1 -From dist/V2pn-win-x64 -Servers 3000
#>
param(
    [string]$From = (Join-Path (Split-Path -Parent $PSScriptRoot) 'v2rayN/v2rayN.Desktop/bin/Release/net10.0'),
    [int]$Servers = 1000,
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'v2pn-perf'),
    [int]$Port = 18780,
    [int]$Seconds = 20
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]
$dir = Join-Path $WorkDir 'app'
$rows = @()

function Find-Element([int]$processId, [string]$automationId, [string]$name, [int]$tries = 100) {
    $byProcess = New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $byWhat = if ($automationId) { New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId) }
    else { New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $name) }
    $cond = New-Object Windows.Automation.AndCondition($byProcess, $byWhat)
    for ($i = 0; $i -lt $tries; $i++) {
        $el = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 100
    }
    throw "UI element '$automationId$name' not found"
}

function Invoke-Element($el) {
    $pattern = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) { $pattern.Expand() }
    else { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 500
}

function Start-Measured {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process (Join-Path $dir 'V2pn.exe') -WorkingDirectory $dir -PassThru
    [void](Find-Element $p.Id 'btnAppMenu')
    $sw.Stop()
    return @{ Process = $p; StartMs = $sw.ElapsedMilliseconds }
}

function Measure-Rest([Diagnostics.Process]$p, [string]$label) {
    Start-Sleep 5
    $p.Refresh()
    $cpu0 = $p.TotalProcessorTime
    $t0 = Get-Date
    Start-Sleep $Seconds
    $p.Refresh()
    $cpu = ($p.TotalProcessorTime - $cpu0).TotalMilliseconds / ((Get-Date) - $t0).TotalMilliseconds * 100
    Write-Host ("  {0}: CPU {1:N2}%, RAM {2} MB" -f $label, $cpu, [int]($p.WorkingSet64 / 1MB))
    return [pscustomobject]@{
        State      = $label
        'CPU %'    = [math]::Round($cpu, 2)
        'RAM MB'   = [int]($p.WorkingSet64 / 1MB)
        'Private MB' = [int]($p.PrivateMemorySize64 / 1MB)
    }
}

function Minimize([int]$processId) {
    Invoke-Element (Find-Element $processId 'btnMinimize')
}

function Stop-App { & (Join-Path $PSScriptRoot 'sandbox.ps1') -Dest $dir -Stop | Out-Null }

# --- empty ------------------------------------------------------------------------------------
& (Join-Path $PSScriptRoot 'sandbox.ps1') -From $From -Dest $dir | Out-Null
$run = Start-Measured
Stop-App
# The first start creates the database and caches; measure the second, as a person sees it daily.
$run = Start-Measured
$startEmpty = $run.StartMs
Write-Host "  start, empty: $startEmpty ms"
$rows += Measure-Rest $run.Process 'empty, window open'
Minimize $run.Process.Id
$rows += Measure-Rest $run.Process 'empty, minimized'
Stop-App

# --- with servers -----------------------------------------------------------------------------
$sub = Start-Process python -ArgumentList @((Join-Path $PSScriptRoot 'fake_sub.py'), $Servers, $Port) -PassThru -WindowStyle Hidden
try {
    Start-Sleep 1
    $db = Join-Path $dir 'guiConfigs/guiNDB.db'
    $insert = @"
import sqlite3, sys
c = sqlite3.connect(sys.argv[1])
c.execute("DELETE FROM SubItem WHERE Id='perf'")
c.execute("INSERT INTO SubItem (Id, Remarks, Url, Enabled, UserAgent, Sort, AutoUpdateInterval, UpdateTime) VALUES ('perf', 'Perf', ?, 1, '', 1, 0, 0)", (sys.argv[2],))
c.commit()
"@
    $insert | python - $db "http://127.0.0.1:$Port/sub"

    $run = Start-Measured
    $p = $run.Process
    # Let the first load settle; a menu opened during it can close again on its own.
    Start-Sleep 3
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $item = $null
    for ($attempt = 0; $attempt -lt 3 -and -not $item; $attempt++) {
        Invoke-Element (Find-Element $p.Id 'btnAppMenu')
        try { $item = Find-Element $p.Id 'menuSubUpdate' -tries 30 } catch { $item = $null }
    }
    if (-not $item) { throw 'The subscription menu did not open.' }
    $sw.Restart()
    Invoke-Element $item
    $count = 0
    # Windows quoting mangles inline python, so the query lives in a file.
    $countPy = Join-Path $WorkDir 'count.py'
    Set-Content -Encoding ascii $countPy "import sqlite3,sys`nprint(sqlite3.connect(sys.argv[1]).execute(""SELECT COUNT(*) FROM ProfileItem WHERE Subid='perf'"").fetchone()[0])"
    while ($sw.Elapsed.TotalSeconds -lt 120) {
        $count = [int](python $countPy $db)
        if ($count -ge $Servers) { break }
        Start-Sleep -Milliseconds 300
    }
    $importMs = $sw.ElapsedMilliseconds
    $rows += Measure-Rest $p "$count servers, window open"
    Minimize $p.Id
    $rows += Measure-Rest $p "$count servers, minimized"
    Stop-App

    $run = Start-Measured
    $startFull = $run.StartMs
    Stop-App
}
finally {
    if (-not $sub.HasExited) { $sub.Kill() }
}

Write-Host ''
Write-Host "Start, empty:            $startEmpty ms"
Write-Host "Start, $count servers:   $startFull ms"
Write-Host "Import of $count servers: $importMs ms (menu click to all stored)"
$rows | Format-Table -AutoSize | Out-String | Write-Host
