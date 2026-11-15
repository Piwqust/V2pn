<#
.SYNOPSIS
  Screenshots both partner looks (Pass and Neon) in light and dark at mini, medium and wide window sizes, off screen,
  and puts them on one contact sheet: the check to run after any change to the looks. With -Theme it shows a provider
  theme (a folder with partner.json and its pictures): the Pass and Neon with each of its Connect buttons.

.DESCRIPTION
  Builds nothing: it runs the Debug build (or -From) in a sandbox (tools/sandbox.ps1) in background mode with the
  debug window (V2PN_DEBUG=1), and drives that window through UI Automation. With no verified partner in the sandbox it
  adds the debug window's test partner (every look, no pictures). Windows only (UI Automation, System.Drawing).

.EXAMPLE
  tools/edition-matrix.ps1
  tools/edition-matrix.ps1 -Looks 'Неон' -Sizes 'Мини 380×640' -Out $env:TEMP/matrix
  tools/edition-matrix.ps1 -Theme tools/demo/themes/aurora
  tools/edition-matrix.ps1 -Theme tools/demo/themes/pine -Connects toggle -Out $env:TEMP/pine
#>
param(
    [string]$From = (Join-Path $PSScriptRoot '..\v2rayN\v2rayN.Desktop\bin\Debug\net10.0'),
    [string]$Dest = (Join-Path $env:TEMP 'v2pn-matrix'),
    [string]$Out = (Join-Path $env:TEMP 'v2pn-matrix-shots'),
    [string[]]$Looks = @('Пропуск', 'Неон'),
    [string[]]$Modes = @('Тёмный', 'Светлый'),
    [string[]]$Sizes = @('Мини 380×640', 'Среднее 820×700', 'Широкое 1280×800'),
    # A provider theme to show: a folder with partner.json ("brand", "theme") and its pictures. The Pass is shown
    # once, Neon with each of -Connects.
    [string]$Theme,
    [string[]]$Connects = @('round', 'toggle'),
    # guiConfigs to start from (servers, subscriptions, a verified partner); a fresh install would open the walkthrough.
    [string]$SeedFrom = (Join-Path $env:TEMP 'v2pn-partner\guiConfigs')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$sandbox = Join-Path $PSScriptRoot 'sandbox.ps1'
$shot = Join-Path $PSScriptRoot 'shot.ps1'
New-Item -ItemType Directory -Force $Out | Out-Null

& $sandbox -Dest $Dest -Stop | Out-Null
& $sandbox -From $From -Dest $Dest | Out-Null
if (Test-Path $SeedFrom) {
    Copy-Item -Recurse -Force (Join-Path $SeedFrom '*') (Join-Path $Dest 'guiConfigs')
    # The sandbox's own ports and proxy settings, over the copied config.
    & $sandbox -Dest $Dest | Out-Null
}
$env:V2PN_DEBUG = '1'
if ($Theme) { $env:V2PN_DEBUG_THEME = (Resolve-Path $Theme).Path }
try { & $sandbox -Dest $Dest -Launch | Out-Null } finally { Remove-Item Env:V2PN_DEBUG; Remove-Item Env:V2PN_DEBUG_THEME -ErrorAction SilentlyContinue }

$proc = $null
for ($i = 0; $i -lt 40 -and -not $proc; $i++) {
    Start-Sleep -Milliseconds 500
    $proc = Get-Process -Name V2pn -ErrorAction SilentlyContinue | Where-Object { try { $_.Path.StartsWith($Dest, [StringComparison]::OrdinalIgnoreCase) } catch { $false } } | Select-Object -First 1
}
if (-not $proc) { throw 'V2pn did not start' }

$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$byPid = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$byName = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Отладка V2pn')
$debug = $null
for ($i = 0; $i -lt 40 -and -not $debug; $i++) {
    Start-Sleep -Milliseconds 500
    foreach ($w in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $byPid)) {
        $debug = if ($w.Current.Name -eq 'Отладка V2pn') { $w } else { $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $byName) }
        if ($debug) { break }
    }
}
if (-not $debug) { throw 'The debug window did not open (a release build without developer mode?)' }

function Invoke-Debug([string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $el = $debug.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $el) { Write-Warning "No '$name' in the debug window"; return }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

Start-Sleep -Seconds 4
Invoke-Debug 'Сбросить всё'
Invoke-Debug 'Добавить тестового'
Start-Sleep -Seconds 2
# The test partner's intro is in the way: open its look, which closes the intro.
Invoke-Debug 'партнёр из списка выше'
Start-Sleep -Seconds 2

if ($Theme) {
    # A theme's blocks show what the subscription has: lay the debug window's full data over it (plan, traffic,
    # announcement, banner, the provider's check of its servers).
    Invoke-Debug 'всё (API)'
    Invoke-Debug '11 из 14 по партнёру'
}
$files = @()
# With a theme, the passes are the Pass and Neon with each Connect: each is picked in the debug window, then shot.
$themeName = if ($Theme) { Split-Path -Leaf (Resolve-Path $Theme).Path } else { $null }
$passes = if ($Theme) { @('Пропуск') + ($Connects | ForEach-Object { "кнопка $_" }) } else { $Looks }
foreach ($look in $passes) {
    Invoke-Debug $look
    Start-Sleep -Milliseconds 1500
    foreach ($mode in $Modes) {
        # The Pass keeps plain V2pn's mode; one pass is enough.
        if ($look -eq 'Пропуск' -and $mode -ne $Modes[0]) { continue }
        Invoke-Debug $mode
        Start-Sleep -Milliseconds 1500
        foreach ($size in $Sizes) {
            Invoke-Debug $size
            Start-Sleep -Milliseconds 2200
            $name = if ($Theme) { "$themeName-$($look -replace ' ', '-')" } else { $look }
            $file = Join-Path $Out ("{0}-{1}-{2}.png" -f $name, $mode, ($size -split ' ')[0])
            & $shot -ProcessId $proc.Id -Out $file -Title '* — *' | Out-Null
            $files += [pscustomobject]@{ File = $file; Label = "$name · $mode · $(($size -split ' ')[0])" }
        }
    }
}
& $sandbox -Dest $Dest -Stop | Out-Null

# One sheet: a row per look and mode, the sizes side by side, each scaled to the same height.
$rowHeight = 360
$sheetRows = New-Object System.Collections.Generic.List[object]
$width = 0
foreach ($row in ($files | Group-Object { ($_.Label -split ' · ')[0..1] -join ' · ' })) {
    $images = New-Object System.Collections.Generic.List[object]
    $rowWidth = 0
    foreach ($f in $row.Group) {
        $img = [System.Drawing.Image]::FromFile($f.File)
        $w = [int]($img.Width * $rowHeight / $img.Height)
        $images.Add([pscustomobject]@{ Image = $img; Width = $w; Label = $f.Label })
        $rowWidth += $w + 12
    }
    if ($rowWidth -gt $width) { $width = $rowWidth }
    $sheetRows.Add($images)
}
$sheet = New-Object System.Drawing.Bitmap $width, (($rowHeight + 30) * $sheetRows.Count)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::White)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = New-Object System.Drawing.Font 'Segoe UI', 11
$y = 0
foreach ($images in $sheetRows) {
    $x = 0
    foreach ($item in $images) {
        $g.DrawString($item.Label, $font, [System.Drawing.Brushes]::Black, $x + 2, $y + 4)
        $g.DrawImage($item.Image, $x, $y + 26, $item.Width, $rowHeight)
        $x += $item.Width + 12
        $item.Image.Dispose()
    }
    $y += $rowHeight + 30
}
$g.Dispose()
$sheetFile = Join-Path $Out $(if ($Theme) { "matrix-$themeName.png" } else { 'matrix.png' })
$sheet.Save($sheetFile, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Host "$($files.Count) shots, sheet: $sheetFile"
