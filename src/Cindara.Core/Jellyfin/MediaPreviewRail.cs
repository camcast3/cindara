namespace Cindara.Core.Jellyfin;

public sealed record MediaPreviewRail(
    string LibraryId,
    string Title,
    IReadOnlyList<MediaPreviewItem> Items,
    string? LibraryName = null);
