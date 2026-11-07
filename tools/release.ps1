<#
.SYNOPSIS
  Builds V2pn release packages, and optionally tags the version for GitHub to publish.

.DESCRIPTION
  Package (default): builds V2pn and AmazTool for each architecture, adds the cores v2rayN ships
  (2dust/v2rayN-core-bin), and writes dist/release/<version>/V2pn-windows-<arch>.zip with a
  .sha256 next to it. The app refuses an update whose hash does not match that file.

  -Tag: also records the version in v2rayN/Directory.Build.targets, commits it and creates an
  annotated tag v<version> whose message becomes the release notes. Pushing the tag
  (git push origin HEAD v<version>) makes .github/workflows/v2pn-release.yml build the same
  packages and publish the GitHub release that every V2pn checks for updates.

.EXAMPLE
  tools/release.ps1 -Version 1.0.1                  # packages only, nothing is committed
  tools/release.ps1 -Version 1.0.1 -Tag             # bump, commit, tag; then push the tag
  tools/release.ps1 -Version 1.0.1 -Arch x64 -NoCores -OutDir $env:TEMP/rel   # quick test build
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string[]]$Arch = @('x64', 'arm64'),
    [string]$OutDir,
    # Leaves the cores out; for local test packages (updates never overwrite bin/ anyway).
    [switch]$NoCores,
    [switch]$Tag,
    # Release notes for -Tag; by default they are built from the commits since the last tag.
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'v2rayN'
if (-not $OutDir) { $OutDir = Join-Path $repo "dist/release/$Version" }
$OutDir = [IO.Path]::GetFullPath($OutDir)

. (Join-Path $PSScriptRoot '_common.ps1')

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Windows PowerShell's ZipFile.CreateFromDirectory writes "V2pn\V2pn.exe"; AmazTool splits entry
# names on "/" and silently skips everything else, so the update would restart the old version.
function New-V2pnZip([string]$Folder, [string]$Zip) {
    $base = Split-Path -Leaf $Folder
    $archive = [IO.Compression.ZipFile]::Open($Zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $Folder -Recurse -File) {
            $relative = $file.FullName.Substring($Folder.Length).TrimStart('\', '/') -replace '\\', '/'
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "$base/$relative", [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }
}

# The checks AmazTool relies on: one top folder, "/" separators, the app and the updater inside.
function Test-V2pnZip([string]$Zip) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $names = $archive.Entries | ForEach-Object { $_.FullName }
        $bad = $names | Where-Object { $_ -match '\\' -or $_ -notmatch '^V2pn/' }
        if ($bad) { throw "Package entries AmazTool cannot install: $($bad | Select-Object -First 3)" }
        foreach ($required in 'V2pn/V2pn.exe', 'V2pn/AmazTool.exe') {
            if ($names -notcontains $required) { throw "Package lacks $required" }
        }
    }
    finally { $archive.Dispose() }
}

if ($Tag) {
    if (git -C $repo status --porcelain) { throw 'The working tree has uncommitted changes; commit them before tagging a release.' }
    if (git -C $repo tag --list "v$Version") { throw "Tag v$Version already exists." }
}

$dotnet = Find-Dotnet
Write-Host "Using $dotnet"

if ($Tag) {
    Write-Host 'Running tests before tagging...'
    Invoke-Checked $dotnet @('test', '--project', (Join-Path $src 'ServiceLib.Tests/ServiceLib.Tests.csproj'), '-c', 'Release')
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$work = Join-Path $OutDir 'work'

foreach ($a in $Arch) {
    $rid = "win-$a"
    $stageRoot = Join-Path $work $a
    # The zip holds one top-level folder: AmazTool strips the first path segment when it unpacks.
    $stage = Join-Path $stageRoot 'V2pn'
    if (Test-Path $stageRoot) { Remove-Item -Recurse -Force $stageRoot }
    New-Item -ItemType Directory -Force $stage | Out-Null

    Write-Host "== ${rid}: AmazTool"
    Invoke-Checked $dotnet @('publish', (Join-Path $src 'AmazTool/AmazTool.csproj'), '-c', 'Release', '-r', $rid,
        '-p:SelfContained=true', '-p:PublishTrimmed=true', '-p:EnableWindowsTargeting=true', '-v', 'q', '-nologo', '-o', $stage)

    Write-Host "== ${rid}: V2pn $Version"
    Invoke-Checked $dotnet @('publish', (Join-Path $src 'v2rayN.Desktop/v2rayN.Desktop.csproj'), '-c', 'Release', '-r', $rid,
        '-p:SelfContained=true', '-p:EnableWindowsTargeting=true', "-p:V2pnVersion=$Version", '-v', 'q', '-nologo', '-o', $stage)

    Get-ChildItem $stage -Recurse -Include '*.pdb', '*.dbg' | Remove-Item -Force

    if (-not $NoCores) {
        $coreArch = if ($a -eq 'x64') { '64' } else { 'arm64' }
        $coreZip = Join-Path $work "cores-$coreArch.zip"
        if (-not (Test-Path $coreZip)) {
            Write-Host "== ${rid}: cores (2dust/v2rayN-core-bin)"
            Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/2dust/v2rayN-core-bin/raw/refs/heads/master/v2rayN-windows-$coreArch.zip" -OutFile $coreZip
        }
        $coreDir = Join-Path $work "cores-$coreArch"
        if (-not (Test-Path $coreDir)) { Expand-Archive -Path $coreZip -DestinationPath $coreDir }
        # The core zip has a single top folder (v2rayN-windows-64/bin/...); its content goes next to V2pn.exe.
        $inner = Get-ChildItem $coreDir -Directory | Select-Object -First 1
        Copy-Item -Recurse -Force (Join-Path $inner.FullName '*') $stage
    }

    $zipName = "V2pn-windows-$a.zip"
    $zip = Join-Path $OutDir $zipName
    if (Test-Path $zip) { Remove-Item -Force $zip }
    New-V2pnZip -Folder $stage -Zip $zip
    Test-V2pnZip -Zip $zip

    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    # sha256sum format, which V2pnInfo.ParseHash and `sha256sum -c` both read.
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n")
    $size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "== $zipName  $size MB  sha256 $hash"
}

Remove-Item -Recurse -Force $work

if ($Tag) {
    $targets = Join-Path $src 'Directory.Build.targets'
    $text = [IO.File]::ReadAllText($targets)
    $text = [regex]::Replace($text, '(<V2pnVersion Condition="''\$\(V2pnVersion\)'' == ''''">)[^<]*(</V2pnVersion>)', "`${1}$Version`${2}")
    [IO.File]::WriteAllText($targets, $text)

    if (-not $Notes) {
        $last = git -C $repo describe --tags --abbrev=0 --match 'v[0-9]*' 2>$null
        $range = if ($last) { "$last..HEAD" } else { 'HEAD' }
        $own = git -C $repo log --no-merges --format='- %s' $range --grep '^V2pn' 2>$null | ForEach-Object { $_ -replace '^- V2pn: ', '- ' }
        # v2rayN commits merged since the last V2pn release.
        $upstream = 0
        $mergedUpstream = git -C $repo merge-base HEAD upstream/master 2>$null
        if ($mergedUpstream) {
            $since = if ($last) { @("^$last") } else { @() }
            $upstream = [int](git -C $repo rev-list --count --no-merges $mergedUpstream @since)
        }
        $upstreamVersion = ([xml](Get-Content (Join-Path $src 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        $lines = @("V2pn $Version (based on v2rayN $upstreamVersion)", '')
        if ($own) { $lines += $own }
        if ($upstream -gt 0) { $lines += "- Changes from v2rayN: $upstream commits" }
        $Notes = $lines -join "`n"
    }

    git -C $repo add $targets
    git -C $repo commit -q -m "V2pn $Version"
    git -C $repo tag -a "v$Version" -m $Notes
    Write-Host ''
    Write-Host "Tagged v$Version. Publish it with:"
    Write-Host "  git push origin HEAD v$Version"
}

Write-Host "Packages: $OutDir"
