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
    damaged    a package that does not match the signed manifest: V2pn must refuse it and keep running.
    unsigned   a release signed with a key V2pn does not know: it must not even be downloaded.
    crash      a new version that exits right after starting: the installer must put the old one back, start it,
               and V2pn must not try that version again by itself.
    rollback   unpack -Old over an installed -New (a person going back by hand): the settings stay.

  The packages are built to look for updates on the local server and to trust a key made for this
  run, so nothing is published and the real release key is not used. Needs python and the .NET 10
  SDK. Takes about 6 minutes.

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
    [ValidateSet('manual', 'auto', 'damaged', 'unsigned', 'crash', 'rollback')][string[]]$Only = @('manual', 'auto', 'damaged', 'unsigned', 'crash', 'rollback')
)

$ErrorActionPreference = 'Stop'
# Every V2pn this script starts (and restarts) keeps its windows off the screen; see tools/sandbox.ps1.
$env:V2PN_BACKGROUND = '1'
$rel = Join-Path $WorkDir 'releases'
$shots = Join-Path $WorkDir 'shots'
New-Item -ItemType Directory -Force $rel, $shots | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]
$results = [ordered]@{}

. (Join-Path $PSScriptRoot '_common.ps1')
$dotnet = Find-Dotnet
$source = "http://127.0.0.1:$Port/releases"

# A key pair for this run only: the packages are built to trust it instead of the release key.
$keyFile = Join-Path $WorkDir 'test-signing-key.pem'
$publicFile = "$keyFile.pub"
if (-not (Test-Path $keyFile) -or -not (Test-Path $publicFile)) {
    Remove-Item -Force $keyFile, $publicFile -ErrorAction SilentlyContinue
    $public = & $dotnet run (Join-Path $PSScriptRoot 'manifest.cs') -- --new-key $keyFile | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { throw 'Could not make a test signing key.' }
    Set-Content -Path $publicFile -Value $public -Encoding ascii
    # Packages built for another key are useless now.
    $SkipBuild = $false
}
$publicKey = (Get-Content $publicFile -Raw).Trim()

function Build-Package([string]$version, [string]$into) {
    & (Join-Path $PSScriptRoot 'release.ps1') -Version $version -Arch x64 -NoCores -OutDir $into `
        -UpdateSources $source -UpdateKeys $publicKey -SigningKey $keyFile | Out-Null
}

# Lists and signs whatever is in a release folder. -Key signs with another key than the trusted one;
# the tool checks its own signature, so it is told which public key that one belongs to.
function Sign-Release([string]$version, [string]$Key = $keyFile) {
    $arguments = @('run', (Join-Path $PSScriptRoot 'manifest.cs'), '--', (Join-Path $rel $version), $version,
        '--repo', (Split-Path -Parent $PSScriptRoot), '--key', $Key, '--public', (Get-Content "$Key.pub" -Raw).Trim())
    & $dotnet @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not sign the $version test release." }
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Copies a package, replacing the content of some entries (name -> text or bytes) and adding new ones.
function Copy-PackageWith([string]$from, [string]$to, [hashtable]$replace) {
    $in = [IO.Compression.ZipFile]::OpenRead($from)
    $out = [IO.Compression.ZipFile]::Open($to, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $seen = @{}
        foreach ($entry in $in.Entries) {
            $seen[$entry.FullName] = $true
            $stream = $out.CreateEntry($entry.FullName).Open()
            try {
                if ($replace.ContainsKey($entry.FullName)) {
                    $bytes = $replace[$entry.FullName]
                    if ($bytes -is [string]) { $bytes = [Text.Encoding]::UTF8.GetBytes($bytes) }
                    $stream.Write($bytes, 0, $bytes.Length)
                }
                else {
                    $content = $entry.Open()
                    try { $content.CopyTo($stream) } finally { $content.Dispose() }
                }
            }
            finally { $stream.Dispose() }
        }
        foreach ($name in $replace.Keys | Where-Object { -not $seen.ContainsKey($_) }) {
            $stream = $out.CreateEntry($name).Open()
            try {
                $bytes = $replace[$name]
                if ($bytes -is [string]) { $bytes = [Text.Encoding]::UTF8.GetBytes($bytes) }
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $out.Dispose(); $in.Dispose() }
}

function Find-Element([int]$processId, [string]$automationId, [int]$tries = 50) {
    # Only this process's own windows are searched (menus and flyouts are separate top-level
    # windows). Walking the whole desktop hangs for as long as any other program's window does
    # not answer.
    $own = New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $wanted = New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)
    for ($i = 0; $i -lt $tries; $i++) {
        foreach ($window in $AE::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $own)) {
            $el = $window.FindFirst([Windows.Automation.TreeScope]::Subtree, $wanted)
            if ($el) { return $el }
        }
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
    $p = Start-Process (Join-Path $dir 'V2pn.exe') -WorkingDirectory $dir -PassThru
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
        # What the running app says about itself, not only what the file on disk is.
        Open-UpdateView $now.Id
        [void](Wait-Until { (Get-Text $now.Id 'txtAppVersion') -like "*$New*" } 10)
        Assert $scenario ((Get-Text $now.Id 'txtAppVersion') -like "*$New*") "the app reports '$(Get-Text $now.Id 'txtAppVersion')'"
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
        Write-Host 'damaged: a package that does not match the signed manifest'
        $bad = '9.9.9'
        $badDir = Join-Path $rel $bad
        New-Item -ItemType Directory -Force $badDir | Out-Null
        Copy-Item (Join-Path $rel "$New/V2pn-windows-x64*.zip") $badDir -Force
        Sign-Release $bad
        # Changed after signing, as on a mirror that serves something else or a cut-off download.
        foreach ($zip in Get-ChildItem $badDir -Filter '*.zip') { Add-Content -Path $zip.FullName -Value 'tampered' }
        try {
            $dir = Install-Sandbox 'damaged' $Old $false
            $p = Start-V2pn $dir
            Open-UpdateView $p.Id
            [void](Wait-Until { (Get-Text $p.Id 'txtAppState') -like "*$bad*" } 20)
            Invoke-Element $p.Id 'btnAppAction'
            $failed = Wait-Until { (Get-Text $p.Id 'txtAppState') -match 'Could not|\u041d\u0435 \u0443\u0434' } 60
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

    if ($Only -contains 'unsigned') {
        Write-Host 'unsigned: a release signed with a key V2pn does not know'
        $bad = '9.9.7'
        $badDir = Join-Path $rel $bad
        New-Item -ItemType Directory -Force $badDir | Out-Null
        Copy-Item (Join-Path $rel "$New/V2pn-windows-x64*.zip") $badDir -Force
        $stranger = Join-Path $WorkDir 'stranger-key.pem'
        Remove-Item -Force $stranger, "$stranger.pub" -ErrorAction SilentlyContinue
        & $dotnet run (Join-Path $PSScriptRoot 'manifest.cs') -- --new-key $stranger | Select-Object -Last 1 | Set-Content -Path "$stranger.pub" -Encoding ascii
        Sign-Release $bad -Key $stranger
        try {
            $dir = Install-Sandbox 'unsigned' $Old $true
            $p = Start-V2pn $dir
            Open-UpdateView $p.Id
            $failed = Wait-Until { (Get-Text $p.Id 'txtAppState') -match 'Could not check|\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0432' } 40
            Assert 'unsigned' $failed "the card says '$(Get-Text $p.Id 'txtAppState')'"
            Assert 'unsigned' (@(Get-ChildItem (Join-Path $dir 'guiTemps/V2pnUpdate') -File -ErrorAction SilentlyContinue).Count -eq 0) 'nothing was downloaded'
            Assert 'unsigned' ((Version-Of $dir) -eq $Old) "V2pn.exe is still $(Version-Of $dir)"
            Shot $p.Id 'unsigned'
            Stop-All $dir
        }
        finally { Remove-Item -Recurse -Force $badDir }
    }

    if ($Only -contains 'crash') {
        Write-Host 'crash: a new version that does not start is rolled back'
        $bad = '9.9.8'
        $badDir = Join-Path $rel $bad
        New-Item -ItemType Directory -Force $badDir | Out-Null
        # A "new version" that starts and leaves at once: the updater's own program under the app's
        # name (without arguments it prints its help and exits after five seconds).
        $package = [IO.Compression.ZipFile]::OpenRead((Join-Path $rel "$New/V2pn-windows-x64.zip"))
        try {
            $memory = New-Object IO.MemoryStream
            $content = $package.GetEntry('V2pn/AmazTool.exe').Open()
            try { $content.CopyTo($memory) } finally { $content.Dispose() }
            [byte[]]$quitter = $memory.ToArray()
        }
        finally { $package.Dispose() }
        foreach ($zip in Get-ChildItem (Join-Path $rel $New) -Filter 'V2pn-windows-x64*.zip') {
            Copy-PackageWith $zip.FullName (Join-Path $badDir $zip.Name) @{ 'V2pn/V2pn.exe' = $quitter; 'V2pn/ru/marker-from-bad-version.txt' = 'x' }
        }
        Sign-Release $bad
        try {
            $dir = Install-Sandbox 'crash' $Old $false
            $oldHash = (Get-FileHash (Join-Path $dir 'V2pn.exe')).Hash
            $p = Start-V2pn $dir
            Open-UpdateView $p.Id
            [void](Wait-Until { (Get-Text $p.Id 'txtAppState') -like "*$bad*" } 20)
            Invoke-Element $p.Id 'btnAppAction'
            $marker = Join-Path $dir 'guiTemps/V2pnUpdate/failed.txt'
            [void](Wait-Until { (Test-Path $marker) -and ($n = Get-V2pn $dir) -and $n.Id -ne $p.Id } 90)
            Start-Sleep 3
            $now = Get-V2pn $dir
            Assert 'crash' ($null -ne $now -and $now.Id -ne $p.Id) 'the previous version was started again'
            Assert 'crash' ((Get-FileHash (Join-Path $dir 'V2pn.exe')).Hash -eq $oldHash) 'V2pn.exe is the old file again'
            Assert 'crash' ((Version-Of $dir) -eq $Old) "V2pn.exe is $(Version-Of $dir)"
            Assert 'crash' ((Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $bad)) "failed.txt names $bad"
            Assert 'crash' (-not (Test-Path (Join-Path $dir 'ru/marker-from-bad-version.txt'))) 'files the bad version added are gone'
            Assert 'crash' (Test-Path (Join-Path $dir 'guiConfigs/keep-me.txt')) 'guiConfigs kept'
            Assert 'crash' (Test-Path (Join-Path $dir 'bin/xray/keep-me.txt')) 'bin kept'
            if ($now) {
                [void](Find-Element $now.Id 'btnAppMenu')
                Open-UpdateView $now.Id
                $told = Wait-Until { (Get-Text $now.Id 'txtAppState') -match 'did not start|\u043d\u0435 \u0437\u0430\u043f\u0443\u0441\u0442' } 40
                Assert 'crash' $told "the card says '$(Get-Text $now.Id 'txtAppState')'"
                Shot $now.Id 'crash-rolled-back'
            }
            Get-Content (Join-Path $dir 'guiTemps/V2pnUpdate/update.log') -ErrorAction SilentlyContinue | Select-Object -Last 6 | ForEach-Object { Write-Host "    log: $_" }
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
