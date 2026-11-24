# Helpers shared by the V2pn scripts in tools/. Dot-source it: . "$PSScriptRoot/_common.ps1"

function Find-Dotnet {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe'); $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet') }
    $candidates += (Join-Path $HOME '.dotnet10/dotnet.exe')
    $candidates += 'dotnet'
    foreach ($c in $candidates) {
        try {
            $sdks = & $c --list-sdks 2>$null
            if ($sdks -match '^1\d\.') { return $c }
        }
        catch { }
    }
    throw 'No .NET 10 SDK found. Install it (https://dot.net) or set DOTNET_ROOT.'
}

# Inno Setup 6's compiler, which makes the Windows installer (tools/installer/V2pn.iss); $null when it is not installed.
function Find-Iscc {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    $candidates = @()
    if ($command) { $candidates += $command.Source }
    foreach ($root in ${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs')) {
        if ($root) { $candidates += (Join-Path $root 'Inno Setup 6\ISCC.exe') }
    }
    # Chocolatey's ISCC.exe in its bin folder is a shim that starts the real one; either works.
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# The core bundle pinned in tools/cores.lock.json for a target (win-x64, osx-arm64...): downloaded once per pin into
# the temp folder and checked against the lock's SHA-256. Returns the path of the zip.
function Get-PinnedCores([string]$Target) {
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
        throw "The core bundle for $Target does not match tools/cores.lock.json (got $hash)."
    }
    return $zip
}

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
