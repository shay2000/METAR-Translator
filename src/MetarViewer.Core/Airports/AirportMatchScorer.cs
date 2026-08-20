namespace MetarViewer.Airports;

/// <summary>
/// Ranks an airport returned by the API against what the user typed, so that "Heathrow" resolves
/// to London Heathrow rather than to a heliport that happens to share the word.
///
/// This scoring was previously spread over five private methods of the lookup service, mixed in
/// with HTTP calls and caching, and so could only be exercised by stubbing out the network. It is
/// pure logic and belongs on its own, where each weighting can be tested directly.
/// </summary>
internal static class AirportMatchScorer
{
    /// <summary>
    /// The score given to an airport that must never be offered, such as a closed one.
    /// </summary>
    public const int NoMatch = int.MinValue;

    /// <summary>
    /// A score at or above this leaves no doubt about which airport was meant, so searching can
    /// stop early instead of issuing further queries.
    /// </summary>
    public const int ConfidentScore = 250;

    /// <summary>
    /// The lowest score accepted when resolving to a single airport. Suggestions are offered
    /// below this, but a weak match is not silently treated as the answer.
    /// </summary>
    /// <remarks>
    /// Airport type and the presence of an IATA code are only tie-breakers. Their maximum combined
    /// value is 130, so this threshold must stay above that: an unrelated large airport returned by
    /// a broad fallback query must never be accepted on priors alone.
    /// </remarks>
    public const int MinimumResolutionScore = 140;

    // A code match is decisive: codes are unique, so an exact one outweighs any name scoring.
    private const int CodeMatchScore = 500;
    private const int ExactNameScore = 300;
    private const int NamePrefixScore = 220;
    private const int NameContainsScore = 150;

    // A published IATA code marks an airport as one passengers actually fly to, which breaks
    // ties between otherwise equally good matches.
    private const int HasIataCodeScore = 10;

    // A perfect fuzzy match is worth less than a name match so that it only ever breaks ties or
    // rescues a typo; each edit costs far more than a small difference in length.
    private const int MaximumFuzzyScore = 120;
    private const int FuzzyDistancePenalty = 28;
    private const int FuzzyLengthPenalty = 3;

    /// <summary>
    /// Scores how well an airport matches the search text. Higher is better.
    /// </summary>
    /// <param name="attributes">The airport to score.</param>
    /// <param name="trimmedInput">The search text as typed, used for name comparisons.</param>
    /// <param name="normalizedInput">The upper-cased search text, used for code comparisons.</param>
    /// <returns>The score, or <see cref="NoMatch"/> if the airport should be discarded.</returns>
    public static int Score(AirportAttributes? attributes, string trimmedInput, string normalizedInput)
    {
        if (attributes == null || !AirportCodes.IsSupportedAirportType(attributes.Type))
        {
            return NoMatch;
        }

        var score = GetAirportTypeScore(attributes.Type);
        var airportName = attributes.Name ?? string.Empty;

        if (MatchesAnyCode(attributes, normalizedInput))
        {
            score += CodeMatchScore;
        }

        // Only the strongest name relationship counts, so a name that starts with the search
        // text does not also collect the weaker "contains" award.
        if (string.Equals(airportName, trimmedInput, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactNameScore;
        }
        else if (airportName.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
        {
            score += NamePrefixScore;
        }
        else if (airportName.Contains(trimmedInput, StringComparison.OrdinalIgnoreCase))
        {
            score += NameContainsScore;
        }

        if (!string.IsNullOrWhiteSpace(attributes.IataCode))
        {
            score += HasIataCodeScore;
        }

        return score + GetFuzzyScore(attributes, normalizedInput);
    }

    /// <summary>
    /// Determines whether the search text is one of the airport's codes.
    /// </summary>
    public static bool MatchesAnyCode(AirportAttributes? attributes, string normalizedInput) =>
        attributes != null &&
        attributes.GetAllCodes().Any(code => string.Equals(code, normalizedInput, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Rewards airports whose name or codes are nearly what the user typed, which is what lets a
    /// misspelling still find the right airport.
    /// </summary>
    public static int GetFuzzyScore(AirportAttributes attributes, string normalizedInput)
    {
        var normalizedQuery = AirportText.Normalize(normalizedInput);
        if (normalizedQuery.Length == 0)
        {
            return 0;
        }

        // The closest of the airport's name, its individual name words and its codes decides the
        // score, so "Heatrow" can match on the word "Heathrow" inside a much longer full name.
        var bestScore = 0;
        foreach (var term in GetComparableTerms(attributes))
        {
            var distance = AirportText.LevenshteinDistance(normalizedQuery, term);
            var lengthPenalty = Math.Abs(term.Length - normalizedQuery.Length) * FuzzyLengthPenalty;
            var score = Math.Max(0, MaximumFuzzyScore - (distance * FuzzyDistancePenalty) - lengthPenalty);

            if (score > bestScore)
            {
                bestScore = score;
            }
        }

        return bestScore;
    }

    /// <summary>
    /// Ranks airports by how likely a pilot is to have meant them. Heliports and balloonports
    /// score negatively so that a large airport sharing their name always wins.
    /// </summary>
    public static int GetAirportTypeScore(string? airportType) => airportType switch
    {
        "large_airport" => 120,
        "medium_airport" => 90,
        "small_airport" => 60,
        "seaplane_base" => 25,
        "heliport" => -10,
        "balloonport" => -20,
        _ => 0
    };

    /// <summary>
    /// Returns every normalised term the search text may reasonably be compared against.
    /// </summary>
    private static IEnumerable<string> GetComparableTerms(AirportAttributes attributes)
    {
        foreach (var term in Normalized(attributes.Name))
        {
            yield return term;
        }

        foreach (var code in attributes.GetAllCodes())
        {
            foreach (var term in Normalized(code))
            {
                yield return term;
            }
        }

        foreach (var token in AirportText.GetNameTokens(attributes.Name))
        {
            foreach (var term in Normalized(token))
            {
                yield return term;
            }
        }

        // Normalising can leave nothing behind (a name of "-"), and an empty term would score
        // purely on length, so those are dropped rather than compared.
        static IEnumerable<string> Normalized(string? value)
        {
            var normalized = AirportText.Normalize(value);
            if (normalized.Length > 0)
            {
                yield return normalized;
            }
        }
    }
}
