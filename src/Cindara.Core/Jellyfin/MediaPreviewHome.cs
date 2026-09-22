namespace Cindara.Core.Jellyfin;

public sealed record MediaPreviewHome(
    MediaPreviewItem? Featured,
    IReadOnlyList<MediaPreviewItem> ContinueWatching,
    IReadOnlyList<MediaPreviewRail> RecentlyAddedLibraries)
{
    public IReadOnlyList<MediaPreviewItem> NextUp { get; init; } = [];
    public IReadOnlyList<MediaLibrary> Libraries { get; init; } = [];
}
