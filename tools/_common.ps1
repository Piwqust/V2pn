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

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
