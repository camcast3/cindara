namespace Cindara.Core.Jellyfin;

public enum MediaLibraryFilter
{
    All,
    Unwatched,
    Favorites,
}

public enum MediaLibrarySortDirection
{
    Ascending,
    Descending,
}

public sealed record MediaLibraryQuery(
    int StartIndex = 0,
    MediaLibraryFilter Filter = MediaLibraryFilter.All,
    MediaLibrarySortDirection SortDirection = MediaLibrarySortDirection.Ascending,
    char? StartsWith = null)
{
    public MediaLibraryQuery Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(StartIndex);
        if (!Enum.IsDefined(Filter))
        {
            throw new ArgumentOutOfRangeException(nameof(Filter));
        }

        if (!Enum.IsDefined(SortDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(SortDirection));
        }

        if (StartsWith is { } letter && (letter < 'A' || letter > 'Z'))
        {
            throw new ArgumentOutOfRangeException(nameof(StartsWith), "The title filter must be A through Z.");
        }

        return this;
    }
}
