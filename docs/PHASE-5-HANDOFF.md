# Phase 5 Handoff — UI / MVVM Refactor

**Status: NOT STARTED. This document is instructions only; no Phase 5 code has been written.**

Phases 0–4 and 6 of the refactor are complete and committed. Phase 5 was deferred
because it touches `src/MetarViewer.App`, which is a WinUI 3 project and **cannot be
compiled on macOS**, where the earlier phases were done. Everything described here
therefore needs a Windows machine with the .NET 8 SDK and the Windows App SDK
workload.

This is written for whoever picks that up. It assumes no memory of the earlier
work.

---

## 1. Read this first: where the project currently stands

```
MetarViewer.sln
Directory.Build.props          LangVersion 12.0, ImplicitUsings + Nullable enabled,
                               EnforceCodeStyleInBuild true
src/MetarViewer.Core/          net8.0  — parsing, decoding, HTTP services. No UI types.
                               RootNamespace MetarViewer, AssemblyName MetarViewer.Core
src/MetarViewer.App/           net8.0-windows10.0.19041.0 — WinUI 3. AssemblyName MetarViewer
tests/MetarViewer.Core.Tests/  net8.0  — 253 tests, all passing
```

`MetarViewer.Core.csproj` has `InternalsVisibleTo` for **`MetarViewer`** and
**`MetarViewer.Core.Tests`**, so `internal` Core types are visible to both the app and
the tests. Note the namespaces do *not* contain `.Core`; they are
`MetarViewer.Models`, `MetarViewer.Services`, `MetarViewer.Helpers`,
`MetarViewer.Parsing`, `MetarViewer.Airports`.

**Establish the baseline before changing anything.** On Windows:

```powershell
dotnet build .\MetarViewer.sln
dotnet test .\tests\MetarViewer.Core.Tests\MetarViewer.Core.Tests.csproj
```

The build of `src/MetarViewer.App` has **never been verified** since Phase 1 moved it
into `src/`. Three files were edited without ever being compiled:

- `src/MetarViewer.App/App.xaml.cs` — its DI wiring was rewritten to call the new
  `services.AddMetarViewerServices()` extension
- `src/MetarViewer.App/MetarViewer.App.csproj` — renamed and moved
- `MetarViewer.sln` — project paths rewritten

If the build fails, **fix that and commit it as its own change before starting Phase
5.** Do not fold a Phase 1 fallout fix into a Phase 5 refactor commit; keeping a
"make it build again" change separate from a "restructure it" change is what makes
either one reviewable.

Then run the app once and confirm it works end to end: search `EGLL`, get a METAR,
toggle the theme, restart and confirm the last station is restored. You need a
known-good starting point, because most of what follows is behaviour-preserving and
you can only tell you preserved it if you saw it work first.

---

## 2. Ground rules

These were followed by the earlier phases; please keep to them so the history stays
consistent.

1. **One phase, one commit**, with a multi-paragraph message that explains *why* the
   change was made — the smell, duplication or bug being removed — not just what
   moved where. Read `git log` for examples.
2. **Every extracted type gets an XML doc comment saying why it exists.** Comments
   explain rationale; they never restate the code.
3. **Tests green before every commit.** Never commit a red suite.
4. **Behaviour-preserving unless a defect is called out below.** Where you find a bug,
   fix it in a *separate* commit from the restructuring, so the behaviour change is
   visible on its own.
5. If a change turns out to be bigger than described here, **stop and split it**
   rather than growing one commit.

---

## 3. Hard constraints — things that will break the build if you rename them

`MainWindow.xaml` uses **compiled `x:Bind`** exclusively; there is not a single classic
`{Binding}` in the project. Every path below is resolved at compile time, so a rename
on either side is a build error rather than a silent runtime failure. That is helpful,
but it means renames must be done in lockstep with the XAML.

The 23 bound paths, all rooted at `ViewModel`:

```
AirportSuggestions      CurrentMetarVisibility   DecodedAltimeter    DecodedClouds
DecodedTemperature      DecodedVisibility        DecodedWeather      DecodedWind
CurrentTheme            ErrorMessage             FetchMetarCommand   HasError
IsLoading               LoadingVisibility        ObservationTimeText SearchText
StationHeaderText       ThemeToggleGlyph         ThemeToggleToolTip  ToggleThemeCommand
FlightCategoryDescription
CurrentMetar.FlightCategory     ← reaches into Core
CurrentMetar.RawMetar           ← reaches into Core
```

Two of those cross into the Core project, so **`MetarData.FlightCategory` and
`MetarData.RawMetar` must keep their exact names.** More generally, do not rename
`MetarData` properties without checking the XAML.

`AirportSuggestion.DisplayText` is also load-bearing in a way the compiler will *not*
catch: the `AutoSuggestBox` uses `TextMemberPath="DisplayText"`, which is resolved by
**reflection at runtime**. Renaming it compiles cleanly and then shows blank rows in
the suggestion list. Leave it alone, or if you must change it, test the suggestion
dropdown by hand.

Do not change `IMetarService.GetMetarAsync`'s signature. It is
`Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)`
and the null-means-failure convention is relied on by the view model's error handling.
Changing it to a result type was considered and deliberately rejected as too wide a
blast radius for the value.

---

## 4. The work

Six tasks, ordered so each one leaves the app runnable. **5.1 is the one that
matters**; 5.2–5.6 are smaller and independently valuable if you run out of time.

### 5.1 — Make `MainViewModel` testable by removing its WinUI dependencies

**The problem.** `src/MetarViewer.App/ViewModels/MainViewModel.cs` is 349 lines and
holds essentially all of the app's behaviour, but **not one line of it is under test**,
because the file transitively depends on WinUI and so can only be compiled on Windows
inside a project that cannot host a plain xUnit run. Concretely, `using
Microsoft.UI.Xaml;` on line 6 pulls in two UI types:

- `Visibility` — line 89 `LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed`,
  and line 94 `CurrentMetarVisibility`
- `ElementTheme` — line 79 `_currentTheme = ElementTheme.Default`, plus
  `OnCurrentThemeChanged` (164), `ThemeToggleGlyph` (99) and `ThemeToggleToolTip` (111)

and it reads and writes the user's settings directly at lines 311–338 via
`Windows.Storage.ApplicationData.Current.LocalSettings`.

Those are the only things standing between this file and a normal unit test.

**What to do.** Create a new project `src/MetarViewer.Presentation` targeting plain
`net8.0`, referencing `MetarViewer.Core` and `CommunityToolkit.Mvvm`, and move
`MainViewModel` into it. `CommunityToolkit.Mvvm` and its `[ObservableProperty]` source
generator work fine on `net8.0`; only the two WinUI enums and the settings API are the
blockers. Remove them as follows.

*Visibility.* Do not map booleans to `Visibility` in the view model at all — that is a
view concern and the view already has the machinery for it. Delete
`LoadingVisibility` and `CurrentMetarVisibility`, bind the XAML to `IsLoading` and to a
new `HasMetar` bool, and convert in the XAML with the **existing but currently unused**
`BoolToVisibilityConverter` (see 5.4 — this is what brings those dead converters back
into service). Remember to update the two `x:Bind` sites and the
`OnPropertyChanged(nameof(...))` calls on lines 156 and 160.

*Theme.* Introduce a small platform-neutral `AppTheme { System, Light, Dark }` enum in
the presentation project and have the view model expose that. Map `AppTheme` to
`ElementTheme` in the App project — a value converter is the natural place. While you
are there, move `ThemeToggleGlyph` (the `\u263E` / `\u2600` glyphs, line 99) and
`ThemeToggleToolTip` (line 111) out of the view model too: a moon-versus-sun glyph and
an English tooltip are presentation details of a specific button, and a view model
should not be the thing that knows them. Prefer expressing them in XAML off the theme
value.

*Settings.* Define an interface in the presentation project — something like
`ISettingsStore` with a get and a set for a string key — and implement it in the App
project over `ApplicationData.Current.LocalSettings`. Inject it into the view model.
This is what finally makes the last-station behaviour testable, and it is also where
the duplication noted in 5.3 gets resolved.

Register the new project's types in `App.xaml.cs` alongside the existing
`AddMetarViewerServices()` call, and add a `ProjectReference` from
`MetarViewer.App` to `MetarViewer.Presentation`.

**Verification.** The app must still build, run and behave identically — check the
loading spinner, the METAR panel appearing, the theme toggle and its icon, and that the
last station still survives a restart. Then add
`tests/MetarViewer.Presentation.Tests` (net8.0, xUnit) and write tests for the view
model. Note the existing test project uses **no mocking library**: dependencies are
hand-written stubs, and there is a hand-rolled `FakeTimeProvider` in
`tests/MetarViewer.Core.Tests/ExpiringCacheTests.cs` you can copy the approach from.
Follow that convention — write a stub `IMetarService`, `IAirportLookupService` and
`ISettingsStore`. Cover at minimum: a successful fetch populating the decoded
properties; a null result from the service producing an error message; a cancelled
suggestion lookup; and the last-station round trip.

### 5.2 — Move the search box logic out of the code-behind

`src/MetarViewer.App/Views/MainWindow.xaml.cs` holds three `AutoSuggestBox` handlers
(lines 50, 81, 93) that are doing more than wiring. `SearchBox_TextChanged` owns the
`CancellationTokenSource` lifecycle for suggestion lookups, and `QuerySubmitted`
decides whether to use the chosen suggestion or the raw text before invoking the fetch
command. That is view-model work sitting in a file that cannot be tested, and it is
why the cancellation behaviour in 5.5 has no coverage today.

Move the cancellation bookkeeping and the choose-suggestion-or-raw-text decision into
the view model, leaving the handlers to forward the event and set
`IsSuggestionListOpen`. The view model already exposes
`UpdateAirportSuggestionsAsync`, `SelectAirportSuggestion` and
`ClearAirportSuggestions`, so this is mostly relocation.

All three handlers are `async void`. That is unavoidable for WinUI event handlers, but
it means an exception escaping one of them takes the process down. Once the bodies are
thin, each should be a single awaited call to the view model, which is where the
exception can actually be handled.

### 5.3 — Fix the swallowed exceptions and the duplicated settings access

Three places discard information in ways that will make a real bug very hard to
diagnose:

- Lines 311–322 (`SaveLastStation`) and 324–338 (`LoadLastStation`) are near-identical
  try/`catch` blocks around `LocalSettings`, both with a **bare `catch`** that hides
  every failure. Task 5.1 replaces both with one `ISettingsStore` implementation, which
  removes the duplication; make sure the surviving `catch` is narrowed to the exception
  that can actually occur and does not silently discard it.
- Line 273 has another **bare `catch`** in the suggestions path, so a genuine bug in
  airport lookup is indistinguishable from "no suggestions found".
- Line 220 `catch (Exception ex)` in `FetchMetarAsync` turns every failure into one
  user-facing string, so a `NullReferenceException` in decoding is reported to the user
  as though it were a network problem.

Narrow these to the exceptions that can really occur — `HttpRequestException`,
`TaskCanceledException`, `JsonException` are the ones the Core services throw; Phase 4
established that convention in `AirportCandidateFinder`. Let genuinely unexpected
exceptions surface rather than mislabeling them as weather-fetch failures. Add tests
for the paths you change; this is a behaviour change, so commit it separately.

### 5.4 — Delete or wire up the four dead converters

`src/MetarViewer.App/Helpers/Converters.cs` defines `StringToBoolConverter`,
`NullToVisibilityConverter`, `BoolToVisibilityConverter` and
`DateTimeToStringConverter`. All four are instantiated as resources in `App.xaml`
(lines 15–18) but **not one is referenced by any `Converter=` in `MainWindow.xaml`** —
they are constructed at startup and never used. This is almost certainly why the
`Visibility` properties ended up on the view model instead.

Task 5.1 gives `BoolToVisibilityConverter` a real job. Do the same for the others or
delete them, and remove the corresponding `App.xaml` resource entries either way.
Leaving unused converters in place invites the next person to add a fifth rather than
use one of these.

### 5.5 — Add a real debounce to the suggestion search

Despite the comment in `MainWindow.xaml.cs` saying "debounce-like cancellation", there
is **no debounce**: `SearchBox_TextChanged` cancels the in-flight request and
immediately starts another, with no `Task.Delay` anywhere in the file. Typing
"London Heathrow" fires a lookup per keystroke, most of them cancelled mid-flight. The
`AirportsAPI` calls are cached (Phase 4's `ExpiringCache`, 2 minutes for suggestions)
so this is not as costly as it looks, but it is still a burst of requests per search.

Add a short delay — 250–300ms is typical — cancelled by the next keystroke, in the view
model where 5.2 puts the cancellation logic. Note that `MainViewModel` also has a
`partial void OnSearchTextChanged` on line 146; check how that interacts before adding
a second trigger, so you do not end up with two mechanisms racing.

Once the logic is in the view model, this is testable with the `FakeTimeProvider`
approach rather than by sleeping in a test.

### 5.6 — Tidy the leftovers

Small, safe, do them last and fold them into a single commit.

- `MainViewModel.cs` lines 1–3 import `System.ComponentModel`,
  `System.Runtime.CompilerServices` and `System.Windows.Input`, none of which are used
  — they are leftovers from a hand-rolled `INotifyPropertyChanged` that
  `ObservableObject` replaced. Note `System.Windows.Input` is WPF's namespace, so its
  presence in a WinUI file is misleading as well as unused.
- Line 21 `_selectedAirportSuggestion` is mutable non-observable state consulted by
  `GetSelectedAirportResolution` (line 340). Check it is cleared whenever
  `SearchText` changes; a stale selection here means fetching the previous airport.
  If it can go stale, that is a real bug — commit the fix separately with a test.
- Line 44 of `MainWindow.xaml.cs` is `_ = ViewModel.LoadLastStationAsync();`, a
  fire-and-forget in the constructor whose failures are unobservable. Prefer a
  `Loaded` handler that can await and surface an error.
- `AppWindow.Resize(new SizeInt32(900, 800))` on line 41 hardcodes the window size in
  the constructor. Low priority, but it belongs with the rest of the window setup
  rather than in the middle of view-model initialisation.

---

## 5. Definition of done

- `dotnet build .\MetarViewer.sln` clean, no new warnings
  (`EnforceCodeStyleInBuild` is on, so style violations fail the build)
- All 253 existing Core tests still pass, plus new presentation tests
- `MainViewModel` contains **no** `Microsoft.UI.*` or `Windows.*` reference
- The app has been run by hand and verified: search by ICAO, by IATA and by name;
  the suggestion dropdown appears and a suggestion can be chosen; the theme toggle
  works and its icon changes; the last station is restored after a restart; an unknown
  code such as `ZZZZ` shows a sensible error rather than crashing
- One commit per task, each with a message explaining why

---

## 6. Explicitly out of scope

Do not widen the refactor into these without a separate discussion — each was
considered and set aside:

- Changing `IMetarService.GetMetarAsync` to return a result type instead of a nullable
- Renaming any `MetarData` property, for the `x:Bind` reasons in §3
- Moving `AirportSuggestion.DisplayText`, which `TextMemberPath` resolves reflectively
- Restructuring `MetarDecoder`. Its seven `Decode*` methods return preformatted
  user-facing English, which is arguably the wrong layer for it to live in, but it is
  well covered by tests and works. If you want to make the app localisable, that is
  its own project.
- Adding a UI test framework. The point of 5.1 is that the interesting logic stops
  being in the view, so plain xUnit is enough.
