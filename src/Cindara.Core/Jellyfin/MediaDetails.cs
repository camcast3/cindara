namespace Cindara.Core.Jellyfin;

public sealed record MediaUserState(
    bool IsFavorite,
    bool IsPlayed,
    double? PlayedPercentage,
    long? PlaybackPositionTicks);

public sealed record MediaRating(string Name, double Value);

public sealed record MediaCredit(
    string Id,
    string Name,
    string? Role,
    string CreditType,
    string? ImageTag);

public sealed record MediaTrackInfo(
    string TrackType,
    string? Codec,
    string? DisplayTitle,
    string? Language,
    int? Width,
    int? Height,
    int? Channels,
    bool IsDefault,
    bool IsExternal);

public sealed record MediaItemDetails(
    string Id,
    string Name,
    string MediaType,
    string? SeriesId,
    string? SeriesName,
    string? SeasonId,
    int? SeasonNumber,
    int? EpisodeNumber,
    int? ProductionYear,
    DateOnly? PremiereDate,
    long? RunTimeTicks,
    string? OfficialRating,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MediaRating> Ratings,
    string? Overview,
    IReadOnlyList<MediaCredit> Credits,
    IReadOnlyList<MediaTrackInfo> Tracks,
    MediaUserState UserState,
    bool HasPrimaryImage,
    bool HasBackdrop,
    int? LocalTrailerCount = null,
    bool HasRemoteTrailers = false,
    bool HasUserState = true);

public sealed record MediaSeason(
    string Id,
    string Name,
    int? SeasonNumber,
    MediaUserState UserState,
    bool HasPrimaryImage);

public sealed record MediaEpisode(
    string Id,
    string Name,
    string SeriesId,
    string? SeasonId,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateOnly? PremiereDate,
    long? RunTimeTicks,
    string? OfficialRating,
    string? Overview,
    IReadOnlyList<MediaCredit> Credits,
    MediaUserState UserState,
    bool HasPrimaryImage);
