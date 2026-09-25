namespace Cindara.Core.Jellyfin;

public sealed record MediaSearchPage(
    IReadOnlyList<MediaPreviewItem> Items,
    int StartIndex,
    int TotalRecordCount)
{
    public const int PageSize = 40;

    public bool HasNextPage => StartIndex + Items.Count < TotalRecordCount;
}
