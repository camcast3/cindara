using Cindara.Core.Authentication;

namespace Cindara.Core.Jellyfin;

public interface IJellyfinMediaPreviewClient
{
    Task<MediaPreviewHome> GetHomeAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<MediaLibraryPage> GetLibraryPageAsync(
        AuthenticatedSession session,
        MediaLibrary library,
        int startIndex,
        CancellationToken cancellationToken = default);

    Task<MediaLibraryPage> GetLibraryPageAsync(
        AuthenticatedSession session,
        MediaLibrary library,
        MediaLibraryQuery query,
        CancellationToken cancellationToken = default) =>
        GetLibraryPageAsync(session, library, query.Validate().StartIndex, cancellationToken);

    Task<MediaSearchPage> SearchAsync(
        AuthenticatedSession session,
        string query,
        int startIndex,
        CancellationToken cancellationToken = default) =>
        Task.FromException<MediaSearchPage>(new NotSupportedException("Search is not supported by this client."));

    Task<MediaItemDetails> GetItemDetailsAsync(
        AuthenticatedSession session,
        string itemId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<MediaItemDetails>(new NotSupportedException("Item details are not supported by this client."));

    Task<IReadOnlyList<MediaSeason>> GetSeasonsAsync(
        AuthenticatedSession session,
        string seriesId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<MediaSeason>>(
            new NotSupportedException("Series seasons are not supported by this client."));

    Task<IReadOnlyList<MediaEpisode>> GetEpisodesAsync(
        AuthenticatedSession session,
        string seriesId,
        string seasonId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<MediaEpisode>>(
            new NotSupportedException("Season episodes are not supported by this client."));

    Task<MediaUserState> SetFavoriteAsync(
        AuthenticatedSession session,
        string itemId,
        bool isFavorite,
        CancellationToken cancellationToken = default) =>
        Task.FromException<MediaUserState>(
            new NotSupportedException("Favorite changes are not supported by this client."));

    Task<MediaUserState> SetPlayedAsync(
        AuthenticatedSession session,
        string itemId,
        bool isPlayed,
        CancellationToken cancellationToken = default) =>
        Task.FromException<MediaUserState>(
            new NotSupportedException("Played changes are not supported by this client."));

    void ClearImageCache();

    Task<byte[]?> GetLibraryArtworkAsync(
        AuthenticatedSession session,
        string itemId,
        CancellationToken cancellationToken = default);
}
