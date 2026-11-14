<#
.SYNOPSIS
  Builds V2pn's Windows release packages and the signed release manifest, and optionally tags
  the version for GitHub to publish.

.DESCRIPTION
  Package (default): builds V2pn and AmazTool for each architecture as a release build, adds the
  cores pinned in tools/cores.lock.json, and writes dist/release/<version>/V2pn-windows-<arch>.zip.
  Then tools/manifest.cs lists the packages in release.json and signs it: an installed V2pn
  installs only what that signed manifest names.

  macOS packages are built on a Mac by tools/package-macos.sh (CI does it); put them into the
  same folder before the manifest is made, or run with -ManifestOnly afterwards.

  -Tag: checks the tree, runs the tests, builds the packages, then records the version in
  v2rayN/Directory.Build.targets, commits it and creates the annotated tag v<version>, whose
  message becomes the release notes (the "## <version>" section of CHANGELOG.md when there is
  one). Pushing the tag (git push origin HEAD v<version>) makes
  .github/workflows/v2pn-release.yml build every package and open a draft release.

.EXAMPLE
  tools/release.ps1 -Version 1.0.1                  # packages + manifest, nothing is committed
  tools/release.ps1 -Version 1.0.1 -Tag             # the same, then bump, commit and tag
  tools/release.ps1 -Version 1.0.1 -Arch x64 -NoCores -OutDir $env:TEMP/rel   # quick test build
  tools/release.ps1 -Version 1.0.1 -ManifestOnly    # re-list and re-sign what is in the folder
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string[]]$Arch = @('x64', 'arm64'),
    [string]$OutDir,
    # Leaves the cores out; for local test packages (updates never overwrite bin/ anyway).
    [switch]$NoCores,
    # Packages only. CI uses it: the manifest is made once, after the macOS packages have joined.
    [switch]$NoManifest,
    [switch]$ManifestOnly,
    # Precompiles the app. Measured on 13.11.2026: the window appears in 0.8 s instead of 1.6 s,
    # and every package and update grows by about 30 MB. Off by default.
    [switch]$ReadyToRun,
    [switch]$Tag,
    # Release notes for -Tag; by default the CHANGELOG.md section, else the commits since the last tag.
    [string]$Notes,
    # For tools/update-e2e.ps1 only: packages that look for updates somewhere else and trust a test key.
    [string]$UpdateSources,
    [string]$UpdateKeys,
    [string]$SigningKey
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'v2rayN'
if (-not $OutDir) { $OutDir = Join-Path $repo "dist/release/$Version" }
# Relative to where the script was called from, which .NET's own current directory may not be.
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)

. (Join-Path $PSScriptRoot '_common.ps1')

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# git writes to stderr even when nothing is wrong ("fatal: No names found" before the first tag);
# under ErrorActionPreference=Stop Windows PowerShell turns that into a terminating error.
function Get-GitOutput {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { return (& git.exe -C $repo @args 2>$null) }
    finally { $ErrorActionPreference = $previous }
}

# Windows PowerShell's ZipFile.CreateFromDirectory writes "V2pn\V2pn.exe"; the installer wants "/".
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

# What the installer (AmazTool) relies on: one top folder, "/" separators, the app and the updater inside.
function Test-V2pnZip([string]$Zip) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $names = $archive.Entries | ForEach-Object { $_.FullName }
        $bad = $names | Where-Object { $_ -match '\\' -or $_ -notmatch '^V2pn/' }
        if ($bad) { throw "Package entries the installer cannot place: $($bad | Select-Object -First 3)" }
        foreach ($required in 'V2pn/V2pn.exe', 'V2pn/AmazTool.exe') {
            if ($names -notcontains $required) { throw "Package lacks $required" }
        }
    }
    finally { $archive.Dispose() }
}

# The core bundle pinned for a target, downloaded once per pin and checked against the lock.
function Get-Cores([string]$Target) {
    $lock = Get-Content (Join-Path $PSScriptRoot 'cores.lock.json') -Raw | ConvertFrom-Json
    $entry = $lock.targets.$Target
    if (-not $entry) { throw "tools/cores.lock.json has no cores for $Target; run tools/update-cores.ps1" }
    $cache = Join-Path ([IO.Path]::GetTempPath()) "v2pn-cores-$($lock.commit)"
    New-Item -ItemType Directory -Force $cache | Out-Null
    $zip = Join-Path $cache (Split-Path -Leaf $entry.url)
    if (-not (Test-Path $zip)) {
        Write-Host "   downloading $($entry.url)"
        Invoke-WebRequest -UseBasicParsing -Uri $entry.url -OutFile "$zip.part"
        Move-Item -Force "$zip.part" $zip
    }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $entry.sha256) {
        Remove-Item -Force $zip
        throw "The core bundle for $Target does not match tools/cores.lock.json (got $hash). Nothing was packaged."
    }
    return $zip
}

function New-Manifest {
    $arguments = @('run', (Join-Path $PSScriptRoot 'manifest.cs'), '--', $OutDir, $Version, '--repo', $repo)
    if ($SigningKey) { $arguments += @('--key', $SigningKey) }
    if ($UpdateKeys) { $arguments += @('--public', $UpdateKeys) }
    Invoke-Checked $dotnet $arguments
}

$dotnet = Find-Dotnet
Write-Host "Using $dotnet"

if ($ManifestOnly) {
    New-Manifest
    Write-Host "Packages: $OutDir"
    return
}

if ($Tag) {
    if (Get-GitOutput status --porcelain) { throw 'The working tree has uncommitted changes; commit them before tagging a release.' }
    $branch = Get-GitOutput rev-parse --abbrev-ref HEAD
    if ($branch -ne 'v2pn') { throw "Releases are tagged on the v2pn branch; this is $branch." }
    if (Get-GitOutput tag --list "v$Version") { throw "Tag v$Version already exists." }
    $lastTag = Get-GitOutput describe --tags --abbrev=0 --match 'v[0-9]*.[0-9]*.[0-9]*'
    if ($lastTag -and ([version]$Version -le [version]$lastTag.TrimStart('v'))) {
        throw "Version $Version is not newer than the last release, $lastTag. Installed copies would never take it."
    }
    Write-Host 'Running tests before tagging...'
    Invoke-Checked $dotnet @('test', '--project', (Join-Path $src 'ServiceLib.Tests/ServiceLib.Tests.csproj'), '-c', 'Release')
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$work = Join-Path $OutDir 'work'

$properties = @('-p:SelfContained=true', '-p:EnableWindowsTargeting=true', "-p:V2pnVersion=$Version", '-p:V2pnChannel=release')
if ($ReadyToRun) { $properties += '-p:PublishReadyToRun=true' }
if ($UpdateSources) { $properties += "-p:V2pnUpdateSources=$UpdateSources" }
if ($UpdateKeys) { $properties += "-p:V2pnUpdateKeys=$UpdateKeys" }

foreach ($a in $Arch) {
    $rid = "win-$a"
    $stageRoot = Join-Path $work $a
    # The zip holds one top-level folder, which the installer maps onto the app's folder.
    $stage = Join-Path $stageRoot 'V2pn'
    if (Test-Path $stageRoot) { Remove-Item -Recurse -Force $stageRoot }
    New-Item -ItemType Directory -Force $stage | Out-Null

    Write-Host "== ${rid}: AmazTool"
    Invoke-Checked $dotnet (@('publish', (Join-Path $src 'AmazTool/AmazTool.csproj'), '-c', 'Release', '-r', $rid,
        '-p:PublishTrimmed=true', '-v', 'q', '-nologo', '-o', $stage) + $properties)

    Write-Host "== ${rid}: V2pn $Version"
    Invoke-Checked $dotnet (@('publish', (Join-Path $src 'v2rayN.Desktop/v2rayN.Desktop.csproj'), '-c', 'Release', '-r', $rid,
        '-v', 'q', '-nologo', '-o', $stage) + $properties)

    Get-ChildItem $stage -Recurse -Include '*.pdb', '*.dbg' | Remove-Item -Force

    # The app alone is what an installed V2pn downloads to update itself: cores that are already
    # installed are never replaced by an update, so it does not carry them.
    $updateName = "V2pn-windows-$a-update.zip"
    $updateZip = Join-Path $OutDir $updateName
    if (Test-Path $updateZip) { Remove-Item -Force $updateZip }
    New-V2pnZip -Folder $stage -Zip $updateZip
    Test-V2pnZip -Zip $updateZip
    Write-Host "== $updateName  $([math]::Round((Get-Item $updateZip).Length / 1MB, 1)) MB"

    if (-not $NoCores) {
        Write-Host "== ${rid}: cores"
        $coreZip = Get-Cores $rid
        $coreDir = Join-Path $work "cores-$a"
        if (Test-Path $coreDir) { Remove-Item -Recurse -Force $coreDir }
        Expand-Archive -Path $coreZip -DestinationPath $coreDir
        # The bundle has a single top folder (v2rayN-windows-64/bin/...); its content goes next to V2pn.exe.
        $inner = Get-ChildItem $coreDir -Directory | Select-Object -First 1
        Copy-Item -Recurse -Force (Join-Path $inner.FullName '*') $stage
    }

    $zipName = "V2pn-windows-$a.zip"
    $zip = Join-Path $OutDir $zipName
    if (Test-Path $zip) { Remove-Item -Force $zip }
    New-V2pnZip -Folder $stage -Zip $zip
    Test-V2pnZip -Zip $zip
    $size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "== $zipName  $size MB"
}

Remove-Item -Recurse -Force $work

if (-not $NoManifest) { New-Manifest }

if ($Tag) {
    # Everything that can fail has run. Notes first, then the only change to the tree; if the commit
    # or the tag fails the file is put back, so the script can simply be run again.
    if (-not $Notes) {
        $changelog = Join-Path $repo 'CHANGELOG.md'
        if (Test-Path $changelog) {
            $section = [regex]::Match((Get-Content $changelog -Raw -Encoding UTF8), "(?ms)^## \[?$([regex]::Escape($Version))\]?[^\n]*\n(.*?)(?=^## |\z)")
            if ($section.Success) { $Notes = $section.Groups[1].Value.Trim() }
        }
    }
    if (-not $Notes) {
        $range = if ($lastTag) { "$lastTag..HEAD" } else { 'HEAD' }
        $own = Get-GitOutput log --no-merges --format='- %s' $range --grep '^V2pn' | ForEach-Object { $_ -replace '^- V2pn: ', '- ' }
        $Notes = ($own | Select-Object -First 40) -join "`n"
    }
    $upstreamVersion = ([xml](Get-Content (Join-Path $src 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    # The first line is the tag's subject; the release page shows only what follows it, under its
    # own title, so the notes must not repeat the name. The v2rayN version is added only when the
    # notes do not already say what the release is built on.
    $message = "V2pn $Version`n`n$Notes"
    if ($Notes -notmatch 'v2rayN') { $message += "`n`nBased on v2rayN $upstreamVersion." }
    # Every release page ends the same way: which file is for whom. Without it a visitor faces
    # eighteen files and may take the smaller update package, which has no cores.
    $footer = Join-Path $PSScriptRoot 'release-notes-footer.md'
    if (Test-Path $footer) {
        $message += "`n`n" + ([IO.File]::ReadAllText($footer, [Text.Encoding]::UTF8)).Replace('{version}', $Version).Trim()
    }

    $targets = Join-Path $src 'Directory.Build.targets'
    $original = [IO.File]::ReadAllText($targets)
    $updated = [regex]::Replace($original, '(<V2pnVersion Condition="''\$\(V2pnVersion\)'' == ''''">)[^<]*(</V2pnVersion>)', "`${1}$Version`${2}")
    try {
        if ($updated -ne $original) {
            [IO.File]::WriteAllText($targets, $updated)
            Invoke-Checked 'git.exe' @('-C', $repo, 'add', $targets)
            Invoke-Checked 'git.exe' @('-C', $repo, 'commit', '-q', '-m', "V2pn $Version")
        }
        $messageFile = Join-Path ([IO.Path]::GetTempPath()) "v2pn-tag-$Version.txt"
        [IO.File]::WriteAllText($messageFile, $message)
        Invoke-Checked 'git.exe' @('-C', $repo, 'tag', '-a', "v$Version", '--cleanup=verbatim', '-F', $messageFile)
        Remove-Item -Force $messageFile
    }
    catch {
        if ((Get-GitOutput status --porcelain -- $targets)) {
            Get-GitOutput reset -q -- $targets | Out-Null
            [IO.File]::WriteAllText($targets, $original)
        }
        throw
    }
    Write-Host ''
    Write-Host "Tagged v$Version. Publish it with:"
    Write-Host "  git push --atomic origin HEAD v$Version"
}

Write-Host "Packages: $OutDir"
