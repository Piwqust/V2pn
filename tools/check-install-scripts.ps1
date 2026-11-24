<#
.SYNOPSIS
  Checks the release signature check of the one-command installers, tools/install.ps1 and
  tools/install.sh, without running either of them.

.DESCRIPTION
  1. The release keys built into install.ps1 ($releaseKeys) and install.sh (V2PN_RELEASE_KEYS)
     must be the keys of V2pnUpdateKeys in v2rayN/Directory.Build.targets. A key replaced there
     and not in the installers would make them refuse every new release, or keep trusting a
     retired key.
  2. Unless -KeysOnly: Test-ReleaseSignature, taken out of install.ps1 by the PowerShell parser
     (only its definition runs, never the installer around it), must accept a signed release.json
     and refuse it with one byte changed, with a changed signature, with no signature and with
     another key. The release is tools/install-test/ (V2pn 1.4.2 as published) unless -Manifest
     and -Signature name other files. When V2pnUpdateKeys no longer holds the key that signed it,
     put a newer published release.json and release.json.sig there.

  Run it with Windows PowerShell 5.1 as well as PowerShell 7: install.ps1 must work on .NET
  Framework, which is what `irm ... | iex` gets in most consoles. tools/check-install-scripts.sh
  does step 2 for install.sh.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-install-scripts.ps1
.EXAMPLE
  tools/check-install-scripts.ps1 -Manifest dist/release/1.4.3/release.json -Signature dist/release/1.4.3/release.json.sig
#>
param(
    [string]$Manifest = (Join-Path $PSScriptRoot 'install-test/release.json'),
    [string]$Signature = (Join-Path $PSScriptRoot 'install-test/release.json.sig'),
    [switch]$KeysOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$script:failed = 0

function Write-Result([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok    $what" }
    else {
        Write-Host "FAIL  $what"
        $script:failed++
    }
}

# The keys of a |-separated list, in a fixed order, so two lists compare as sets.
function Get-KeyList([string]$value) {
    @($value.Split('|') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
}

$targets = [IO.File]::ReadAllText((Join-Path $repo 'v2rayN/Directory.Build.targets'))
$builtIn = [regex]::Match($targets, '<V2pnUpdateKeys[^>]*>([^<]+)</V2pnUpdateKeys>')
if (-not $builtIn.Success) { throw 'V2pnUpdateKeys is not in v2rayN/Directory.Build.targets.' }
$expected = (Get-KeyList $builtIn.Groups[1].Value) -join '|'

$installPs1 = Join-Path $PSScriptRoot 'install.ps1'
$installSh = Join-Path $PSScriptRoot 'install.sh'
$embedded = @{
    'install.ps1' = [regex]::Matches([IO.File]::ReadAllText($installPs1), "(?m)^[ \t]*\`$releaseKeys = '([^']*)'")
    'install.sh'  = [regex]::Matches([IO.File]::ReadAllText($installSh), "(?m)^V2PN_RELEASE_KEYS='([^']*)'")
}
foreach ($name in 'install.ps1', 'install.sh') {
    $found = $embedded[$name]
    if ($found.Count -ne 1) {
        Write-Result $false "$name has its release keys in exactly one place (found $($found.Count))"
        continue
    }
    $keys = (Get-KeyList $found[0].Groups[1].Value) -join '|'
    Write-Result ($keys -eq $expected) "$name has the release keys of V2pnUpdateKeys (v2rayN/Directory.Build.targets)"
}

if (-not $KeysOnly) {
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($installPs1, [ref]$tokens, [ref]$errors)
    if ($errors) { throw "install.ps1 does not parse: $($errors[0])" }
    $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-ReleaseSignature' }, $true)
    if (-not $definition) { throw 'install.ps1 has no Test-ReleaseSignature.' }
    . ([scriptblock]::Create($definition.Extent.Text))

    $keys = $embedded['install.ps1'][0].Groups[1].Value
    # A P-256 key that signed nothing here.
    $otherKey = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHlwBBkG+MdpxUKEfp5BmAXDS87WL0HSq/MaE+8vjRiBdXQmZ0QqhGtNJvS43DXNJtptk7qg4P8L+4R//Zu6B9A=='
    $data = [IO.File]::ReadAllBytes((Resolve-Path $Manifest))
    $sig = [IO.File]::ReadAllText((Resolve-Path $Signature))
    Write-Host "PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition)), $([Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)"

    Write-Result (Test-ReleaseSignature $data $sig $keys) 'install.ps1: the signed release.json is accepted'

    $changed = [byte[]]$data.Clone()
    $at = [int]($changed.Length / 2)
    $changed[$at] = $changed[$at] -bxor 1
    Write-Result (-not (Test-ReleaseSignature $changed $sig $keys)) "install.ps1: release.json with one byte changed (at $at) is refused"

    $der = [Convert]::FromBase64String($sig.Trim())
    $der[$der.Length - 1] = $der[$der.Length - 1] -bxor 1
    Write-Result (-not (Test-ReleaseSignature $data ([Convert]::ToBase64String($der)) $keys)) 'install.ps1: a signature with one byte changed is refused'

    Write-Result (-not (Test-ReleaseSignature $data '' $keys)) 'install.ps1: no signature is refused'
    Write-Result (-not (Test-ReleaseSignature $data 'not a signature' $keys)) 'install.ps1: a signature that is not base64 is refused'
    Write-Result (-not (Test-ReleaseSignature $data $sig $otherKey)) 'install.ps1: a signature by another key is refused'
    Write-Result (Test-ReleaseSignature $data $sig "$otherKey|$keys") 'install.ps1: with two keys (a new one next to the old), either signs'
}

if ($script:failed) {
    Write-Host "$($script:failed) check(s) failed."
    exit 1
}
Write-Host 'All checks passed.'
