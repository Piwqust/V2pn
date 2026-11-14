# Installs V2pn for the current user with one command and no administrator rights:
#
#   irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex
#
# Downloads the newest release, checks the package against the release's list of files (size and
# SHA-256), unpacks it to %LOCALAPPDATA%\Programs\V2pn, adds Start menu and desktop shortcuts and
# starts V2pn. At that first start V2pn moves over a v2rayN with servers found on this computer:
# servers, subscriptions, routing and settings. v2rayN is closed and stops starting with Windows;
# its own files are left as they are.
#
# Options, as environment variables set before the command:
#   $env:V2PN_DIR = 'D:\Apps\V2pn'    install somewhere else
#   $env:V2PN_NO_IMPORT = 1           do not move anything from v2rayN
#   $env:V2PN_NO_SHORTCUTS = 1        no shortcuts
#   $env:V2PN_NO_START = 1            do not start V2pn
#   $env:V2PN_SOURCE = <url>          another releases page or mirror (same layout as GitHub's)
#
# ASCII only: Windows PowerShell reads a downloaded script that has no charset as Latin-1.
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    catch { }

    $source = if ($env:V2PN_SOURCE) { $env:V2PN_SOURCE.TrimEnd('/') } else { 'https://github.com/Piwqust/V2pn/releases' }
    $dir = if ($env:V2PN_DIR) { $env:V2PN_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\V2pn' }
    $exe = Join-Path $dir 'V2pn.exe'

    function Start-V2pn {
        if ($env:V2PN_NO_START) { return }
        # Through Explorer: a program started from this console could be closed together with it.
        Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -ArgumentList "`"$exe`""
    }

    function Get-Text([string]$url) {
        # GitHub serves release files as application/octet-stream, which Windows PowerShell returns as bytes.
        $content = (Invoke-WebRequest -UseBasicParsing -Uri $url).Content
        if ($content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($content) }
        return $content
    }

    if (Test-Path $exe) {
        Write-Host "V2pn is already installed in $dir. It keeps itself up to date; starting it."
        Start-V2pn
        return
    }

    $cpu = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    $arch = if ($cpu -eq 'ARM64') { 'arm64' } else { 'x64' }
    $name = "V2pn-windows-$arch.zip"

    Write-Host 'Looking for the newest V2pn...'
    $manifest = (Get-Text "$source/latest/download/release.json") | ConvertFrom-Json
    $file = $manifest.files | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $file) { throw "V2pn $($manifest.version) has no package for this computer ($name)." }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('v2pn-install-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $work | Out-Null
    try {
        $zip = Join-Path $work $name
        Write-Host ('Downloading V2pn {0} ({1:N0} MB)...' -f $manifest.version, ($file.size / 1MB))
        Invoke-WebRequest -UseBasicParsing -Uri "$source/download/v$($manifest.version)/$name" -OutFile $zip
        $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
        if ((Get-Item $zip).Length -ne [long]$file.size -or $hash -ne $file.sha256) {
            throw 'The download does not match the release. Nothing was installed; try again.'
        }

        Write-Host "Installing to $dir..."
        $unpacked = Join-Path $work 'unpacked'
        Expand-Archive -Path $zip -DestinationPath $unpacked
        # The package holds one folder, V2pn\, with the app in it.
        $inner = Join-Path $unpacked 'V2pn'
        if (-not (Test-Path (Join-Path $inner 'V2pn.exe'))) { throw 'The package has no V2pn.exe.' }
        New-Item -ItemType Directory -Force $dir | Out-Null
        Copy-Item -Recurse -Force (Join-Path $inner '*') $dir
    }
    finally {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }

    if (-not $env:V2PN_NO_IMPORT) {
        # Read by V2pn at its first start (V2pnImport.AutoMarker); Explorer passes it no arguments.
        $temps = Join-Path $dir 'guiTemps'
        New-Item -ItemType Directory -Force $temps | Out-Null
        Set-Content -Path (Join-Path $temps 'V2pnImport.txt') -Value 'auto' -Encoding Ascii
    }

    if (-not $env:V2PN_NO_SHORTCUTS) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($folder in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
            $link = $shell.CreateShortcut((Join-Path $folder 'V2pn.lnk'))
            $link.TargetPath = $exe
            $link.WorkingDirectory = $dir
            $link.Save()
        }
    }

    Write-Host "V2pn $($manifest.version) is installed in $dir."
    Start-V2pn
}
