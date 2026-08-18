using System.Text.RegularExpressions;

namespace MetarViewer.Airports;

/// <summary>
/// The string handling shared by airport matching: how a name or code is normalised before it is
/// compared, how a name is broken into words, and how far apart two terms are.
///
/// The lookup service normalised text and split names in several places, each with its own copy
/// of the separator list and the "words that carry no meaning" list. Keeping those rules here
/// means a name is split the same way whether it is being scored or turned into a search query.
/// </summary>
internal static partial class AirportText
{
    /// <summary>
    /// Characters that separate words in an airport name, for example
    /// "London/Heathrow (Intl)".
    /// </summary>
    private static readonly char[] WordSeparators = [' ', '/', '-', ',', '(', ')'];

    /// <summary>
    /// Words that appear in so many airport names that matching on them says nothing about
    /// which airport the user meant.
    /// </summary>
    private static readonly HashSet<string> IgnoredNameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AIRPORT", "AIRFIELD", "AERODROME", "INTERNATIONAL", "INTL", "REGIONAL",
        "MUNICIPAL", "CITY", "FIELD", "HELIPORT", "BASE", "STRIP"
    };

    /// <summary>
    /// Reduces a value to upper-case letters and digits so that punctuation and spacing
    /// differences ("St. John's" against "St Johns") do not count as a mismatch.
    /// </summary>
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : NonAlphaNumericPattern().Replace(value.ToUpperInvariant(), string.Empty);

    /// <summary>
    /// Splits a name into its words, discarding the separators.
    /// </summary>
    public static IEnumerable<string> SplitWords(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Returns the words of an airport name that identify it, so that "London Heathrow Airport"
    /// can be matched on "Heathrow" without "Airport" matching every airport in the world.
    /// </summary>
    public static IEnumerable<string> GetNameTokens(string? airportName) =>
        SplitWords(airportName)
            .Where(token => token.Length >= 3 && !IgnoredNameWords.Contains(token));

    /// <summary>
    /// Counts the single-character insertions, deletions and substitutions needed to turn
    /// <paramref name="source"/> into <paramref name="target"/>. Used to tolerate typos such as
    /// "Heatrow" for "Heathrow".
    /// </summary>
    public static int LevenshteinDistance(string source, string target)
    {
        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        var distances = new int[source.Length + 1, target.Length + 1];

        for (var sourceIndex = 0; sourceIndex <= source.Length; sourceIndex++)
        {
            distances[sourceIndex, 0] = sourceIndex;
        }

        for (var targetIndex = 0; targetIndex <= target.Length; targetIndex++)
        {
            distances[0, targetIndex] = targetIndex;
        }

        for (var sourceIndex = 1; sourceIndex <= source.Length; sourceIndex++)
        {
            for (var targetIndex = 1; targetIndex <= target.Length; targetIndex++)
            {
                var cost = source[sourceIndex - 1] == target[targetIndex - 1] ? 0 : 1;

                distances[sourceIndex, targetIndex] = Math.Min(
                    Math.Min(
                        distances[sourceIndex - 1, targetIndex] + 1,
                        distances[sourceIndex, targetIndex - 1] + 1),
                    distances[sourceIndex - 1, targetIndex - 1] + cost);
            }
        }

        return distances[source.Length, target.Length];
    }

    // Source-generated so the pattern is compiled at build time rather than on first use.
    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonAlphaNumericPattern();
}
