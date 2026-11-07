<#
.SYNOPSIS
  End-to-end test of V2pn updating itself, against a local copy of the releases page.

.DESCRIPTION
  Builds two packages (-Old, -New), serves them with tools/fake_releases.py and drives real V2pn
  windows through UI Automation, in sandboxes that never touch the system proxy (tools/sandbox.ps1):

    manual     automatic updates off. Install -Old, open Help > Check Update, press "Download and
               install": V2pn must come back as -New, keeping guiConfigs/ and bin/.
    auto       automatic updates on (the default). Install -Old and wait: the package downloads by
               itself, the header says "Restart to update"; closing and starting V2pn again must
               install -New on the way.
    damaged    a package that does not match its checksum: V2pn must refuse it and keep running.
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
$rel = Join-Path $WorkDir 'releases'
$shots = Join-Path $WorkDir 'shots'
New-Item -ItemType Directory -Force $rel, $shots | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]
$results = [ordered]@{}

function Build-Package([string]$version, [string]$into) {
    & (Join-Path $PSScriptRoot 'release.ps1') -Version $version -Arch x64 -NoCores -OutDir $into | Out-Null
}

function Find-Element([int]$processId, [string]$automationId, [int]$tries = 50) {
    $cond = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)),
        (New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)))
    for ($i = 0; $i -lt $tries; $i++) {
        $el = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 300
    }
    if ($tries -gt 1) { throw "UI element '$automationId' not found" }
    return $null
}

function Invoke-Element([int]$processId, [string]$automationId) {
    $el = Find-Element $processId $automationId
    $pattern = $null
    if ($el.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) { $pattern.Expand() }
    else { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 600
}

function Get-Text([int]$processId, [string]$automationId) {
    $el = Find-Element $processId $automationId 1
    if ($el) { $el.Current.Name } else { $null }
}

function Wait-Until([scriptblock]$condition, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 700
    }
    return $false
}

function Install-Sandbox([string]$name, [string]$version, [bool]$auto) {
    $dir = Join-Path $WorkDir $name
    & (Join-Path $PSScriptRoot 'sandbox.ps1') -From (Join-Path $rel "$version/V2pn-windows-x64.zip") -Dest $dir | Out-Null
    $configFile = Join-Path $dir 'guiConfigs/guiNConfig.json'
    $config = Get-Content $configFile -Raw | ConvertFrom-Json
    $config.UiItem | Add-Member -NotePropertyName V2pnAutoUpdate -NotePropertyValue $auto -Force
    # Cores are not part of these scenarios; nothing should be fetched from GitHub.
    $config | Add-Member -NotePropertyName CheckUpdateItem -NotePropertyValue @{ SelectedCoreTypes = @(); UpdateViaProxy = $false } -Force
    $config | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 $configFile
    New-Item -ItemType Directory -Force (Join-Path $dir 'bin/xray') | Out-Null
    Set-Content (Join-Path $dir 'bin/xray/keep-me.txt') 'core marker'
    Set-Content (Join-Path $dir 'guiConfigs/keep-me.txt') 'data marker'
    return $dir
}

function Start-V2pn([string]$dir, [switch]$NoWait) {
    $env:V2PN_RELEASES_URL = "http://127.0.0.1:$Port/releases"
    $p = Start-Process (Join-Path $dir 'V2pn.exe') -WorkingDirectory $dir -PassThru
    Remove-Item Env:V2PN_RELEASES_URL
    # -NoWait: this start is expected to hand over to AmazTool at once.
    if (-not $NoWait) { [void](Find-Element $p.Id 'btnAppMenu') }
    return $p
}

function Get-V2pn([string]$dir) {
    Get-Process V2pn -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -like "$dir*" } catch { $false } }
}

function Version-Of([string]$dir) { (Get-Item (Join-Path $dir 'V2pn.exe')).VersionInfo.FileVersion }

function Stop-All([string]$dir) { & (Join-Path $PSScriptRoot 'sandbox.ps1') -Dest $dir -Stop | Out-Null }

function Shot([int]$processId, [string]$name) {
    & (Join-Path $PSScriptRoot 'shot.ps1') -ProcessId $processId -Out (Join-Path $shots "$name.png") | Out-Null
}

function Assert([string]$scenario, [bool]$ok, [string]$detail) {
    if (-not $results.Contains($scenario)) { $results[$scenario] = @() }
    $results[$scenario] += [pscustomobject]@{ Ok = $ok; Check = $detail }
    Write-Host ("  [{0}] {1}" -f ($(if ($ok) { 'ok' } else { 'FAIL' })), $detail)
}

function Assert-Updated([string]$scenario, [string]$dir, [int]$oldPid) {
    $now = Get-V2pn $dir
    Assert $scenario ($null -ne $now -and $now.Id -ne $oldPid) 'V2pn restarted after the update'
    Assert $scenario ((Version-Of $dir) -eq $New) "V2pn.exe is $(Version-Of $dir)"
    Assert $scenario (Test-Path (Join-Path $dir 'guiConfigs/keep-me.txt')) 'guiConfigs kept'
    Assert $scenario (Test-Path (Join-Path $dir 'bin/xray/keep-me.txt')) 'bin kept'
    Assert $scenario ((Get-Content (Join-Path $dir 'guiConfigs/guiNConfig.json') -Raw) -match '"LocalPort":\s*20808') 'settings kept'
    if ($now) {
        [void](Find-Element $now.Id 'btnAppMenu')
        $title = $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
            (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $now.Id))).Current.Name
        Assert $scenario ($title -like "*$New*") "window title '$title'"
    }
}

function Open-UpdateView([int]$processId) {
    Invoke-Element $processId 'btnAppMenu'
    # Older builds keep the item under Help.
    if (-not (Find-Element $processId 'menuCheckUpdate' 1)) { Invoke-Element $processId 'menuHelp' }
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
        Write-Host "manual: $Old -> $New with the button, automatic updates off"
        $dir = Install-Sandbox 'manual' $Old $false
        $p = Start-V2pn $dir
        Open-UpdateView $p.Id
        # Opening the window checks; with automatic updates off it only reports the version.
        $found = Wait-Until { (Get-Text $p.Id 'txtAppState') -like "*$New*" } 20
        Assert 'manual' $found "the card offers $New ('$(Get-Text $p.Id 'txtAppState')')"
        Shot $p.Id 'manual-1-available'
        Invoke-Element $p.Id 'btnAppAction'
        [void](Wait-Until { $n = Get-V2pn $dir; $n -and $n.Id -ne $p.Id } 60)
        Start-Sleep 3
        Assert-Updated 'manual' $dir $p.Id
        if ($n = Get-V2pn $dir) { Shot $n.Id 'manual-2-after' }
        Stop-All $dir
    }

    if ($Only -contains 'auto') {
        Write-Host "auto: $Old downloads $New by itself and installs it at the next start"
        $dir = Install-Sandbox 'auto' $Old $true
        $p = Start-V2pn $dir
        # The first check runs 20 s after start.
        $ready = Wait-Until { (Get-Text $p.Id 'txtNewUpdate') -match 'Restart|\u041f\u0435\u0440\u0435\u0437' } 75
        Assert 'auto' $ready "header says '$(Get-Text $p.Id 'txtNewUpdate')'"
        Shot $p.Id 'auto-1-ready'
        Assert 'auto' ((Version-Of $dir) -eq $Old) 'nothing installed while V2pn runs'
        Stop-All $dir
        $p = Start-V2pn $dir -NoWait
        [void](Wait-Until { $n = Get-V2pn $dir; $n -and $n.Id -ne $p.Id } 60)
        Start-Sleep 3
        Assert-Updated 'auto' $dir $p.Id
        if ($n = Get-V2pn $dir) {
            # The new version cleans up the installed package after its own first check.
            [void](Wait-Until { @(Get-ChildItem (Join-Path $dir 'guiTemps/V2pnUpdate') -Filter '*.zip' -ErrorAction SilentlyContinue).Count -eq 0 } 40)
            Assert 'auto' (@(Get-ChildItem (Join-Path $dir 'guiTemps/V2pnUpdate') -Filter '*.zip' -ErrorAction SilentlyContinue).Count -eq 0) 'installed package removed'
            $badge = Find-Element $n.Id 'btnNewUpdate' 1
            Assert 'auto' ($null -eq $badge -or $badge.Current.IsOffscreen) 'no update badge after updating'
            Shot $n.Id 'auto-2-after'
        }
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
            $dir = Install-Sandbox 'damaged' $Old $false
            $p = Start-V2pn $dir
            Open-UpdateView $p.Id
            [void](Wait-Until { (Get-Text $p.Id 'txtAppState') -like "*$bad*" } 20)
            Invoke-Element $p.Id 'btnAppAction'
            $failed = Wait-Until { (Get-Text $p.Id 'txtAppState') -match 'Could not|\u041d\u0435 \u043f\u043e\u043b' } 40
            Assert 'damaged' $failed "the card says '$(Get-Text $p.Id 'txtAppState')'"
            $now = Get-V2pn $dir
            Assert 'damaged' ($null -ne $now -and $now.Id -eq $p.Id) 'V2pn kept running'
            Assert 'damaged' ((Version-Of $dir) -eq $Old) "V2pn.exe is still $(Version-Of $dir)"
            Assert 'damaged' (@(Get-ChildItem (Join-Path $dir 'guiTemps/V2pnUpdate') -File -ErrorAction SilentlyContinue).Count -eq 0) 'damaged download deleted'
            Shot $p.Id 'damaged'
            Stop-All $dir
        }
        finally { Remove-Item -Recurse -Force $badDir }
    }

    if ($Only -contains 'rollback') {
        Write-Host "rollback: unpack $Old over $New by hand"
        $dir = Install-Sandbox 'rollback' $New $false
        $p = Start-V2pn $dir
        Start-Sleep 3
        Stop-All $dir
        & (Join-Path $PSScriptRoot 'sandbox.ps1') -From (Join-Path $rel "$Old/V2pn-windows-x64.zip") -Dest $dir -KeepData | Out-Null
        $p = Start-V2pn $dir
        Start-Sleep 3
        Assert 'rollback' ($null -ne (Get-V2pn $dir)) "$Old starts on $New's data"
        Assert 'rollback' ((Version-Of $dir) -eq $Old) "V2pn.exe is $(Version-Of $dir)"
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
