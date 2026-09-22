using System.Globalization;
using Cindara.Core.Authentication;

namespace Cindara.Desktop.Localization;

public static class LocaleFormat
{
    public static string Date(DateTimeOffset value) => value.ToString("d", Loc.Culture);

    public static string Number(double value, int decimalPlaces = 0) =>
        value.ToString("N" + decimalPlaces.ToString(CultureInfo.InvariantCulture), Loc.Culture);

    public static string Rating(double value, double maximum = 10) =>
        Loc.Format("Format.Rating", value, maximum);

    public static string Duration(TimeSpan value)
    {
        var minutes = Math.Max(0, (long)value.TotalMinutes);
        return minutes >= 60
            ? Loc.Format("Format.Duration.HoursMinutes", minutes / 60, minutes % 60)
            : Loc.Format("Format.Duration.Minutes", minutes);
    }

    public static string SessionDisplayName(SessionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Loc.Format("Format.Session",
            profile.Username, profile.Server.DisplayName, profile.Server.BaseUri.AbsoluteUri.TrimEnd('/'));
    }

    public static string EpisodeNumber(int? season, int? episode) =>
        season is { } seasonNumber && episode is { } episodeNumber
            ? Loc.Format("Format.EpisodeNumber", seasonNumber, episodeNumber)
            : Loc.Get("Format.Episode");

    public static string Subtitle(
        string name,
        string mediaType,
        string? seriesName,
        int? season,
        int? episode,
        int? productionYear,
        bool preferSeriesTitle)
    {
        if (!string.IsNullOrWhiteSpace(seriesName))
        {
            if (mediaType == "Season" && preferSeriesTitle)
            {
                return name;
            }

            var episodeNumber = EpisodeNumber(season, episode);
            return preferSeriesTitle
                ? Loc.Format("Format.Subtitle", episodeNumber, name)
                : Loc.Format("Format.Subtitle", seriesName, episodeNumber);
        }

        return productionYear?.ToString(Loc.Culture) ?? Loc.Get("Format.MediaSource");
    }

    public static string Details(int? productionYear, long? runtimeTicks, string? officialRating)
    {
        var parts = new List<string>(3);
        if (productionYear is { } year)
        {
            parts.Add(year.ToString(Loc.Culture));
        }

        if (runtimeTicks is > 0)
        {
            parts.Add(Duration(TimeSpan.FromTicks(runtimeTicks.Value)));
        }

        if (!string.IsNullOrWhiteSpace(officialRating))
        {
            parts.Add(officialRating);
        }

        return string.Join(Loc.Get("Format.DetailSeparator"), parts);
    }
}
