using MetarViewer.Parsing;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the token classification that both METAR sources now share.
///
/// This logic existed as two copies that had drifted apart, so the same report could be read
/// differently depending on which source served it. These tests pin down the single behaviour,
/// including the trend groups that the aviationweather.gov copy used to misread as weather.
/// </summary>
public class MetarTokenClassifierTests
{
    [Theory]
    [InlineData("EGLL")]
    [InlineData("KLAX")]
    public void LooksLikeStationIdentifier_AcceptsFourLetterIcaoCodes(string token)
    {
        Assert.True(MetarTokenClassifier.LooksLikeStationIdentifier(token));
    }

    [Theory]
    [InlineData("LHR")]      // three letters is an IATA code, which METARs are not issued against
    [InlineData("EGLLX")]
    [InlineData("EG1L")]     // digits do not appear in station identifiers
    [InlineData("")]
    public void LooksLikeStationIdentifier_RejectsAnythingElse(string token)
    {
        Assert.False(MetarTokenClassifier.LooksLikeStationIdentifier(token));
    }

    [Theory]
    [InlineData("RA")]
    [InlineData("SN")]
    [InlineData("BR")]
    [InlineData("TSRA")]
    [InlineData("FZRA")]
    [InlineData("SHRA")]
    public void LooksLikeWeatherToken_AcceptsWeatherGroups(string token)
    {
        Assert.True(MetarTokenClassifier.LooksLikeWeatherToken(token));
    }

    [Theory]
    [InlineData("+RA")]      // heavy
    [InlineData("-DZ")]      // light
    [InlineData("VCTS")]     // in the vicinity
    [InlineData("-VCSH")]
    public void LooksLikeWeatherToken_AcceptsIntensityAndVicinityPrefixes(string token)
    {
        Assert.True(MetarTokenClassifier.LooksLikeWeatherToken(token));
    }

    [Theory]
    [InlineData("TEMPO")]    // contains "PO", the code for dust whirls
    [InlineData("BECMG")]
    [InlineData("METAR")]
    [InlineData("SPECI")]
    [InlineData("AUTO")]
    [InlineData("COR")]
    [InlineData("AMD")]
    [InlineData("RTD")]
    [InlineData("NOSIG")]
    [InlineData("RMK")]
    [InlineData("CAVOK")]
    [InlineData("NSW")]
    public void LooksLikeWeatherToken_RejectsStructuralTokens(string token)
    {
        // These are parts of a report's structure. Several contain a weather code as a substring,
        // which is what previously caused a trend group to be reported as observed weather.
        Assert.False(MetarTokenClassifier.LooksLikeWeatherToken(token));
    }

    [Theory]
    [InlineData("27015KT")]  // contains no weather code and is not all letters
    [InlineData("R")]        // too short to be a weather code
    [InlineData("")]
    [InlineData("XX")]       // two letters, but not a weather code
    public void LooksLikeWeatherToken_RejectsTokensWithoutAWeatherCode(string token)
    {
        Assert.False(MetarTokenClassifier.LooksLikeWeatherToken(token));
    }

    [Theory]
    [InlineData("METAR", true)]
    [InlineData("SPECI", true)]
    [InlineData("AUTO", false)]
    [InlineData("EGLL", false)]
    public void IsReportTypePrefix_IdentifiesOnlyTheReportTypes(string token, bool expected)
    {
        Assert.Equal(expected, MetarTokenClassifier.IsReportTypePrefix(token));
    }

    [Theory]
    [InlineData("AUTO", true)]
    [InlineData("COR", true)]
    [InlineData("AMD", true)]
    [InlineData("RTD", true)]
    [InlineData("METAR", false)]
    [InlineData("EGLL", false)]
    public void IsReportModifier_IdentifiesOnlyTheModifiers(string token, bool expected)
    {
        Assert.Equal(expected, MetarTokenClassifier.IsReportModifier(token));
    }
}
