# METAR Viewer

[![All versions on main](https://img.shields.io/badge/main-Windows%20%2B%20macOS%20%2B%20MSFS%202020-2ea043)](#whats-in-this-repository)
[![Release](https://github.com/shay2000/METAR-Translator/actions/workflows/release.yml/badge.svg)](https://github.com/shay2000/METAR-Translator/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/shay2000/METAR-Translator?sort=semver&label=latest)](https://github.com/shay2000/METAR-Translator/releases/latest)

[![Windows](https://img.shields.io/badge/Windows-x64%20.exe-0078D6?logo=windows&logoColor=white)](https://github.com/shay2000/METAR-Translator/releases/tag/win-v1.0.7)
[![macOS](https://img.shields.io/badge/macOS-Apple%20Silicon%20.dmg-000000?logo=apple&logoColor=white)](https://github.com/shay2000/METAR-Translator/releases/tag/mac-v1.0.7)
[![MSFS 2020](https://img.shields.io/badge/MSFS%202020-in--game%20toolbar%20panel-1f6feb)](https://github.com/shay2000/METAR-Translator/releases/tag/msfs-v1.0.7)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](global.json)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

METAR Viewer fetches and decodes aviation weather reports (METARs) into plain English.
Search by ICAO code, IATA code, airport name, or even a common misspelling, and get
wind, visibility, cloud layers, temperature, altimeter, present weather, and flight
category back in a readable form.

It ships in **three flavours**, all built from this one branch: a Windows desktop app,
a macOS desktop app, and an in-game toolbar panel for Microsoft Flight Simulator 2020.

> **Simulation use only.** This project is for flight simulation and hobby use. Never
> use it for real-world aviation, flight planning, navigation, or weather decisions.

## What's in this repository

`main` is the single source of truth and contains **all three versions at once**. The
old `Mac-Version`, `Windows-Version`, and `FlightSimIntegration` branches are kept only
as read-only historical backups — do not develop on them.

All three versions share the same METAR decoding engine (`MetarViewer.Core`), so a
parsing fix benefits every version at once.

## Which version do I want?

| Version | What it is | Runs on | Release to download |
| --- | --- | --- | --- |
| 🪟 **Windows desktop app** | Standalone window on your desktop. Nothing to install — one self-contained `.exe`. | Windows 10/11 (x64) | [**METAR Viewer for Windows 1.0.7**](https://github.com/shay2000/METAR-Translator/releases/tag/win-v1.0.7) |
| 🍎 **macOS desktop app** | Standalone `.app` in a disk image, for Apple Silicon *and* Intel. | macOS 14+ | [**METAR Viewer for Mac 1.0.7**](https://github.com/shay2000/METAR-Translator/releases/tag/mac-v1.0.7) |
| ✈️ **MSFS 2020 panel** | Weather panel *inside* the simulator, opened from the in-game toolbar. | MSFS 2020 on PC | [**METAR Viewer for MSFS 2020 1.0.7**](https://github.com/shay2000/METAR-Translator/releases/tag/msfs-v1.0.7) |

Each version is published as its **own** release. The current platform releases are
[Windows 1.0.7](https://github.com/shay2000/METAR-Translator/releases/tag/win-v1.0.7),
[Mac 1.0.7](https://github.com/shay2000/METAR-Translator/releases/tag/mac-v1.0.7), and
[MSFS 2020 1.0.7](https://github.com/shay2000/METAR-Translator/releases/tag/msfs-v1.0.7).
Matching version numbers across the three releases are built from the same commit.

Pick a desktop app if you want to look up weather on its own; pick the MSFS package if
you want weather without leaving the cockpit. Installing one does not affect the others.


---

### 🪟 Windows desktop app

A self-contained single-file executable — no .NET runtime, installer, or admin rights
needed.

1. Download `METAR-Viewer-Windows-x64-<version>.exe` from the
   [METAR Viewer for Windows 1.0.7 release](https://github.com/shay2000/METAR-Translator/releases/tag/win-v1.0.7).
2. Double-click it.


Windows SmartScreen may warn about an unrecognised publisher because the build is not
code-signed. Choose **More info → Run anyway** if you trust the download.

### 🍎 macOS desktop app

1. Open the [METAR Viewer for Mac 1.0.7 release](https://github.com/shay2000/METAR-Translator/releases/tag/mac-v1.0.7)
   and download the disk image for your Mac:
   - `METAR-Viewer-Mac-Apple-Silicon-<version>.dmg` for M-series Macs.
   - `METAR-Viewer-Mac-Intel-<version>.dmg` for Intel Macs.
2. Open the `.dmg` and drag **METAR Viewer** into your Applications folder.
3. Launch it.

The DMG is ad-hoc signed rather than notarised, so on first launch macOS Gatekeeper may
refuse to open it. Right-click the app and choose **Open**, or allow it under **System
Settings → Privacy & Security**.


### ✈️ MSFS 2020 in-game panel

This version runs entirely inside the simulator. There is no separate app, background
service, or custom installer — it is a normal MSFS Community package.

The panel asks the simulator for weather first, falls back to the VATSIM METAR API when
the simulator has no report, and decodes everything locally.

Download `metar-viewer-toolbar.zip` from the
[METAR Viewer for MSFS 2020 1.0.7 release](https://github.com/shay2000/METAR-Translator/releases/tag/msfs-v1.0.7),
or grab it from the
[Flightsim.to page](https://flightsim.to/addon/106602/metar-translator).


> A source checkout is **not** installable on its own. `manifest.json`, `layout.json`,
> and the toolbar registration have to be generated first — see
> [Build from source](#build-from-source).

1. Close Microsoft Flight Simulator 2020 completely.
2. Open your Community folder. If you do not know the path, start MSFS and use
   **Developer Mode → Tools → Virtual File System → Packages Folders → Open Community
   Folder**, then close MSFS again before copying. Alternatively find
   `InstalledPackagesPath` in `UserCfg.opt` and open its `Community` subfolder. Do not
   use `Official`.
3. Extract the ZIP directly into the Community folder.
4. Confirm the package metadata sits at the package root:

   ```text
   Community\
   └── metar-viewer-toolbar\
       ├── manifest.json
       ├── layout.json
       ├── InGamePanels\
       └── html_ui\
   ```

   If you see `metar-viewer-toolbar\metar-viewer-toolbar\...`, move the inner folder up
   one level. Do not copy the repository's `integrations\msfs2020` folder into Community.

5. Start MSFS 2020, begin a flight, and pick **METAR Viewer** from the toolbar. It may
   be hidden in the toolbar overflow menu if you have many panels installed.

For a step-by-step walkthrough, see [QUICKSTART.md](QUICKSTART.md).

**What the panel does**

- Requests weather from the simulator's facility service first.
- Falls back to the VATSIM METAR API when the simulator has no report or the request fails.
- Searches airports through AirportsAPI for names and IATA codes.
- Still accepts a direct four-letter ICAO code when airport search is unavailable.
- Decodes wind, visibility, clouds, temperature, altimeter, weather, and flight category locally.
- Briefly caches successful results to avoid repeated requests.
- Remembers the last station when the simulator data-store is available.

An internet connection is needed for airport-name/IATA lookup and the VATSIM fallback.
The simulator METAR path still works without them when MSFS has the report.

---

## Troubleshooting

### MSFS: the toolbar icon does not appear

- Make sure MSFS was closed while you copied the package, and restarted afterwards.
- Confirm `manifest.json` and `layout.json` are directly inside `metar-viewer-toolbar`.
- Make sure you copied the *built* package folder, not the repository or `PackageSources`.
- Temporarily remove other toolbar panel mods if the toolbar is unstable after a sim update.
- With Developer Mode on, check **Tools → Packages** for `metar-viewer-toolbar`.

### The app or panel cannot find weather

- Try a direct ICAO code such as `EGLL`, `KJFK`, or `OMDB`.
- Check your internet connection for airport search and the VATSIM fallback.
- Wait a minute and refresh; public weather services are occasionally unavailable.

### Removing or updating the MSFS package

Close MSFS, then delete or replace only the `Community\metar-viewer-toolbar` folder.
Never hand-edit `layout.json` or `manifest.json` inside a packaged release.

---

## Repository structure

Every version lives on `main` as sibling projects in one solution:

```text
src/
  MetarViewer.Core/            Shared parsing, models and services. No UI code.
  MetarViewer.App.Avalonia/    Cross-platform desktop UI -> ships Windows .exe + macOS .dmg
  MetarViewer.App.WinUI/       Alternative Windows-native WinUI 3 UI. Windows-only build.
integrations/
  msfs2020/                    MSFS 2020 in-game toolbar panel (HTML/JS + packagers).
tests/
  MetarViewer.Core.Tests/            Shared-logic tests.
  MetarViewer.App.Avalonia.Tests/    View-model tests for the Avalonia app.
```

Both desktop projects reference `MetarViewer.Core`; Core references neither. Any METAR
or airport logic worth sharing belongs in Core.

### Build and run the desktop apps

Both shipping desktop apps (Windows `.exe` and macOS `.dmg`) are built from
`MetarViewer.App.Avalonia`, which runs on macOS, Linux, and Windows:

```bash
dotnet build src/MetarViewer.App.Avalonia
dotnet run --project src/MetarViewer.App.Avalonia
```

`MetarViewer.App.WinUI` is a **Windows-native alternative UI** that is not currently
shipped in releases. It must be built from Windows:

```bash
dotnet build src/MetarViewer.App.WinUI
dotnet run --project src/MetarViewer.App.WinUI
```

On macOS and Linux, `dotnet build MetarViewer.sln` deliberately *skips* the WinUI
project rather than failing: it is mapped to the `x64` solution platform only, so the
default `Any CPU` build stays green everywhere. Building it directly off Windows fails
with `NETSDK1100`, which is expected.

> The WinUI project is retained from the old `Windows-Version` branch for a
> Windows-native look, and its build has not been verified since the Phase 1 refactor
> (see [docs/PHASE-5-HANDOFF.md](docs/PHASE-5-HANDOFF.md)).

### Local .NET SDK setup

`global.json` pins the SDK to `8.0.418` with `rollForward: latestFeature`, which only
resolves to an `8.0.4xx` SDK. If your system-wide `dotnet` is a different major version,
commands fail with an error that looks like a broken solution file:

```text
The application 'sln' does not exist or is not a managed .dll or .exe
A compatible .NET SDK was not found.
Requested SDK version: 8.0.418
```

The solution file is fine; the SDK simply cannot be resolved. Install the pinned SDK
into the gitignored `./.dotnet` directory:

```bash
./scripts/bootstrap-dotnet.sh
```

The script reads the version from `global.json`, is safe to re-run, and leaves your
system-wide .NET installation alone. `.vscode/settings.json` already points the C#
extension and integrated terminal at `./.dotnet`, so **reload the VS Code window**
afterwards ("Developer: Reload Window") for new terminals to pick it up.

From an external terminal, either call `./.dotnet/dotnet` directly or export:

```bash
export DOTNET_ROOT="$PWD/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

Then build and test as usual:

```bash
dotnet build MetarViewer.sln
dotnet test MetarViewer.sln
```

Installing the .NET 8 SDK system-wide satisfies the pin without the bootstrap step. CI
needs none of this: the workflows use `actions/setup-dotnet` with `dotnet-version: 8.0.x`.

## Build from source

### MSFS 2020 package

The integration lives under [`integrations/msfs2020`](integrations/msfs2020). There are
two ways to produce an installable ZIP.

**SDK build (authoritative).** Requires Windows, the current MSFS 2020 SDK, and Node.js.
It validates the source, compiles the panel registration into a real `.spb`, validates
the generated package, and writes the ZIP to `integrations/msfs2020/Packages/`.

**Portable build (no SDK).** Runs anywhere Python 3.9+ is available, including macOS and
Linux, and writes the ZIP to `builds/`:

```bash
python3 integrations/msfs2020/tools/build-community-package.py
```

This generates `manifest.json` and `layout.json` itself and ships the toolbar
registration as XML under the `.spb` filename, which the simulator's loader generally
accepts. It is the quickest path to a ZIP you can drop into Community, but the SDK build
remains authoritative.

Panel tests run from `integrations/msfs2020` with:

```text
node --test
node tools\validate-source.mjs .
```

See [integrations/msfs2020/README.md](integrations/msfs2020/README.md) for the full
maintainer workflow.

> `integrations/msfs2020/PackageSources/.../metar-core.js` is a **separate JavaScript
> implementation** of the decoding logic, because the simulator panel cannot load .NET
> assemblies. Behavioural fixes made in `MetarViewer.Core` must be mirrored there by hand.
> The MSFS integration is therefore not a .NET project and cannot be referenced with
> `dotnet add reference`; it appears in `MetarViewer.sln` as a solution folder only.

## Releases

Each platform gets its **own** GitHub Release, so the Releases page lists three clearly
labelled downloads per version instead of one release with three files inside it:

| Release | Tag | Workflow | Asset | Runner |
| --- | --- | --- | --- | --- |
| METAR Viewer for Windows | `win-v1.2.3` | `windows-release.yml` | `METAR-Viewer-Windows-x64-*.exe` | `windows-latest` |
| METAR Viewer for Mac | `mac-v1.2.3` | `mac-release.yml` | Apple Silicon **and** Intel `.dmg` | `macos-14` |
| METAR Viewer for MSFS 2020 | `msfs-v1.2.3` | `msfs-release.yml` | `metar-viewer-toolbar.zip` | `ubuntu-latest` |

`release.yml` ("Release all platforms") is the entry point. Every code push to `main`,
or a manual **Actions → Release all platforms → Run workflow**, will:

1. Pick the next version (or use the one you type in), skipping any already tagged.
2. Create `win-v*`, `mac-v*`, and `msfs-v*` in a single atomic push, so the three
   tracks always describe the same commit.
3. Call the three platform workflows in parallel, each publishing its own release.

The Windows release carries the **Latest** badge; the other two are published with
`--latest=false` so they do not compete for it. The three platform jobs are independent,
so a failure in one still lets the other two publish.

To rebuild a single platform, push just that tag — for example
`git push origin mac-v1.2.3` — or dispatch that platform's workflow directly.

> **Why `release.yml` calls the other workflows instead of only pushing tags.** GitHub
> does not raise workflow-triggering events for refs created with the default
> `GITHUB_TOKEN`, to stop workflows triggering themselves. The retired
> `auto-release.yml` pushed a `v*` tag and expected the tag-triggered release workflow
> to notice, which never happened — that is why `v1.0.3`–`v1.0.6` were tagged but only
> ever released by hand. `workflow_call` invokes each platform workflow in-process, so
> no event delivery is involved and no personal access token is required.


## License

MIT License. See [LICENSE](LICENSE).
