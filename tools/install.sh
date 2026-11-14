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
# Options, as environment variables in front of "bash":
#   V2PN_DIR=~/Apps        install somewhere else
#   V2PN_NO_IMPORT=1       do not move anything from v2rayN
#   V2PN_NO_START=1        do not open V2pn
#   V2PN_SOURCE=<url>      another releases page or mirror (same layout as GitHub's)
#
# Everything is inside main, so a download cut short runs nothing.

main() {
    set -euo pipefail

    fail() { echo "V2pn: $*" >&2; exit 1; }

    [ "$(uname -s)" = Darwin ] || fail "this script is for macOS. On Windows: irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex"

    local source="${V2PN_SOURCE:-https://github.com/Piwqust/V2pn/releases}"
    source="${source%/}"
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
        echo "V2pn is already installed in $dir. It keeps itself up to date; opening it."
        start
        return 0
    fi

    # A Terminal running under Rosetta reports x86_64 on Apple silicon; the hardware flag does not.
    local arch=x64
    if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then
        arch=arm64
    fi
    local name="V2pn-macos-$arch.zip"

    echo "Looking for the newest V2pn..."
    local manifest
    manifest="$(curl -fsSL --retry 3 "$source/latest/download/release.json")" || fail "could not read the list of releases."
    # macOS has no jq; JavaScript for Automation is always there.
    local found
    found="$(osascript -l JavaScript -e '
        function run(argv) {
            var m = JSON.parse(argv[0]);
            var f = (m.files || []).filter(function (x) { return x.name === argv[1]; })[0];
            return f ? [m.version, f.size, f.sha256].join(" ") : "";
        }' "$manifest" "$name")"
    [ -n "$found" ] || fail "the newest release has no package for this Mac ($name)."
    local version size sha256
    read -r version size sha256 <<<"$found"

    # Not local: the EXIT trap runs after main has returned.
    V2PN_WORK="$(mktemp -d -t v2pn-install)"
    trap 'rm -rf "$V2PN_WORK"' EXIT
    local work="$V2PN_WORK"

    echo "Downloading V2pn $version ($((size / 1048576)) MB)..."
    curl -fL --retry 3 --progress-bar -o "$work/$name" "$source/download/v$version/$name" || fail "the download failed. Nothing was installed; try again."
    if [ "$(stat -f %z "$work/$name")" != "$size" ] || [ "$(shasum -a 256 "$work/$name" | cut -d' ' -f1)" != "$sha256" ]; then
        fail "the download does not match the release. Nothing was installed; try again."
    fi

    echo "Installing to $dir..."
    # ditto keeps the execute bits and the app's signature, which unzip can break.
    ditto -x -k "$work/$name" "$work/unpacked"
    [ -d "$work/unpacked/V2pn.app" ] || fail "the package has no V2pn.app."
    mkdir -p "$dir"
    ditto "$work/unpacked/V2pn.app" "$app"
    # V2pn has no Apple Developer ID, and macOS would refuse a quarantined copy of it.
    xattr -dr com.apple.quarantine "$app" 2>/dev/null || true

    if [ -z "${V2PN_NO_IMPORT:-}" ]; then
        # Read by V2pn at its first start (V2pnImport.AutoMarker).
        mkdir -p "$data/guiTemps"
        printf 'auto' >"$data/guiTemps/V2pnImport.txt"
    fi

    echo "V2pn $version is installed in $dir."
    start
}

main "$@"
