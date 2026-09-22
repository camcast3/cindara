using Cindara.Core.Authentication;
using Cindara.Core.Models;
using Cindara.Desktop.Localization;

namespace Cindara.Desktop.Tests.Localization;

[Collection(LocalizationTestGroup.Name)]
public sealed class LocaleFormatTests
{
    [Theory]
    [InlineData("en-US", "9/22/2026", "1,234.50", "8.5 / 10")]
    [InlineData("fr-FR", "22/09/2026", "1\u202f234,50", "8,5 / 10")]
    [InlineData("de-DE", "22.09.2026", "1.234,50", "8,5 / 10")]
    public void DatesNumbersAndRatingsUseSelectedCulture(
        string culture, string date, string number, string rating)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(date, LocaleFormat.Date(new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(number, LocaleFormat.Number(1234.5, 2));
        Assert.Equal(rating, LocaleFormat.Rating(8.5));
    }

    [Theory]
    [InlineData("en", 0, "0m")]
    [InlineData("en", -1, "0m")]
    [InlineData("en", 42, "42m")]
    [InlineData("en", 60, "1h 0m")]
    [InlineData("en", 1505, "25h 5m")]
    [InlineData("fr", 42, "42 min")]
    [InlineData("fr", 65, "1 h 5 min")]
    public void DurationsUseLocalizedUnitTemplates(string culture, int minutes, string expected)
    {
        using var scope = new CultureScope(culture);

        Assert.Equal(expected, LocaleFormat.Duration(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void MetadataFormatsRawValuesWithoutTranslatingServerRatings()
    {
        using var scope = new CultureScope("fr");

        Assert.Equal("S2 É3", LocaleFormat.EpisodeNumber(2, 3));
        Assert.Equal("Épisode", LocaleFormat.EpisodeNumber(null, 3));
        Assert.Equal("2026  ·  1 h 5 min  ·  TV-14",
            LocaleFormat.Details(2026, TimeSpan.FromMinutes(65).Ticks, "TV-14"));
        Assert.Equal(string.Empty, LocaleFormat.Details(null, null, null));
        Assert.Equal("PG", LocaleFormat.Details(null, -10, "PG"));
    }

    [Fact]
    public void EpisodeAndSeasonSubtitlesFormatLabelsButPreserveServerTitles()
    {
        using var scope = new CultureScope("fr");

        Assert.Equal("Northstar · S2 É3",
            LocaleFormat.Subtitle("Homecoming", "Episode", "Northstar", 2, 3, 2026, false));
        Assert.Equal("S2 É3 · Homecoming",
            LocaleFormat.Subtitle("Homecoming", "Episode", "Northstar", 2, 3, 2026, true));
        Assert.Equal("Season Two",
            LocaleFormat.Subtitle("Season Two", "Season", "Northstar", 2, null, 2026, true));
        Assert.Equal("2026",
            LocaleFormat.Subtitle("Movie", "Movie", null, null, null, 2026, false));
        Assert.Equal("Jellyfin",
            LocaleFormat.Subtitle("Movie", "Movie", null, null, null, null, false));
    }

    [Fact]
    public void PseudoLocaleDoesNotRewriteAccountOrServerData()
    {
        using var scope = new CultureScope("qps-plocm");
        var server = new ServerIdentity("id", new Uri("https://media.example.com/"),
            "My server", "10.10", "Linux");
        var profile = new SessionProfile(server, "user", "viewer");
        var formatted = LocaleFormat.SessionDisplayName(profile);

        Assert.Contains("viewer", formatted, StringComparison.Ordinal);
        Assert.Contains("My server", formatted, StringComparison.Ordinal);
        Assert.Contains("https://media.example.com", formatted, StringComparison.Ordinal);
        Assert.Contains("TV-14", LocaleFormat.Details(2026, TimeSpan.FromMinutes(42).Ticks, "TV-14"),
            StringComparison.Ordinal);
    }
}
