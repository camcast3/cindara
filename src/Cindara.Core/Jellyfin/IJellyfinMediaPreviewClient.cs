using Cindara.Core.Authentication;

namespace Cindara.Core.Jellyfin;

public interface IJellyfinMediaPreviewClient
{
    Task<MediaPreviewHome> GetHomeAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
