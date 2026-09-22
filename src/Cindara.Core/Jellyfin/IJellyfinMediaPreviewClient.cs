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

    void ClearImageCache();
}
