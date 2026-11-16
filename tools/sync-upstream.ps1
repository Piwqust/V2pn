<#
.SYNOPSIS
  Brings the latest v2rayN into V2pn and proves nothing broke before it lands.

.DESCRIPTION
  1. Fetches 2dust/v2rayN (remote "upstream", added when missing).
  2. Brings it in as one commit on a branch sync/v2rayN-<commit> made from the current branch:
     V2pn's history holds no v2rayN commits, so this is a three-way merge against the v2rayN
     commit named in tools/v2rayN-upstream.txt (the last one taken), not a git merge. Views V2pn
     rewrote keep the V2pn version (see v2rayN/v2rayN.Desktop/Views/.gitattributes).
  3. Builds V2pn and AmazTool and runs the tests.
  4. Writes docs/upstream-sync/<date>-<commit>.md: the v2rayN commits that came in, what v2rayN
     changed inside the views V2pn replaced (to port by hand), build and test results.
  5. With -Apply, when everything passed, moves the current branch to the merge and deletes the
     sync branch. Otherwise the sync branch stays for fixing, and the current branch is untouched.

  Exit codes: 0 merged or nothing new, 2 merge conflicts, 3 build failed, 4 tests failed.

.EXAMPLE
  tools/sync-upstream.ps1            # try it, look at the report
  tools/sync-upstream.ps1 -Apply     # and take it when it is green
#>
param(
    [string]$Ref = 'upstream/master',
    [switch]$Apply,
    [switch]$NoTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_common.ps1')
Set-Location $repo

# PowerShell names are case-insensitive: a function called Git would shadow git.exe itself.
function Invoke-Git { & git.exe @args; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed" } }

if (git status --porcelain --untracked-files=no) { throw 'Commit or stash your changes first.' }

if (-not (git remote | Where-Object { $_ -eq 'upstream' })) {
    Invoke-Git remote add upstream https://github.com/2dust/v2rayN.git
}
Write-Host 'Fetching v2rayN...'
Invoke-Git fetch upstream --quiet

$branch = git rev-parse --abbrev-ref HEAD
$target = git rev-parse $Ref
$lockFile = Join-Path $PSScriptRoot 'v2rayN-upstream.txt'
$base = (Get-Content $lockFile -TotalCount 1).Trim()
git cat-file -e "$base^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw "The v2rayN commit $base from $lockFile is not in upstream." }
git merge-base --is-ancestor $target $base
if ($LASTEXITCODE -eq 0) {
    Write-Host "V2pn already has everything from $Ref."
    exit 0
}

$short = $target.Substring(0, 8)
$commits = @(git log --no-merges --format='%h %s' "$base..$target")
$versionOf = { param($rev) ([regex]::Match((git show "${rev}:v2rayN/Directory.Build.props"), '<Version>([^<]+)</Version>')).Groups[1].Value }
$fromVersion = & $versionOf $base
$toVersion = & $versionOf $target
Write-Host "$($commits.Count) new v2rayN commits, v2rayN $fromVersion -> $toVersion"

# What v2rayN changed in files V2pn owns: these changes do not arrive by merging.
$changed = @(git diff --name-only $base $target)
$owned = @()
if ($changed) {
    $owned = @(git check-attr merge -- @changed | Where-Object { $_ -match ': merge: ours$' } | ForEach-Object { ($_ -split ': merge:')[0] })
}

$syncBranch = "sync/v2rayN-$short"
if (git branch --list $syncBranch) { Invoke-Git branch -D $syncBranch }
Invoke-Git switch --quiet -c $syncBranch

$status = 'merged'
$exitCode = 0
$conflicts = @()
$buildLog = ''
$testSummary = ''

# Cherry-picking a commit that turns the last v2rayN taken into the new one runs a three-way merge
# with that v2rayN as the base, and brings in none of v2rayN's commits. Both helper commits are
# parentless snapshots and stay out of every branch.
# merge.ours.driver is set for this merge only: the views marked merge=ours keep V2pn's version
# against v2rayN, while merges of V2pn's own branches must still bring their view changes in.
$mergeMessage = "V2pn: v2rayN $toVersion ($short)`n`nhttps://github.com/2dust/v2rayN/compare/$($base.Substring(0, 8))...$short"
$from = git commit-tree "$base^{tree}" -m "v2rayN $fromVersion"
$step = git commit-tree "$target^{tree}" -p $from -m $mergeMessage
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
git -c merge.ours.driver=true cherry-pick --no-commit $step 2>&1 | Out-Host
$ErrorActionPreference = $previousPreference
[IO.File]::WriteAllText($lockFile, "$target`n")
Invoke-Git add -- $lockFile

# V2pn has its own GitHub workflows (v2pn-*.yml) and removed v2rayN's: they would publish v2rayN's
# packages from this repository, one of them to winget. Whatever v2rayN changed or added there is
# dropped again, which also settles the "deleted by us, modified by them" conflicts.
$foreign = @(git ls-files -- .github/workflows | Sort-Object -Unique | Where-Object { (Split-Path -Leaf $_) -notlike 'v2pn-*' })
foreach ($file in $foreign) { git rm -q -f -- $file 2>$null | Out-Null }

$conflicts = @(git diff --name-only --diff-filter=U)
if ($conflicts) {
    $status = 'conflicts'
    $exitCode = 2
}
else {
    Invoke-Git commit --quiet --no-edit -m $mergeMessage
}

if ($exitCode -eq 0) {
    $dotnet = Find-Dotnet
    Write-Host 'Building...'
    foreach ($project in 'v2rayN/AmazTool/AmazTool.csproj', 'v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj') {
        $out = & $dotnet build $project -c Release -v q -nologo 2>&1
        if ($LASTEXITCODE -ne 0) {
            # Paths relative to the repo, without the trailing [project] part MSBuild repeats on every line.
            $buildLog += ($out | Where-Object { $_ -match ' error ' } | ForEach-Object { ($_ -replace [regex]::Escape("$repo\"), '') -replace '\s+\[[^\]]+\.csproj\]$', '' } | Sort-Object -Unique) -join "`n"
            $status = 'build failed'
            $exitCode = 3
        }
    }
}

if ($exitCode -eq 0 -and -not $NoTests) {
    Write-Host 'Testing...'
    $out = & $dotnet test --project v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj -c Release 2>&1
    $testSummary = ($out | Where-Object { $_ -match '^\s*(total|failed|succeeded|skipped):' } | ForEach-Object { $_.Trim() }) -join ', '
    if ($LASTEXITCODE -ne 0) {
        $testSummary += "`n" + (($out | Where-Object { $_ -match 'failed |Assert|Expected' } | Select-Object -First 30) -join "`n")
        $status = 'tests failed'
        $exitCode = 4
    }
}

# --- report -----------------------------------------------------------------------------------
$reportDir = Join-Path $repo 'docs/upstream-sync'
New-Item -ItemType Directory -Force $reportDir | Out-Null
$report = Join-Path $reportDir ("{0}-{1}.md" -f (Get-Date -Format 'yyyy-MM-dd'), $short)
$fence = '```'
$md = @()
$md += "# v2rayN $fromVersion -> $toVersion ($short): $status"
$md += ''
$md += "Taken as one commit on branch ``$syncBranch`` from ``$branch``. Commits from v2rayN: $($commits.Count)."
$md += ''
if ($conflicts) {
    $md += '## Conflicts'
    $md += ''
    $md += 'The merge is left uncommitted on the sync branch. Resolve, `git add` and `git commit`.'
    $md += ''
    $md += $conflicts | ForEach-Object { "- ``$_``" }
    $md += ''
}
if ($buildLog) {
    $md += '## Build errors'
    $md += ''
    $md += 'Usually v2rayN added a control to a view that V2pn rewrote: add it to the V2pn view (diff below).'
    $md += ''
    $md += $fence
    $md += $buildLog
    $md += $fence
    $md += ''
}
if ($testSummary) {
    $md += '## Tests'
    $md += ''
    $md += $testSummary
    $md += ''
}
$md += '## To port by hand'
$md += ''
if ($owned) {
    $md += 'v2rayN changed these views, but V2pn keeps its own version of them. Bring over what matters (new settings, fixes):'
    $md += ''
    foreach ($f in $owned) {
        $md += "### ``$f``"
        $md += ''
        $md += "${fence}diff"
        $md += (git diff $base $target -- $f) -join "`n"
        $md += $fence
        $md += ''
    }
}
else {
    $md += 'Nothing: v2rayN did not touch the views V2pn replaced.'
    $md += ''
}
$md += '## v2rayN commits'
$md += ''
$md += $commits | ForEach-Object { "- $_" }
[IO.File]::WriteAllText($report, ($md -join "`n") + "`n")
Write-Host "Report: $report"

if ($exitCode -ne 0) {
    Write-Host "Stopped: $status. Branch $syncBranch is left for fixing; $branch is untouched."
    exit $exitCode
}

Invoke-Git add $report
Invoke-Git commit --quiet -m "V2pn: report for the v2rayN $toVersion merge"

if ($Apply) {
    Invoke-Git switch --quiet $branch
    Invoke-Git merge --quiet --ff-only $syncBranch
    Invoke-Git branch -d $syncBranch
    Write-Host "Done: $branch now has v2rayN $toVersion. Port what the report lists, then release."
}
else {
    Invoke-Git switch --quiet $branch
    Write-Host "Green. Take it with: git merge --ff-only $syncBranch   (or run again with -Apply)"
}
