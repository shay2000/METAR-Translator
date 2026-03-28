# METAR Viewer

A modern Windows 11 desktop application for viewing aviation weather reports (METARs). Built with WinUI 3, .NET 8, and featuring a liquid glass UI design aesthetic.

![Windows 11](https://img.shields.io/badge/Windows-11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

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

### Easiest Option

If the repository has a packaged Windows release attached, download the self-contained ZIP package, extract it, and run `MetarViewer.exe`.

**Important**: do **not** download only `MetarViewer.exe` by itself. This WinUI 3 app needs the supporting files from the published folder alongside the `.exe`.

### If No Packaged ZIP Is Available

Build a self-contained Windows package locally:

```powershell
dotnet publish .\MetarViewer.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishDir=.\publish\win-x64-self-contained\
```

Then run:

`publish\win-x64-self-contained\MetarViewer.exe`

## Screenshots

The app features:
- Clean, minimalist search interface
- Glassmorphic cards with subtle blur effects
- Comprehensive weather breakdown (wind, visibility, clouds, temperature, altimeter)
- Monospace-formatted raw METAR for easy reading
- Decoded weather information in plain English

## Prerequisites

Before building the app, ensure you have:

- **Windows 11** (version 21H2 or later recommended)
- **Visual Studio 2022** (version 17.8 or later)
  - Workload: ".NET Desktop Development"
  - Workload: "Universal Windows Platform development"
  - Individual component: "Windows App SDK C# Templates"
- **.NET 8 SDK** (https://dotnet.microsoft.com/download/dotnet/8.0)
- **Windows App SDK** (usually installed with Visual Studio workloads)

## Installation & Setup

### 1. Clone or Download

Download this project to your local machine.

### 2. Open in Visual Studio

Open `MetarViewer.sln` in Visual Studio 2022.

### 3. Restore NuGet Packages

Visual Studio should automatically restore packages. If not:
- Right-click the solution in Solution Explorer
- Select "Restore NuGet Packages"

### 4. Build the Solution

Press `Ctrl+Shift+B` or:
- Build → Build Solution

### 5. Run the Application

Press `F5` or click the "Start" button in Visual Studio.

## Configuration

### API Access

The app uses the **Aviation Weather Center Data API** to fetch METAR data.

**Endpoint**:
- `GET https://aviationweather.gov/api/data/metar?ids={ICAO}&format=json`

**Notes**:
- No API key is required for normal app usage
- The app sets a custom `User-Agent` and caches successful responses for 60 seconds
- The Aviation Weather Center asks clients to stay under 100 requests per minute and to avoid polling the same thread more often than once per minute

### Airport Search

The app uses AirportsAPI for worldwide airport lookups.

Airport resolution is network-based. If AirportsAPI is unavailable, the app falls back to direct 3-4 letter code heuristics, but name-based airport search requires internet access.

## Usage

1. **Launch the app**: Run from Visual Studio or use the compiled .exe
2. **Enter an airport**: Type any of:
   - ICAO code: `EGLL`
   - IATA code: `LHR`
   - Airport name: `London Heathrow` or just `Heathrow`
3. **Get METAR**: Click "Get METAR" or press Enter
4. **View results**:
   - Quick summary card shows current conditions at a glance
   - Raw METAR section displays the unprocessed observation
   - Decoded section breaks down all weather elements
5. **Toggle theme**: Click the theme icon (☀️/🌙) in the top-right

## Project Structure

```
MetarViewer/
├── Models/              # Data models (METAR data and API responses)
├── Services/            # Business logic (METAR API, airport lookup)
├── ViewModels/          # MVVM view models (MainViewModel)
├── Views/               # UI windows (MainWindow)
├── Helpers/             # Utilities (MetarDecoder, converters)
└── Assets/              # Images and resources

MetarViewer.Tests/       # Unit tests for decoder logic
```

## Architecture

The app follows **MVVM (Model-View-ViewModel)** pattern:

- **Models**: Plain data classes for METAR data and API responses
- **Services**: API calls and data access (`AviationWeatherMetarService`, `AirportLookupService`)
- **ViewModels**: UI logic and state management (MainViewModel)
- **Views**: XAML UI definitions (MainWindow)
- **Dependency Injection**: Services are injected via Microsoft.Extensions.DependencyInjection

## Testing

The solution includes unit tests for the METAR decoding logic.

**To run tests**:
1. Open Test Explorer: `Test → Test Explorer`
2. Click "Run All Tests"

Or from command line:
```bash
dotnet test .\MetarViewer.Tests.csproj
```

Tests cover:
- Wind decoding (direction, speed, gusts, calm conditions)
- Visibility interpretation (standard and CAVOK)
- Cloud layer parsing (multiple layers, coverage types)
- Temperature/dew point handling
- Altimeter conversion (inHg to hPa)
- Flight category descriptions
- Weather phenomena decoding

## Deployment

### Debug Build

Located in: `bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\`

### Release Build

1. Change configuration to **Release**
2. Select platform: **x64** (or x86/ARM64)
3. Build → Rebuild Solution
4. Output in: `bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\`

### Creating a Package

For distribution, publish a self-contained Windows build and share the whole published folder or a ZIP of that folder:

```powershell
dotnet publish .\MetarViewer.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishDir=.\publish\win-x64-self-contained\
```

Share this folder:

`publish\win-x64-self-contained\`

Do **not** share only `MetarViewer.exe`. The app still needs the rest of the published WinUI support files that ship alongside the executable.

## Troubleshooting

### "Could not retrieve METAR"

**Causes**:
- Network connectivity issue
- Invalid station ID (airport doesn't report weather)
- Aviation Weather Center rate limit reached
- Aviation Weather Center is temporarily unavailable

**Solutions**:
- Check internet connection
- Try a major airport (KJFK, EGLL, LFPG)
- Wait a minute and try again
- Try again shortly if the weather service is temporarily unavailable

### "Could not find airport"

**Causes**:
- Airport lookup did not return a match
- Typo in search term

**Solutions**:
- Try the exact ICAO code (4 letters)
- Try the airport's official name or IATA/ICAO code
- The app will still try 3-4 letter codes directly

### App won't start

**Causes**:
- Missing Windows App SDK runtime
- Wrong .NET version

**Solutions**:
- Install Windows App SDK: https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
- Install .NET 8 Runtime: https://dotnet.microsoft.com/download/dotnet/8.0
- Rebuild in Visual Studio

### Theme doesn't apply

**Cause**: Theme setting stored in app data

**Solution**:
- Delete app local data: `%LocalAppData%/Packages/[PackageFamily]/LocalState`
- Restart app

## Technologies Used

- **WinUI 3**: Modern Windows UI framework
- **.NET 8**: Latest .NET runtime
- **Windows App SDK**: Windows 11 platform features
- **CommunityToolkit.Mvvm**: MVVM helpers and commands
- **Aviation Weather Center Data API**: Aviation weather data source
- **xUnit**: Unit testing framework

## API Documentation

This app uses the Aviation Weather Center Data API:
- Documentation: https://aviationweather.gov/data/api/
- Endpoint: `GET /api/data/metar?ids={station_id}&format=json`
- Response: JSON array containing the latest METAR data for the requested station

## License

MIT License - feel free to use, modify, and distribute.

## Contributing

Contributions welcome! Areas for improvement:

- Add TAF (Terminal Aerodrome Forecast) support
- Implement airport search with auto-complete
- Add weather map visualization
- Save favorite airports
- Export METAR history
- Add NOTAM viewer
- Implement multiple language support

## Acknowledgments

- Weather data provided by the Aviation Weather Center (https://aviationweather.gov/data/api/)
- Airport data compiled from various public sources
- Windows 11 design guidelines followed throughout

## Support

For issues or questions:
1. Check the Troubleshooting section above
2. Review the Aviation Weather Center API documentation
3. Ensure all prerequisites are installed
4. Try rebuilding from a clean state

---

**Built with ❤️ for the aviation community**
