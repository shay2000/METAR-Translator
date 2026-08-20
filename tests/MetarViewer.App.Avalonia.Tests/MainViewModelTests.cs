using MetarViewer.Models;
using MetarViewer.Services;
using MetarViewer.ViewModels;
using Xunit;

namespace MetarViewer.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task FetchMetarCommand_KeepsProviderStationNameAndReportUnchanged()
    {
        var providerReport = new MetarData
        {
            StationId = "EGLL",
            StationName = "Provider Airport Name",
            RawMetar = "METAR EGLL 201250Z 24008KT 9999 FEW030 18/10 Q1016"
        };
        var metarService = new StubMetarService
        {
            Handler = (_, _) => Task.FromResult<MetarData?>(providerReport)
        };
        var airportLookupService = new StubAirportLookupService
        {
            ResolveDetailsHandler = (_, _) => Task.FromResult<ResolvedAirport?>(
                new ResolvedAirport("EGLL", "Lookup Airport Name", "LHR"))
        };
        var viewModel = new MainViewModel(metarService, airportLookupService)
        {
            SearchText = "LHR"
        };

        await viewModel.FetchMetarCommand.ExecuteAsync(null);

        Assert.Same(providerReport, viewModel.CurrentMetar);
        Assert.Equal("Provider Airport Name", providerReport.StationName);
        Assert.Equal("Lookup Airport Name", viewModel.ResolvedStationName);
        Assert.Equal("EGLL - Provider Airport Name", viewModel.StationHeaderText);
    }

    [Fact]
    public async Task UpdateAirportSuggestionsAsync_SelectedSuggestionDoesNotQueryOrRepopulate()
    {
        var airportLookupService = new StubAirportLookupService
        {
            SuggestionsHandler = (_, _) => Task.FromResult<IReadOnlyList<AirportSuggestion>>(
                [new AirportSuggestion("EGKK", "London Gatwick Airport", "LGW")])
        };
        var viewModel = new MainViewModel(new StubMetarService(), airportLookupService);
        var selectedSuggestion = new AirportSuggestion("EGLL", "London Heathrow Airport", "LHR");

        viewModel.SelectAirportSuggestion(selectedSuggestion);
        await viewModel.UpdateAirportSuggestionsAsync(viewModel.SearchText);

        Assert.Empty(airportLookupService.SuggestionRequests);
        Assert.Empty(viewModel.AirportSuggestions);
        Assert.Equal(selectedSuggestion.DisplayText, viewModel.SearchText);
    }

    [Fact]
    public async Task UpdateAirportSuggestionsAsync_SubmittedSearchDoesNotReopenSuggestions()
    {
        var airportLookupService = new StubAirportLookupService
        {
            ResolveDetailsHandler = (_, _) => Task.FromResult<ResolvedAirport?>(
                new ResolvedAirport("EGLL", "London Heathrow Airport", "LHR")),
            SuggestionsHandler = (_, _) => Task.FromResult<IReadOnlyList<AirportSuggestion>>(
                [new AirportSuggestion("EGLL", "London Heathrow Airport", "LHR")])
        };
        var metarService = new StubMetarService
        {
            Handler = (_, _) => Task.FromResult<MetarData?>(new MetarData { StationId = "EGLL" })
        };
        var viewModel = new MainViewModel(metarService, airportLookupService)
        {
            SearchText = "EGLL"
        };

        await viewModel.FetchMetarCommand.ExecuteAsync(null);
        await viewModel.UpdateAirportSuggestionsAsync(viewModel.SearchText);

        Assert.Empty(airportLookupService.SuggestionRequests);
        Assert.Empty(viewModel.AirportSuggestions);
    }

    [Fact]
    public async Task UpdateAirportSuggestionsAsync_StaleResponseCannotReplaceNewerResults()
    {
        var firstResponse = new TaskCompletionSource<IReadOnlyList<AirportSuggestion>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<IReadOnlyList<AirportSuggestion>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var airportLookupService = new StubAirportLookupService
        {
            SuggestionsHandler = (input, _) => input switch
            {
                "Lon" => firstResponse.Task,
                "London" => secondResponse.Task,
                _ => throw new InvalidOperationException($"Unexpected suggestion input: {input}")
            }
        };
        var viewModel = new MainViewModel(new StubMetarService(), airportLookupService)
        {
            SearchText = "Lon"
        };

        var firstUpdate = viewModel.UpdateAirportSuggestionsAsync("Lon");
        viewModel.SearchText = "London";
        var secondUpdate = viewModel.UpdateAirportSuggestionsAsync("London");

        var newestSuggestions = new[]
        {
            new AirportSuggestion("EGLL", "London Heathrow Airport", "LHR")
        };
        secondResponse.SetResult(newestSuggestions);
        await secondUpdate;

        firstResponse.SetResult(
            [new AirportSuggestion("EGLC", "London City Airport", "LCY")]);
        await firstUpdate;

        Assert.Equal(["Lon", "London"], airportLookupService.SuggestionRequests);
        Assert.Same(newestSuggestions, viewModel.AirportSuggestions);
    }
}
