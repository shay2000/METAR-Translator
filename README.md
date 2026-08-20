# METAR Viewer for Microsoft Flight Simulator 2020

METAR Viewer is a PC-only Microsoft Flight Simulator 2020 Community package that adds a METAR panel to the in-game toolbar. It runs inside the simulator: no separate desktop app, background service, or custom installer is required.

The panel uses the simulator's METAR first, falls back to VATSIM when needed, and decodes the report locally. Airport search accepts ICAO codes, IATA codes, airport names, and common misspellings.

> **Simulation use only.** This project is for flight simulation and hobby use. Never use it for real-world aviation, flight planning, navigation, or weather decisions.

## Install it like an MSFS mod

Download a packaged release ZIP from the [METAR Translator Flightsim.to page](https://flightsim.to/addon/106602/metar-translator) or a project release. A source checkout is not itself installable because the toolbar registration XML must be compiled into an `.spb` file by the MSFS 2020 SDK.

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

The MSFS integration lives under [`integrations/msfs2020`](integrations/msfs2020). Building requires Windows, the current MSFS 2020 SDK, and Node.js. The build validates the source, compiles the panel registration, validates the generated package, and creates a standard Community-folder ZIP.

Portable tests can be run from that directory with:

```text
node --test
node tools\validate-source.mjs .
```

See [integrations/msfs2020/README.md](integrations/msfs2020/README.md) for the complete maintainer/build workflow.

## Repository note

The `src/` and `tests/` directories also contain the separate METAR Viewer desktop application and its tests. They are not required to install or run the MSFS 2020 toolbar panel.

## License

MIT License. See [LICENSE](LICENSE).
