# METAR Viewer

METAR Viewer is a Windows 11 desktop app for searching airports and viewing live METAR weather reports in both raw and decoded format.

![Windows 11](https://img.shields.io/badge/Windows-11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## ⚠️ **Disclaimer:**  

- METAR Viewer is intended **for virtual simulation and hobbyist use only**.  
- **Never use this application, or any information it provides, for real-world aviation, flight planning, or navigation purposes.**  
- Always obtain official weather and briefing information from certified sources before conducting any real-world flight.

## Features

- 🔍 **Flexible Airport Search**: Enter ICAO codes (e.g., EGLL), IATA codes (e.g., LHR), or airport names (e.g., "London Heathrow")
- 📊 **Dual Display**: View both raw METAR data and human-readable decoded information
- 🎨 **Liquid Glass UI**: Modern Windows 11 design with Mica backdrop and glassmorphic elements
- 🌓 **Theme Toggle**: Switch between light and dark modes with a single click
- ⚡ **Smart Caching**: Automatic 60-second response caching to minimize API calls
- 💾 **Session Persistence**: Remembers your last searched airport across app restarts
- ✈️ **Flight Categories**: Clear visual indication of VFR, MVFR, IFR, and LIFR conditions
- 📱 **Responsive Design**: Adaptive layout that works well on different screen sizes

## Download

Download the latest packaged build from the [GitHub Releases page](https://github.com/shay2000/METAR-Translator/releases/latest), extract the ZIP, and run `MetarViewer.exe`.

Important:
- Download the full ZIP release, not just a single `.exe`
- Keep all files from the extracted folder together
- The app is intended for Windows 11

## Alternative Download

You can also download the Zip file from flightsim.to from this link: https://flightsim.to/addon/106602/metar-translator

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

- Make sure you extracted the whole ZIP before running it
- Do not move `MetarViewer.exe` out of its published folder
- If Windows warns about an unknown app, use `More info` and then `Run anyway` if you trust the release source

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
- Windows 11
- Visual Studio 2022
- .NET 8 SDK

Open `MetarViewer.sln` in Visual Studio, or publish from the command line:

```powershell
dotnet publish .\MetarViewer.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishDir=.\publish\win-x64-self-contained\
```

Run tests with:

```powershell
dotnet test .\MetarViewer.Tests.csproj
```

## Project Notes

- Airport lookup: [AirportsAPI](https://airportsapi.com/docs/api)
- Primary METAR source: [VATSIM METAR API](https://vatsim.dev/api/metar-api/get-metar/)
- Fallback METAR source: [Aviation Weather Center Data API](https://aviationweather.gov/data/api/)

**Built with ❤️ for the aviation community**

## License

MIT License.
