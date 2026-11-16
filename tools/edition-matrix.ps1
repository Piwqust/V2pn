<#
.SYNOPSIS
  Screenshots plain V2pn, or a partner's own build ("Accent", and "Scene" when the theme has a background), in light
  and dark at mini, medium and wide window sizes, off screen, and puts them on one contact sheet: the check to run
  after any change to the partner look. Without -Partner it is plain V2pn, which must look like V2pn whatever
  subscriptions -SeedFrom holds (a partner's included): the debug window's test partner is asked for and refused. With
  -Partner <id> the sandbox is that partner's build (v2pn-partner.txt) and wears partners/<id> (or -Theme: a folder
  with partner.json and its pictures), with Scene when that theme has a gradient or picture background.

.DESCRIPTION
  Builds nothing: it runs the Debug build (or -From) in a sandbox (tools/sandbox.ps1) in background mode with the
  debug window (V2PN_DEBUG=1), and drives that window through UI Automation. It adds the debug window's test partner
  (standing in for the build's own, with -Theme's brand and background) and shows the build as it is ("как в сборке"),
  with the subscription data of -Account laid over. Windows only (UI Automation, System.Drawing).

.EXAMPLE
  tools/edition-matrix.ps1                                   # plain V2pn: no partner may show
  tools/edition-matrix.ps1 -Partner configra                 # Configra's build: Accent and Scene
  tools/edition-matrix.ps1 -Partner configra -Theme tools/demo/themes/aurora
  tools/edition-matrix.ps1 -Partner configra -Theme tools/demo/themes/ember -Looks 'Сцена' -Sizes 'Мини 380×640' -Out $env:TEMP/ember
  tools/edition-matrix.ps1 -Partner configra -Account 'ничего' -Out $env:TEMP/noplan
#>
param(
    [string]$From = (Join-Path $PSScriptRoot '..\v2rayN\v2rayN.Desktop\bin\Debug\net10.0'),
    [string]$Dest = (Join-Path $env:TEMP 'v2pn-matrix'),
    [string]$Out = (Join-Path $env:TEMP 'v2pn-matrix-shots'),
    # The partner whose own build to shoot; without it, plain V2pn.
    [ValidatePattern('^[a-z0-9][a-z0-9-]{1,31}$')][string]$Partner,
    # V2pn (plain V2pn), Акцент (the partner's colour, logo and card) and Сцена (its background and plan; needs a theme
    # with one). By default V2pn without -Partner, Акцент and Сцена with it.
    [string[]]$Looks,
    [string[]]$Modes = @('Тёмный', 'Светлый'),
    [string[]]$Sizes = @('Мини 380×640', 'Среднее 820×700', 'Широкое 1280×800'),
    # A provider theme to show: a folder with partner.json ("brand", "theme") and its pictures.
    [string]$Theme,
    # What the partner's panel says, as the debug window lays it over: 'всё (API)', 'только панель', 'заканчивается', 'ничего'…
    [string]$Account = 'всё (API)',
    # guiConfigs to start from (servers, subscriptions); a fresh install would open the walkthrough.
    [string]$SeedFrom = (Join-Path $env:TEMP 'v2pn-partner\guiConfigs')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
if (-not $Looks) { $Looks = if ($Partner) { @('Акцент', 'Сцена') } else { @('V2pn') } }
if ($Partner -and -not $Theme -and (Test-Path (Join-Path $PSScriptRoot "..\partners\$Partner\partner.json"))) { $Theme = Join-Path $PSScriptRoot "..\partners\$Partner" }
$sandbox = Join-Path $PSScriptRoot 'sandbox.ps1'
$shot = Join-Path $PSScriptRoot 'shot.ps1'
New-Item -ItemType Directory -Force $Out | Out-Null

& $sandbox -Dest $Dest -Stop | Out-Null
if ($Partner) { & $sandbox -From $From -Dest $Dest -Partner $Partner | Out-Null } else { & $sandbox -From $From -Dest $Dest | Out-Null }
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
# Plain V2pn refuses the test partner (with a toast): what is shot is what a person with a partner's subscription sees.
Invoke-Debug 'Добавить тестового'
Start-Sleep -Seconds 2
Invoke-Debug $Account
if (-not $Partner) { Start-Sleep -Seconds 7 }  # the refusal's toast goes first

# Scene needs a theme with a background; without -Theme the test partner has none. Plain V2pn has no partner look.
$looksToShoot = $Looks | Where-Object { $_ -in @('V2pn', 'Акцент', 'Сцена') -and ($Theme -or $_ -ne 'Сцена') -and ($Partner -or $_ -eq 'V2pn') }
$themeName = if ($Theme) { Split-Path -Leaf (Resolve-Path $Theme).Path } else { $null }
$build = if ($Partner) { $Partner } else { 'plain' }
# The partner's own folder adds nothing to the names; another theme is named.
if ($themeName -eq $Partner) { $themeName = $null }
$files = @()
foreach ($look in $looksToShoot) {
    # The build as it is: in plain V2pn that is V2pn whatever is selected; the other buttons only add the background.
    switch ($look) {
        'V2pn' { Invoke-Debug 'как в сборке' }
        'Акцент' { Invoke-Debug 'как в сборке'; Invoke-Debug 'без фона' }
        'Сцена' { Invoke-Debug 'как в сборке'; Invoke-Debug 'как в теме' }
    }
    Start-Sleep -Milliseconds 1500
    foreach ($mode in $Modes) {
        Invoke-Debug $mode
        Start-Sleep -Milliseconds 1500
        foreach ($size in $Sizes) {
            Invoke-Debug $size
            Start-Sleep -Milliseconds 2200
            $name = if ($themeName -and $Partner) { "$build-$themeName-$look" } else { "$build-$look" }
            $file = Join-Path $Out ("{0}-{1}-{2}.png" -f $name, $mode, ($size -split ' ')[0])
            & $shot -ProcessId $proc.Id -Out $file -Title 'V2pn*' | Out-Null
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
$sheetFile = Join-Path $Out $(if ($themeName -and $Partner) { "matrix-$build-$themeName.png" } else { "matrix-$build.png" })
$sheet.Save($sheetFile, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
Write-Host "$($files.Count) shots, sheet: $sheetFile"
