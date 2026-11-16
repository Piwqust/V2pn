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
# From a provider's page, the command names the provider, and V2pn gets that provider's tab beside
# its own (the provider's look, plan and sign-in):
#
#   $env:V2PN_PARTNER = 'configra'; irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex
#
# It is the same V2pn in the same folder, with v2pn-partner.txt beside V2pn.exe holding the
# provider's id; updates keep it. When V2pn is already installed, only that file is added: servers,
# subscriptions and settings stay as they are. The provider must be in the release's
# partner-index.json.
#
# Options, as environment variables set before the command:
#   $env:V2PN_PARTNER = 'configra'    the provider's tab (above)
#   $env:V2PN_DIR = 'D:\Apps\V2pn'    install somewhere else
#   $env:V2PN_NO_IMPORT = 1           do not move anything from v2rayN
#   $env:V2PN_NO_SHORTCUTS = 1        no shortcuts
#   $env:V2PN_NO_START = 1            do not start V2pn
#   $env:V2PN_SOURCE = <url>          another releases page or mirror (same layout as GitHub's)
#   $env:NO_COLOR = 1                 no colours
#
# ASCII only: Windows PowerShell reads a downloaded script that has no charset as Latin-1, so every
# character drawn on screen beyond ASCII is made from its code.
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    catch { }

    $repo = 'https://github.com/Piwqust/V2pn'
    $source = if ($env:V2PN_SOURCE) { $env:V2PN_SOURCE.TrimEnd('/') } else { "$repo/releases" }
    $dir = if ($env:V2PN_DIR) { $env:V2PN_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\V2pn' }
    $exe = Join-Path $dir 'V2pn.exe'
    $inv = [Globalization.CultureInfo]::InvariantCulture
    # The provider whose tab V2pn gets: its id as V2pn's list of partners has it (V2pnEdition).
    $partner = if ($env:V2PN_PARTNER) { $env:V2PN_PARTNER.Trim().ToLowerInvariant() } else { $null }
    if ($partner -and $partner -notmatch '^[a-z0-9][a-z0-9-]{1,31}$') { throw "V2PN_PARTNER '$partner' is not a provider id." }

    # ---- The screen ---------------------------------------------------------------------------
    # live: a console the installer can redraw, so progress stays on one line; anywhere else (a log,
    # a pipe, the ISE) it writes finished lines only. vt: the console takes ANSI colours. rich: its
    # font has the finer glyphs (Windows Terminal, VS Code); the classic console gets plainer ones.
    $live = $Host.Name -eq 'ConsoleHost' -and -not [Console]::IsOutputRedirected
    $vt = $live -and $Host.UI.SupportsVirtualTerminal -eq $true
    $rich = $vt -and ($env:WT_SESSION -or $env:TERM_PROGRAM -eq 'vscode')
    $colour = $live -and -not $env:NO_COLOR
    $width = 80
    if ($live) { try { $width = [Math]::Max(40, [Console]::WindowWidth) } catch { } }
    $esc = [char]27

    function Chr([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }
    $g = @{
        ok   = if ($rich) { Chr 0x2713 } else { Chr 0x221A }
        bad  = if ($rich) { Chr 0x2717 } else { 'x' }
        dot  = Chr 0xB7
        ell  = Chr 0x2026
        fill = if ($rich) { Chr 0x2501 } else { Chr 0x2588 }
        rest = if ($rich) { Chr 0x2501 } else { Chr 0x2591 }
    }
    $spin = @($(if ($rich) { 0x280B, 0x2819, 0x2839, 0x2838, 0x283C, 0x2834, 0x2826, 0x2827, 0x2807, 0x280F } else { 0xB7, 0x2022, 0x25CF, 0x2022 }) | ForEach-Object { [string][char]$_ })
    # The app's Signal red and its neighbours; the last value is the nearest classic console colour.
    $palette = @{
        brand = 255, 61, 31, 'Red'
        ok    = 34, 181, 115, 'Green'
        bad   = 255, 90, 78, 'Red'
        dim   = 128, 134, 145, 'DarkGray'
        track = 72, 76, 84, 'DarkGray'
        link  = 90, 160, 255, 'Cyan'
        bold  = 255, 255, 255, 'White'
        face  = 255, 61, 31, 'Red'
        shade = 107, 25, 13, 'DarkRed'
    }
    $ui = @{ frame = 0; step = ''; installed = $false; open = $false }

    # Writes one line from (text, style) pairs. -Temp: a line the next one overwrites (progress); it
    # is not written at all where the screen cannot be redrawn.
    function Say([object[]]$parts = @(), [switch]$Temp) {
        if ($Temp -and -not $live) { return }
        $line = New-Object Text.StringBuilder
        if ($live) {
            if ($vt) { [void]$line.Append("`r$esc[2K") } else { [void]$line.Append("`r" + (' ' * ($width - 1)) + "`r") }
        }
        if ($colour -and -not $vt) {
            Write-Host $line.ToString() -NoNewline
            for ($i = 0; $i -lt $parts.Count; $i += 2) {
                $p = $palette[[string]$parts[$i + 1]]
                if ($p) { Write-Host $parts[$i] -NoNewline -ForegroundColor $p[3] } else { Write-Host $parts[$i] -NoNewline }
            }
        }
        else {
            for ($i = 0; $i -lt $parts.Count; $i += 2) {
                $text = [string]$parts[$i]
                $style = [string]$parts[$i + 1]
                if ($colour -and $style -eq 'bold') { [void]$line.Append("$esc[1m$text$esc[22m") }
                elseif ($colour -and $palette[$style]) {
                    $p = $palette[$style]
                    [void]$line.Append(("$esc[38;2;{0};{1};{2}m" -f $p[0], $p[1], $p[2]) + $text + "$esc[39m")
                }
                else { [void]$line.Append($text) }
            }
            Write-Host $line.ToString() -NoNewline
        }
        if (-not $Temp) { Write-Host '' }
        $ui.open = [bool]$Temp
    }

    function Step([string]$label, [object[]]$value) { Say (@('   ', '', $g.ok, 'ok', " $($label.PadRight(11))", '') + $value) }
    function Skip([string]$label, [string]$why) { Say @('   ', '', '-', 'dim', " $($label.PadRight(11))", '', $why, 'dim') }
    function Fact([string]$label, [object[]]$value) { Say (@('     ', '', $label.PadRight(11), 'dim') + $value) }
    function MB([double]$bytes) { [string]::Format($inv, '{0:0.0} MB', $bytes / 1MB) }
    function Words([string[]]$list) {
        if ($list.Count -le 1) { return [string]($list -join '') }
        ($list[0..($list.Count - 2)] -join ', ') + ' and ' + $list[-1]
    }

    # A progress line: spinner, bar and a tail of numbers that is cut short in a narrow console.
    function Show-Progress([string]$label, [double]$part, [string]$tail, [string]$short) {
        if (-not $live) { return }
        $ui.frame++
        $room = $width - 1 - 16 - 2 - $tail.Length
        if ($room -lt 10) { $tail = $short; $room = $width - 1 - 16 - 2 - $tail.Length }
        $bar = [Math]::Max(6, [Math]::Min(30, $room))
        # Double literals: with an int first argument PowerShell picks Math.Max(int, int) and rounds $part away.
        $filled = [int][Math]::Floor($bar * [Math]::Min(1.0, [Math]::Max(0.0, $part)))
        Say -Temp @('   ', '', $spin[$ui.frame % $spin.Count], 'brand', " $($label.PadRight(11))", '',
            ($g.fill * $filled), 'brand', ($g.rest * ($bar - $filled)), 'track', "  $tail", 'dim')
    }

    function Show-Header {
        Say
        if ($live -and $width -ge 44) {
            $rows = '##7   ##7######7 ######7 ###7   ##7',
                    '##!   ##!L====##7##F==##7####7  ##!',
                    '##!   ##! #####FJ######FJ##F##7 ##!',
                    'L##7 ##FJ##F===J ##F===J ##!L##7##!',
                    ' L####FJ #######7##!     ##! L####!',
                    '  L===J  L======JL=J     L=J  L===J'
            $glyph = @{ '#' = Chr 0x2588; '=' = Chr 0x2550; '!' = Chr 0x2551; 'F' = Chr 0x2554; '7' = Chr 0x2557; 'L' = Chr 0x255A; 'J' = Chr 0x255D; ' ' = ' ' }
            for ($r = 0; $r -lt $rows.Count; $r++) {
                # Signal red at the top to orange at the bottom; the outlines are a dark shade of the row.
                $t = $r / ($rows.Count - 1)
                $rgb = [int](255 - 13 * $t), [int](61 + 79 * $t), [int](31 + 9 * $t)
                $palette['face'] = $rgb[0], $rgb[1], $rgb[2], 'Red'
                $palette['shade'] = [int]($rgb[0] * 0.42), [int]($rgb[1] * 0.42), [int]($rgb[2] * 0.42), 'DarkRed'
                $parts = @('   ', '')
                $run = ''
                $kind = ''
                foreach ($ch in $rows[$r].ToCharArray()) {
                    $k = switch ([string]$ch) { '#' { 'face' } ' ' { '' } default { 'shade' } }
                    if ($k -ne $kind -and $run) { $parts += $run, $kind; $run = '' }
                    $kind = $k
                    $run += $glyph[[string]$ch]
                }
                if ($run) { $parts += $run, $kind }
                Say $parts
            }
            Say
            Say @('   A simple VPN client for Windows and macOS, by ', '', 'Piwqust', 'bold')
        }
        else {
            Say @('   ', '', 'V2pn', 'bold', ': a simple VPN client for Windows and macOS, by Piwqust', '')
        }
        Say @('   Built on v2rayN by 2dust ', 'dim', $g.dot, 'dim', ' GPL-3.0 ', 'dim', $g.dot, 'dim', ' ', '', ($repo -replace '^https://', ''), 'link')
        Say
    }

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

    # Only a provider V2pn lists gets a tab; its name, for the screen.
    function Get-PartnerName {
        $listed = $null
        try { $listed = ((Get-Text "$source/latest/download/partner-index.json") | ConvertFrom-Json).partners | Where-Object { $_.id -eq $partner } | Select-Object -First 1 }
        catch { throw "Could not read V2pn's list of providers: $($_.Exception.Message)" }
        if (-not $listed) { throw "V2pn has no provider '$partner'. Take the command from your provider's page." }
        return [string]$listed.name
    }

    # v2pn-partner.txt beside V2pn.exe: V2pn shows the provider's tab from its next start.
    function Add-PartnerMarker {
        New-Item -ItemType Directory -Force $dir | Out-Null
        [IO.File]::WriteAllText((Join-Path $dir 'v2pn-partner.txt'), "$partner`n")
    }

    # Downloads with a live bar and hashes on the way, so the check needs no second read of the file.
    function Receive-Package([string]$url, [string]$path, [long]$total) {
        $request = [Net.WebRequest]::Create($url)
        $request.UserAgent = 'V2pn-installer'
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 60000
        $response = $request.GetResponse()
        $in = $null
        $out = $null
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $in = $response.GetResponseStream()
            $out = [IO.File]::Create($path)
            $buffer = New-Object byte[] 262144
            $done = 0L
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $shown = -1000L
            $mark = 0L
            $markBytes = 0L
            $speed = 0.0
            while (($n = $in.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $out.Write($buffer, 0, $n)
                [void]$sha.TransformBlock($buffer, 0, $n, $null, 0)
                $done += $n
                $now = $clock.ElapsedMilliseconds
                if ($now - $shown -lt 100) { continue }
                $shown = $now
                if ($now - $mark -ge 500) {
                    $current = ($done - $markBytes) * 1000.0 / ($now - $mark)
                    $speed = if ($speed -gt 0) { 0.6 * $speed + 0.4 * $current } else { $current }
                    $mark = $now
                    $markBytes = $done
                }
                $left = if ($speed -gt 0) { [Math]::Ceiling(($total - $done) / $speed) } else { -1 }
                $eta = if ($left -ge 0) { '{0}:{1:00}' -f [int][Math]::Floor($left / 60), [int]($left % 60) } else { '-:--' }
                $part = $done / [Math]::Max(1, $total)
                $tail = [string]::Format($inv, '{0,3}%  {1:0.0}/{2:0.0} MB  {3:0.0} MB/s  {4}', [int][Math]::Floor($part * 100), ($done / 1MB), ($total / 1MB), ($speed / 1MB), $eta)
                Show-Progress 'Download' $part $tail ('{0,3}%' -f [int][Math]::Floor($part * 100))
            }
            [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
            @{
                size    = $done
                seconds = $clock.Elapsed.TotalSeconds
                sha256  = ([BitConverter]::ToString($sha.Hash) -replace '-', '').ToLowerInvariant()
            }
        }
        finally {
            if ($out) { $out.Dispose() }
            if ($in) { $in.Dispose() }
            $response.Dispose()
            $sha.Dispose()
        }
    }

    function Save-Package([string]$url, [string]$path, [long]$total) {
        for ($attempt = 1; ; $attempt++) {
            try { return Receive-Package $url $path $total }
            catch [Net.WebException], [IO.IOException] {
                if ($attempt -ge 3) { throw }
                Say -Temp @('   ', '', $spin[0], 'brand', ' Download   ', '', "the connection broke; trying again ($($attempt + 1) of 3)$($g.ell)", 'dim')
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
    }

    # Unpacks straight into the install folder, V2pn.exe last: until it is there, an install cut
    # short is not taken for a finished one by the next run.
    function Install-Package([string]$zip, [string]$into) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            # The package holds one folder, V2pn\, with the app in it.
            $files = @($archive.Entries | Where-Object { $_.FullName.StartsWith('V2pn/') -and $_.Name })
            $main = @($files | Where-Object { $_.FullName -eq 'V2pn/V2pn.exe' })
            if (-not $main) { throw 'The package has no V2pn.exe.' }
            $files = @($files | Where-Object { $_.FullName -ne 'V2pn/V2pn.exe' }) + $main
            $total = 0L
            foreach ($f in $files) { $total += $f.Length }
            $root = [IO.Path]::GetFullPath($into).TrimEnd('\') + '\'
            $done = 0L
            $count = 0
            foreach ($f in $files) {
                $target = [IO.Path]::GetFullPath((Join-Path $into ($f.FullName.Substring(5) -replace '/', '\')))
                if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "The package has a file outside its folder: $($f.FullName)" }
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
                Show-Progress 'Install' ($done / [Math]::Max(1, $total)) ('{0}/{1} files  {2}' -f $count, $files.Count, $f.Name) ('{0}/{1}' -f $count, $files.Count)
                [IO.Compression.ZipFileExtensions]::ExtractToFile($f, $target, $true)
                $done += $f.Length
                $count++
            }
            $cores = @()
            foreach ($core in @(@('xray', 'Xray'), @('sing_box', 'sing-box'), @('mihomo', 'mihomo'))) {
                if ($files | Where-Object { $_.FullName.StartsWith("V2pn/bin/$($core[0])/") }) { $cores += $core[1] }
            }
            @{ count = $files.Count; size = $total; cores = $cores }
        }
        finally { $archive.Dispose() }
    }

    $work = $null
    try {
        if ($live) { try { [Console]::CursorVisible = $false } catch { } }
        Show-Header

        if (Test-Path $exe) {
            $have = ''
            try { $have = ' ' + ([string]((Get-Item $exe).VersionInfo.ProductVersion) -split '\+')[0] } catch { }
            Step 'Installed' @("V2pn$have is already in ", '', $dir, 'bold')
            if ($partner) {
                # The same V2pn: only the marker is added; servers and settings stay.
                $ui.step = 'Provider'
                $partnerName = Get-PartnerName
                Add-PartnerMarker
                $running = @(Get-Process -Name V2pn -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $exe } catch { $false } }).Count -gt 0
                Step 'Provider' @("$partnerName's tab added", '', $(if ($running) { '   it shows once V2pn is restarted' } else { '   servers and settings stay as they are' }), 'dim')
            }
            Say @("     It keeps itself up to date$(if ($env:V2PN_NO_START) { '.' } else { '; opening it.' })", 'dim')
            Say
            Start-V2pn
            return
        }

        $cpu = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
        $arch = if ($cpu -eq 'ARM64') { 'arm64' } else { 'x64' }
        $name = "V2pn-windows-$arch.zip"

        $ui.step = 'Release'
        Say -Temp @('   ', '', $spin[2], 'brand', " Looking for the newest V2pn$($g.ell)", 'dim')
        $manifest = (Get-Text "$source/latest/download/release.json") | ConvertFrom-Json
        $file = $manifest.files | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if (-not $file) { throw "V2pn $($manifest.version) has no package for this computer ($name)." }
        $partnerName = if ($partner) { Get-PartnerName } else { $null }
        $version = [string]$manifest.version
        $published = ''
        try { $published = ([datetime]$manifest.published).ToUniversalTime().ToString('d MMM yyyy', $inv) } catch { }

        Fact 'Version' @($version, 'bold', $(if ($published) { "   published $published" } else { '' }), 'dim')
        Fact 'Package' @($name, '', "   $(MB $file.size), Windows $(if ($arch -eq 'arm64') { 'on ARM' } else { 'x64' })", 'dim')
        if ($partner) { Fact 'Provider' @($partnerName, 'bold', "   its tab in V2pn", 'dim') }
        Fact 'From' @(($source -replace '^https?://', ''), '')
        Fact 'Into' @($dir, '')
        Fact 'Rights' @('this Windows user only, no administrator needed', '')
        Say

        $work = Join-Path ([IO.Path]::GetTempPath()) ('v2pn-install-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force $work | Out-Null
        $zip = Join-Path $work $name

        $ui.step = 'Download'
        $got = Save-Package "$source/download/v$version/$name" $zip ([long]$file.size)
        $secs = [Math]::Max(0.1, $got.seconds)
        Step 'Download' @((MB $got.size), '', [string]::Format($inv, '   in {0:0.#} s, {1:0.0} MB/s', $secs, ($got.size / 1MB / $secs)), 'dim')

        $ui.step = 'Check'
        if ($got.size -ne [long]$file.size -or $got.sha256 -ne $file.sha256) {
            throw 'The download does not match the release (size or SHA-256).'
        }
        Step 'Check' @('size and SHA-256 match the release', '', "   $($got.sha256.Substring(0, 8))$($g.ell)$($got.sha256.Substring(56))", 'dim')

        $ui.step = 'Install'
        $put = Install-Package $zip $dir
        $ui.installed = $true
        $what = if ($put.cores.Count) { 'V2pn with ' + (Words $put.cores) } else { 'V2pn' }
        Step 'Install' @($what, '', "   $($put.count) files, $(MB $put.size)", 'dim')
        if ($partner) {
            $ui.step = 'Provider'
            Add-PartnerMarker
            Step 'Provider' @("$partnerName's tab", '', '   v2pn-partner.txt beside V2pn.exe', 'dim')
        }

        if ($env:V2PN_NO_SHORTCUTS) { Skip 'Shortcuts' 'none (V2PN_NO_SHORTCUTS)' }
        else {
            $ui.step = 'Shortcuts'
            $shell = New-Object -ComObject WScript.Shell
            foreach ($folder in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
                $link = $shell.CreateShortcut((Join-Path $folder 'V2pn.lnk'))
                $link.TargetPath = $exe
                $link.WorkingDirectory = $dir
                $link.Save()
            }
            Step 'Shortcuts' @('Start menu and desktop', '')
        }

        if ($env:V2PN_NO_IMPORT) { Skip 'v2rayN' 'left alone (V2PN_NO_IMPORT)' }
        else {
            $ui.step = 'v2rayN'
            # Read by V2pn at its first start (V2pnImport.AutoMarker); Explorer passes it no arguments.
            $temps = Join-Path $dir 'guiTemps'
            New-Item -ItemType Directory -Force $temps | Out-Null
            Set-Content -Path (Join-Path $temps 'V2pnImport.txt') -Value 'auto' -Encoding Ascii
            Step 'v2rayN' @('if it is here with servers, V2pn takes them over at its first start', '')
        }

        Say
        Say @('   ', '', "V2pn $version", 'bold', " is ready.$(if ($env:V2PN_NO_START) { '' } else { ' Opening it now.' })", '')
        Say @('   It keeps itself up to date. Questions and problems: ', 'dim', "$repo/issues", 'link')
        Say
        Start-V2pn
    }
    catch {
        if (-not $live) { throw }
        $problem = $_.Exception
        while ($problem.InnerException) { $problem = $problem.InnerException }
        Say @('   ', '', $g.bad, 'bad', " $($ui.step.PadRight(11))", '', $problem.Message, 'bad')
        Say
        if (-not $ui.installed) {
            Say @('   Nothing was installed. Run the command again, or download V2pn from ', 'dim', "$source/latest", 'link')
            Say
        }
    }
    finally {
        if ($live) {
            # Stopped with Ctrl+C on a progress line: the prompt goes on a line of its own. Write-Host
            # may refuse to write in a pipeline that is being stopped; the console does not.
            if ($ui.open) { [Console]::WriteLine() }
            try { [Console]::CursorVisible = $true } catch { }
        }
        if ($work) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
    }
}
