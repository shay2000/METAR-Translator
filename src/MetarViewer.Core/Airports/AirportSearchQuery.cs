namespace MetarViewer.Airports;

/// <summary>
/// A single search to send to airportsapi.com: which field to filter on, the value to filter by,
/// and how many results to ask for.
/// </summary>
/// <param name="FilterKey">The API filter parameter, for example <c>filter[name]</c>.</param>
/// <param name="Value">The value to filter by.</param>
/// <param name="PageSize">How many results to request.</param>
internal sealed record AirportSearchQuery(string FilterKey, string Value, int PageSize)
{
    /// <summary>The API filter for matching an airport's codes.</summary>
    public const string CodeFilter = "filter[code]";

    /// <summary>The API filter for matching an airport's name.</summary>
    public const string NameFilter = "filter[name]";

    /// <summary>
    /// The page size used when filtering by code. Codes are nearly unique, so a small page is
    /// enough.
    /// </summary>
    public const int CodePageSize = 20;

    /// <summary>
    /// The page size used for an exact name search.
    /// </summary>
    public const int NamePageSize = 20;

    /// <summary>
    /// The page size used for a shortened name search. These match broadly, so a large page is
    /// needed for the intended airport to appear at all.
    /// </summary>
    public const int RelaxedNamePageSize = 50;

    /// <summary>
    /// Identifies the query so that the same search is not issued twice.
    /// </summary>
    public string DeduplicationKey => $"{FilterKey}:{Value}";
}

/// <summary>
/// Builds the progressively broader searches used when an exact search finds nothing, which is
/// how a misspelled airport name is still found.
///
/// This was three private methods of the lookup service that yielded query tuples. On its own the
/// sequence of fallback searches can be asserted directly, rather than inferred from which URLs a
/// stubbed HTTP handler happened to be asked for.
/// </summary>
internal static class AirportSearchQueryBuilder
{
    /// <summary>
    /// The shortened forms tried for a term, from most to least specific. Anything below two
    /// characters would match most of the world's airports and is not worth requesting.
    /// </summary>
    private static readonly int[] FragmentLengths = [5, 4, 2];

    /// <summary>
    /// Builds the fallback searches for a search string, in the order they should be tried.
    /// </summary>
    /// <param name="trimmedInput">The search text as typed.</param>
    /// <param name="normalizedInput">The upper-cased search text.</param>
    public static IEnumerable<AirportSearchQuery> BuildRelaxedQueries(string trimmedInput, string normalizedInput)
    {
        var seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A code that found nothing may have one character too many, so the shorter prefix is
        // tried first: "EGLLX" becomes "EGLL".
        if (AirportCodes.LooksLikeAirportCode(normalizedInput) && normalizedInput.Length >= 3)
        {
            var codePrefix = normalizedInput[..Math.Max(3, normalizedInput.Length - 1)];
            var codeQuery = new AirportSearchQuery(AirportSearchQuery.CodeFilter, codePrefix, AirportSearchQuery.CodePageSize);

            if (seenQueries.Add(codeQuery.DeduplicationKey))
            {
                yield return codeQuery;
            }
        }

        foreach (var fragment in GetNameFragments(trimmedInput))
        {
            var nameQuery = new AirportSearchQuery(AirportSearchQuery.NameFilter, fragment, AirportSearchQuery.RelaxedNamePageSize);

            if (seenQueries.Add(nameQuery.DeduplicationKey))
            {
                yield return nameQuery;
            }
        }
    }

    /// <summary>
    /// Returns the leading fragments of the search text to filter names by.
    /// </summary>
    /// <remarks>
    /// Each word is tried before the whole string, because a typo in one word should not stop the
    /// others from matching. Prefixes are used rather than the whole word so that a misspelling
    /// near the end of a word ("Heat" from "Heatrow") still matches.
    /// </remarks>
    private static IEnumerable<string> GetNameFragments(string trimmedInput)
    {
        foreach (var word in AirportText.SplitWords(trimmedInput).Where(word => word.Length >= 2))
        {
            foreach (var fragment in BuildFragments(word))
            {
                yield return fragment;
            }
        }

        // Finally the search text with its punctuation removed, which catches a name the user
        // ran together, such as "SanFrancisco".
        foreach (var fragment in BuildFragments(AirportText.Normalize(trimmedInput)))
        {
            yield return fragment;
        }
    }

    private static IEnumerable<string> BuildFragments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var normalizedValue = value.Trim();

        foreach (var length in FragmentLengths)
        {
            if (normalizedValue.Length >= length)
            {
                yield return normalizedValue[..length];
            }
        }
    }
}
