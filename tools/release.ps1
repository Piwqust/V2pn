<#
.SYNOPSIS
  Builds V2pn's Windows release packages and the signed release manifest, and optionally tags
  the version for GitHub to publish.

.DESCRIPTION
  Package (default): builds V2pn, AmazTool and v2pnctl for each architecture as a release build, adds the
  cores pinned in tools/cores.lock.json, and writes dist/release/<version>/V2pn-windows-<arch>.zip and,
  from the same files, the installer V2pn-windows-<arch>-setup.exe: V2pn's own window (v2rayN/V2pnSetup)
  with the Inno Setup 6 engine (tools/installer/V2pn.iss) appended, which it runs hidden.
  Then tools/manifest.cs lists the packages in release.json and signs it: an installed V2pn
  installs only what that signed manifest names.

  macOS packages are built on a Mac by tools/package-macos.sh (CI does it); put them into the
  same folder before the manifest is made, or run with -ManifestOnly afterwards.

  -Tag: checks the tree, runs the tests, builds the packages, then records the version in
  v2rayN/Directory.Build.targets, commits it and creates the annotated tag v<version>, whose
  message becomes the release notes (the "## <version>" section of CHANGELOG.md when there is
  one). Pushing the tag (git push origin HEAD v<version>) makes
  .github/workflows/v2pn-release.yml build every package and open a draft release.

  -Beta: a beta version, <version>-beta.<n> (-Version 1.5.0 takes the next free n). Its GitHub
  release is a pre-release, still a draft until you publish it: GitHub's "latest" never names it,
  so only copies with Updates -> "Get beta versions" on take it (the hub's beta channel). The
  release after the betas (1.5.0) is a newer version for them, and replaces the beta by itself.

.EXAMPLE
  tools/release.ps1 -Version 1.0.1                  # packages + manifest, nothing is committed
  tools/release.ps1 -Version 1.0.1 -Tag             # the same, then bump, commit and tag
  tools/release.ps1 -Version 1.1.0 -Beta -Tag       # the next beta of 1.1.0 (v1.1.0-beta.1, then .2 ...)
  tools/release.ps1 -Version 1.0.1 -Arch x64 -NoCores -NoInstaller -OutDir $env:TEMP/rel   # quick test build
  tools/release.ps1 -Version 1.0.1 -ManifestOnly    # re-list and re-sign what is in the folder
  tools/release.ps1 -Version 1.0.1-local.202611141320 -Arch x64 -UpdateOnly -OutDir <dir>   # developer mode: signed like a release
                                                    # what the app's developer mode runs
  tools/release.ps1 -Version 1.1.1 -NoManifest -OutDir dist/special/x -Preset https://host/s/abc
                                                    # a special build that adds that subscription at first start
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')][string]$Version,
    [ValidateSet('x64', 'arm64')][string[]]$Arch = @('x64', 'arm64'),
    [string]$OutDir,
    # Leaves the cores out; for local test packages (updates never overwrite bin/ anyway).
    [switch]$NoCores,
    # Only the package an installed V2pn updates itself from, no full zip; the developer mode uses it.
    [switch]$UpdateOnly,
    # Packages only. CI uses it: the manifest is made once, after the macOS packages have joined.
    [switch]$NoManifest,
    [switch]$ManifestOnly,
    # Leaves the app to be compiled at every start (JIT) instead of precompiled (ReadyToRun). Measured
    # on 16.11.2026, medians of five starts: the window appears in 1.6-1.8 s precompiled, 2.4-2.6 s without; the
    # x64 update zip grows from 64 to 76 MB (v2rayN.Desktop.csproj keeps what a start never loads as IL).
    [switch]$NoReadyToRun,
    [switch]$Tag,
    # A beta, <version>-beta.<n>: a pre-release for the copies that take betas (see above).
    [switch]$Beta,
    # Release notes for -Tag; by default the CHANGELOG.md section, else the commits since the last tag.
    [string]$Notes,
    # For tools/update-e2e.ps1 only: packages that look for updates somewhere else and trust a test key.
    [string]$UpdateSources,
    [string]$UpdateKeys,
    [string]$SigningKey,
    # A special build that adds these subscriptions at its first start (v2pn-preset.txt next to V2pn.exe,
    # ServiceLib/V2pn/V2pnPreset.cs). Only the full zip and the installer carry it, never the update zip everyone downloads.
    [string[]]$Preset,
    # No V2pn-windows-<arch>-setup.exe (it needs Inno Setup 6 and takes about 1.5 minutes per architecture).
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'v2rayN'
if ($Preset -and ($Tag -or $UpdateOnly)) { throw '-Preset makes a special build: not with -Tag or -UpdateOnly' }
if ($Beta) {
    if ($Preset -or $UpdateOnly) { throw '-Beta makes a beta release: not with -Preset or -UpdateOnly' }
    if ($Version -match '^\d+\.\d+\.\d+$') {
        # The next beta of that version: 1.5.0-beta.1, then 1.5.0-beta.2 ...
        $taken = @(& git.exe -C $repo tag --list "v$Version-beta.*" 2>$null) | ForEach-Object { if ($_ -match '-beta\.(\d+)$') { [int]$Matches[1] } }
        $Version = "$Version-beta.$(1 + (@($taken) + 0 | Measure-Object -Maximum).Maximum)"
    }
    elseif ($Version -notmatch '^\d+\.\d+\.\d+-beta\.\d+$') { throw "A beta version looks like 1.5.0-beta.1 (or give 1.5.0 for its next beta), not $Version." }
    Write-Host "Beta $Version"
}
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

# Orders versions as V2pn does (SemanticVersion): by the numbers, then a version with a "-" part before the same one
# without it (1.5.0-beta.2 < 1.5.0), the parts after "-" piece by piece, numbers as numbers (beta.2 < beta.10).
function Compare-Version([string]$A, [string]$B) {
    $a1, $a2 = $A.Split('-', 2)
    $b1, $b2 = $B.Split('-', 2)
    $c = ([version]$a1).CompareTo([version]$b1)
    if ($c -ne 0) { return [Math]::Sign($c) }
    if (-not $a2 -or -not $b2) { return [int][bool]$b2 - [int][bool]$a2 }
    $p = $a2.Split('.')
    $q = $b2.Split('.')
    for ($i = 0; $i -lt [Math]::Max($p.Count, $q.Count); $i++) {
        if ($i -ge $p.Count) { return -1 }
        if ($i -ge $q.Count) { return 1 }
        $pn = $p[$i] -match '^\d+$'
        $qn = $q[$i] -match '^\d+$'
        if ($pn -and $qn) { $c = ([long]$p[$i]).CompareTo([long]$q[$i]) }
        elseif ($pn -ne $qn) { $c = if ($pn) { -1 } else { 1 } }
        else { $c = [string]::CompareOrdinal($p[$i], $q[$i]) }
        if ($c -ne 0) { return [Math]::Sign($c) }
    }
    return 0
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

# What the installer (AmazTool) relies on: one top folder, "/" separators, the app and the updater inside;
# and v2pnctl, the command line that comes with the app.
function Test-V2pnZip([string]$Zip) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $names = $archive.Entries | ForEach-Object { $_.FullName }
        $bad = $names | Where-Object { $_ -match '\\' -or $_ -notmatch '^V2pn/' }
        if ($bad) { throw "Package entries the installer cannot place: $($bad | Select-Object -First 3)" }
        foreach ($required in 'V2pn/V2pn.exe', 'V2pn/AmazTool.exe', 'V2pn/v2pnctl.exe') {
            if ($names -notcontains $required) { throw "Package lacks $required" }
        }
    }
    finally { $archive.Dispose() }
}

# The installer people download: the window, then the engine, then what the window reads about it
# (v2rayN/V2pnSetup/Payload.cs): "key=value" lines, the two lengths and the magic "V2PNSTP1".
function New-V2pnSetup([string]$Window, [string]$Engine, [string]$Out, [string]$Arch) {
    $hash = (Get-FileHash -Algorithm SHA256 $Engine).Hash.ToLowerInvariant()
    $header = [Text.Encoding]::UTF8.GetBytes("version=$Version`narch=$Arch`nsha256=$hash`n")
    $engineLength = (Get-Item $Engine).Length
    $output = [IO.File]::Create($Out)
    try {
        foreach ($part in $Window, $Engine) {
            $source = [IO.File]::OpenRead($part)
            try { $source.CopyTo($output) } finally { $source.Dispose() }
        }
        $output.Write($header, 0, $header.Length)
        $output.Write([BitConverter]::GetBytes([long]$engineLength), 0, 8)
        $output.Write([BitConverter]::GetBytes([long]$header.Length), 0, 8)
        $magic = [Text.Encoding]::ASCII.GetBytes('V2PNSTP1')
        $output.Write($magic, 0, $magic.Length)
    }
    finally { $output.Dispose() }
    Remove-Item -Force $Engine
}

function New-Manifest {
    $arguments = @('run', (Join-Path $PSScriptRoot 'manifest.cs'), '--', $OutDir, $Version, '--repo', $repo)
    if ($SigningKey) { $arguments += @('--key', $SigningKey) }
    if ($UpdateKeys) { $arguments += @('--public', $UpdateKeys) }
    Invoke-Checked $dotnet $arguments
}

$dotnet = Find-Dotnet
Write-Host "Using $dotnet"

$iscc = $null
if (-not $NoInstaller -and -not $UpdateOnly -and -not $ManifestOnly) {
    $iscc = Find-Iscc
    if (-not $iscc) { throw 'Inno Setup 6 is needed for the installer: winget install JRSoftware.InnoSetup (or pass -NoInstaller).' }
    Write-Host "Using $iscc"
}

if ($ManifestOnly) {
    New-Manifest
    Write-Host "Packages: $OutDir"
    return
}

if ($Tag) {
    if ($Version.Contains('-') -and -not $Beta) { throw "Releases have plain versions like 1.0.1; $Version is for local builds (a beta takes -Beta)." }
    if (Get-GitOutput status --porcelain) { throw 'The working tree has uncommitted changes; commit them before tagging a release.' }
    $branch = Get-GitOutput rev-parse --abbrev-ref HEAD
    if ($branch -ne 'v2pn') { throw "Releases are tagged on the v2pn branch; this is $branch." }
    if (Get-GitOutput tag --list "v$Version") { throw "Tag v$Version already exists." }
    # The tag before this one, for the notes: the last release; for a beta, the last release or beta.
    $describe = @('describe', '--tags', '--abbrev=0', '--match', 'v[0-9]*.[0-9]*.[0-9]*')
    if (-not $Beta) { $describe += @('--exclude', 'v*-*') }
    $lastTag = Get-GitOutput @describe
    # A release must be newer than every release before it, a beta newer than every release and beta: the copies it is
    # for take only a higher version. A release of an older line (1.4.3 after 1.5.0-beta.1) is fine.
    $before = @(Get-GitOutput tag --list 'v*') | Where-Object { $_ -match '^v\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$' -and ($Beta -or -not $_.Contains('-')) }
    foreach ($other in $before) {
        if ((Compare-Version $Version $other.TrimStart('v')) -le 0) {
            throw "Version $Version is not newer than $other. Installed copies would never take it."
        }
    }
    Write-Host 'Running tests before tagging...'
    Invoke-Checked $dotnet @('test', '--project', (Join-Path $src 'ServiceLib.Tests/ServiceLib.Tests.csproj'), '-c', 'Release')
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$work = Join-Path $OutDir 'work'

$properties = @('-p:SelfContained=true', '-p:EnableWindowsTargeting=true', "-p:V2pnVersion=$Version", '-p:V2pnChannel=release')
# Which code a package holds: the commit, "+" when the tree had changes on top of it.
$commit = Get-GitOutput rev-parse --short HEAD
if ($commit) {
    if (Get-GitOutput status --porcelain) { $commit += '+' }
    $properties += "-p:V2pnCommit=$commit"
}
# A developer-mode build remembers its source folder, so the next build needs no folder chosen.
# Published versions never carry it: it is a path on the building computer.
if ($Version.Contains('-local.')) { $properties += "-p:V2pnSourceDir=$repo" }
# Only V2pn itself: AmazTool runs for a few seconds during an update, v2pnctl for one command.
$appProperties = if ($NoReadyToRun) { @() } else { @('-p:PublishReadyToRun=true') }
# Test packages ask their own sources for the beta channel too, never V2pn's server.
if ($UpdateSources) { $properties += "-p:V2pnUpdateSources=$UpdateSources", "-p:V2pnBetaSources=$UpdateSources" }
if ($UpdateKeys) { $properties += "-p:V2pnUpdateKeys=$UpdateKeys" }

$setupWindow = $null
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

    # The command line and MCP server for the running app, next to V2pn.exe (v2rayN/V2pnCtl).
    Write-Host "== ${rid}: v2pnctl"
    Invoke-Checked $dotnet (@('publish', (Join-Path $src 'V2pnCtl/V2pnCtl.csproj'), '-c', 'Release', '-r', $rid,
        '-p:PublishTrimmed=true', '-v', 'q', '-nologo', '-o', $stage) + $properties)

    Write-Host "== ${rid}: V2pn $Version"
    Invoke-Checked $dotnet (@('publish', (Join-Path $src 'v2rayN.Desktop/v2rayN.Desktop.csproj'), '-c', 'Release', '-r', $rid,
        '-v', 'q', '-nologo', '-o', $stage) + $properties + $appProperties)

    Get-ChildItem $stage -Recurse -Include '*.pdb', '*.dbg' | Remove-Item -Force

    # The app alone is what an installed V2pn downloads to update itself: cores that are already
    # installed are never replaced by an update, so it does not carry them.
    $updateName = "V2pn-windows-$a-update.zip"
    $updateZip = Join-Path $OutDir $updateName
    if (Test-Path $updateZip) { Remove-Item -Force $updateZip }
    New-V2pnZip -Folder $stage -Zip $updateZip
    Test-V2pnZip -Zip $updateZip
    Write-Host "== $updateName  $([math]::Round((Get-Item $updateZip).Length / 1MB, 1)) MB"

    if ($UpdateOnly) { continue }

    if (-not $NoCores) {
        Write-Host "== ${rid}: cores"
        # Pinned in tools/cores.lock.json and checked against its hash (_common.ps1); a mismatch packages nothing.
        $coreZip = Get-PinnedCores $rid
        $coreDir = Join-Path $work "cores-$a"
        if (Test-Path $coreDir) { Remove-Item -Recurse -Force $coreDir }
        Expand-Archive -Path $coreZip -DestinationPath $coreDir
        # The bundle has a single top folder (v2rayN-windows-64/bin/...); its content goes next to V2pn.exe.
        $inner = Get-ChildItem $coreDir -Directory | Select-Object -First 1
        Copy-Item -Recurse -Force (Join-Path $inner.FullName '*') $stage
    }

    if ($Preset) {
        [IO.File]::WriteAllLines((Join-Path $stage 'v2pn-preset.txt'), [string[]]$Preset)
    }

    $zipName = "V2pn-windows-$a.zip"
    $zip = Join-Path $OutDir $zipName
    if (Test-Path $zip) { Remove-Item -Force $zip }
    New-V2pnZip -Folder $stage -Zip $zip
    Test-V2pnZip -Zip $zip
    $size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "== $zipName  $size MB"

    # What people download: V2pn's installer window with the Inno Setup engine inside, which installs
    # the same files as the full zip for the current user.
    if ($iscc) {
        if (-not $setupWindow) {
            Write-Host '== installer window'
            $setupOut = Join-Path $work 'setup-window'
            Invoke-Checked $dotnet @('build', (Join-Path $src 'V2pnSetup/V2pnSetup.csproj'), '-c', 'Release', '-v', 'q', '-nologo',
                '-o', $setupOut, "-p:V2pnVersion=$Version")
            $setupWindow = Join-Path $setupOut 'V2pnSetup.exe'
        }
        $engineName = "V2pn-windows-$a-engine"
        Invoke-Checked $iscc @('/Q', "/DAppVersion=$Version", "/DArch=$a", "/DSourceDir=$stage", "/DUninstaller=$setupWindow",
            "/DOutputDir=$work", "/DOutputName=$engineName", (Join-Path $PSScriptRoot 'installer/V2pn.iss'))
        $setup = Join-Path $OutDir "V2pn-windows-$a-setup.exe"
        New-V2pnSetup -Window $setupWindow -Engine (Join-Path $work "$engineName.exe") -Out $setup -Arch $a
        Write-Host "== $(Split-Path -Leaf $setup)  $([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB"
    }
}

Remove-Item -Recurse -Force $work

if (-not $NoManifest) { New-Manifest }

if ($Tag) {
    # Everything that can fail has run. Notes first, then the only change to the tree; if the commit
    # or the tag fails the file is put back, so the script can simply be run again.
    if (-not $Notes) {
        $changelog = Join-Path $repo 'CHANGELOG.md'
        if (Test-Path $changelog) {
            $text = Get-Content $changelog -Raw -Encoding UTF8
            $section = [regex]::Match($text, "(?ms)^## \[?$([regex]::Escape($Version))\]?[^\n]*\n(.*?)(?=^## |\z)")
            # A beta tells what is coming: the section that is not released yet.
            if (-not $section.Success -and $Beta) { $section = [regex]::Match($text, "(?ms)^## Unreleased[^\n]*\n(.*?)(?=^## |\z)") }
            if ($section.Success) { $Notes = $section.Groups[1].Value.Trim() }
        }
    }
    if (-not $Notes) {
        $range = if ($lastTag) { "$lastTag..HEAD" } else { 'HEAD' }
        $own = Get-GitOutput log --no-merges --format='- %s' $range --grep '^V2pn' | ForEach-Object { $_ -replace '^- V2pn: ', '- ' }
        $Notes = ($own | Select-Object -First 40) -join "`n"
    }
    # CHANGELOG.md shows its pictures by relative path; a release page needs full addresses. They point
    # at the tag, so the page keeps the pictures of its own version.
    $Notes = [regex]::Replace($Notes, '(\]\(|src=")(docs/images/)', "`${1}https://raw.githubusercontent.com/Piwqust/V2pn/v$Version/`${2}")
    $upstreamVersion = ([xml](Get-Content (Join-Path $src 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    # The first line is the tag's subject; the release page shows only what follows it, under its
    # own title, so the notes must not repeat the name. The v2rayN version is added only when the
    # notes do not already say what the release is built on.
    $message = "V2pn $Version`n`n$Notes"
    if ($Beta) {
        $message = "V2pn $Version`n`nA beta: V2pn installs it only where Updates, ""Get beta versions"" is on; everyone else stays on the latest release.`n`n$Notes"
    }
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
    if ($Beta) { Write-Host 'GitHub builds it and opens a draft pre-release: published, it goes only to copies that take betas.' }
}

Write-Host "Packages: $OutDir"
