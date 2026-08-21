# MSFS 2020 Quick Start

Install METAR Viewer as a normal Microsoft Flight Simulator 2020 Community add-on.

This repository currently contains source files only. If you do not already have a compiled `metar-viewer-toolbar` ZIP, follow the [build guide](integrations/msfs2020/README.md) first; the package cannot be created on macOS without the Windows MSFS SDK.

## Before you start

You need:

- Microsoft Flight Simulator 2020 for Windows.
- A compiled `metar-viewer-toolbar` package, normally supplied as a release ZIP.

You do **not** need the .NET desktop app, Visual Studio, Node.js, or the MSFS SDK just to install a packaged release. Those tools are only needed when building the package from source; see [the build guide](integrations/msfs2020/README.md).

## Install

1. If you do not know the Community folder, start MSFS 2020, enable **Developer Mode** under **Options → General Options → Developers**, and open **Developer Toolbar → Tools → Virtual File System → Packages Folders → Open Community Folder**. This opens the exact folder the simulator is watching. Then exit MSFS before copying the mod. The Microsoft Store/Xbox App and Steam versions use different default locations, so use this method rather than guessing a path. If you already know the folder, open it directly. Do not copy a mod while the simulator is running.
2. Open the METAR Viewer release ZIP.
3. Copy or extract the folder named `metar-viewer-toolbar` into the Community folder.
4. Check the final layout:

   ```text
   Community\metar-viewer-toolbar\manifest.json
   Community\metar-viewer-toolbar\layout.json
   Community\metar-viewer-toolbar\InGamePanels\InGamePanel_MetarViewer.spb
   Community\metar-viewer-toolbar\html_ui\InGamePanels\MetarViewer\MetarViewer.html
   ```

   `manifest.json` and `layout.json` must be directly inside `metar-viewer-toolbar`. A common mistake is creating an extra nested folder:

   ```text
   Community\metar-viewer-toolbar\metar-viewer-toolbar\manifest.json  ← wrong
   ```

5. Start MSFS 2020 and start a flight.
6. Select the **METAR Viewer** icon in the toolbar. If it is not immediately visible, check the toolbar overflow menu.
7. Test with `EGLL`, `KJFK`, or another airport with a current METAR.

## If you downloaded the source repository

Do not copy the repository into Community. Files such as `MetarViewerToolbar.xml`, `PackageDefinitions`, and `PackageSources` are build inputs, not an installable mod. The package must contain the SDK-generated `InGamePanel_MetarViewer.spb`, `layout.json`, and `manifest.json`.

If this repository's GitHub Actions workflow is enabled, open **Actions → Build MSFS 2020 Community Package**, choose **Run workflow**, and download the ZIP from the completed run's **Artifacts** section. A tag such as `msfs-v1.0.0` publishes the same ZIP as a GitHub Release asset. If the hosted build fails while running `fspackagetool.exe`, use a Windows machine with the MSFS 2020 SDK and Node.js, then follow [integrations/msfs2020/README.md](integrations/msfs2020/README.md).

## Desktop apps: build and run

The desktop variants are no longer separate branches. They are sibling projects in
`MetarViewer.sln`, so one checkout builds whichever one you need:

| Project | Platforms | Use it for |
| --- | --- | --- |
| `src/MetarViewer.App.Avalonia` | macOS, Linux, Windows | The cross-platform desktop app. This is what ships as the Windows `.exe` and the macOS `.dmg`. |
| `src/MetarViewer.App.WinUI` | Windows only | A Windows-native WinUI 3 UI, retained in the repository but not included in current releases. |
| `src/MetarViewer.Core` | any | Shared parsing/models/services. Referenced by both apps; contains no UI code. |
| `integrations/msfs2020` | MSFS 2020 | The in-game toolbar panel described above. |

Run the cross-platform app on any OS:

```bash
dotnet build src/MetarViewer.App.Avalonia
dotnet run --project src/MetarViewer.App.Avalonia
```

Run the Windows-native app (**from Windows only**):

```bash
dotnet build src/MetarViewer.App.WinUI
dotnet run --project src/MetarViewer.App.WinUI
```

Build everything and run the tests:

```bash
dotnet build MetarViewer.sln
dotnet test MetarViewer.sln
```

On macOS and Linux the solution build intentionally skips
`MetarViewer.App.WinUI`, because WinUI 3 targets Windows only. Building that one
project directly on a non-Windows machine fails with `NETSDK1100`; that is
expected and does not indicate a broken checkout.

If `dotnet` cannot resolve the SDK pinned in `global.json`, run
`./scripts/bootstrap-dotnet.sh` once. See the README for details.

`integrations/msfs2020` is used by whichever variant needs MSFS 2020 support. It
is not a .NET project — the panel is HTML/JavaScript loaded by the simulator — so
it is built with the packaging tools above rather than with `dotnet`.

## Remove or update

1. Close MSFS 2020.
2. Delete or replace `Community\metar-viewer-toolbar`.
3. Restart the simulator.


## Troubleshooting

### No toolbar icon

- Confirm that the package folder is in the Community folder opened by Developer Mode.
- Confirm that `manifest.json` and `layout.json` are at the package root.
- Confirm that `InGamePanels\InGamePanel_MetarViewer.spb` exists and is not an XML file renamed to `.spb`.
- Restart the simulator after copying the package.
- Temporarily remove other toolbar-panel mods to check for a package conflict after a simulator update.

### The panel opens but weather is unavailable

- Try a direct four-letter ICAO code such as `EGLL`.
- Check your internet connection; airport-name lookup and the VATSIM fallback use online services.
- Try again after a minute if a public weather service is temporarily unavailable.

### Important limitation

The panel is intended for MSFS 2020 PC Community packages. It has not been tested on Xbox, and simulator updates can change undocumented toolbar-panel behavior. Remove or update community packages if a future simulator update causes compatibility problems.
