using MetarViewer.Airports;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the fallback searches issued when an exact search finds nothing.
///
/// The sequence of these searches is what allows a misspelled airport name to be found. It used to
/// be observable only through which URLs a stubbed HTTP handler was asked for, which meant a change
/// in the order would only show up as a puzzling failure in an end-to-end lookup test.
/// </summary>
public class AirportSearchQueryBuilderTests
{
    [Fact]
    public void BuildRelaxedQueries_TriesAShorterCodeFirst()
    {
        // A code that found nothing may have one character too many, and dropping the last
        // character is both the cheapest fix and the most likely one.
        var queries = Build("EGLL");

        var first = queries[0];
        Assert.Equal("filter[code]", first.FilterKey);
        Assert.Equal("EGL", first.Value);
    }

    [Fact]
    public void BuildRelaxedQueries_NeverShortensACodeBelowThreeCharacters()
    {
        // Two characters would match a large share of the world's airports.
        var queries = Build("LHR");

        Assert.Equal("LHR", Assert.Single(queries, query => query.FilterKey == "filter[code]").Value);
    }

    [Fact]
    public void BuildRelaxedQueries_DoesNotSearchByCodeForAName()
    {
        var queries = Build("Heathrow");

        Assert.DoesNotContain(queries, query => query.FilterKey == "filter[code]");
    }

    [Fact]
    public void BuildRelaxedQueries_ShortensANameFromMostToLeastSpecific()
    {
        // Trying the longest fragment first means the closest match is found before broader,
        // noisier searches are needed.
        var fragments = Build("Heatrow")
            .Where(query => query.FilterKey == "filter[name]")
            .Select(query => query.Value)
            .ToList();

        Assert.Equal(["Heatr", "Heat", "He"], fragments);
    }

    [Fact]
    public void BuildRelaxedQueries_SearchesEachWordOfAMultiWordName()
    {
        // A typo in one word should not stop the others from finding the airport.
        var fragments = Build("San Francisco")
            .Select(query => query.Value)
            .ToList();

        Assert.Contains("Franc", fragments);

        // "San" is shorter than the 5- and 4-character fragment lengths, so the shortest
        // fragment length is the only one it can produce.
        Assert.Contains("Sa", fragments);
    }

    [Fact]
    public void BuildRelaxedQueries_AlsoSearchesTheWholeInputWithPunctuationRemoved()
    {
        // This is what finds an airport whose name the user ran together.
        var fragments = Build("San Francisco").Select(query => query.Value).ToList();

        Assert.Contains("SANFR", fragments);
    }

    [Fact]
    public void BuildRelaxedQueries_SkipsWordsTooShortToBeWorthSearching()
    {
        var fragments = Build("A Heathrow").Select(query => query.Value).ToList();

        Assert.DoesNotContain("A", fragments);
    }

    [Fact]
    public void BuildRelaxedQueries_DoesNotIssueTheSameSearchTwice()
    {
        // A single-word name produces the same fragments from the word and from the whole input,
        // and repeating a request would waste a round trip for no new results.
        var queries = Build("Heathrow");

        Assert.Equal(
            queries.Select(query => query.DeduplicationKey).Distinct().Count(),
            queries.Count);
    }

    [Fact]
    public void BuildRelaxedQueries_AsksForMoreResultsForBroaderNameSearches()
    {
        // A short fragment matches widely, so a larger page is needed for the intended airport to
        // appear at all.
        var nameQueries = Build("Heatrow").Where(query => query.FilterKey == "filter[name]");

        Assert.All(nameQueries, query => Assert.Equal(50, query.PageSize));
    }

    [Fact]
    public void BuildRelaxedQueries_ReturnsNothingForInputTooShortToRelax()
    {
        Assert.Empty(Build("A"));
    }

    private static List<AirportSearchQuery> Build(string input) =>
        AirportSearchQueryBuilder.BuildRelaxedQueries(input, input.ToUpperInvariant()).ToList();
}
