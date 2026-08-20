# METAR Viewer

METAR Viewer is a native-feeling macOS desktop app for searching airports and viewing live METAR weather reports in both raw and decoded format. The repository also contains a separate self-contained Microsoft Flight Simulator 2020 toolbar-panel implementation.

![macOS](https://img.shields.io/badge/macOS-14%2B%20%7C%20Intel%20and%20Apple%20Silicon-black)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## ⚠️ **Disclaimer:**  

- METAR Viewer is intended **for virtual simulation and hobbyist use only**.  
- **Never use this application, or any information it provides, for real-world aviation, flight planning, or navigation purposes.**  
- Always obtain official weather and briefing information from certified sources before conducting any real-world flight.

## Features

- 🔍 **Flexible Airport Search**: Enter ICAO codes (e.g., EGLL), IATA codes (e.g., LHR), or airport names (e.g., "London Heathrow")
- 📊 **Dual Display**: View both raw METAR data and human-readable decoded information
- 🍎 **Mac Desktop App**: A responsive interface with native light and dark appearances
- 🌓 **Theme Toggle**: Switch between light and dark modes with a single click
- ⚡ **Smart Caching**: Automatic 60-second response caching to minimize API calls
- 💾 **Session Persistence**: Remembers your last searched airport across app restarts
- ✈️ **Flight Categories**: Clear visual indication of VFR, MVFR, IFR, and LIFR conditions
- 📱 **Responsive Design**: Adaptive layout that works well on different screen sizes
- 🛩️ **In-Game Toolbar Panel**: A separate MSFS 2020 Community package displays and decodes METARs without launching the desktop app

## Download for Mac

1. Open the [GitHub Releases page](https://github.com/shay2000/METAR-Translator/releases).
2. Find the newest release titled **“METAR Viewer for Mac”** (its tag begins with `mac-v`).
3. Download the `.dmg` that matches your Mac:
   - **Apple-Silicon** for M1, M2, M3, M4, and newer Apple chips.
   - **Intel** for older Intel-based Macs.
4. Open the `.dmg`, then drag **METAR Viewer** into the **Applications** shortcut.
5. Start METAR Viewer from Applications.

> **First launch:** CI builds are ad-hoc signed for bundle integrity, but they are not
> Developer ID signed or notarized. If macOS blocks the app, Control-click it in
> Applications, choose **Open**, and confirm **Open**. Only do this for a download
> you trust from this repository.

## Alternative Download

You can also download the Zip file from flightsim.to from this link: https://flightsim.to/addon/106602/metar-translator

## Microsoft Flight Simulator 2020 Toolbar

The [`FlightSimIntegration`](https://github.com/shay2000/METAR-Translator/tree/FlightSimIntegration) branch contains a native MSFS 2020 toolbar-panel port under `integrations/msfs2020`. It runs entirely inside the simulator's HTML/JavaScript UI, requests the simulator METAR first, and uses VATSIM as a fallback. No external METAR Viewer process is required while flying.

The source and portable tests are complete, but the final panel-registration `.spb` must be compiled on Windows with the current MSFS 2020 SDK and validated in the simulator before release. See the [MSFS integration build and installation guide](integrations/msfs2020/README.md) for prerequisites, commands, and the in-simulator acceptance checklist.

## How To Use

1. Open the app.
2. Type an airport code or name such as `EGLL`, `LHR`, or `London Heathrow`.
3. Click `Get METAR` or press Enter.
4. Read the decoded weather summary and raw METAR.

The app can search by:
- ICAO code
- IATA code
- Airport name
- Close-match suggestions for minor typos

## Features

- Fast airport search with live suggestions
- Raw and decoded METAR display
- Flight category badge for quick reading
- Altimeter shown in both `hPa` and `inHg`
- Light and dark theme toggle
- Airport lookup powered by AirportsAPI
- VATSIM METAR as the primary weather source with Aviation Weather fallback

## Troubleshooting

### App Will Not Open

- Confirm that you downloaded the correct Apple-Silicon or Intel `.dmg`
- Move the app to Applications before launching it
- For an ad-hoc signed build, Control-click the app and choose **Open** on first launch

### Could Not Retrieve METAR

Possible causes:
- The airport does not currently publish a METAR
- Your internet connection is unavailable
- The weather providers are temporarily unavailable

Try:
- A major airport such as `EGLL`, `KJFK`, `LFPG`, or `OMDB`
- Searching by ICAO instead of name
- Waiting a minute and trying again

### Could Not Find Airport

Try:
- The exact ICAO or IATA code
- The airport's official name
- A simpler search term with fewer words

## For Developers

If you want to build the app locally, you will need:
- macOS 14 or newer on Apple Silicon or Intel
- .NET 8 SDK

Build and run from the command line:

```bash
dotnet run --project src/MetarViewer.App/MetarViewer.App.csproj
```

Run tests with:

```bash
dotnet test MetarViewer.sln
```

Build an ad-hoc signed and verified DMG locally with:

```bash
scripts/package-macos.sh osx-arm64 1.0.0
# Or, for an Intel Mac:
scripts/package-macos.sh osx-x64 1.0.0
```

The DMG and its SHA-256 checksum are written under `artifacts/macos/<runtime>/`.
The packaging script validates the bundle metadata, executable architecture and
code signature, verifies the disk image, then mounts it read-only and validates
the installed contents before returning success.

The code is split into two projects. `MetarViewer.Core` holds the METAR parsing,
decoding and web service code and targets plain `net8.0`, so its tests run on any
operating system. `MetarViewer.App` holds the Avalonia desktop UI and publishes for
both Apple Silicon (`osx-arm64`) and Intel (`osx-x64`) Macs.

## Maintainer: Publishing a Mac Release

Relevant pushes to `Mac-Version` run the **Build macOS Release** workflow and
upload Apple Silicon and Intel DMGs as workflow artifacts retained for 30 days.
Branch-push artifacts are development builds and do not create a GitHub Release.

To publish permanent release assets, push a strict `mac-vX.Y.Z` tag from a commit
on the `Mac-Version` branch:

```bash
git switch Mac-Version
git tag mac-v1.0.0
git push origin mac-v1.0.0
```

For a release tag, the workflow tests the whole solution, builds separate Apple
Silicon and Intel application bundles, packages and verifies each `.dmg`, and
creates an explicitly titled **METAR Viewer for Mac** entry under GitHub Releases.
The release is created only after both architecture jobs succeed.

## Project Notes

- Airport lookup: [AirportsAPI](https://airportsapi.com/docs/api)
- Primary METAR source: [VATSIM METAR API](https://vatsim.dev/api/metar-api/get-metar/)
- Fallback METAR source: [Aviation Weather Center Data API](https://aviationweather.gov/data/api/)

**Built with ❤️ for the aviation community**

## License

MIT License.
