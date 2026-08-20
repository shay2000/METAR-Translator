# METAR Viewer for Microsoft Flight Simulator 2020

METAR Viewer is a PC-only Microsoft Flight Simulator 2020 Community package that adds a METAR panel to the in-game toolbar. It runs inside the simulator: no separate desktop app, background service, or custom installer is required.

The panel uses the simulator's METAR first, falls back to VATSIM when needed, and decodes the report locally. Airport search accepts ICAO codes, IATA codes, airport names, and common misspellings.

> **Simulation use only.** This project is for flight simulation and hobby use. Never use it for real-world aviation, flight planning, navigation, or weather decisions.

> **Source checkout notice:** this repository contains the package source, not a ready-to-install ZIP. A packaged ZIP has to be generated first, either by the MSFS 2020 SDK or by the portable packager described in [Build from source](#build-from-source).


## Install it like an MSFS mod

Download a packaged release ZIP from the [MSFS 2020 releases](../../releases?q=msfs-v) (tags starting with `msfs-v`) or the [METAR Translator Flightsim.to page](https://flightsim.to/addon/106602/metar-translator). A source checkout is not itself installable, because `manifest.json`, `layout.json`, and the toolbar registration must be generated before the simulator will load the package.

1. Close Microsoft Flight Simulator 2020 completely.
2. Open the Community folder used by your installation. The safest method, if you do not already know the path, is to start MSFS, use **Developer Mode → Tools → Virtual File System → Packages Folders → Open Community Folder**, then close MSFS again before copying the package. Alternatively, find `InstalledPackagesPath` in `UserCfg.opt` and open its `Community` subfolder. Do not use `Official`.
3. Extract the release ZIP directly into the Community folder.
4. Confirm that the package metadata is at the package root:

   ```text
   Community\
   └── metar-viewer-toolbar\
       ├── manifest.json
       ├── layout.json
       ├── InGamePanels\
       └── html_ui\
   ```

   If the archive contains `metar-viewer-toolbar\metar-viewer-toolbar\...`, move the inner folder up one level. Do not copy the repository's `integrations\msfs2020` folder into Community.

5. Start MSFS 2020, start a flight, and select the **METAR Viewer** icon from the toolbar. It may be in the toolbar overflow menu if you have many toolbar panels installed.

For a step-by-step version of this process, see [QUICKSTART.md](QUICKSTART.md). For building the package from source, see [the MSFS build guide](integrations/msfs2020/README.md).

## What the panel does

- Requests weather from the simulator's facility service first.
- Uses the VATSIM METAR API as a fallback when the simulator has no report or the request fails.
- Searches airports through AirportsAPI when you enter an airport name or IATA code.
- Still accepts a direct four-letter ICAO code when airport search is unavailable.
- Decodes wind, visibility, clouds, temperature, altimeter, weather, and flight category locally.
- Caches successful weather results briefly to avoid repeated requests.
- Remembers the last station when the simulator data-store is available.

An internet connection is needed for airport-name/IATA lookup and the VATSIM fallback. The simulator METAR path can still work without those services when MSFS has the report available.

## Troubleshooting

### The icon does not appear

- Make sure MSFS was closed before you copied the package and restarted after installation.
- Open the package folder and confirm that `manifest.json` and `layout.json` are directly inside `metar-viewer-toolbar`.
- Make sure you copied the compiled package folder, not the repository or `PackageSources` folder.
- Temporarily remove other in-game toolbar panel mods if the toolbar or panel system is unstable after a simulator update.
- With Developer Mode enabled, check **Tools → Packages** to see whether `metar-viewer-toolbar` is detected.

### The panel opens but cannot find weather

- Try a direct ICAO code such as `EGLL`, `KJFK`, or `OMDB`.
- Check your internet connection for airport search and VATSIM fallback.
- Refresh the report after waiting a minute; public weather services can be temporarily unavailable.

### Remove or update it

Close MSFS, then delete or replace only the `Community\metar-viewer-toolbar` folder. Do not edit `layout.json` or `manifest.json` inside a packaged release.

## Build from source

The MSFS integration lives under [`integrations/msfs2020`](integrations/msfs2020). There are two ways to produce an installable ZIP.

**SDK build (authoritative).** Requires Windows, the current MSFS 2020 SDK, and Node.js. It validates the source, compiles the panel registration into a real `.spb`, validates the generated package, and writes the ZIP to `integrations/msfs2020/Packages/`.

**Portable build (no SDK).** Runs anywhere Python 3.9+ is available, including macOS and Linux, and writes the ZIP to `builds/`:

```bash
python3 integrations/msfs2020/tools/build-community-package.py
```

This generates `manifest.json` and `layout.json` itself and ships the toolbar
registration as XML under the `.spb` filename, which the simulator's loader
generally accepts. It is the quickest path to a ZIP you can drop into Community,
but the SDK build remains the authoritative one.

Portable tests can be run from `integrations/msfs2020` with:

```text
node --test
node tools\validate-source.mjs .
```

See [integrations/msfs2020/README.md](integrations/msfs2020/README.md) for the complete maintainer/build workflow.

## Releases

The three platforms are released independently, each on its own tag prefix, so
publishing one never disturbs the others:

| Platform | Tag prefix | Workflow | Asset |
| --- | --- | --- | --- |
| Windows desktop app | `v1.2.3` | `release.yml` | Windows build |
| macOS desktop app | `mac-v1.2.3` | `mac-release.yml` | `.dmg` for arm64 and x64 |
| MSFS 2020 package | `msfs-v1.2.3` | `msfs-release.yml` | `metar-viewer-toolbar.zip` |

Pushing a matching tag builds and publishes that platform only. The Windows
track stays the repository's "Latest" release; the Mac and MSFS releases are
marked as not-latest so they sit alongside it.

`msfs-package.yml` still runs the Windows/SDK build on pushes to the integration
branch for validation, but it no longer publishes releases, so only one workflow
ever writes a release asset.

## Repository structure

Every platform variant now lives on **one branch** as sibling projects in a single
solution. There are no longer permanent `Mac-Version` / `Windows-Version` /
`FlightSimIntegration` branches to keep in sync.

```text
src/
  MetarViewer.Core/            Shared parsing, models and services. No UI code.
  MetarViewer.App.Avalonia/    Cross-platform desktop UI (macOS, Linux, Windows).
  MetarViewer.App.WinUI/       Windows-native WinUI 3 UI. Builds on Windows only.
integrations/
  msfs2020/                    MSFS 2020 in-game toolbar panel (HTML/JS + packagers).
tests/
  MetarViewer.Core.Tests/            Shared-logic tests.
  MetarViewer.App.Avalonia.Tests/    View-model tests for the Avalonia app.
```

Both desktop apps reference `MetarViewer.Core`, and `MetarViewer.Core` references
neither of them. Any METAR or airport logic worth sharing belongs in Core.

### Build and run the desktop apps

The cross-platform app runs on macOS, Linux and Windows:

```bash
dotnet build src/MetarViewer.App.Avalonia
dotnet run --project src/MetarViewer.App.Avalonia
```

The WinUI 3 app is Windows-only and **must be built from Windows**:

```bash
dotnet build src/MetarViewer.App.WinUI
dotnet run --project src/MetarViewer.App.WinUI

```

On macOS or Linux, `dotnet build MetarViewer.sln` deliberately skips the WinUI
project instead of failing: it is mapped to the `x64` solution platform only, so
the default `Any CPU` build stays green everywhere. Building it directly on a
non-Windows machine fails with `NETSDK1100`, which is expected.

> The shipping Windows `.exe` is currently published from
> **`MetarViewer.App.Avalonia`**, not from `MetarViewer.App.WinUI`. The WinUI
> project is retained from the old `Windows-Version` branch for a Windows-native
> look, and its build has not been verified since the Phase 1 refactor (see
> [docs/PHASE-5-HANDOFF.md](docs/PHASE-5-HANDOFF.md)).

### MSFS 2020 integration

`integrations/msfs2020` is the MSFS 2020 support used by whichever app variant
needs in-simulator weather. It is **not** a .NET project: the panel is HTML, CSS
and JavaScript loaded by the simulator, packaged by the Python and PowerShell
tools in `integrations/msfs2020/tools`. It therefore cannot be referenced with
`dotnet add reference`, and it is surfaced in `MetarViewer.sln` as a solution
folder so it is visible in IDEs.

Note that `integrations/msfs2020/PackageSources/.../metar-core.js` is a **separate
JavaScript implementation** of the decoding logic, because the simulator panel
cannot load .NET assemblies. Behavioural fixes made in `MetarViewer.Core` must be
mirrored there by hand.

### Desktop app: local .NET SDK setup


`global.json` pins the SDK to `8.0.418` with `rollForward: latestFeature`, which
only ever resolves to an `8.0.4xx` SDK. If your system-wide `dotnet` is a
different major version, any command against the solution fails with a
misleading error that looks like a broken solution file:

```text
The application 'sln' does not exist or is not a managed .dll or .exe
A compatible .NET SDK was not found.
Requested SDK version: 8.0.418
```

The solution file is fine; the SDK simply cannot be resolved. Install the pinned
SDK into the gitignored `./.dotnet` directory:

```bash
./scripts/bootstrap-dotnet.sh
```

The script reads the version from `global.json`, is safe to re-run, and does not
touch your system-wide .NET installation. `.vscode/settings.json` already points
the C# extension and integrated terminal at `./.dotnet`, so **reload the VS Code
window** afterwards ("Developer: Reload Window") for new terminals to pick it up.

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

Alternatively, installing the .NET 8 SDK system-wide satisfies the pin without
the bootstrap step. CI needs none of this: the workflows use
`actions/setup-dotnet` with `dotnet-version: 8.0.x`.

## License

MIT License. See [LICENSE](LICENSE).
