#!/usr/bin/env bash
# Builds V2pn's macOS packages. Run on a Mac (CI does): it needs codesign, ditto and hdiutil.
#
#   tools/package-macos.sh <version> <arm64|x64> [output folder]
#
# Writes into the output folder (dist/release/<version> by default):
#   V2pn-macos-<arch>.zip   V2pn.app zipped; what an installed V2pn downloads to update itself
#   V2pn-macos-<arch>.dmg   the same app in a disk image with an Applications shortcut, for people
#
# V2PN_NO_CORES=1 leaves the cores out (CI's smoke test: the app must start, not connect).
#
# Signing: every program in the bundle carries an ad-hoc signature, which Apple silicon requires
# to run anything at all. The app is not signed with an Apple Developer ID and not notarized, so
# the first launch of a downloaded copy needs "Open Anyway" in System Settings > Privacy &
# Security (README explains). The bundle as a whole is deliberately not sealed: it holds data
# files next to the programs (cores, geo files), a sealed bundle keeps their signatures in
# extended attributes, and those are lost when a zip is unpacked by anything but Finder, turning
# "unidentified developer" into "damaged".
set -euo pipefail

VERSION="${1:?usage: package-macos.sh <version> <arm64|x64> [output folder]}"
ARCH="${2:?usage: package-macos.sh <version> <arm64|x64> [output folder]}"
REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${3:-$REPO/dist/release/$VERSION}"
RID="osx-$ARCH"

case "$ARCH" in arm64|x64) ;; *) echo "architecture must be arm64 or x64" >&2; exit 2 ;; esac
[[ "$(uname)" == "Darwin" ]] || { echo "package-macos.sh runs on macOS only" >&2; exit 2; }

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APP="$WORK/V2pn.app"
MACOS="$APP/Contents/MacOS"
mkdir -p "$MACOS" "$APP/Contents/Resources"

PROPS=(-p:SelfContained=true "-p:V2pnVersion=$VERSION" -p:V2pnChannel=release)

echo "== $RID: AmazTool"
dotnet publish "$REPO/v2rayN/AmazTool/AmazTool.csproj" -c Release -r "$RID" -p:PublishTrimmed=true "${PROPS[@]}" -v q -nologo -o "$MACOS"
echo "== $RID: V2pn $VERSION"
dotnet publish "$REPO/v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj" -c Release -r "$RID" "${PROPS[@]}" -v q -nologo -o "$MACOS"
find "$MACOS" \( -name '*.pdb' -o -name '*.dbg' \) -delete

if [[ "${V2PN_NO_CORES:-}" != "1" ]]; then
    echo "== $RID: cores"
    read -r CORE_URL CORE_SHA < <(python3 -c '
import json, sys
target = json.load(open(sys.argv[1], encoding="utf-8-sig"))["targets"][sys.argv[2]]
print(target["url"], target["sha256"])' "$REPO/tools/cores.lock.json" "$RID")
    curl -fsSL --retry 3 -o "$WORK/cores.zip" "$CORE_URL"
    echo "$CORE_SHA  $WORK/cores.zip" | shasum -a 256 -c - >/dev/null \
        || { echo "The core bundle for $RID does not match tools/cores.lock.json. Nothing was packaged." >&2; exit 1; }
    unzip -q "$WORK/cores.zip" -d "$WORK/cores"
    # The bundle has a single top folder (v2rayN-macos-arm64/bin/...); its content goes next to V2pn.
    cp -R "$WORK"/cores/*/. "$MACOS/"
fi

cp "$MACOS/v2rayN.icns" "$APP/Contents/Resources/AppIcon.icns"
# With this file next to the program, V2pn keeps settings, logs and cores in
# ~/Library/Application Support/V2pn, so replacing the bundle (an update) never touches them.
echo "When this file exists, the app does not store its data in this folder." > "$MACOS/NotStoreConfigHere.txt"
chmod +x "$MACOS/V2pn" "$MACOS/AmazTool"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleLocalizations</key>
  <array>
    <string>en</string>
    <string>ru</string>
    <string>zh-Hans</string>
    <string>zh-Hant</string>
    <string>fa</string>
    <string>fr</string>
    <string>hu</string>
  </array>
  <key>CFBundleDisplayName</key>
  <string>V2pn</string>
  <key>CFBundleName</key>
  <string>V2pn</string>
  <key>CFBundleExecutable</key>
  <string>V2pn</string>
  <key>CFBundleIdentifier</key>
  <string>io.github.piwqust.v2pn</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
  <key>CFBundleVersion</key>
  <string>${VERSION}</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.6</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSHumanReadableCopyright</key>
  <string>V2pn contributors. Based on v2rayN by 2dust. GPL-3.0.</string>
</dict>
</plist>
EOF

# Ad-hoc sign every program that is not validly signed yet (the .NET SDK and the Go linker
# usually have signed theirs already).
IDENTITY="${CODESIGN_IDENTITY:--}"
while IFS= read -r -d '' file; do
    if file -b "$file" | grep -q 'Mach-O'; then
        codesign --verify "$file" 2>/dev/null || codesign --force --sign "$IDENTITY" "$file"
    fi
done < <(find "$MACOS" -type f -print0)

ZIP="$OUT/V2pn-macos-$ARCH.zip"
rm -f "$ZIP"
# ditto keeps the execute bits; --norsrc keeps Finder metadata folders (__MACOSX) out of the zip.
ditto -c -k --norsrc --keepParent "$APP" "$ZIP"

DMG="$OUT/V2pn-macos-$ARCH.dmg"
rm -f "$DMG"
mkdir "$WORK/dmg"
cp -R "$APP" "$WORK/dmg/"
ln -s /Applications "$WORK/dmg/Applications"
hdiutil create -quiet -volname "V2pn" -srcfolder "$WORK/dmg" -ov -format UDZO "$DMG"

echo "== $(basename "$ZIP")  $(du -h "$ZIP" | cut -f1)"
echo "== $(basename "$DMG")  $(du -h "$DMG" | cut -f1)"
