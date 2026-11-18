<#
.SYNOPSIS
  Signs the packages GitHub built for a tag and opens the draft release, with the signing key
  staying on this computer.

.DESCRIPTION
  .github/workflows/v2pn-release.yml builds the Windows and macOS packages for a pushed tag and
  attaches them to its run. This script downloads them, writes and signs release.json with the
  key in ~/.v2pn/release-signing-key.pem (tools/manifest.cs), publishes the root-signed partners
  (tools/partner.cs: partner-index.json of partners/<id>, an empty partners.json for 1.2.1 and
  older), and creates the DRAFT release with every file. Nothing becomes visible to installed
  copies until you press Publish. There are no partner builds: a provider's people get plain
  V2pn and come from the provider by its v2pn:// link or a sign-in by code.

  The root-signed partner-index.json on the release never shadows the V2pn Hub's index
  (v2pn.angleford.co/partners): V2pn goes by the hub's entry for every partner the hub lists,
  whatever the dates, and takes from this one only the partners the hub does not list
  (V2pnPartnerIndex.Merge). It is the fallback when the hub cannot be reached, and the only way
  to the grants the owner reviews by hand (routing, transport, core).

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

$notes = (Get-Output 'git.exe' @('-C', $repo, 'tag', '-l', '--format=%(contents:body)', $tag)) -join "`n"
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
# The one-command installers go with every release: .../releases/latest/download/install.ps1 (Windows), install.sh (macOS)
Copy-Item (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'install.sh') $dir
# The partners (partners/<id>), signed with ~/.v2pn/partner-root-key.pem: partner-index.json and partner-<id>.zip each,
# which V2pn reads from the latest release for partners' tabs, and partners.json with no partner in it, which V2pn 1.2.1 and
# older read: they then drop the partner they kept and stay plain V2pn. The summary says what changed since the index
# of the release before.
$previous = Join-Path ([IO.Path]::GetTempPath()) "v2pn-partners-before-$Version"
if (Test-Path $previous) { Remove-Item -Recurse -Force $previous }
New-Item -ItemType Directory -Force $previous | Out-Null
Get-Output 'gh' @('release', 'download', '--repo', $Repository, '--pattern', 'partner-index.json', '--dir', $previous) | Out-Null
$partnersOut = Join-Path $dir 'partners'
$publish = @('run', (Join-Path $PSScriptRoot 'partner.cs'), '--', 'publish', '--partners', (Join-Path $repo 'partners'), '--out', $partnersOut)
if (Test-Path (Join-Path $previous 'partner-index.json')) { $publish += @('--previous', (Join-Path $previous 'partner-index.json')) }
# A non-zero exit means a partner has a problem or the app's built-in keys would refuse the signature.
Invoke-Checked $dotnet $publish
foreach ($required in 'partner-index.json', 'partner-index.json.sig', 'partners.json', 'partners.json.sig') {
    if (-not (Test-Path (Join-Path $partnersOut $required))) { throw "tools/partner.cs did not write $required." }
}
Get-ChildItem $partnersOut -File | Move-Item -Destination $dir
Remove-Item -Recurse -Force $partnersOut, $previous

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
