# METAR Viewer for Microsoft Flight Simulator 2020

This directory contains a self-contained Microsoft Flight Simulator 2020 toolbar-panel version of METAR Viewer. At runtime it does not launch the Avalonia desktop app or require a companion process: the panel is HTML, CSS, and JavaScript loaded by the simulator.

> This integration is for flight simulation and hobby use only. Never use it for real-world aviation, flight planning, or navigation.

## Current status

The panel source, package definitions, build validation, parser, airport matching, weather-source fallback, caching, and UI lifecycle are implemented and covered by dependency-free Node tests.

One release gate cannot be completed on macOS: the panel registration XML must be compiled to an `.spb` by the Windows-only MSFS 2020 SDK. The resulting Community package must then be installed and exercised in MSFS 2020 before it is described as a working release. No placeholder `.spb` is checked in.

Custom toolbar registration through `InGamePanels.InGamePanelDefinition` is a widely used MSFS 2020 package pattern, but it is not a stable, formally documented extension contract. Revalidate the package after supported Sim Updates.

## How it works

The in-game panel uses this source order:

1. MSFS facility listener `JS_LISTENER_FACILITY` and `GET_METAR_BY_IDENT`, so the first result reflects the simulator's weather source.
2. The VATSIM METAR endpoint when the simulator returns no observation or its facility request fails.
3. AirportsAPI for ICAO, IATA, airport-name, and typo-tolerant suggestions. A four-letter ICAO remains usable when airport search is offline.

Reports are decoded locally. Positive METAR results are cached for 60 seconds, airport resolutions for 10 minutes, and suggestions for 2 minutes. The last successfully viewed station is saved through the simulator data-store functions when available.

AviationWeather.gov is deliberately not called from the panel because its API does not permit cross-origin browser requests.

## Source layout

```text
MetarViewerToolbar.xml                         MSFS project
PackageDefinitions/metar-viewer-toolbar.xml   package and asset groups
PackageSources/InGamePanels/                  toolbar registration source
PackageSources/html_ui/InGamePanels/           panel UI and portable logic
PackageSources/html_ui/Textures/               toolbar icon
tools/                                         source/built-package validators
tests/                                         Node regression tests
build.bat                                      Windows SDK build entry point
```

The SDK creates generated `_PackageInt/` and `Packages/` directories locally. They are build outputs and must not be committed.

## Prerequisites

- Windows with Microsoft Flight Simulator 2020 installed
- Developer Mode enabled in MSFS 2020
- The current MSFS 2020 SDK installed from the simulator's Developer Mode **Help** menu
- Node.js 24 LTS or newer

The SDK is a Windows MSI and its package compiler is `fspackagetool.exe`; Microsoft does not provide a macOS equivalent.

## Run portable tests

From this directory:

```text
node --test
node tools\validate-source.mjs .
```

These tests work on macOS, Linux, and Windows. They do not prove that the undocumented toolbar registration still works in the current simulator build.

## Build the Community package on Windows

Open Command Prompt and point `MSFS_SDK` at the SDK installation directory:

```bat
cd integrations\msfs2020
set "MSFS_SDK=C:\MSFS SDK"
build.bat
```

If `node.exe` is not on `PATH`, set `NODE_EXE` to its full path before running the script.

The script:

1. Validates source XML and every local asset reference in the panel HTML.
2. invokes `%MSFS_SDK%\Tools\bin\fspackagetool.exe` for a clean package build;
3. validates the generated `manifest.json`, `layout.json`, compiled `.spb`, path safety, byte sizes, and payload inventory.

The expected output is:

```text
Packages\metar-viewer-toolbar\
├── InGamePanels\InGamePanel_MetarViewer.spb
├── html_ui\InGamePanels\MetarViewer\...
├── html_ui\Textures\Menu\toolbar\ICON_TOOLBAR_METAR_VIEWER.svg
├── layout.json
└── manifest.json
```

## Install and validate in MSFS 2020

1. In Developer Mode, open **Tools → Virtual File System → Packages Folders** and use **Open Community Folder** to locate the active Community folder.
2. Copy the complete `Packages\metar-viewer-toolbar` directory into that Community folder.
3. Restart the simulator and start a flight.
4. Confirm that the METAR Viewer icon appears once in the in-game toolbar.
5. Open, close, move, and resize the panel, then test `EGLL`, `LHR`, `London Heathrow`, and a misspelling such as `Heatrow`.
6. Confirm the displayed raw report matches the simulator weather, VATSIM fallback works when required, and the previous station returns after a simulator restart.
7. Use the Coherent debugger to confirm there are no load, network, or lifecycle errors when the panel is repeatedly opened and closed.

Also validate on each supported Sim Update, on both Microsoft Store and Steam installations where possible, and with other toolbar mods installed. MSFS VFS conflicts are resolved by package load order.

## References

- [MSFS 2020 SDK overview](https://docs.flightsimulator.com/html/Introduction/SDK_Overview.htm)
- [Using the SDK and Community package structure](https://docs.flightsimulator.com/html/Introduction/Using_The_SDK.htm)
- [Package Tool](https://docs.flightsimulator.com/html/Additional_Information/Tools/Package_Tool/Package_Tool.htm)
- [Coherent JavaScript API](https://docs.flightsimulator.com/html/Programming_Tools/JavaScript/Coherent.htm)
- [Facility listener calls](https://docs.flightsimulator.com/html/Programming_Tools/JavaScript/Coherent_Listeners/JS_LISTENER_FACILITY.htm)
- [AviationWeather.gov API restrictions](https://aviationweather.gov/data/api/)
