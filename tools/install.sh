#!/bin/bash
# Installs V2pn on a Mac with one command:
#
#   curl -fsSL https://github.com/Piwqust/V2pn/releases/latest/download/install.sh | bash
#
# Downloads the newest release for this Mac (Apple silicon or Intel), checks it against the
# release's list of files (size and SHA-256), puts V2pn.app into /Applications (or ~/Applications
# when /Applications is not writable) and opens it. At that first start V2pn moves over a v2rayN
# with servers found on this Mac: servers, subscriptions, routing and settings. v2rayN is closed
# and stops starting at login; its own files are left as they are.
#
# A provider's people install this same V2pn; its tab comes from the provider's own button
# (a v2pn://add link with the provider's id) or a sign-in by code, not from the installer.
#
# Options, as environment variables in front of "bash":
#   V2PN_DIR=~/Apps        install somewhere else
#   V2PN_NO_IMPORT=1       do not move anything from v2rayN
#   V2PN_NO_START=1        do not open V2pn
#   V2PN_SOURCE=<url>      another releases page or mirror (same layout as GitHub's)
#   NO_COLOR=1             no colours
#
# Everything is inside main, so a download cut short runs nothing. Written for the bash 3.2 that
# macOS has in /bin.

# Runs however the script ends: stops a download or unpacking still going, removes the temporary
# files and gives the cursor back.
v2pn_cleanup() {
    if [ -n "${V2PN_PID:-}" ]; then kill "$V2PN_PID" 2>/dev/null || true; fi
    if [ -n "${V2PN_WORK:-}" ]; then rm -rf "$V2PN_WORK"; fi
    # Stopped in the middle of a progress line: the shell's prompt goes on a line of its own.
    if [ -n "${V2PN_OPEN:-}" ]; then printf '\n'; fi
    if [ -n "${V2PN_CURSOR:-}" ]; then printf '\033[?25h'; fi
}

main() {
    set -euo pipefail

    local repo=https://github.com/Piwqust/V2pn
    local source="${V2PN_SOURCE:-$repo/releases}"
    source="${source%/}"

    # ---- The screen ---------------------------------------------------------------------------
    # live: a terminal the installer can redraw, so progress stays on one line; anywhere else (a log,
    # a pipe) it writes finished lines only.
    local live=0 colour=0 truecolour=0 cols=80
    if [ -t 1 ]; then live=1; fi
    if [ "$live" = 1 ] && [ -z "${NO_COLOR:-}" ] && [ "${TERM:-dumb}" != dumb ]; then colour=1; fi
    case "${COLORTERM:-}" in truecolor | 24bit) truecolour=1 ;; esac
    if [ "$live" = 1 ]; then
        cols="$(stty size </dev/tty 2>/dev/null | cut -d' ' -f2)" || true
        case "$cols" in '' | *[!0-9]*) cols=80 ;; esac
    fi

    # The escape for a colour: 24-bit where the terminal says it has it (iTerm2, Terminal on recent
    # macOS), else the nearest of the 256.
    cube() { if [ "$1" -lt 48 ]; then echo 0; elif [ "$1" -lt 115 ]; then echo 1; else echo $((($1 - 35) / 40)); fi; }
    rgb() {
        [ "$colour" = 1 ] || return 0
        if [ "$truecolour" = 1 ]; then
            printf '\033[38;2;%d;%d;%dm' "$1" "$2" "$3"
        else
            printf '\033[38;5;%dm' $((16 + 36 * $(cube "$1") + 6 * $(cube "$2") + $(cube "$3")))
        fi
    }
    # The app's Signal red and its neighbours.
    local BRAND OK BAD DIM TRACK LINK BOLD='' OFF=''
    BRAND="$(rgb 255 61 31)"
    OK="$(rgb 34 181 115)"
    BAD="$(rgb 255 90 78)"
    DIM="$(rgb 128 134 145)"
    TRACK="$(rgb 72 76 84)"
    LINK="$(rgb 90 160 255)"
    if [ "$colour" = 1 ]; then BOLD=$'\033[1m' OFF=$'\033[0m'; fi
    local spin=(⠋ ⠙ ⠹ ⠸ ⠼ ⠴ ⠦ ⠧ ⠇ ⠏)
    local frame=0 step='' installed=0

    # A finished line, and a line the next one overwrites (progress; written only where the screen
    # can be redrawn).
    say() {
        if [ "$live" = 1 ]; then printf '\r\033[2K'; fi
        printf '%s\n' "${1:-}"
        V2PN_OPEN=
    }
    temp() {
        [ "$live" = 1 ] || return 0
        printf '\r\033[2K%s' "$1"
        V2PN_OPEN=1
    }
    pad() { printf '%-11s' "$1"; }
    ok() { say "   ${OK}✓${OFF} $(pad "$1")$2"; }
    fact() { say "     ${DIM}$(pad "$1")${OFF}$2"; }
    mb() { awk -v b="$1" 'BEGIN { printf "%.1f MB", b / 1048576 }'; }
    fail() {
        say "   ${BAD}✗${OFF} $(pad "$step")${BAD}$*${OFF}" >&2
        say >&2
        if [ "$installed" = 0 ]; then
            say "   ${DIM}Nothing was installed. Run the command again, or download V2pn from${OFF} ${LINK}$source/latest${OFF}" >&2
            say >&2
        fi
        exit 1
    }

    # A progress line: spinner, bar and a tail of numbers that is cut short in a narrow terminal.
    progress() { # label, done, total, tail, short tail
        [ "$live" = 1 ] || return 0
        frame=$((frame + 1))
        local tail="$4" room bar filled i fill='' rest=''
        room=$((cols - 1 - 16 - 2 - ${#tail}))
        if [ "$room" -lt 10 ]; then tail="$5"; room=$((cols - 1 - 16 - 2 - ${#tail})); fi
        bar=$room
        if [ "$bar" -gt 30 ]; then bar=30; fi
        if [ "$bar" -lt 6 ]; then bar=6; fi
        filled=0
        if [ "$3" -gt 0 ]; then filled=$(($2 * bar / $3)); fi
        if [ "$filled" -gt "$bar" ]; then filled=$bar; fi
        for ((i = 0; i < filled; i++)); do fill+='━'; done
        for ((i = filled; i < bar; i++)); do rest+='━'; done
        temp "   ${BRAND}${spin[$((frame % 10))]}${OFF} $(pad "$1")${BRAND}${fill}${OFF}${TRACK}${rest}${OFF}  ${DIM}${tail}${OFF}"
    }
    # Waits for the background job in V2PN_PID, redrawing with the given command every tenth of a second.
    follow() {
        while kill -0 "$V2PN_PID" 2>/dev/null; do
            "$@"
            sleep 0.1
        done
        local status=0
        wait "$V2PN_PID" || status=$?
        V2PN_PID=
        return "$status"
    }

    header() {
        say
        if [ "$live" = 1 ] && [ "$cols" -ge 44 ]; then
            local rows=(
                '##7   ##7######7 ######7 ###7   ##7'
                '##!   ##!L====##7##F==##7####7  ##!'
                '##!   ##! #####FJ######FJ##F##7 ##!'
                'L##7 ##FJ##F===J ##F===J ##!L##7##!'
                ' L####FJ #######7##!     ##! L####!'
                '  L===J  L======JL=J     L=J  L===J'
            )
            local r i ch kind last row out glyph face shade R G B
            # The 256-colour cube has too few steps between red and orange for a computed gradient.
            local faces256=(196 196 202 202 208 208)
            for r in 0 1 2 3 4 5; do
                # Signal red at the top to orange at the bottom; the outlines are a dark shade of the row.
                R=$((255 - 13 * r / 5)) G=$((61 + 79 * r / 5)) B=$((31 + 9 * r / 5))
                face="$(rgb $R $G $B)"
                shade="$(rgb $((R * 42 / 100)) $((G * 42 / 100)) $((B * 42 / 100)))"
                if [ "$colour" = 1 ] && [ "$truecolour" = 0 ]; then
                    face="$(printf '\033[38;5;%dm' "${faces256[$r]}")" shade=$'\033[38;5;52m'
                fi
                row="${rows[$r]}" out='   ' last=''
                for ((i = 0; i < ${#row}; i++)); do
                    ch="${row:i:1}"
                    case "$ch" in
                        '#') kind=face glyph='█' ;;
                        ' ') kind=none glyph=' ' ;;
                        '=') kind=shade glyph='═' ;;
                        '!') kind=shade glyph='║' ;;
                        F) kind=shade glyph='╔' ;;
                        7) kind=shade glyph='╗' ;;
                        L) kind=shade glyph='╚' ;;
                        *) kind=shade glyph='╝' ;;
                    esac
                    if [ "$kind" != "$last" ]; then
                        case "$kind" in face) out+="$OFF$face" ;; shade) out+="$OFF$shade" ;; *) out+="$OFF" ;; esac
                        last=$kind
                    fi
                    out+="$glyph"
                done
                say "$out$OFF"
            done
            say
            say "   A simple VPN client for Windows and macOS, by ${BOLD}Piwqust${OFF}"
        else
            say "   ${BOLD}V2pn${OFF}: a simple VPN client for Windows and macOS, by Piwqust"
        fi
        say "   ${DIM}Built on v2rayN by 2dust · GPL-3.0 ·${OFF} ${LINK}${repo#https://}${OFF}"
        say
    }

    trap v2pn_cleanup EXIT
    trap 'exit 130' INT TERM
    if [ "$live" = 1 ]; then
        V2PN_CURSOR=1
        printf '\033[?25l'
    fi
    header

    [ "$(uname -s)" = Darwin ] || fail "this script is for macOS. On Windows: irm $source/latest/download/install.ps1 | iex"

    local dir
    if [ -n "${V2PN_DIR:-}" ]; then
        dir="$V2PN_DIR"
    elif [ -w /Applications ]; then
        dir=/Applications
    else
        dir="$HOME/Applications"
    fi
    local app="$dir/V2pn.app"
    # Where V2pn keeps its settings on a Mac (LocalApplicationData/V2pn).
    local data="$HOME/Library/Application Support/V2pn"

    start() {
        [ -z "${V2PN_NO_START:-}" ] || return 0
        open "$app"
    }

    if [ -d "$app" ]; then
        local have
        have="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app/Contents/Info.plist" 2>/dev/null)" || have=''
        ok Installed "V2pn${have:+ $have} is already in ${BOLD}$app${OFF}"
        if [ -n "${V2PN_NO_START:-}" ]; then say "     ${DIM}It keeps itself up to date.${OFF}"; else say "     ${DIM}It keeps itself up to date; opening it.${OFF}"; fi
        say
        start
        return 0
    fi

    # A Terminal running under Rosetta reports x86_64 on Apple silicon; the hardware flag does not.
    local arch=x64 kind='Intel'
    # By its full path: /usr/sbin is not on every PATH, and without it an M1 would get the Intel build.
    if [ "$(/usr/sbin/sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then
        arch=arm64 kind='Apple silicon'
    fi
    local name="V2pn-macos-$arch.zip"

    V2PN_WORK="$(mktemp -d -t v2pn-install)"
    local work="$V2PN_WORK" zip="$V2PN_WORK/$name"

    step=Release
    temp "   ${BRAND}${spin[2]}${OFF} ${DIM}Looking for the newest V2pn…${OFF}"
    local manifest
    manifest="$(curl -fsSL --retry 3 "$source/latest/download/release.json" 2>"$work/error")" ||
        fail "could not read the list of files of the newest release: $(sed 's/^curl: ([0-9]*) //' "$work/error" | tail -n 1)"
    # macOS has no jq; JavaScript for Automation is always there.
    local found
    found="$(osascript -l JavaScript -e '
        function run(argv) {
            var m = JSON.parse(argv[0]);
            var f = (m.files || []).filter(function (x) { return x.name === argv[1]; })[0];
            if (!f) return "";
            var d = new Date(m.published || ""), when = "";
            if (!isNaN(d)) when = d.getUTCDate() + " " + "Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec".split(" ")[d.getUTCMonth()] + " " + d.getUTCFullYear();
            return [m.version, f.size, f.sha256, when].join(" ");
        }' "$manifest" "$name")" || found=''
    [ -n "$found" ] || fail "the newest release has no package for this Mac ($name)."
    local version size sha256 published
    read -r version size sha256 published <<<"$found"

    fact Version "${BOLD}$version${OFF}${published:+   ${DIM}published $published${OFF}}"
    fact Package "$name   ${DIM}$(mb "$size"), $kind${OFF}"
    fact From "${source#https://}"
    fact Into "$app"
    fact macOS "the download flag is taken off, so it opens without a warning"
    say

    # curl's own meter, written to a file, gives the speed and the time left; the file's size gives
    # the bar. --write-out leaves the totals for the finished line.
    step=Download
    curl -fL --retry 3 --connect-timeout 20 -o "$zip" -w '%{time_total} %{speed_download}\n' \
        "$source/download/v$version/$name" >"$work/totals" 2>"$work/meter" &
    V2PN_PID=$!
    download_frame() {
        local got=0 line speed='' left='' tail
        got="$(stat -f %z "$zip" 2>/dev/null)" || got=0
        line="$(tail -c 200 "$work/meter" 2>/dev/null | tr '\r' '\n' | grep -E '^ *[0-9]' | tail -n 1)" || line=''
        # shellcheck disable=SC2086 # split curl's columns: speed now is the 12th, time left the 11th
        set -- $line
        if [ $# -ge 12 ]; then
            case "${12}" in
                *k) speed="${12%k} KB/s" ;;
                *M) speed="${12%M} MB/s" ;;
                *G) speed="${12%G} GB/s" ;;
                *) speed="${12} B/s" ;;
            esac
            case "${11}" in --*) left='-:--' ;; *) left="${11#0:}" left="${left#0}" ;; esac
        fi
        tail="$(awk -v g="$got" -v s="$size" 'BEGIN { printf "%3d%%  %.1f/%.1f MB", (s > 0 ? int(g * 100 / s) : 0), g / 1048576, s / 1048576 }')"
        progress Download "$got" "$size" "$tail${speed:+  $speed}${left:+  $left}" "${tail%%%*}%"
    }
    follow download_frame || fail "the download failed: $(grep -E '^curl:' "$work/meter" | tail -n 1 | sed 's/^curl: ([0-9]*) //')"
    ok Download "$(mb "$size")   ${DIM}$(awk '{ printf "in %.1f s, %.1f MB/s", $1, ($1 > 0 ? $2 / 1048576 : 0) }' "$work/totals")${OFF}"

    step=Check
    temp "   ${BRAND}${spin[4]}${OFF} $(pad Check)${DIM}SHA-256…${OFF}"
    local got_size got_sha
    got_size="$(stat -f %z "$zip")"
    got_sha="$(shasum -a 256 "$zip" | cut -d' ' -f1)"
    if [ "$got_size" != "$size" ] || [ "$got_sha" != "$sha256" ]; then
        fail "the download does not match the release (size or SHA-256)."
    fi
    ok Check "size and SHA-256 match the release   ${DIM}${got_sha:0:8}…${got_sha:56}${OFF}"

    # ditto keeps the execute bits and the app's signature, which unzip can break. The bar follows
    # the unpacked size against the package's own list.
    step=Install
    local unpacked_total
    unpacked_total="$(unzip -l "$zip" | tail -n 1 | awk '{ print $1 }')" || unpacked_total=0
    case "$unpacked_total" in '' | *[!0-9]*) unpacked_total=0 ;; esac
    ditto -x -k "$zip" "$work/unpacked" &
    V2PN_PID=$!
    install_frame() {
        local kb=0
        kb="$(du -sk "$work/unpacked" 2>/dev/null | cut -f1)" || kb=0
        case "$kb" in '' | *[!0-9]*) kb=0 ;; esac
        progress Install $((kb * 1024)) "$unpacked_total" "unpacking V2pn.app" ""
    }
    follow install_frame || fail "could not unpack the package."
    [ -d "$work/unpacked/V2pn.app" ] || fail "the package has no V2pn.app."
    mkdir -p "$dir"
    mv "$work/unpacked/V2pn.app" "$app" || fail "could not put V2pn.app into $dir."
    installed=1
    # V2pn has no Apple Developer ID, and macOS would refuse a quarantined copy of it.
    xattr -dr com.apple.quarantine "$app" 2>/dev/null || true
    local files cores='' core
    files="$(find "$app" -type f | wc -l | tr -d ' ')"
    for core in xray:Xray sing_box:sing-box mihomo:mihomo; do
        if [ -d "$app/Contents/MacOS/bin/${core%%:*}" ]; then cores="${cores:+$cores, }${core#*:}"; fi
    done
    case "$cores" in *,*) cores="${cores%, *} and ${cores##*, }" ;; esac
    ok Install "V2pn${cores:+ with $cores}   ${DIM}$files files, $(mb "$unpacked_total")${OFF}"

    if [ -n "${V2PN_NO_IMPORT:-}" ]; then
        say "   ${DIM}-${OFF} $(pad v2rayN)${DIM}left alone (V2PN_NO_IMPORT)${OFF}"
    else
        step=v2rayN
        # Read by V2pn at its first start (V2pnImport.AutoMarker).
        mkdir -p "$data/guiTemps"
        printf 'auto' >"$data/guiTemps/V2pnImport.txt"
        ok v2rayN "if it is here with servers, V2pn takes them over at its first start"
    fi

    say
    if [ -n "${V2PN_NO_START:-}" ]; then say "   ${BOLD}V2pn $version${OFF} is ready."; else say "   ${BOLD}V2pn $version${OFF} is ready. Opening it now."; fi
    say "   ${DIM}It keeps itself up to date. Questions and problems:${OFF} ${LINK}$repo/issues${OFF}"
    say
    start
}

main "$@"
