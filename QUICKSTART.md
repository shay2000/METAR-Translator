# Quick Start Guide

Get the METAR Viewer app running in 5 minutes.

## Step 1: Prerequisites

Install these first:

1. **Visual Studio 2022** (Community, Professional, or Enterprise)
   - Download: https://visualstudio.microsoft.com/downloads/
   - During install, select:
     - ".NET Desktop Development" workload
     - "Universal Windows Platform development" workload

2. **.NET 8 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Install the SDK (not just runtime)

## Step 2: Open the Project

1. Extract the project files to a folder (e.g., `C:\Projects\MetarViewer`)
2. Open `MetarViewer.sln` in Visual Studio 2022
3. Wait for NuGet packages to restore (check status bar)

## Step 3: Build

1. Select **Debug** and **x64** from the dropdowns at the top
2. Press `Ctrl+Shift+B` or click Build → Build Solution
3. Wait for "Build succeeded" message

## Step 4: Run

1. Press `F5` or click the green "Start" button
2. The METAR Viewer window will open

## Step 5: Test It

1. Type `EGLL` in the search box
2. Press Enter or click "Get METAR"
3. You should see London Heathrow weather data

## Troubleshooting

**Build errors?**
- Make sure you installed both workloads in Visual Studio
- Try: Tools → NuGet Package Manager → Package Manager Console
- Run: `Update-Package -Reinstall`

**App won't start?**
- Check you selected x64 (not x86 or ARM64)
- Try rebuilding: Build → Rebuild Solution

**"Could not retrieve METAR"?**
- Check your internet connection
- One of the public weather providers may be temporarily unavailable
- Try again in 60 seconds

**Still not getting a report?**
1. Try a major reporting airport such as `EGLL`, `KJFK`, or `OMDB`
2. Check that the airport actually publishes METAR data
3. Try again shortly in case a provider is lagging

## What's Next?

- Try other airports: KJFK, LFPG, RJTT
- Toggle dark/light theme (button in top-right)
- Try airport names or partial searches in the search box
- Read the full README.md for advanced features

## Need Help?

Check README.md for detailed troubleshooting and documentation.
