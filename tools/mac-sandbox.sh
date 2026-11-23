#!/usr/bin/env bash
# Runs a V2pn build on a Mac in a throwaway bundle that cannot disturb the V2pn you use there.
# The Mac's counterpart of tools/sandbox.ps1; run it on the Mac (over ssh is fine).
#
#   tools/mac-sandbox.sh --from <publish folder> --dest <folder> [--port 20808] [--cores-from <folder>]
#                        [--lang ru] [--keep-data] [--name V2pnSandbox] [--real-proxy] [--launch] [--front]
#                        [--env NAME=VALUE]... [-- app arguments]
#   tools/mac-sandbox.sh --dest <folder> --launch        start what is already there
#   tools/mac-sandbox.sh --dest <folder> --stop
#   tools/mac-sandbox.sh --dest <folder> --pid           the running sandbox's process id, for UI scripting
#
# --from is the output of, on any system with the .NET SDK:
#   dotnet publish v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release -r osx-arm64 -p:SelfContained=true -o <folder>
# sent to the Mac, for example:
#   tar -C <folder> -cf - . | ssh mac 'mkdir -p ~/v2pn-dev/pub && tar -C ~/v2pn-dev/pub -xf -'
#
# What keeps it apart from the installed V2pn:
#   - the bundle has no NotStoreConfigHere.txt, so settings, logs and cores stay inside <dest>/V2pn.app, and V2pn
#     takes a copy with its own data for a separate instance (Program.InstanceName);
#   - its settings leave the system proxy alone, keep VPN mode off and listen on --port, not 10808;
#   - whatever its settings become later (a person can move v2rayN's settings into it, system proxy mode and port
#     10808 included), it never changes the Mac's system proxy: the bundle starts V2pn with V2PN_KEEP_SYSTEM_PROXY=1,
#     which builds that are not releases honour. --real-proxy leaves that out, for testing the proxy itself with the
#     installed V2pn closed. It never writes the sign-in agent either: that file belongs to the installed app;
#   - its bundle id and its name (V2pnSandbox: the program's file, the menu bar, the Dock) are its own and it claims
#     no v2pn:// links, so macOS never opens it instead of the installed app and scripts can address it:
#       osascript -e 'tell application "System Events" to tell process "V2pnSandbox" to get name of every window'
#   - --stop ends only processes started from <dest>, never anything by name.
#
# --cores-from copies bin/ (cores, geo files) from a folder that has it; the installed V2pn keeps its own in
# "$HOME/Library/Application Support/V2pn". --keep-data keeps the sandbox's settings and cores when --from replaces
# the program. --launch starts it without bringing it to the front; --front does bring it there. The standard
# output and errors go to <dest>/out.log.
#
# Looking at it over ssh, where the screen cannot be captured: start it with --env V2PN_SHOTS=<folder>, then
# "touch <folder>/shoot" and V2pn saves into that folder its windows as macOS shows them, title bar buttons and
# popups included (mac-N.png), the same windows as Avalonia alone draws them and the status item's pictures
# (MainWindow.Debug.cs). The menus are read through System Events, as above.
set -euo pipefail

FROM="" DEST="" PORT=20808 CORES="" LANG_CODE="" KEEP=0 LAUNCH=0 FRONT=0 STOP=0 PID=0 NAME="" KEEP_PROXY=1
ENVS=() APP_ARGS=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --from) FROM="$2"; shift 2 ;;
        --dest) DEST="$2"; shift 2 ;;
        --port) PORT="$2"; shift 2 ;;
        --cores-from) CORES="$2"; shift 2 ;;
        --lang) LANG_CODE="$2"; shift 2 ;;
        --name) NAME="$2"; shift 2 ;;
        --real-proxy) KEEP_PROXY=0; shift ;;
        --env) ENVS+=("$2"); shift 2 ;;
        --keep-data) KEEP=1; shift ;;
        --launch) LAUNCH=1; shift ;;
        --front) FRONT=1; shift ;;
        --stop) STOP=1; shift ;;
        --pid) PID=1; shift ;;
        --) shift; APP_ARGS=("$@"); break ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$DEST" ]] || { echo "usage: mac-sandbox.sh --dest <folder> [--from <publish folder>] [--launch] [--stop]" >&2; exit 2; }
[[ "$(uname)" == "Darwin" ]] || { echo "mac-sandbox.sh runs on macOS only" >&2; exit 2; }

mkdir -p "$DEST"
DEST="$(cd "$DEST" && pwd -P)"
APP="$DEST/V2pn.app"
MACOS="$APP/Contents/MacOS"
# The sandbox's program and bundle name. UI scripting (System Events) addresses a window's controls through the
# name of the program's file, so two processes called V2pn cannot be told apart there. A second sandbox that runs
# beside the first takes a name of its own (--name); a sandbox that exists keeps the name it was made with.
if [[ -z "$NAME" && -f "$APP/Contents/Info.plist" ]]; then
    NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Contents/Info.plist" 2>/dev/null || true)"
fi
PROCESS_NAME="${NAME:-V2pnSandbox}"
[[ "$PROCESS_NAME" =~ ^[A-Za-z0-9]+$ ]] || { echo "--name takes letters and digits only" >&2; exit 2; }
BUNDLE_ID="io.github.piwqust.v2pn.$(echo "$PROCESS_NAME" | tr '[:upper:]' '[:lower:]' | sed 's/^v2pn//')"

# Processes whose program lies under the sandbox: V2pn itself and the cores it started.
sandbox_pids() { pgrep -f "^$DEST/" || true; }
app_pid() { pgrep -f "^$MACOS/$PROCESS_NAME( |$)" | head -1 || true; }

stop_sandbox() {
    local pid
    pid="$(app_pid)"
    if [[ -n "$pid" ]]; then
        echo "Asking V2pn ($pid) to quit"
        # The quit an app gets from the Dock or at sign-out: V2pn stops its core and saves its settings.
        osascript -e "tell application id \"$BUNDLE_ID\" to quit" >/dev/null 2>&1 || true
        for _ in $(seq 1 30); do
            kill -0 "$pid" 2>/dev/null || break
            sleep 0.2
        done
    fi
    for pid in $(sandbox_pids); do
        echo "Stopping $pid"
        kill "$pid" 2>/dev/null || true
    done
}

if [[ $PID -eq 1 ]]; then app_pid; exit 0; fi
if [[ $STOP -eq 1 ]]; then stop_sandbox; exit 0; fi

if [[ -n "$FROM" ]]; then
    [[ -f "$FROM/V2pn" ]] || { echo "No V2pn program in $FROM" >&2; exit 1; }
    stop_sandbox
    SAVED=""
    if [[ $KEEP -eq 1 && -d "$MACOS" ]]; then
        SAVED="$(mktemp -d)"
        for d in guiConfigs bin; do
            if [[ -d "$MACOS/$d" ]]; then mv "$MACOS/$d" "$SAVED/$d"; fi
        done
    fi
    # Put together in a plain folder and signed there: inside a bundle codesign takes the main program for the whole
    # app and tries to seal the folder, data files and all (tools/package-macos.sh does the same).
    STAGE="$DEST/stage"
    rm -rf "$APP" "$STAGE"
    mkdir -p "$STAGE" "$APP/Contents/Resources"
    cp -R "$FROM"/. "$STAGE/"
    rm -f "$STAGE/NotStoreConfigHere.txt"
    rm -rf "$STAGE/guiConfigs" "$STAGE/guiLogs" "$STAGE/guiTemps" "$STAGE/binConfigs"
    if [[ -n "$SAVED" ]]; then
        for d in guiConfigs bin; do
            if [[ -d "$SAVED/$d" ]]; then rm -rf "$STAGE/$d"; mv "$SAVED/$d" "$STAGE/$d"; fi
        done
        rm -rf "$SAVED"
    fi
    if [[ -n "$CORES" && ! -d "$STAGE/bin" ]]; then
        [[ -d "$CORES/bin" ]] || { echo "No bin folder in $CORES" >&2; exit 1; }
        cp -R "$CORES/bin" "$STAGE/bin"
    fi
    if [[ -f "$STAGE/v2rayN.icns" ]]; then cp "$STAGE/v2rayN.icns" "$APP/Contents/Resources/AppIcon.icns"; fi
    mv "$STAGE/V2pn" "$STAGE/$PROCESS_NAME"
    chmod +x "$STAGE/$PROCESS_NAME"
    VERSION="$(/usr/bin/strings "$STAGE/$PROCESS_NAME" 2>/dev/null | grep -m1 -E '^[0-9]+\.[0-9]+\.[0-9]+-dev$' || true)"
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
  </array>
  <key>CFBundleDisplayName</key>
  <string>$PROCESS_NAME</string>
  <key>CFBundleName</key>
  <string>$PROCESS_NAME</string>
  <key>CFBundleExecutable</key>
  <string>$PROCESS_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION:-0.0.0-dev}</string>
  <key>CFBundleVersion</key>
  <string>${VERSION:-0.0.0-dev}</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.6</string>
  <key>LSEnvironment</key>
  <dict>
    <key>V2PN_KEEP_SYSTEM_PROXY</key>
    <string>$KEEP_PROXY</string>
  </dict>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSHumanReadableCopyright</key>
  <string>V2pn contributors. Based on v2rayN by 2dust. GPL-3.0.</string>
</dict>
</plist>
EOF
    # Apple silicon runs nothing unsigned; an ad-hoc signature is enough for a copy that never leaves this Mac.
    while IFS= read -r -d '' file; do
        if file -b "$file" | grep -q 'Mach-O'; then
            codesign --verify "$file" 2>/dev/null || codesign --force --sign - "$file"
        fi
    done < <(find "$STAGE" -type f -print0)
    mv "$STAGE" "$MACOS"
    echo "Sandbox bundle ready: $APP"
fi

[[ -x "$MACOS/$PROCESS_NAME" ]] || { echo "No sandbox in $DEST yet; pass --from" >&2; exit 1; }

CONFIG="$MACOS/guiConfigs/guiNConfig.json"
if [[ ! -f "$CONFIG" ]]; then
    mkdir -p "$MACOS/guiConfigs"
    # SysProxyType 2 is "leave the system proxy alone" (ESysProxyType.Unchanged). Missing sections get defaults on load.
    LANG_LINE=""
    [[ -n "$LANG_CODE" ]] && LANG_LINE="\"CurrentLanguage\": \"$LANG_CODE\", "
    cat > "$CONFIG" <<EOF
{
  "Inbound": [ { "Protocol": "socks", "LocalPort": $PORT, "UdpEnabled": true, "SniffingEnabled": true, "RouteOnly": false } ],
  "SystemProxyItem": { "SysProxyType": 2 },
  "TunModeItem": { "EnableTun": false },
  "UiItem": { ${LANG_LINE}"V2pnProxyMethod": 2, "V2pnPreferTun": false, "EnableAutoAdjustMainLvColWidth": true },
  "GuiItem": { "AutoRun": false, "DisplayRealTimeSpeed": true }
}
EOF
    echo "Sandbox config written: system proxy untouched, socks port $PORT"
fi

if [[ $LAUNCH -eq 1 ]]; then
    if [[ -n "$(app_pid)" ]]; then
        echo "The sandbox is already running ($(app_pid))"
        exit 0
    fi
    OPEN=(open -n)
    [[ $FRONT -eq 1 ]] || OPEN+=(-g)
    OPEN+=(--stdout "$DEST/out.log" --stderr "$DEST/out.log")
    # A shell's proxy variables (often the installed V2pn) would carry the sandbox's own requests.
    for name in HTTP_PROXY HTTPS_PROXY ALL_PROXY http_proxy https_proxy all_proxy; do
        unset "$name" || true
    done
    for pair in ${ENVS[@]+"${ENVS[@]}"}; do
        OPEN+=(--env "$pair")
    done
    OPEN+=("$APP")
    if [[ ${#APP_ARGS[@]} -gt 0 ]]; then
        OPEN+=(--args "${APP_ARGS[@]}")
    fi
    : > "$DEST/out.log"
    "${OPEN[@]}"
    for _ in $(seq 1 50); do
        [[ -n "$(app_pid)" ]] && break
        sleep 0.2
    done
    PIDNOW="$(app_pid)"
    [[ -n "$PIDNOW" ]] || { echo "V2pn did not start; see $DEST/out.log" >&2; exit 1; }
    echo "Started V2pn ($PIDNOW) from $APP"
fi
