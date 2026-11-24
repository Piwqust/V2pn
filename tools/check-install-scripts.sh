#!/bin/bash
# Checks the release signature check of tools/install.sh without running the installer: takes
# V2PN_RELEASE_KEYS and v2pn_verify out of it and runs them on a signed release.json, on the same
# file with one byte changed, with a changed signature, with no signature and with another key.
#
#   tools/check-install-scripts.sh [release.json release.json.sig]
#
# The release is tools/install-test/ (V2pn 1.4.2 as published) unless two files are given. On a
# Mac, run it with the system's own tools, as a fresh Mac has them (LibreSSL's openssl):
#
#   PATH=/usr/bin:/bin:/usr/sbin:/sbin tools/check-install-scripts.sh
#
# tools/check-install-scripts.ps1 does the same for install.ps1 and checks that both scripts carry
# the keys of V2pnUpdateKeys.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
manifest="${1:-$here/install-test/release.json}"
signature="${2:-$here/install-test/release.json.sig}"
work="$(mktemp -d "${TMPDIR:-/tmp}/v2pn-check-install.XXXXXX")"
trap 'rm -rf "$work"' EXIT

# Only the two definitions, each from its first line to its end; nothing else of the installer runs.
awk '/^V2PN_RELEASE_KEYS=/ { print } /^v2pn_verify\(\) \{/ { on = 1 } on { print } on && /^\}/ { on = 0 }' \
    "$here/install.sh" >"$work/verify.sh"
if ! grep -q '^V2PN_RELEASE_KEYS=' "$work/verify.sh" || ! grep -q '^v2pn_verify() {' "$work/verify.sh"; then
    echo "FAIL  install.sh has no V2PN_RELEASE_KEYS or no v2pn_verify"
    exit 1
fi
# shellcheck source=/dev/null
. "$work/verify.sh"

echo "openssl: $(command -v openssl), $(openssl version)"
failed=0
# expect (0: accepted, 1: refused), what it is, release.json, release.json.sig
check() {
    local got=0 dir
    dir="$(mktemp -d "$work/case.XXXXXX")"
    v2pn_verify "$3" "$4" "$dir" || got=1
    if [ "$got" = "$1" ]; then
        echo "ok    install.sh: $2"
    else
        echo "FAIL  install.sh: $2"
        failed=$((failed + 1))
    fi
}
# Flips the lowest bit of the byte at an offset, in place.
flip() {
    local byte
    byte="$(dd if="$1" bs=1 skip="$2" count=1 2>/dev/null | xxd -p)"
    # shellcheck disable=SC2059 # the format is the byte itself
    printf "\\x$(printf '%02x' $((0x$byte ^ 1)))" | dd of="$1" bs=1 seek="$2" count=1 conv=notrunc 2>/dev/null
}

cp "$manifest" "$work/release.json"
cp "$signature" "$work/release.json.sig"
check 0 "the signed release.json is accepted" "$work/release.json" "$work/release.json.sig"

cp "$work/release.json" "$work/changed.json"
at=$(($(wc -c <"$work/changed.json") / 2))
flip "$work/changed.json" "$at"
cmp -s "$work/release.json" "$work/changed.json" && { echo "FAIL  could not change a byte"; exit 1; }
check 1 "release.json with one byte changed (at $at) is refused" "$work/changed.json" "$work/release.json.sig"

base64 --decode <"$work/release.json.sig" >"$work/signature.der"
flip "$work/signature.der" $(($(wc -c <"$work/signature.der") - 1))
base64 <"$work/signature.der" >"$work/changed.sig"
check 1 "a signature with one byte changed is refused" "$work/release.json" "$work/changed.sig"

: >"$work/empty.sig"
check 1 "no signature is refused" "$work/release.json" "$work/empty.sig"
printf 'not a signature\n' >"$work/garbage.sig"
check 1 "a signature that is not base64 is refused" "$work/release.json" "$work/garbage.sig"

# A P-256 key that signed nothing here.
other='MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHlwBBkG+MdpxUKEfp5BmAXDS87WL0HSq/MaE+8vjRiBdXQmZ0QqhGtNJvS43DXNJtptk7qg4P8L+4R//Zu6B9A=='
real="$V2PN_RELEASE_KEYS"
V2PN_RELEASE_KEYS="$other"
check 1 "a signature by another key is refused" "$work/release.json" "$work/release.json.sig"
V2PN_RELEASE_KEYS="$other|$real"
check 0 "with two keys (a new one next to the old), either signs" "$work/release.json" "$work/release.json.sig"
V2PN_RELEASE_KEYS="$real"

if [ "$failed" != 0 ]; then
    echo "$failed check(s) failed."
    exit 1
fi
echo "All checks passed."
