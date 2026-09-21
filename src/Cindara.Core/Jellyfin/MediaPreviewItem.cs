namespace Cindara.Core.Jellyfin;

public sealed record MediaPreviewItem(
    string Id,
    string Name,
    string Subtitle,
    string MediaType,
    byte[]? Artwork,
    byte[]? Backdrop,
    string? Overview,
    string Details,
    double? PlaybackProgress,
    string? HeroName = null,
    string? HeroSubtitle = null);
