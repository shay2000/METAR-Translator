# METAR Viewer MSFS 2020 integration

This directory contains the source and build tools for the METAR Viewer in-game toolbar panel. The final output is a normal Microsoft Flight Simulator 2020 Community package.

The panel runs inside the simulator's HTML/JavaScript UI. It does not launch the Avalonia desktop application and does not need a companion process.

> **Simulation use only.** This integration is for flight simulation and hobby use. Never use it for real-world aviation, flight planning, navigation, or weather decisions.

## Install a packaged release

Players should install a compiled release ZIP, not this source directory:

1. If you do not know the Community folder, start MSFS 2020, use **Developer Mode → Tools → Virtual File System → Packages Folders → Open Community Folder**, then close MSFS before copying the package. If you already know the folder, open it directly. `UserCfg.opt` also records the `InstalledPackagesPath`; the active Community folder is beneath it. Do not use `Official`.
2. Extract the release ZIP directly into the Community folder.
3. Confirm the package has this shape:

   ```text
   Community\
   └── metar-viewer-toolbar\
       ├── manifest.json
       ├── layout.json
       ├── InGamePanels\
       │   └── InGamePanel_MetarViewer.spb
       └── html_ui\
           ├── InGamePanels\MetarViewer\...
           └── Textures\Menu\toolbar\ICON_TOOLBAR_METAR_VIEWER.svg
   ```

   `manifest.json` and `layout.json` must be at the root of `metar-viewer-toolbar`. Do not leave an extra archive folder around the package, and do not copy `PackageSources`, `PackageDefinitions`, or the whole repository into Community.

4. Restart MSFS 2020, start a flight, and open **METAR Viewer** from the toolbar. The icon may be in the toolbar overflow menu.

This is intentionally the same folder-and-package workflow used by ordinary MSFS 2020 mods. The release ZIP contains one package folder; it is not an executable installer.

## Why the source checkout cannot be installed directly

The toolbar registration starts as `PackageSources/InGamePanels/InGamePanel_MetarViewer.xml`, but MSFS 2020 loads the SDK-compiled binary `InGamePanels/InGamePanel_MetarViewer.spb`. The SDK also generates `manifest.json` and `layout.json`, including the file inventory and byte sizes required by the simulator.

The repository intentionally does not contain a placeholder `.spb`. A package built without the SDK would not be a valid toolbar mod.

## Build a Community package from source

Building is for developers or maintainers who need to create a release package.

### Prerequisites

- Windows.
- Microsoft Flight Simulator 2020 and its current SDK. Enable Developer Mode, then install/update the SDK from the simulator's **Help** menu.
- Node.js 24 LTS or newer for the portable tests and validators.

This HTML/JavaScript panel does not require the .NET desktop application or Visual Studio to run. The SDK's `fspackagetool.exe` is the important build dependency.

### Build and package

Open Command Prompt and run:

```bat
cd path\to\METAR-Translator\integrations\msfs2020
build.bat "C:\MSFS SDK"
```

The SDK path can also be supplied through `MSFS_SDK`:

```bat
set "MSFS_SDK=C:\MSFS SDK"
build.bat
```

If `node.exe` is not on `PATH`, set `NODE_EXE` to its full path before running the build. The build performs four checks/actions:

1. Validate the source XML, HTML references, and package paths.
2. Compile the panel registration with `fspackagetool.exe`.
3. Validate the generated manifest, layout, payload inventory, compiled `.spb`, and file sizes.
4. Create a distribution ZIP whose root is `metar-viewer-toolbar`.

Successful output is:

```text
Packages\metar-viewer-toolbar\
├── InGamePanels\InGamePanel_MetarViewer.spb
├── html_ui\InGamePanels\MetarViewer\...
├── html_ui\Textures\Menu\toolbar\ICON_TOOLBAR_METAR_VIEWER.svg
├── layout.json
└── manifest.json

Packages\metar-viewer-toolbar.zip
└── metar-viewer-toolbar\...
```

Copy the folder into Community for local testing, or distribute the ZIP. Do not modify the generated package after validation; rebuild it when source files change.

### Portable tests and source validation

From this directory:

```text
node --test
node tools\validate-source.mjs .
```

These checks run on macOS, Linux, and Windows. They validate the portable parser, services, controller, XML wiring, local assets, and package rules. They cannot prove that the undocumented toolbar registration behaves correctly in a running simulator.

## In-simulator acceptance check

After building or installing a release package:

1. Start MSFS 2020 and a flight with the package enabled.
2. Confirm that the **METAR Viewer** toolbar icon appears once.
3. Open, close, move, and resize the panel.
4. Search `EGLL`, `LHR`, `London Heathrow`, and a typo such as `Heatrow`.
5. Confirm the raw report, decoded values, and flight category are populated.
6. Confirm the provider badge shows the simulator source when available and VATSIM when fallback is required.
7. Close and reopen the panel, then restart the simulator and confirm the last station is restored when the simulator data store is available.
8. Check the Coherent debugger for load, network, and lifecycle errors while repeatedly opening and closing the panel.

If the package is not detected, first check that `manifest.json` and `layout.json` are at the package root. Developer Mode **Tools → Packages** can show whether MSFS mounted the package.

## Compatibility notes

The package uses the widely used `InGamePanels.InGamePanelDefinition` toolbar pattern. The registration surface is not a stable, formally documented extension contract, so re-test the package after simulator updates. Package conflicts or broken toolbar-panel mods can also prevent a panel from appearing; isolate the package in a clean Community folder when diagnosing that case.

## Source layout

```text
MetarViewerToolbar.xml                              MSFS project definition
PackageDefinitions/metar-viewer-toolbar.xml        package and asset groups
PackageSources/InGamePanels/                       toolbar registration source
PackageSources/html_ui/InGamePanels/MetarViewer/   panel UI and portable logic
PackageSources/html_ui/Textures/                   toolbar icon
tools/                                              source/package validators and ZIP helper
tests/                                              Node regression tests
build.bat                                           Windows SDK build entry point
```

The SDK creates `_PackageInt/` and `Packages/` as local build output. They are ignored by Git and should not be committed.

## References

- [Microsoft: How to install mods in MSFS 2020](https://flightsimulator.zendesk.com/hc/en-us/articles/7058492594588-How-to-install-mods-in-Microsoft-Flight-Simulator-2020)
- [MSFS 2020 SDK overview](https://docs.flightsimulator.com/html/Introduction/SDK_Overview.htm)
- [Using the SDK and Community package structure](https://docs.flightsimulator.com/html/Introduction/Using_The_SDK.htm)
- [Package Tool](https://docs.flightsimulator.com/html/Additional_Information/Tools/Package_Tool/Package_Tool.htm)
- [Community package export](https://docs.flightsimulator.com/html/Developer_Mode/Project_Editor/Export_Window.htm)
- [Coherent JavaScript API](https://docs.flightsimulator.com/html/Programming_Tools/JavaScript/Coherent.htm)
- [Facility listener calls](https://docs.flightsimulator.com/html/Programming_Tools/JavaScript/Coherent_Listeners/JS_LISTENER_FACILITY.htm)
- [AviationWeather.gov API restrictions](https://aviationweather.gov/data/api/)
