<#
.SYNOPSIS
  Signs the packages GitHub built for a tag and opens the draft release, with the signing key
  staying on this computer.

.DESCRIPTION
  .github/workflows/v2pn-release.yml builds the Windows and macOS packages for a pushed tag and
  attaches them to its run. This script downloads them, writes and signs release.json with the
  key in ~/.v2pn/release-signing-key.pem (tools/manifest.cs), and creates the DRAFT release
  with every file. Nothing becomes visible to installed copies until you press Publish.

  Use it when the key is not stored in the repository's "release" environment. If it is, the
  workflow signs by itself and this script is not needed.

.EXAMPLE
  tools/sign-release.ps1 -Version 1.0.1
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Repository = 'Piwqust/V2pn',
    # The workflow run to take the packages from; by default the newest successful one for the tag.
    [string]$Run
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_common.ps1')
$tag = "v$Version"

function Get-Output([string]$exe, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { return (& $exe @arguments 2>$null) }
    finally { $ErrorActionPreference = $previous }
}

$notes = (Get-Output 'git.exe' @('-C', $repo, 'tag', '-l', '--format=%(contents)', $tag)) -join "`n"
if (-not $notes.Trim()) { throw "There is no tag $tag here. Make it with tools/release.ps1 -Version $Version -Tag and push it." }

$state = Get-Output 'gh' @('release', 'view', $tag, '--repo', $Repository, '--json', 'isDraft', '--jq', '.isDraft')
if ($state -eq 'false') { throw "Release $tag is already published. Installed copies may have its packages; release a new version instead." }

if (-not $Run) {
    $Run = Get-Output 'gh' @('run', 'list', '--repo', $Repository, '--workflow', 'v2pn-release.yml', '--branch', $tag,
        '--status', 'success', '--limit', '1', '--json', 'databaseId', '--jq', '.[0].databaseId')
    if (-not $Run) { throw "No successful 'V2pn release' run for $tag yet. Push the tag and wait for the run to finish." }
}
Write-Host "Packages from run $Run"

$dir = Join-Path $repo "dist/release/$Version"
$download = Join-Path $dir 'download'
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
New-Item -ItemType Directory -Force $download | Out-Null
Invoke-Checked 'gh' @('run', 'download', $Run, '--repo', $Repository, '--pattern', 'packages-*', '--dir', $download)
# One folder per artifact (packages-windows, packages-macos); the release wants them side by side.
Get-ChildItem $download -Recurse -File | Move-Item -Destination $dir
Remove-Item -Recurse -Force $download

$expected = 'V2pn-windows-x64.zip', 'V2pn-windows-x64-update.zip', 'V2pn-windows-arm64.zip', 'V2pn-windows-arm64-update.zip',
    'V2pn-macos-arm64.zip', 'V2pn-macos-arm64.dmg', 'V2pn-macos-x64.zip', 'V2pn-macos-x64.dmg'
$missing = $expected | Where-Object { -not (Test-Path (Join-Path $dir $_)) }
if ($missing) { throw "The run did not produce: $($missing -join ', ')" }

$dotnet = Find-Dotnet
Invoke-Checked $dotnet @('run', (Join-Path $PSScriptRoot 'manifest.cs'), '--', $dir, $Version, '--repo', $repo)

$notesFile = Join-Path $dir 'notes.md'
[IO.File]::WriteAllText($notesFile, $notes)
$files = Get-ChildItem $dir -File | Where-Object { $_.Name -ne 'notes.md' } | ForEach-Object { $_.FullName }
if ($state -eq 'true') {
    Invoke-Checked 'gh' (@('release', 'upload', $tag, '--repo', $Repository, '--clobber') + $files)
}
else {
    Invoke-Checked 'gh' (@('release', 'create', $tag, '--repo', $Repository, '--draft', '--verify-tag',
            '--title', "V2pn $Version", '--notes-file', $notesFile) + $files)
}
Remove-Item -Force $notesFile
Write-Host ''
Write-Host "Draft release $tag is ready with $($files.Count) files. Check it and publish:"
Write-Host "  gh release edit $tag --repo $Repository --draft=false"
