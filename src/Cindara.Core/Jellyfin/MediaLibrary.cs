namespace Cindara.Core.Jellyfin;

public sealed record MediaLibrary(string Id, string Name, string? CollectionType);

public sealed record MediaLibraryPage(
    IReadOnlyList<MediaPreviewItem> Items,
    int StartIndex,
    int TotalRecordCount)
{
    public const int PageSize = 40;
    public bool HasNextPage => StartIndex + Items.Count < TotalRecordCount;
}
