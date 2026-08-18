using MetarViewer.Models;

namespace MetarViewer.Parsing;

/// <summary>
/// Parses a single group of a METAR report.
///
/// Each observation group (wind, visibility, cloud, temperature, pressure) gets its own
/// implementation, replacing a fixed short-circuiting chain of private methods. This makes
/// each group independently testable and allows new groups to be supported by adding a
/// parser rather than editing the main loop.
/// </summary>
internal interface IMetarTokenParser
{
    /// <summary>
    /// Attempts to parse the token at the current position, writing any values onto
    /// <paramref name="metar"/>.
    /// </summary>
    /// <param name="context">The tokens of the report and the current position.</param>
    /// <param name="metar">The report being populated.</param>
    /// <returns>True when the token was consumed by this parser.</returns>
    bool TryParse(MetarTokenContext context, MetarData metar);
}

/// <summary>
/// The token stream being parsed, together with the current position.
/// </summary>
/// <remarks>
/// A few groups span two tokens: a visibility of "1 1/2SM" arrives as "1" followed by
/// "1/2SM". Parsers therefore need to look ahead and report how many tokens they consumed,
/// which this type exposes through <see cref="ConsumeAdditionalToken"/>.
/// </remarks>
internal sealed class MetarTokenContext(string[] tokens, int index)
{
    /// <summary>All tokens in the report.</summary>
    public string[] Tokens { get; } = tokens;

    /// <summary>The index of the token currently being parsed.</summary>
    public int Index { get; private set; } = index;

    /// <summary>The token currently being parsed.</summary>
    public string CurrentToken => Tokens[Index];

    /// <summary>The following token, or null when at the end of the report.</summary>
    public string? NextToken => Index + 1 < Tokens.Length ? Tokens[Index + 1] : null;

    /// <summary>
    /// Records that a parser also consumed the following token.
    /// </summary>
    public void ConsumeAdditionalToken() => Index++;
}
