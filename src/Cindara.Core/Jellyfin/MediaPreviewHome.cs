namespace Cindara.Core.Jellyfin;

public sealed record MediaPreviewHome(
    MediaPreviewItem? Featured,
    IReadOnlyList<MediaPreviewItem> ContinueWatching,
    IReadOnlyList<MediaPreviewRail> RecentlyAddedLibraries);
