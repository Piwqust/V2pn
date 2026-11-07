<#
.SYNOPSIS
  End-to-end test of V2pn updating itself, against a local copy of the releases page.

.DESCRIPTION
  Builds two packages (-Old, -New), serves them with tools/fake_releases.py and drives real V2pn
  windows through UI Automation, in sandboxes that never touch the system proxy (tools/sandbox.ps1):

    manual     install -Old, open Help > Check Update, press Update: V2pn must come back as -New,
               keeping guiConfigs/ and bin/.
    auto       install -Old and wait: the scheduled check (a minute after start) must show the
               update badge in the header.
    damaged    serve a package whose checksum does not match: V2pn must refuse it and keep running.
    rollback   unpack -Old over an installed -New (a person going back by hand): the settings stay.

  Nothing is published. Needs python and the .NET 10 SDK. Takes about 4 minutes.

.EXAMPLE
  tools/update-e2e.ps1
  tools/update-e2e.ps1 -SkipBuild -Only manual,damaged
#>
param(
    [string]$Old = '1.0.0',
    [string]$New = '1.0.1',
    [string]$WorkDir = (Join-Path ([IO.Path]::GetTempPath()) 'v2pn-update-e2e'),
    [int]$Port = 18765,
    [switch]$SkipBuild,
    [ValidateSet('manual', 'auto', 'damaged', 'rollback')][string[]]$Only = @('manual', 'auto', 'damaged', 'rollback')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$rel = Join-Path $WorkDir 'releases'
$shots = Join-Path $WorkDir 'shots'
New-Item -ItemType Directory -Force $rel, $shots | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]
$results = [ordered]@{}

function Build-Package([string]$version, [string]$into) {
    & (Join-Path $PSScriptRoot 'release.ps1') -Version $version -Arch x64 -NoCores -OutDir $into | Out-Null
}

function Find-Element([int]$processId, [string]$automationId) {
    $cond = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)),
        (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)))
    for ($i = 0; $i -lt 50; $i++) {
        $el = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 300
    }
    throw "UI element '$automationId' not found"
}

function Invoke-Element([int]$processId, [string]$automationId) {
    $el = Find-Element $processId $automationId
    $pattern = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) { $pattern.Expand() }
    else { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 600
}

function Install-Sandbox([string]$name, [string]$version) {
    $dir = Join-Path $WorkDir $name
    & (Join-Path $PSScriptRoot 'sandbox.ps1') -From (Join-Path $rel "$version/V2pn-windows-x64.zip") -Dest $dir | Out-Null
    $configFile = Join-Path $dir 'guiConfigs/guiNConfig.json'
    $config = Get-Content $configFile -Raw | ConvertFrom-Json
    # Only the app itself, so the test does not download cores from GitHub.
    $config | Add-Member -NotePropertyName CheckUpdateItem -NotePropertyValue @{ SelectedCoreTypes = @('v2rayN'); UpdateViaProxy = $false } -Force
    $config | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 $configFile
    New-Item -ItemType Directory -Force (Join-Path $dir 'bin/xray') | Out-Null
    Set-Content (Join-Path $dir 'bin/xray/keep-me.txt') 'core marker'
    Set-Content (Join-Path $dir 'guiConfigs/keep-me.txt') 'data marker'
    return $dir
}

function Start-V2pn([string]$dir) {
    $env:V2PN_RELEASES_URL = "http://127.0.0.1:$Port/releases"
    $p = Start-Process (Join-Path $dir 'V2pn.exe') -WorkingDirectory $dir -PassThru
    Remove-Item Env:V2PN_RELEASES_URL
    [void](Find-Element $p.Id 'btnAppMenu')
    return $p
}

function Get-V2pn([string]$dir) {
    Get-Process V2pn -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -like "$dir*" } catch { $false } }
}

function Stop-All([string]$dir) { & (Join-Path $PSScriptRoot 'sandbox.ps1') -Dest $dir -Stop | Out-Null }

function Assert([string]$scenario, [bool]$ok, [string]$detail) {
    if (-not $results.Contains($scenario)) { $results[$scenario] = @() }
    $results[$scenario] += [pscustomobject]@{ Ok = $ok; Check = $detail }
    Write-Host ("  [{0}] {1}" -f ($(if ($ok) { 'ok' } else { 'FAIL' })), $detail)
}

function Open-UpdateView([int]$processId) {
    Invoke-Element $processId 'btnAppMenu'
    Invoke-Element $processId 'menuHelp'
    Invoke-Element $processId 'menuCheckUpdate'
}

# --- packages ---------------------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "Building $Old and $New..."
    Remove-Item -Recurse -Force $rel -ErrorAction SilentlyContinue
    Build-Package $Old (Join-Path $rel $Old)
    Build-Package $New (Join-Path $rel $New)
}

# --- server -----------------------------------------------------------------------------------
$server = Start-Process python -ArgumentList @((Join-Path $PSScriptRoot 'fake_releases.py'), "`"$rel`"", $Port) -PassThru -WindowStyle Hidden
Start-Sleep 2

try {
    if ($Only -contains 'manual') {
        Write-Host "manual: $Old -> $New through the Update button"
        $dir = Install-Sandbox 'manual' $Old
        $p = Start-V2pn $dir
        Open-UpdateView $p.Id
        & (Join-Path $PSScriptRoot 'shot.ps1') -ProcessId $p.Id -Out (Join-Path $shots 'manual-1-dialog.png') | Out-Null
        Invoke-Element $p.Id 'btnCheckUpdate'
        $deadline = (Get-Date).AddSeconds(60)
        do { Start-Sleep 1; $now = Get-V2pn $dir } until (((Get-Date) -gt $deadline) -or ($now -and $now.Id -ne $p.Id))
        Start-Sleep 4
        $now = Get-V2pn $dir
        Assert 'manual' ($null -ne $now -and $now.Id -ne $p.Id) 'V2pn restarted after the update'
        $version = (Get-Item (Join-Path $dir 'V2pn.exe')).VersionInfo.FileVersion
        Assert 'manual' ($version -eq $New) "V2pn.exe is $version"
        Assert 'manual' (Test-Path (Join-Path $dir 'guiConfigs/keep-me.txt')) 'guiConfigs kept'
        Assert 'manual' (Test-Path (Join-Path $dir 'bin/xray/keep-me.txt')) 'bin kept'
        Assert 'manual' ((Get-Content (Join-Path $dir 'guiConfigs/guiNConfig.json') -Raw) -match '"LocalPort":\s*20808') 'settings kept'
        if ($now) {
            $title = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
                (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $now.Id))).Current.Name
            Assert 'manual' ($title -like "*$New*") "window title '$title'"
            & (Join-Path $PSScriptRoot 'shot.ps1') -ProcessId $now.Id -Out (Join-Path $shots 'manual-2-after.png') | Out-Null
        }
        Stop-All $dir
    }

    if ($Only -contains 'auto') {
        Write-Host "auto: $Old finds $New on its own (about 70 s)"
        $dir = Install-Sandbox 'auto' $Old
        $p = Start-V2pn $dir
        $badge = $null
        $deadline = (Get-Date).AddSeconds(100)
        do {
            Start-Sleep 5
            $el = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.AndCondition(
                (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)),
                (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'btnNewUpdate')))))
            if ($el -and -not $el.Current.IsOffscreen) { $badge = $el }
        } until ($badge -or (Get-Date) -gt $deadline)
        Assert 'auto' ($null -ne $badge) 'update badge shown without any click'
        & (Join-Path $PSScriptRoot 'shot.ps1') -ProcessId $p.Id -Out (Join-Path $shots 'auto-badge.png') | Out-Null
        Stop-All $dir
    }

    if ($Only -contains 'damaged') {
        Write-Host 'damaged: a package that does not match its checksum'
        $bad = '9.9.9'
        $badDir = Join-Path $rel $bad
        New-Item -ItemType Directory -Force $badDir | Out-Null
        Copy-Item (Join-Path $rel "$New/V2pn-windows-x64.zip") $badDir -Force
        [IO.File]::WriteAllText((Join-Path $badDir 'V2pn-windows-x64.zip.sha256'), ('0' * 64) + "  V2pn-windows-x64.zip`n")
        try {
            $dir = Install-Sandbox 'damaged' $Old
            $p = Start-V2pn $dir
            Open-UpdateView $p.Id
            Invoke-Element $p.Id 'btnCheckUpdate'
            Start-Sleep 15
            $now = Get-V2pn $dir
            Assert 'damaged' ($null -ne $now -and $now.Id -eq $p.Id) 'V2pn kept running, nothing was installed'
            $version = (Get-Item (Join-Path $dir 'V2pn.exe')).VersionInfo.FileVersion
            Assert 'damaged' ($version -eq $Old) "V2pn.exe is still $version"
            & (Join-Path $PSScriptRoot 'shot.ps1') -ProcessId $p.Id -Out (Join-Path $shots 'damaged.png') | Out-Null
            Stop-All $dir
        }
        finally { Remove-Item -Recurse -Force $badDir }
    }

    if ($Only -contains 'rollback') {
        Write-Host "rollback: unpack $Old over $New by hand"
        $dir = Install-Sandbox 'rollback' $New
        $p = Start-V2pn $dir
        Start-Sleep 3
        Stop-All $dir
        & (Join-Path $PSScriptRoot 'sandbox.ps1') -From (Join-Path $rel "$Old/V2pn-windows-x64.zip") -Dest $dir -KeepData | Out-Null
        $p = Start-V2pn $dir
        Start-Sleep 3
        Assert 'rollback' ($null -ne (Get-V2pn $dir)) "$Old starts on $New's data"
        $version = (Get-Item (Join-Path $dir 'V2pn.exe')).VersionInfo.FileVersion
        Assert 'rollback' ($version -eq $Old) "V2pn.exe is $version"
        Assert 'rollback' ((Get-Content (Join-Path $dir 'guiConfigs/guiNConfig.json') -Raw) -match '"LocalPort":\s*20808') 'settings kept'
        Stop-All $dir
    }
}
finally {
    if (-not $server.HasExited) { $server.Kill() }
}

Write-Host ''
$failed = 0
foreach ($k in $results.Keys) {
    $bad = @($results[$k] | Where-Object { -not $_.Ok }).Count
    $failed += $bad
    Write-Host ("{0,-9} {1}" -f $k, $(if ($bad) { "FAILED ($bad)" } else { 'passed' }))
}
Write-Host "Screenshots: $shots"
if ($failed) { exit 1 }
