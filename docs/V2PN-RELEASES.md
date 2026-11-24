# V2pn: releases, updates, the mirror and merging v2rayN

## How V2pn updates for its users

- V2pn has its own version (`1.0.0`, `1.0.1`…), kept in `v2rayN/Directory.Build.targets`. The v2rayN version stays as it is: subscription panels use it to pick the config format (`User-Agent: v2rayN/7.25.6`).
- 20 seconds after start and then every 6 hours, V2pn asks the update sources for the manifest of the latest release. If none answers, it tries again in 15 minutes, and the pause doubles up to 6 hours. The same reason goes to the log once.
- With "Update V2pn automatically" on (the default), the package downloads in the background to `guiTemps/V2pnUpdate`, and "Restart and update" appears in the header. It installs on that button or at the next start, so an update never cuts a working connection.
- On Windows the download is the package without cores (`V2pn-windows-x64-update.zip`, about 60 MB instead of 140): an update does not replace the cores already installed.
- The Xray, sing-box and mihomo cores and the site lists update as in v2rayN: the Updates window, "Cores and lists".
- A build from source (`dotnet build`) shows version `1.0.0-dev` and does not update itself. Only packages built by `tools/release.ps1` and `tools/package-macos.sh` update.

### Sources

A source is an address that holds files in the layout of a GitHub releases page:

```text
<source>/latest/download/release.json        manifest of the latest release
<source>/latest/download/release.json.sig    its signature
<source>/download/v1.0.1/<file>              packages of version 1.0.1
```

The list of sources is set at build time by the `V2pnUpdateSources` property in `v2rayN/Directory.Build.targets`, separated by `|`. By default there is one: `https://github.com/Piwqust/V2pn/releases`. V2pn goes down the list in order and takes the first source that returns a manifest with a valid signature. It tries each source directly and through its own proxy, if that is running; the "Download through the VPN" switch in the Updates window decides which goes first. A 404 means "no releases yet" and is not an error.

### What exactly is checked

`release.json` lists the packages of a release: name, size, SHA-256.

```json
{
  "version": "1.0.1",
  "published": "2026-11-13T18:23:49Z",
  "files": [
    { "name": "V2pn-windows-x64.zip", "size": 147082097, "sha256": "6926bb79…" }
  ]
}
```

`release.json.sig` is the signature of that file (ECDSA P-256, SHA-256, DER in base64). The public keys are built into the app (`V2pnUpdateKeys` in the same file). The private key is not in the repository.

1. A manifest is accepted only if its signature matches one of the built-in keys.
2. The version in the manifest must be higher than the installed one. A downgrade is not possible.
3. The downloaded package must match the manifest in size and SHA-256; otherwise it is deleted and the next source is tried.
4. The manifest and the signature are kept next to a package waiting to be installed, and everything is checked again before installing. An archive that another program put in that folder does not count as an update.

So a source does not need to be trusted. A mirror, a proxy on the way or a spoofed DNS can only withhold an update; they cannot slip in their own.

The `<package>.sha256` files in a release are for people (`sha256sum -c`). The app does not read them.

### Installing and rolling back

AmazTool installs the package (`v2rayN/AmazTool/Installer.cs`, `InstallFlow.cs`):

1. Waits for V2pn to close.
2. Unpacks the package into a temporary folder next to the app (`.v2pn-update/stage`) and checks it. The installed app is not touched yet.
3. Moves the files into place; every old file goes to `.v2pn-update/backup`. If a file cannot be replaced, everything moved so far goes back.
4. Starts V2pn and watches it for 12 seconds. If the new version exits with an error in that time, it puts the old files back and starts the old version.
5. On a rollback it writes `guiTemps/V2pnUpdate/failed.txt` with the version number. V2pn does not install that version on its own again and shows the reason in the Updates window; "Try again" lifts the block.

`guiConfigs/` (settings, servers, subscriptions) is not touched. Files in `bin/` (the cores) that already exist are not overwritten. On macOS the whole `V2pn.app` is replaced, and the data lives in `~/Library/Application Support/V2pn`. The install writes its progress to `guiTemps/V2pnUpdate/update.log`.

## One-command install, winget and Homebrew

### The `install.ps1` script

```powershell
irm https://github.com/Piwqust/V2pn/releases/latest/download/install.ps1 | iex
```

`tools/install.ps1` is attached to every release: `tools/sign-release.ps1` or the signing step in `v2pn-release.yml` copies it. The `latest/download` link always opens the script of the latest published release. What it does:

1. Takes `release.json` and `release.json.sig` of the latest release and checks the signature with V2pn's release keys, which are built into the script as they are into the app. Without a valid signature it uses nothing from `release.json` and installs nothing, from GitHub and from a mirror (`V2PN_SOURCE`) alike. Windows PowerShell 5.1 runs on .NET Framework, which cannot read the key's usual form, so the script puts the key's point into a CNG key and turns the DER signature into the r|s pair itself.
2. Finds the package for its architecture (x64 or arm64), downloads it and checks the size and SHA-256 against the signed list. If they do not match, it installs nothing.
3. Unpacks to `%LOCALAPPDATA%\Programs\V2pn` and adds shortcuts to Start and the desktop.
4. Writes the word `auto` to `guiTemps\V2pnImport.txt`: at the first start V2pn moves over the data of the newest v2rayN that has servers (`V2pnImport.ApplyAtStart`).
5. Starts V2pn through Explorer rather than as a child of the console: Windows may close a program started from a terminal together with the terminal.

If V2pn is already in that folder, the script just starts it: from there V2pn updates itself. The script is ASCII only: Windows PowerShell reads downloaded text without an encoding as Latin-1.

Options are environment variables set before the command: `V2PN_DIR` (another folder), `V2PN_NO_IMPORT`, `V2PN_NO_SHORTCUTS`, `V2PN_NO_START`, `V2PN_SOURCE` (another releases page or a mirror). To test without publishing: put the package, `release.json`, `release.json.sig` and the script in `<folder>/<version>/`, serve it with `tools/fake_releases.py`, and run the command with `V2PN_SOURCE=http://127.0.0.1:<port>/releases`. The script trusts only the release keys, so the manifest has to be signed with the real key (`tools/manifest.cs`); there is deliberately no switch for another key.

The keys in both scripts must equal `V2pnUpdateKeys`. `tools/check-install-scripts.ps1` checks that and runs the scripts' signature check alone (taken out of the script, the installer itself never runs) on a published release in `tools/install-test/`: valid, one byte changed, a changed signature, no signature, another key. `tools/check-install-scripts.sh` does the same for `install.sh`. CI (`v2pn-ci.yml`) runs the first with Windows PowerShell 5.1 and PowerShell 7, the second on macOS with the system's own LibreSSL. When a key is replaced, add it to both scripts together with `V2pnUpdateKeys`.

### The `install.sh` script (macOS)

```bash
curl -fsSL https://github.com/Piwqust/V2pn/releases/latest/download/install.sh | bash
```

It is attached to the release together with `install.ps1` and does the same on a Mac:

1. Checks the signature of `release.json` (`release.json.sig`) with the same built-in release keys before it reads anything from it, with the Mac's own `openssl` (LibreSSL): the signature is DER already, which is what `openssl dgst -verify` takes. Without a valid signature it installs nothing, from a mirror too.
2. Picks the build by `sysctl hw.optional.arm64`, not by `uname -m`: Terminal under Rosetta on Apple silicon answers `x86_64`. It takes the size and SHA-256 from the signed `release.json`; a Mac has no jq, so JavaScript for Automation (`osascript -l JavaScript`) reads the JSON.
3. Unpacks with `ditto` (it keeps execute permissions and file signatures) into `/Applications`, or into `~/Applications` if `/Applications` is not writable, and removes the quarantine flag. curl does not set that flag anyway, so Gatekeeper asks nothing.
4. Writes `auto` to `~/Library/Application Support/V2pn/guiTemps/V2pnImport.txt` and opens V2pn. On a Mac the move closes `v2rayN.app` and removes its login item `~/Library/LaunchAgents/v2rayN-LaunchAgent.plist`.

The whole script is inside a `main` function: if the download breaks off halfway, bash runs nothing. Options: `V2PN_DIR`, `V2PN_NO_IMPORT`, `V2PN_NO_START`, `V2PN_SOURCE`. Tested on a Mac with release 1.0.0 (with a separate `HOME` and `V2PN_DIR`, without starting the app). The move from v2rayN on a Mac has not been tested live: the test Mac runs a v2rayN that is used every day.

### winget

`winget install Piwqust.V2pn` installs the same zip as the release (`InstallerType: zip`, a portable program inside). winget unpacks it into `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Piwqust.V2pn_…` and remembers the hashes of its files. So that copy of V2pn behaves differently (`V2pnWinGet.cs`):

- it keeps its data in `%LOCALAPPDATA%\V2pn` (v2rayN's rule for the WinGet folder) and copies the cores from the package there at start;
- it does not download or install updates itself: the Updates window has "Update with winget", which closes V2pn, runs `winget upgrade --id Piwqust.V2pn --exact` and opens V2pn again;
- at the first start it creates a Start shortcut, because winget does not make one for a portable program. After `winget uninstall` the shortcut stays and has to be deleted by hand;
- developer mode is not available.

`ArchiveBinariesDependOnPath: true` in the manifest keeps winget from starting V2pn through a link in another folder: the exe needs its DLLs next to it.

The first submission is made by hand, once a release with all of this is published:

```powershell
tools/winget-manifest.ps1 -Version 1.0.1          # dist/winget/1.0.1/, hashes taken from the release's release.json
winget validate --manifest dist/winget/1.0.1
wingetcreate submit --token <GitHub token> dist/winget/1.0.1
```

`wingetcreate` (`winget install wingetcreate`) opens a pull request in `microsoft/winget-pkgs`. A bot (an install in a sandbox, an antivirus scan) and a moderator review it, usually in a few days to a week. Without an Authenticode signature Defender may flag the exe, and then the review takes longer. Later versions are submitted by `.github/workflows/v2pn-winget.yml` when a release is published, if the `WINGET_TOKEN` secret holds a GitHub token (classic, `public_repo`).

Installing from a local manifest (`winget install --manifest dist/winget/<version>`) needs `winget settings --enable LocalManifestFiles` run as administrator.

### Homebrew

```bash
brew install --cask piwqust/tap/v2pn
```

The cask lives in a separate repository, `Piwqust/homebrew-tap`, file `Casks/v2pn.rb`. `tools/homebrew-cask.ps1 -Version <version>` writes it (into `dist/homebrew/Casks/`) and takes the hashes from `release.json`. In the cask:

- `auto_updates true`: V2pn updates itself, and `brew upgrade` leaves it alone;
- the quarantine flag is removed after install: V2pn has no Apple Developer ID signature, and macOS would not open a flagged copy;
- `zap` deletes `~/Library/Application Support/V2pn` on `brew uninstall --zap`.

To set up the tap: create the public repository `Piwqust/homebrew-tap` and put `Casks/v2pn.rb` in it. From then on `.github/workflows/v2pn-homebrew.yml` updates the cask, if the `HOMEBREW_TAP_TOKEN` secret holds a token with write access to that repository.

## Developer mode: your own changes in the installed V2pn

Installing changes that are not in a release yet needs neither a release nor a sandbox. The installed V2pn does it itself (Windows only).

1. Updates → "Developer mode" → "Choose…" → the repository folder that holds `tools/release.ps1`.
2. "Build and install". V2pn runs `tools/release.ps1 -UpdateOnly -NoCores -NoManifest` for its own architecture. It builds the working tree as it is, uncommitted changes included. The connection keeps working during the build, and the window can be closed. It needs the .NET 10 SDK, like any build.
3. When the build is ready, V2pn installs it through AmazTool: a restart of a few seconds, and a rollback if the new version does not start. The build and install logs are in `guiTemps/V2pnDevBuild/` (`build.log`, `update.log`); the "Log" button opens the right one.

The version of such a build is the next patch with a timestamp: `1.0.1-local.202611141320`. So:

- the usual update check leaves it alone: the published 1.0.0 is older;
- every new build from source is newer than the previous one;
- when the real 1.0.1 comes out, it replaces the local build by itself, as a normal update;
- "Back to the release" downloads the latest published release, checks its signature and installs it, even though its number is lower. Only on that button: V2pn never moves to an older version on its own.

For a local build the Updates window shows the commit it was built from, and a note if the tree had uncommitted changes. It has no manifest and no signature: the package was built on this computer and installed right away, it never travels over the network.

## The signing key

Created on 13 November 2026 and kept outside the repository: `~/.v2pn/release-signing-key.pem` (on Windows, `C:\Users\<you>\.v2pn\`). Its public part is in `V2pnUpdateKeys`.

- **Make a copy of the key** in a password manager or on a separate drive. Without it no update can be released that the installed copies will accept.
- Never send it to anyone or put it in the repository. `.gitignore` keeps `*.pem` out.
- By default the key stays on your computer only: GitHub builds the packages, and `tools/sign-release.ps1` signs them on your machine. If you want GitHub to sign by itself, put the key in the `V2PN_SIGNING_KEY` secret of the `release` environment.

To replace the key:

```powershell
dotnet run tools/manifest.cs -- --new-key $HOME/.v2pn/release-signing-key-2.pem
```

The command prints the public key. Add it to `V2pnUpdateKeys` after a `|`, **without removing the old one**, and the same way to `$releaseKeys` in `tools/install.ps1` and `V2PN_RELEASE_KEYS` in `tools/install.sh` (`tools/check-install-scripts.ps1` fails while they differ), and release a version signed with the old key. Once everyone has updated to it, later versions can be signed with the new key, and the old one can go one release after that.

## How to release a version

You need: the `Piwqust/V2pn` repository on GitHub with the main branch `v2pn`, and the signing key on this computer.

1. Fill in the `## Unreleased` section of `CHANGELOG.md` and rename its heading to the version number (`## 1.0.1`): it becomes the text of the release.
2. Run:

   ```powershell
   tools/release.ps1 -Version 1.0.1 -Tag
   ```

   The script checks that the tree is clean, the branch is `v2pn` and the version is higher than the last one; runs the tests; builds the Windows packages; writes the version, commits and tags. If anything fails, nothing changes in the repository and the script can be run again.

3. Push the commit and the tag together:

   ```powershell
   git push --atomic origin HEAD v1.0.1
   ```

4. GitHub Actions (`.github/workflows/v2pn-release.yml`) builds the Windows (x64, arm64) and macOS (arm64, x64) packages, which takes about 15 minutes. When the run is done, sign the packages and create a **draft** release:

   ```powershell
   tools/sign-release.ps1 -Version 1.0.1
   ```

   The script downloads the packages from the run, signs the manifest with the key on this computer and creates a draft with all the files.

5. Open the draft on GitHub, check the files and publish it (the Publish button or the command the script printed). Until the release is published, installed copies do not see it. A published release cannot be changed: it takes a new version.

If the key is in the `V2PN_SIGNING_KEY` secret of the `release` environment, step 4 happens by itself: the workflow signs the manifest and creates the draft. The key is then kept on GitHub; turn on required approval for that environment.

Push only your own branch and your own tags. `git push --tags` would also publish three hundred v2rayN tags.

To build the packages locally without committing anything:

```powershell
tools/release.ps1 -Version 1.0.1
```

`dist/release/1.0.1/` then holds `V2pn-windows-x64.zip`, `V2pn-windows-arm64.zip`, the installers made from the same files (`V2pn-windows-x64-setup.exe`, `V2pn-windows-arm64-setup.exe`: V2pn's own window from `v2rayN/V2pnSetup` with the Inno Setup engine `tools/installer/V2pn.iss` appended; needs Inno Setup 6, `winget install JRSoftware.InnoSetup`. To look at every screen without installing: `V2pnSetup.exe --shot <folder> --lang ru`), their `-update.zip` variants, the `.sha256` files, `release.json` and `release.json.sig`. macOS packages are built only on a Mac: `tools/package-macos.sh 1.0.1 arm64`.

### Cores

The packages include v2rayN's set of cores (`2dust/v2rayN-core-bin`). So that two builds of the same tag do not differ, the set is pinned in `tools/cores.lock.json`: the commit and the SHA-256 of the archive for each system. The build downloads exactly that archive and stops if the hash does not match. To move to fresh cores:

```powershell
tools/update-cores.ps1
```

and commit the changed `cores.lock.json`.

### Testing on a Mac without .NET

A package can be built on a Mac that has no .NET SDK, from a build made on another computer:

```bash
V2PN_PUBLISH_DIR=~/pub V2PN_CORES_ZIP=~/v2rayN-macos-arm64.zip tools/package-macos.sh 1.0.1 arm64 out
```

`V2PN_PUBLISH_DIR` is the folder where `dotnet publish -r osx-arm64` was run for AmazTool and V2pn on any computer. Signing the programs, the zip and the dmg happen on the Mac. This is how a real Mac (macOS 27.2, Apple silicon) was tested on 13 November 2026: start, connecting through a test server, restarting a crashed core, quitting, the 1.0.0 → 1.0.1 update from the user's folder and from `/Applications`, and rolling back a version that does not start. Not tested: Proxy mode with the system proxy, VPN mode, start at login, and the Gatekeeper warning (it was turned off on that Mac).

### macOS: Apple signing

The macOS packages carry only a local (ad-hoc) signature, without an Apple Developer ID certificate and without notarization. So on the first start of a downloaded copy macOS asks for "Open Anyway" (the README says how). Getting rid of the warning needs the paid Apple Developer Program (99 dollars a year); then `tools/package-macos.sh` gets the certificate (`CODESIGN_IDENTITY`) and a notarization step. That is not in place yet.

Windows has the same story with SmartScreen: the exe has no Authenticode signature.

## A mirror on your own server

A mirror lets updates arrive when GitHub is unreachable or slow. It is a folder of static files behind any web server.

To put a version on it:

```powershell
tools/publish-mirror.ps1 -Version 1.0.1 -Target deploy@example.com:/var/www/v2pn
```

The script checks the packages against the manifest, uploads them over SSH and only then switches `latest`. With `-FromGitHub` it takes the files from the published release instead of a local build. `.github/workflows/v2pn-mirror.yml` does the same automatically when a release is published, if the repository settings have the `MIRROR_TARGET` variable and the `MIRROR_SSH_KEY` secret. It also needs the `MIRROR_KNOWN_HOSTS` variable, the server's host key from `ssh-keyscan <host>` (compare its fingerprint with `ssh-keygen -lf` on the server): without it the job fails instead of trusting whatever answers at that address.

An example for nginx:

```nginx
server {
    listen 443 ssl;
    server_name updates.example.com;
    # ssl_certificate ...

    location /v2pn/ {
        alias /var/www/v2pn/;
        autoindex off;
        # release.json changes with every release; a version's packages never change.
        location ~ /latest/ { add_header Cache-Control "no-cache"; }
    }
}
```

For the app to know about the mirror, add it in `v2rayN/Directory.Build.targets`:

```xml
<V2pnUpdateSources Condition="'$(V2pnUpdateSources)' == ''">https://github.com/Piwqust/V2pn/releases|https://updates.example.com/v2pn</V2pnUpdateSources>
```

and release a version. Copies from that version on go to the mirror when GitHub does not answer. Older copies do not know about the mirror.

The upload and the `v2pn-mirror.yml` workflow are written but have not run against a real server yet.

## GitHub Actions

| Workflow | When | What it does |
| --- | --- | --- |
| `v2pn-ci.yml` | every push and pull request to `v2pn` | tests and builds on Windows and macOS; builds `V2pn.app`, starts it on a Mac, takes a screenshot and attaches it with the logs to the run; the installers' signature check (`tools/check-install-scripts.*`); the hub's tests and a check that `hub/test/fixtures/csharp` is what the hub makes now; the website's build with its CSP check |
| `v2pn-release.yml` | a `v1.2.3` tag | packages, signature, draft release |
| `v2pn-mirror.yml` | a release is published | a copy on your own server, if set up |
| `v2pn-winget.yml` | a release is published | a pull request to `microsoft/winget-pkgs`, if `WINGET_TOKEN` is set |
| `v2pn-homebrew.yml` | a release is published | updates the cask in `Piwqust/homebrew-tap`, if `HOMEBREW_TAP_TOKEN` is set |
| `v2pn-upstream.yml` | Mondays | a merge of the latest v2rayN on a separate branch and a pull request with a report |

v2rayN's workflows were removed from the repository: they built and published v2rayN packages, and one submitted them to winget.

## Testing updates without publishing

```powershell
tools/update-e2e.ps1
```

The script builds two versions that look for updates on a local server (`tools/fake_releases.py`) and trust a key made for this run, and drives real V2pn windows through UI Automation:

| Scenario | What it checks |
| --- | --- |
| `manual` | Auto-update off. Updates window → "Download and install". V2pn restarts as the new version; settings and cores are in place. |
| `auto` | Auto-update on. The package downloads by itself, "Restart and update" shows in the header. The new version installs at the next start, and the package is deleted. |
| `damaged` | The package was changed after signing. It is deleted, and V2pn keeps running the old version. |
| `unsigned` | The release is signed with someone else's key. Nothing downloads. |
| `crash` | The new version does not start. The installer puts the old files back and starts the old version; V2pn reports it and does not try again on its own. |
| `rollback` | The old version was unpacked over the new one by hand. It starts, and the settings are in place. |

The script publishes nothing, does not use the real key and does not touch the V2pn you use: each copy runs in a sandbox (`tools/sandbox.ps1`), the system proxy does not change, and the port is its own.

To run any build next to your working V2pn:

```powershell
tools/sandbox.ps1 -From v2rayN/v2rayN.Desktop/bin/Release/net10.0 -Dest $env:TEMP/v2pn-try -Launch
```

```powershell
tools/sandbox.ps1 -Dest $env:TEMP/v2pn-try -Stop
```

## Pulling in a new v2rayN

```powershell
tools/sync-upstream.ps1          # try it and read the report
tools/sync-upstream.ps1 -Apply   # and accept it if everything is green
```

The script merges the latest v2rayN into a separate branch `sync/v2rayN-…`, builds V2pn and AmazTool, and runs the tests. The `v2pn` branch changes only when all of that passed and `-Apply` is given. The report goes to `docs/upstream-sync/`. It lists v2rayN's commits and has a "To port by hand" section: what v2rayN changed in the windows that V2pn rewrote its own way.

Why it works like this: the windows V2pn redrew are marked `merge=ours` in `v2rayN/v2rayN.Desktop/Views/.gitattributes`, and the READMEs, issue templates and `dependabot.yml` in the root `.gitattributes`. In a merge git keeps V2pn's version of them. New v2rayN settings in those windows have to be ported by hand; the report shows their diff. If v2rayN binds a new element in code, the build fails with a clear error ("no `cmbDnsMode`"), and that is the hint of what to port. GitHub workflows that v2rayN changed or added are removed again after the merge.

Exit codes: `0`: merged or nothing to merge; `2`: conflicts; `3`: does not build; `4`: tests failed. On any error `v2pn` stays as it was.

Once a week `.github/workflows/v2pn-upstream.yml` does the same and opens a pull request with the report. If something broke, the pull request is a draft. The build and the tests run v2rayN's code as it came, so that job has a read-only token that is not kept in the checkout; it hands the sync branch over as a git bundle to a second job, which runs nothing from the merged tree, checks the branch name, pushes the branch and opens the pull request.

### Rules that keep merges from breaking

- V2pn's new logic lives in its own files: `ServiceLib/V2pn/`, `v2rayN.Desktop/Common/`, `L.*.cs`, `AmazTool/Installer.cs`.
- If a v2rayN file has to change, keep the change to a couple of lines with a `// V2pn:` comment.
- If you rewrote a v2rayN window entirely, add it to `Views/.gitattributes`.
- The `merge.ours` driver is set only for the duration of a v2rayN merge (the script does that). Do not put it in the repository config: merges of V2pn's own branches would start silently losing changes in those files.
