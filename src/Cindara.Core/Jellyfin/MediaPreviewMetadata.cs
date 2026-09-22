namespace Cindara.Core.Jellyfin;

public sealed record MediaPreviewMetadata(
    string Name,
    string? SeriesName,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? ProductionYear,
    long? RunTimeTicks,
    string? OfficialRating,
    bool PreferSeriesTitle);
